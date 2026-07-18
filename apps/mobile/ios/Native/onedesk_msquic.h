#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef void* ODQuicHandle;
typedef bool (*ODQuicCertificateCallback)(const uint8_t* bytes, size_t length, void* context);
typedef void (*ODQuicEventCallback)(const uint8_t* bytes, size_t length, void* context);
typedef void (*ODQuicDisconnectedCallback)(const char* reason, void* context);

ODQuicHandle ODQuicConnect(
    const char* host,
    uint16_t port,
    uint32_t timeoutMilliseconds,
    ODQuicCertificateCallback certificateCallback,
    ODQuicEventCallback eventCallback,
    ODQuicDisconnectedCallback disconnectedCallback,
    void* callbackContext,
    char* errorBuffer,
    size_t errorBufferLength);

bool ODQuicRequest(
    ODQuicHandle handle,
    const uint8_t* payload,
    size_t payloadLength,
    uint32_t timeoutMilliseconds,
    uint8_t** response,
    size_t* responseLength,
    char* errorBuffer,
    size_t errorBufferLength);

void ODQuicFreeBuffer(uint8_t* buffer);
void ODQuicClose(ODQuicHandle handle);

#ifdef __cplusplus
}
#endif
