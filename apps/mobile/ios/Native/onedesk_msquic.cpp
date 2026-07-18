#include "onedesk_msquic.h"

#include <msquic.h>

#include <algorithm>
#include <chrono>
#include <condition_variable>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

namespace {

constexpr size_t kMaximumFrameBytes = 16 * 1024 * 1024;

struct ClientContext;

struct SendContext {
    explicit SendContext(std::vector<uint8_t> bytes) : data(std::move(bytes)) {
        buffer.Buffer = data.data();
        buffer.Length = static_cast<uint32_t>(data.size());
    }
    std::vector<uint8_t> data;
    QUIC_BUFFER buffer{};
};

struct RequestContext {
    HQUIC stream{};
    std::mutex mutex;
    std::condition_variable signal;
    std::vector<uint8_t> received;
    size_t expectedPayloadBytes{};
    bool lengthKnown{};
    bool completed{};
    bool shutdownComplete{};
    std::string error;
};

struct EventStreamContext {
    ClientContext* client{};
    std::vector<uint8_t> received;
    size_t expectedPayloadBytes{};
    bool lengthKnown{};
    bool delivered{};
};

struct ClientContext {
    const QUIC_API_TABLE* api{};
    HQUIC registration{};
    HQUIC configuration{};
    HQUIC connection{};
    ODQuicCertificateCallback certificateCallback{};
    ODQuicEventCallback eventCallback{};
    ODQuicDisconnectedCallback disconnectedCallback{};
    void* callbackContext{};
    std::mutex mutex;
    std::condition_variable signal;
    bool connected{};
    bool shutdownComplete{};
    bool closing{};
    std::string connectError;
};

void SetError(char* buffer, size_t length, const std::string& message) {
    if (buffer == nullptr || length == 0) return;
    const auto bytes = std::min(length - 1, message.size());
    std::memcpy(buffer, message.data(), bytes);
    buffer[bytes] = '\0';
}

bool AppendFrame(
    std::vector<uint8_t>& destination,
    bool& lengthKnown,
    size_t& expectedPayloadBytes,
    uint32_t bufferCount,
    const QUIC_BUFFER* buffers,
    std::string& error) {
    for (uint32_t index = 0; index < bufferCount; ++index) {
        const auto& buffer = buffers[index];
        if (destination.size() + buffer.Length > kMaximumFrameBytes + sizeof(uint32_t)) {
            error = "GatewayFrameTooLarge";
            return false;
        }
        destination.insert(destination.end(), buffer.Buffer, buffer.Buffer + buffer.Length);
    }
    if (!lengthKnown && destination.size() >= sizeof(uint32_t)) {
        expectedPayloadBytes =
            (static_cast<size_t>(destination[0]) << 24U) |
            (static_cast<size_t>(destination[1]) << 16U) |
            (static_cast<size_t>(destination[2]) << 8U) |
            static_cast<size_t>(destination[3]);
        lengthKnown = true;
        if (expectedPayloadBytes == 0 || expectedPayloadBytes > kMaximumFrameBytes) {
            error = "GatewayFrameTooLarge";
            return false;
        }
    }
    return lengthKnown && destination.size() >= expectedPayloadBytes + sizeof(uint32_t);
}

std::vector<uint8_t> Frame(const uint8_t* payload, size_t length) {
    std::vector<uint8_t> frame(length + sizeof(uint32_t));
    frame[0] = static_cast<uint8_t>((length >> 24U) & 0xffU);
    frame[1] = static_cast<uint8_t>((length >> 16U) & 0xffU);
    frame[2] = static_cast<uint8_t>((length >> 8U) & 0xffU);
    frame[3] = static_cast<uint8_t>(length & 0xffU);
    std::copy(payload, payload + length, frame.begin() + sizeof(uint32_t));
    return frame;
}

QUIC_STATUS QUIC_API RequestStreamCallback(HQUIC, void* context, QUIC_STREAM_EVENT* event) {
    auto* request = static_cast<RequestContext*>(context);
    switch (event->Type) {
        case QUIC_STREAM_EVENT_SEND_COMPLETE:
            delete static_cast<SendContext*>(event->SEND_COMPLETE.ClientContext);
            break;
        case QUIC_STREAM_EVENT_RECEIVE: {
            std::scoped_lock lock(request->mutex);
            const auto complete = AppendFrame(
                request->received,
                request->lengthKnown,
                request->expectedPayloadBytes,
                event->RECEIVE.BufferCount,
                event->RECEIVE.Buffers,
                request->error);
            if (complete || !request->error.empty()) {
                request->completed = true;
                request->signal.notify_all();
            }
            break;
        }
        case QUIC_STREAM_EVENT_PEER_SEND_ABORTED:
        case QUIC_STREAM_EVENT_PEER_RECEIVE_ABORTED: {
            std::scoped_lock lock(request->mutex);
            request->error = "GatewayStreamAborted";
            request->completed = true;
            request->signal.notify_all();
            break;
        }
        case QUIC_STREAM_EVENT_PEER_SEND_SHUTDOWN: {
            std::scoped_lock lock(request->mutex);
            if (!request->completed) {
                request->error = "GatewayFrameTruncated";
                request->completed = true;
                request->signal.notify_all();
            }
            break;
        }
        case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE: {
            std::scoped_lock lock(request->mutex);
            request->shutdownComplete = true;
            request->signal.notify_all();
            break;
        }
        default:
            break;
    }
    return QUIC_STATUS_SUCCESS;
}

QUIC_STATUS QUIC_API EventStreamCallback(HQUIC stream, void* context, QUIC_STREAM_EVENT* event) {
    auto* eventStream = static_cast<EventStreamContext*>(context);
    switch (event->Type) {
        case QUIC_STREAM_EVENT_RECEIVE: {
            std::string error;
            const auto complete = AppendFrame(
                eventStream->received,
                eventStream->lengthKnown,
                eventStream->expectedPayloadBytes,
                event->RECEIVE.BufferCount,
                event->RECEIVE.Buffers,
                error);
            if (complete && !eventStream->delivered) {
                eventStream->delivered = true;
                if (eventStream->client->eventCallback != nullptr) {
                    eventStream->client->eventCallback(
                        eventStream->received.data() + sizeof(uint32_t),
                        eventStream->expectedPayloadBytes,
                        eventStream->client->callbackContext);
                }
            } else if (!error.empty()) {
                eventStream->client->api->StreamShutdown(
                    stream,
                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT_RECEIVE,
                    0x102);
            }
            break;
        }
        case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE:
            eventStream->client->api->StreamClose(stream);
            delete eventStream;
            break;
        default:
            break;
    }
    return QUIC_STATUS_SUCCESS;
}

QUIC_STATUS QUIC_API ConnectionCallback(HQUIC, void* context, QUIC_CONNECTION_EVENT* event) {
    auto* client = static_cast<ClientContext*>(context);
    switch (event->Type) {
        case QUIC_CONNECTION_EVENT_PEER_CERTIFICATE_RECEIVED: {
            const auto* certificate = reinterpret_cast<const QUIC_BUFFER*>(
                event->PEER_CERTIFICATE_RECEIVED.Certificate);
            const auto accepted = certificate != nullptr && certificate->Buffer != nullptr &&
                certificate->Length > 0 && client->certificateCallback != nullptr &&
                client->certificateCallback(
                    certificate->Buffer,
                    certificate->Length,
                    client->callbackContext);
            return accepted ? QUIC_STATUS_SUCCESS : QUIC_STATUS_BAD_CERTIFICATE;
        }
        case QUIC_CONNECTION_EVENT_CONNECTED: {
            std::scoped_lock lock(client->mutex);
            client->connected = true;
            client->signal.notify_all();
            break;
        }
        case QUIC_CONNECTION_EVENT_PEER_STREAM_STARTED: {
            auto* streamContext = new EventStreamContext{client};
            client->api->SetCallbackHandler(
                event->PEER_STREAM_STARTED.Stream,
                reinterpret_cast<void*>(EventStreamCallback),
                streamContext);
            break;
        }
        case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_TRANSPORT: {
            std::scoped_lock lock(client->mutex);
            client->connectError = "QUIC transport error: " +
                std::to_string(event->SHUTDOWN_INITIATED_BY_TRANSPORT.Status);
            client->signal.notify_all();
            break;
        }
        case QUIC_CONNECTION_EVENT_SHUTDOWN_INITIATED_BY_PEER: {
            std::scoped_lock lock(client->mutex);
            client->connectError = "Desktop closed QUIC connection: " +
                std::to_string(event->SHUTDOWN_INITIATED_BY_PEER.ErrorCode);
            client->signal.notify_all();
            break;
        }
        case QUIC_CONNECTION_EVENT_SHUTDOWN_COMPLETE: {
            std::string reason;
            bool notify = false;
            {
                std::scoped_lock lock(client->mutex);
                client->shutdownComplete = true;
                client->connected = false;
                reason = client->connectError.empty() ? "QUIC connection closed" : client->connectError;
                notify = !client->closing;
                client->signal.notify_all();
            }
            if (notify && client->disconnectedCallback != nullptr) {
                client->disconnectedCallback(reason.c_str(), client->callbackContext);
            }
            break;
        }
        default:
            break;
    }
    return QUIC_STATUS_SUCCESS;
}

void DestroyClient(ClientContext* client) {
    if (client == nullptr) return;
    {
        std::scoped_lock lock(client->mutex);
        client->closing = true;
    }
    if (client->connection != nullptr) {
        client->api->ConnectionShutdown(client->connection, QUIC_CONNECTION_SHUTDOWN_FLAG_SILENT, 0);
        std::unique_lock lock(client->mutex);
        client->signal.wait_for(lock, std::chrono::seconds(2), [&] { return client->shutdownComplete; });
        lock.unlock();
        client->api->ConnectionClose(client->connection);
    }
    if (client->configuration != nullptr) client->api->ConfigurationClose(client->configuration);
    if (client->registration != nullptr) client->api->RegistrationClose(client->registration);
    if (client->api != nullptr) MsQuicClose(client->api);
    delete client;
}

} // namespace

