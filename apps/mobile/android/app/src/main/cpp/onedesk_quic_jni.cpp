#include <jni.h>
#include <android/log.h>
#include <msquic.h>

#include <algorithm>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <memory>
#include <mutex>
#include <string>
#include <vector>

namespace {

constexpr size_t kMaximumFrameBytes = 16 * 1024 * 1024;
constexpr char kLogTag[] = "OneDeskQuic";

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
    ClientContext* client{};
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
    JavaVM* vm{};
    jobject owner{};
    jmethodID validateCertificateMethod{};
    jmethodID eventMethod{};
    jmethodID disconnectedMethod{};
    std::mutex mutex;
    std::condition_variable signal;
    bool connected{};
    bool shutdownComplete{};
    bool closing{};
    std::string connectError;
};

void ThrowIllegalState(JNIEnv* env, const std::string& message) {
    const auto exceptionClass = env->FindClass("java/lang/IllegalStateException");
    if (exceptionClass != nullptr) {
        env->ThrowNew(exceptionClass, message.c_str());
    }
}

JNIEnv* Attach(ClientContext* client, bool& attached) {
    attached = false;
    JNIEnv* env = nullptr;
    if (client->vm->GetEnv(reinterpret_cast<void**>(&env), JNI_VERSION_1_6) == JNI_OK) {
        return env;
    }
    if (client->vm->AttachCurrentThread(&env, nullptr) != JNI_OK) {
        return nullptr;
    }
    attached = true;
    return env;
}

void Detach(ClientContext* client, bool attached) {
    if (attached) {
        client->vm->DetachCurrentThread();
    }
}

bool ValidateServerCertificate(ClientContext* client, const QUIC_BUFFER* certificate) {
    if (certificate == nullptr || certificate->Buffer == nullptr || certificate->Length == 0) {
        return false;
    }
    bool attached = false;
    JNIEnv* env = Attach(client, attached);
    if (env == nullptr) {
        return false;
    }
    auto bytes = env->NewByteArray(static_cast<jsize>(certificate->Length));
    env->SetByteArrayRegion(
        bytes,
        0,
        static_cast<jsize>(certificate->Length),
        reinterpret_cast<const jbyte*>(certificate->Buffer));
    const auto accepted = env->CallBooleanMethod(client->owner, client->validateCertificateMethod, bytes) == JNI_TRUE;
    env->DeleteLocalRef(bytes);
    if (env->ExceptionCheck()) {
        env->ExceptionClear();
        Detach(client, attached);
        return false;
    }
    Detach(client, attached);
    return accepted;
}

void NotifyEvent(ClientContext* client, const uint8_t* bytes, size_t length) {
    bool attached = false;
    JNIEnv* env = Attach(client, attached);
    if (env == nullptr) {
        return;
    }
    auto payload = env->NewByteArray(static_cast<jsize>(length));
    env->SetByteArrayRegion(payload, 0, static_cast<jsize>(length), reinterpret_cast<const jbyte*>(bytes));
    env->CallVoidMethod(client->owner, client->eventMethod, payload);
    env->DeleteLocalRef(payload);
    if (env->ExceptionCheck()) {
        env->ExceptionDescribe();
        env->ExceptionClear();
    }
    Detach(client, attached);
}

