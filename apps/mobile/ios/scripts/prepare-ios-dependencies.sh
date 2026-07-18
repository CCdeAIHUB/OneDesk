#!/usr/bin/env bash
set -euo pipefail

IOS_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REPO_ROOT="$(cd "${IOS_ROOT}/../../.." && pwd)"
PLATFORM_NAME="${PLATFORM_NAME:-iphoneos}"
NATIVE_ARCH="${NATIVE_ARCH_ACTUAL:-arm64}"

pnpm --dir "${REPO_ROOT}" --filter @onedesk/mobile-ui build

if [[ "${PLATFORM_NAME}" == "iphonesimulator" ]]; then
  if [[ "${NATIVE_ARCH}" == "x86_64" ]]; then
    MSQUIC_PLATFORM="SIMULATOR64"
  else
    MSQUIC_PLATFORM="SIMULATORARM64"
  fi
else
  MSQUIC_PLATFORM="OS64"
fi

BUILD_ROOT="${REPO_ROOT}/build/ios-msquic-${MSQUIC_PLATFORM}"
OUTPUT_ROOT="${IOS_ROOT}/Native/Build/${PLATFORM_NAME}"
cmake -S "${REPO_ROOT}/third_party/msquic" -B "${BUILD_ROOT}" \
  -DCMAKE_TOOLCHAIN_FILE="${REPO_ROOT}/third_party/msquic/cmake/toolchains/ios.cmake" \
  -DPLATFORM="${MSQUIC_PLATFORM}" \
  -DDEPLOYMENT_TARGET=16.0 \
  -DCMAKE_OSX_DEPLOYMENT_TARGET=16.0 \
  -DENABLE_ARC=0 \
  -DQUIC_BUILD_SHARED=OFF \
  -DQUIC_BUILD_TOOLS=OFF \
  -DQUIC_BUILD_TEST=OFF \
  -DQUIC_BUILD_PERF=OFF \
  -DQUIC_ENABLE_LOGGING=OFF \
  -DQUIC_TLS_LIB=quictls \
  -DQUIC_SKIP_CI_CHECKS=ON
cmake --build "${BUILD_ROOT}" --config Release --target msquic --parallel

MSQUIC_LIBRARY="$(find "${BUILD_ROOT}" -name 'libmsquic.a' -type f | head -n 1)"
if [[ -z "${MSQUIC_LIBRARY}" ]]; then
  echo "未找到 iOS MsQuic 静态库" >&2
  exit 1
fi
mkdir -p "${OUTPUT_ROOT}"
cp "${MSQUIC_LIBRARY}" "${OUTPUT_ROOT}/libmsquic.a"