extern "C" ODQuicHandle ODQuicConnect(
    const char* host,
    uint16_t port,
    uint32_t timeoutMilliseconds,
    ODQuicCertificateCallback certificateCallback,
    ODQuicEventCallback eventCallback,
    ODQuicDisconnectedCallback disconnectedCallback,
    void* callbackContext,
    char* errorBuffer,
    size_t errorBufferLength) {
    if (host == nullptr || *host == '\0' || certificateCallback == nullptr) {
        SetError(errorBuffer, errorBufferLength, "InvalidConnectionArguments");
        return nullptr;
    }

    auto client = std::make_unique<ClientContext>();
    client->certificateCallback = certificateCallback;
    client->eventCallback = eventCallback;
    client->disconnectedCallback = disconnectedCallback;
    client->callbackContext = callbackContext;

    auto status = MsQuicOpen2(&client->api);
    if (QUIC_FAILED(status)) {
        SetError(errorBuffer, errorBufferLength, "MsQuicOpenFailed: " + std::to_string(status));
        return nullptr;
    }

    const QUIC_REGISTRATION_CONFIG registrationConfig{"OneDesk iOS", QUIC_EXECUTION_PROFILE_LOW_LATENCY};
    status = client->api->RegistrationOpen(&registrationConfig, &client->registration);
    if (QUIC_FAILED(status)) {
        SetError(errorBuffer, errorBufferLength, "MsQuicRegistrationFailed: " + std::to_string(status));
        DestroyClient(client.release());
        return nullptr;
    }

    const uint8_t alpnBytes[] = {'o', 'n', 'e', 'd', 'e', 's', 'k', '/', '1'};
    const QUIC_BUFFER alpn{sizeof(alpnBytes), const_cast<uint8_t*>(alpnBytes)};
    QUIC_SETTINGS settings{};
    settings.IdleTimeoutMs = 120000;
    settings.IsSet.IdleTimeoutMs = TRUE;
    settings.PeerUnidiStreamCount = 128;
    settings.IsSet.PeerUnidiStreamCount = TRUE;
    settings.PeerBidiStreamCount = 16;
    settings.IsSet.PeerBidiStreamCount = TRUE;
    status = client->api->ConfigurationOpen(
        client->registration,
        &alpn,
        1,
        &settings,
        sizeof(settings),
        nullptr,
        &client->configuration);
    if (QUIC_FAILED(status)) {
        SetError(errorBuffer, errorBufferLength, "MsQuicConfigurationFailed: " + std::to_string(status));
        DestroyClient(client.release());
        return nullptr;
    }

    QUIC_CREDENTIAL_CONFIG credentials{};
    credentials.Type = QUIC_CREDENTIAL_TYPE_NONE;
    credentials.Flags = static_cast<QUIC_CREDENTIAL_FLAGS>(
        QUIC_CREDENTIAL_FLAG_CLIENT |
        QUIC_CREDENTIAL_FLAG_NO_CERTIFICATE_VALIDATION |
        QUIC_CREDENTIAL_FLAG_INDICATE_CERTIFICATE_RECEIVED |
        QUIC_CREDENTIAL_FLAG_USE_PORTABLE_CERTIFICATES);
    status = client->api->ConfigurationLoadCredential(client->configuration, &credentials);
    if (QUIC_FAILED(status)) {
        SetError(errorBuffer, errorBufferLength, "MsQuicCredentialFailed: " + std::to_string(status));
        DestroyClient(client.release());
        return nullptr;
    }

    status = client->api->ConnectionOpen(client->registration, ConnectionCallback, client.get(), &client->connection);
    if (QUIC_FAILED(status)) {
        SetError(errorBuffer, errorBufferLength, "MsQuicConnectionOpenFailed: " + std::to_string(status));
        DestroyClient(client.release());
        return nullptr;
    }
    status = client->api->ConnectionStart(
        client->connection,
        client->configuration,
        QUIC_ADDRESS_FAMILY_UNSPEC,
        host,
        port);
    if (QUIC_FAILED(status)) {
        SetError(errorBuffer, errorBufferLength, "MsQuicConnectionStartFailed: " + std::to_string(status));
        DestroyClient(client.release());
        return nullptr;
    }

    std::unique_lock lock(client->mutex);
    const auto signaled = client->signal.wait_for(
        lock,
        std::chrono::milliseconds(std::max<uint32_t>(1000, timeoutMilliseconds)),
        [&] { return client->connected || client->shutdownComplete || !client->connectError.empty(); });
    const auto success = signaled && client->connected;
    const auto error = client->connectError;
    lock.unlock();
    if (!success) {
        SetError(errorBuffer, errorBufferLength, error.empty() ? "连接桌面端超时" : error);
        DestroyClient(client.release());
        return nullptr;
    }
    return client.release();
}