void NotifyDisconnected(ClientContext* client, const std::string& reason) {
    bool attached = false;
    JNIEnv* env = Attach(client, attached);
    if (env == nullptr) {
        return;
    }
    auto message = env->NewStringUTF(reason.c_str());
    env->CallVoidMethod(client->owner, client->disconnectedMethod, message);
    env->DeleteLocalRef(message);
    if (env->ExceptionCheck()) {
        env->ExceptionDescribe();
        env->ExceptionClear();
    }
    Detach(client, attached);
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

QUIC_STATUS QUIC_API RequestStreamCallback(HQUIC stream, void* context, QUIC_STREAM_EVENT* event) {
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
                NotifyEvent(
                    eventStream->client,
                    eventStream->received.data() + sizeof(uint32_t),
                    eventStream->expectedPayloadBytes);
            } else if (!error.empty()) {
                __android_log_print(ANDROID_LOG_ERROR, kLogTag, "Server event frame rejected: %s", error.c_str());
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

QUIC_STATUS QUIC_API ConnectionCallback(HQUIC connection, void* context, QUIC_CONNECTION_EVENT* event) {
    auto* client = static_cast<ClientContext*>(context);
    switch (event->Type) {
        case QUIC_CONNECTION_EVENT_PEER_CERTIFICATE_RECEIVED:
            return ValidateServerCertificate(
                client,
                reinterpret_cast<const QUIC_BUFFER*>(event->PEER_CERTIFICATE_RECEIVED.Certificate))
                ? QUIC_STATUS_SUCCESS
                : QUIC_STATUS_BAD_CERTIFICATE;
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
            if (notify) {
                NotifyDisconnected(client, reason);
            }
            break;
        }
        default:
            break;
    }
    return QUIC_STATUS_SUCCESS;
}

void DestroyClient(JNIEnv* env, ClientContext* client) {
    if (client == nullptr) {
        return;
    }
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
    if (client->configuration != nullptr) {
        client->api->ConfigurationClose(client->configuration);
    }
    if (client->registration != nullptr) {
        client->api->RegistrationClose(client->registration);
    }
    if (client->api != nullptr) {
        MsQuicClose(client->api);
    }
    if (client->owner != nullptr) {
        env->DeleteGlobalRef(client->owner);
    }
    delete client;
}

} // namespace

extern "C" JNIEXPORT jlong JNICALL
Java_cc_onedesk_mobile_MsQuicNativeTransport_nativeConnect(
    JNIEnv* env,
    jobject owner,
    jstring host,
    jint port,
    jint timeoutMilliseconds) {
    auto client = std::make_unique<ClientContext>();
    env->GetJavaVM(&client->vm);
    client->owner = env->NewGlobalRef(owner);
    const auto ownerClass = env->GetObjectClass(owner);
    client->validateCertificateMethod = env->GetMethodID(ownerClass, "validateServerCertificate", "([B)Z");
    client->eventMethod = env->GetMethodID(ownerClass, "onNativeEvent", "([B)V");
    client->disconnectedMethod = env->GetMethodID(ownerClass, "onNativeDisconnected", "(Ljava/lang/String;)V");
    env->DeleteLocalRef(ownerClass);
    if (client->validateCertificateMethod == nullptr || client->eventMethod == nullptr || client->disconnectedMethod == nullptr) {
        DestroyClient(env, client.release());
        ThrowIllegalState(env, "MsQuicCallbackBindingFailed");
        return 0;
    }

    QUIC_STATUS status = MsQuicOpen2(&client->api);
    if (QUIC_FAILED(status)) {
        DestroyClient(env, client.release());
        ThrowIllegalState(env, "MsQuicOpenFailed: " + std::to_string(status));
        return 0;
    }

    const QUIC_REGISTRATION_CONFIG registrationConfig{"OneDesk Android", QUIC_EXECUTION_PROFILE_LOW_LATENCY};
    status = client->api->RegistrationOpen(&registrationConfig, &client->registration);
    if (QUIC_FAILED(status)) {
        DestroyClient(env, client.release());
        ThrowIllegalState(env, "MsQuicRegistrationFailed: " + std::to_string(status));
        return 0;
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
        DestroyClient(env, client.release());
        ThrowIllegalState(env, "MsQuicConfigurationFailed: " + std::to_string(status));
        return 0;
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
        DestroyClient(env, client.release());
        ThrowIllegalState(env, "MsQuicCredentialFailed: " + std::to_string(status));
        return 0;
    }

    status = client->api->ConnectionOpen(client->registration, ConnectionCallback, client.get(), &client->connection);
    if (QUIC_FAILED(status)) {
        DestroyClient(env, client.release());
        ThrowIllegalState(env, "MsQuicConnectionOpenFailed: " + std::to_string(status));
        return 0;
    }

    const char* hostChars = env->GetStringUTFChars(host, nullptr);
    status = client->api->ConnectionStart(
        client->connection,
        client->configuration,
        QUIC_ADDRESS_FAMILY_UNSPEC,
        hostChars,
        static_cast<uint16_t>(port));
    env->ReleaseStringUTFChars(host, hostChars);
    if (QUIC_FAILED(status)) {
        DestroyClient(env, client.release());
        ThrowIllegalState(env, "MsQuicConnectionStartFailed: " + std::to_string(status));
        return 0;
    }

    std::unique_lock lock(client->mutex);
    const auto connected = client->signal.wait_for(
        lock,
        std::chrono::milliseconds(std::max(1000, timeoutMilliseconds)),
        [&] { return client->connected || client->shutdownComplete || !client->connectError.empty(); });
    const auto error = client->connectError;
    const auto success = connected && client->connected;
    lock.unlock();
    if (!success) {
        DestroyClient(env, client.release());
        ThrowIllegalState(env, error.empty() ? "连接桌面端超时" : error);
        return 0;
    }
    return reinterpret_cast<jlong>(client.release());
}