extern "C" bool ODQuicRequest(
    ODQuicHandle handle,
    const uint8_t* payload,
    size_t payloadLength,
    uint32_t timeoutMilliseconds,
    uint8_t** response,
    size_t* responseLength,
    char* errorBuffer,
    size_t errorBufferLength) {
    auto* client = static_cast<ClientContext*>(handle);
    if (client == nullptr || !client->connected || payload == nullptr || response == nullptr || responseLength == nullptr) {
        SetError(errorBuffer, errorBufferLength, "GatewaySessionOffline");
        return false;
    }
    if (payloadLength == 0 || payloadLength > kMaximumFrameBytes) {
        SetError(errorBuffer, errorBufferLength, "GatewayFrameTooLarge");
        return false;
    }

    auto request = std::make_unique<RequestContext>();
    auto status = client->api->StreamOpen(
        client->connection,
        QUIC_STREAM_OPEN_FLAG_NONE,
        RequestStreamCallback,
        request.get(),
        &request->stream);
    if (QUIC_FAILED(status)) {
        SetError(errorBuffer, errorBufferLength, "GatewayStreamOpenFailed: " + std::to_string(status));
        return false;
    }
    status = client->api->StreamStart(request->stream, QUIC_STREAM_START_FLAG_FAIL_BLOCKED);
    if (QUIC_FAILED(status)) {
        client->api->StreamClose(request->stream);
        SetError(errorBuffer, errorBufferLength, "GatewayStreamStartFailed: " + std::to_string(status));
        return false;
    }

    auto* send = new SendContext(Frame(payload, payloadLength));
    status = client->api->StreamSend(request->stream, &send->buffer, 1, QUIC_SEND_FLAG_FIN, send);
    if (QUIC_FAILED(status)) {
        delete send;
        client->api->StreamShutdown(
            request->stream,
            static_cast<QUIC_STREAM_SHUTDOWN_FLAGS>(
                QUIC_STREAM_SHUTDOWN_FLAG_ABORT_SEND | QUIC_STREAM_SHUTDOWN_FLAG_ABORT_RECEIVE),
            0x103);
        client->api->StreamClose(request->stream);
        SetError(errorBuffer, errorBufferLength, "GatewayStreamSendFailed: " + std::to_string(status));
        return false;
    }

    std::unique_lock lock(request->mutex);
    const auto received = request->signal.wait_for(
        lock,
        std::chrono::milliseconds(std::max<uint32_t>(1000, timeoutMilliseconds)),
        [&] { return request->completed; });
    if (!received) request->error = "连接桌面端超时";
    const auto error = request->error;
    std::vector<uint8_t> result;
    if (error.empty() && request->lengthKnown) {
        result.assign(
            request->received.begin() + sizeof(uint32_t),
            request->received.begin() + sizeof(uint32_t) + request->expectedPayloadBytes);
    }
    lock.unlock();

    client->api->StreamShutdown(
        request->stream,
        static_cast<QUIC_STREAM_SHUTDOWN_FLAGS>(
            QUIC_STREAM_SHUTDOWN_FLAG_ABORT_SEND | QUIC_STREAM_SHUTDOWN_FLAG_ABORT_RECEIVE),
        0);
    lock.lock();
    request->signal.wait_for(lock, std::chrono::seconds(2), [&] { return request->shutdownComplete; });
    lock.unlock();
    client->api->StreamClose(request->stream);

    if (!error.empty()) {
        SetError(errorBuffer, errorBufferLength, error);
        return false;
    }
    auto* copy = static_cast<uint8_t*>(std::malloc(result.size()));
    if (copy == nullptr && !result.empty()) {
        SetError(errorBuffer, errorBufferLength, "ResponseAllocationFailed");
        return false;
    }
    std::memcpy(copy, result.data(), result.size());
    *response = copy;
    *responseLength = result.size();
    return true;
}

extern "C" void ODQuicFreeBuffer(uint8_t* buffer) {
    std::free(buffer);
}

extern "C" void ODQuicClose(ODQuicHandle handle) {
    DestroyClient(static_cast<ClientContext*>(handle));
}