extern "C" JNIEXPORT jbyteArray JNICALL
Java_cc_onedesk_mobile_MsQuicNativeTransport_nativeRequest(
    JNIEnv* env,
    jobject,
    jlong handle,
    jbyteArray payload,
    jint timeoutMilliseconds) {
    auto* client = reinterpret_cast<ClientContext*>(handle);
    if (client == nullptr || !client->connected) {
        ThrowIllegalState(env, "GatewaySessionOffline");
        return nullptr;
    }
    const auto length = static_cast<size_t>(env->GetArrayLength(payload));
    if (length == 0 || length > kMaximumFrameBytes) {
        ThrowIllegalState(env, "GatewayFrameTooLarge");
        return nullptr;
    }
    std::vector<uint8_t> payloadBytes(length);
    env->GetByteArrayRegion(payload, 0, static_cast<jsize>(length), reinterpret_cast<jbyte*>(payloadBytes.data()));

    auto request = std::make_unique<RequestContext>();
    request->client = client;
    QUIC_STATUS status = client->api->StreamOpen(
        client->connection,
        QUIC_STREAM_OPEN_FLAG_NONE,
        RequestStreamCallback,
        request.get(),
        &request->stream);
    if (QUIC_FAILED(status)) {
        ThrowIllegalState(env, "GatewayStreamOpenFailed: " + std::to_string(status));
        return nullptr;
    }
    status = client->api->StreamStart(request->stream, QUIC_STREAM_START_FLAG_FAIL_BLOCKED);
    if (QUIC_FAILED(status)) {
        client->api->StreamClose(request->stream);
        ThrowIllegalState(env, "GatewayStreamStartFailed: " + std::to_string(status));
        return nullptr;
    }

    auto* send = new SendContext(Frame(payloadBytes.data(), payloadBytes.size()));
    status = client->api->StreamSend(request->stream, &send->buffer, 1, QUIC_SEND_FLAG_FIN, send);
    if (QUIC_FAILED(status)) {
        delete send;
        client->api->StreamShutdown(
            request->stream,
            static_cast<QUIC_STREAM_SHUTDOWN_FLAGS>(
                QUIC_STREAM_SHUTDOWN_FLAG_ABORT_SEND | QUIC_STREAM_SHUTDOWN_FLAG_ABORT_RECEIVE),
            0x103);
        client->api->StreamClose(request->stream);
        ThrowIllegalState(env, "GatewayStreamSendFailed: " + std::to_string(status));
        return nullptr;
    }

    std::unique_lock lock(request->mutex);
    const auto received = request->signal.wait_for(
        lock,
        std::chrono::milliseconds(std::max(1000, timeoutMilliseconds)),
        [&] { return request->completed; });
    if (!received) {
        request->error = "连接桌面端超时";
    }
    const auto error = request->error;
    std::vector<uint8_t> response;
    if (error.empty() && request->lengthKnown) {
        response.assign(
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
        ThrowIllegalState(env, error);
        return nullptr;
    }
    auto result = env->NewByteArray(static_cast<jsize>(response.size()));
    env->SetByteArrayRegion(result, 0, static_cast<jsize>(response.size()), reinterpret_cast<const jbyte*>(response.data()));
    return result;
}

extern "C" JNIEXPORT void JNICALL
Java_cc_onedesk_mobile_MsQuicNativeTransport_nativeClose(
    JNIEnv* env,
    jobject,
    jlong handle) {
    DestroyClient(env, reinterpret_cast<ClientContext*>(handle));
}
