cmake_minimum_required(VERSION 3.22.1)
project(onedesk_mobile_quic LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# MsQuic 固定由仓库子模块提供，Android 构建不依赖系统上不可控的动态库版本。
set(QUIC_BUILD_SHARED ON CACHE BOOL "" FORCE)
set(QUIC_BUILD_TOOLS OFF CACHE BOOL "" FORCE)
set(QUIC_BUILD_TEST OFF CACHE BOOL "" FORCE)
set(QUIC_BUILD_PERF OFF CACHE BOOL "" FORCE)
set(QUIC_ENABLE_LOGGING OFF CACHE BOOL "" FORCE)
set(QUIC_TLS_LIB "quictls" CACHE STRING "" FORCE)
set(QUIC_SKIP_CI_CHECKS ON CACHE BOOL "" FORCE)

get_filename_component(ONEDESK_ROOT "${CMAKE_CURRENT_LIST_DIR}/../../../../../../.." ABSOLUTE)
set(MSQUIC_ROOT "${ONEDESK_ROOT}/third_party/msquic")

if(ANDROID AND CMAKE_HOST_WIN32)
    # MsQuic 官方 Android 脚本只支持 Linux 主机。Windows 本地验证时在构建目录复制源码，
    # 仅修正 OpenSSL 的环境变量、Perl 和 GNU Make 调用，第三方子模块工作区保持原样。
    find_program(ONEDESK_PERL_EXECUTABLE NAMES perl
        HINTS
            "D:/Program Files/Git/usr/bin"
            "C:/Program Files/Git/usr/bin"
            "$ENV{LOCALAPPDATA}/Programs/Git/usr/bin")
    find_program(ONEDESK_MAKE_EXECUTABLE NAMES make
        HINTS "$ENV{LOCALAPPDATA}/Microsoft/WinGet/Links")
    if(NOT ONEDESK_PERL_EXECUTABLE OR NOT ONEDESK_MAKE_EXECUTABLE)
        message(FATAL_ERROR "Windows Android MsQuic build requires Perl and GNU Make")
    endif()

    # OpenSSL Makefile 由 Windows cmd 执行，带空格的 Git Perl 路径无法直接展开。
    # 使用本地构建工具目录中的联接提供无空格路径，不复制或修改 Git 安装内容。
    get_filename_component(ONEDESK_GIT_BIN_DIR "${ONEDESK_PERL_EXECUTABLE}" DIRECTORY)
    get_filename_component(ONEDESK_GIT_USR_DIR "${ONEDESK_GIT_BIN_DIR}" DIRECTORY)
    set(ONEDESK_GIT_USR_LINK "$ENV{LOCALAPPDATA}/OneDeskBuildTools/git-usr")
    if(NOT EXISTS "${ONEDESK_GIT_USR_LINK}/bin/perl.exe")
        file(MAKE_DIRECTORY "$ENV{LOCALAPPDATA}/OneDeskBuildTools")
        file(TO_NATIVE_PATH "${ONEDESK_GIT_USR_LINK}" ONEDESK_GIT_USR_LINK_NATIVE)
        file(TO_NATIVE_PATH "${ONEDESK_GIT_USR_DIR}" ONEDESK_GIT_USR_DIR_NATIVE)
        execute_process(
            COMMAND cmd.exe /c mklink /J "${ONEDESK_GIT_USR_LINK_NATIVE}" "${ONEDESK_GIT_USR_DIR_NATIVE}"
            RESULT_VARIABLE ONEDESK_GIT_LINK_RESULT
            OUTPUT_QUIET
            ERROR_QUIET)
        if(NOT ONEDESK_GIT_LINK_RESULT EQUAL 0)
            message(FATAL_ERROR "Unable to create no-space Git Perl build link")
        endif()
    endif()
    cmake_path(CONVERT "${ONEDESK_GIT_USR_LINK}/bin/perl.exe"
        TO_CMAKE_PATH_LIST ONEDESK_PERL_EXECUTABLE NORMALIZE)
    cmake_path(CONVERT "${ONEDESK_GIT_USR_LINK}/bin/sh.exe"
        TO_CMAKE_PATH_LIST ONEDESK_SHELL_EXECUTABLE NORMALIZE)
    cmake_path(CONVERT "${ONEDESK_GIT_USR_LINK}"
        TO_CMAKE_PATH_LIST ONEDESK_GIT_USR_CMAKE_PATH NORMALIZE)

    set(MSQUIC_BUILD_SOURCE "${CMAKE_CURRENT_BINARY_DIR}/onedesk-msquic-source")
    set(ONEDESK_MSQUIC_PATCH_REVISION "4")
    set(ONEDESK_MSQUIC_PATCH_MARKER "${MSQUIC_BUILD_SOURCE}/.onedesk-source-ready")
    set(ONEDESK_MSQUIC_APPLIED_REVISION "")
    if(EXISTS "${ONEDESK_MSQUIC_PATCH_MARKER}")
        file(READ "${ONEDESK_MSQUIC_PATCH_MARKER}" ONEDESK_MSQUIC_APPLIED_REVISION)
        string(STRIP "${ONEDESK_MSQUIC_APPLIED_REVISION}" ONEDESK_MSQUIC_APPLIED_REVISION)
    endif()
    if(NOT ONEDESK_MSQUIC_APPLIED_REVISION STREQUAL ONEDESK_MSQUIC_PATCH_REVISION)
        file(REMOVE_RECURSE "${MSQUIC_BUILD_SOURCE}")
        file(COPY "${MSQUIC_ROOT}/" DESTINATION "${MSQUIC_BUILD_SOURCE}" PATTERN ".git" EXCLUDE)
        file(COPY "${CMAKE_CURRENT_LIST_DIR}/build-support/"
            DESTINATION "${MSQUIC_BUILD_SOURCE}/submodules/quictls/util/perl")
        set(MSQUIC_OPENSSL_CMAKE "${MSQUIC_BUILD_SOURCE}/submodules/CMakeLists.txt")
        file(READ "${MSQUIC_OPENSSL_CMAKE}" MSQUIC_OPENSSL_CONTENT)

        # Git/MSYS Perl 符合 Android 所需的 Unix 路径语义，但其精简发行版没有
        # IPC::Cmd 的全部依赖。OpenSSL 已提供等价的逐 PATH 扫描分支，直接使用它。
        set(MSQUIC_OPENSSL_CONFIGURE "${MSQUIC_BUILD_SOURCE}/submodules/quictls/Configure")
        file(READ "${MSQUIC_OPENSSL_CONFIGURE}" MSQUIC_OPENSSL_CONFIGURE_CONTENT)
        string(REPLACE
            [[if (eval { require IPC::Cmd; 1; }) {]]
            [[if (0 && eval { require IPC::Cmd; 1; }) {]]
            MSQUIC_OPENSSL_CONFIGURE_CONTENT
            "${MSQUIC_OPENSSL_CONFIGURE_CONTENT}")
        file(WRITE "${MSQUIC_OPENSSL_CONFIGURE}" "${MSQUIC_OPENSSL_CONFIGURE_CONTENT}")

        # OpenSSL 的 Android 配置只按 Unix 分隔符匹配 NDK 工具链；Windows Perl
        # 返回反斜杠路径时会误判 clang 不存在，因此只修正临时构建副本。
        set(MSQUIC_OPENSSL_ANDROID_CONF
            "${MSQUIC_BUILD_SOURCE}/submodules/quictls/Configurations/15-android.conf")
        file(READ "${MSQUIC_OPENSSL_ANDROID_CONF}" MSQUIC_OPENSSL_ANDROID_CONTENT)
        string(REPLACE
            [[$ndk = canonpath($ndk);]]
            [[$ndk = canonpath($ndk);
            $ndk =~ s{\\}{/}g;
            $ndk =~ s{^([A-Za-z]):}{"/" . lc($1)}e if $^O eq "msys";]]
            MSQUIC_OPENSSL_ANDROID_CONTENT
            "${MSQUIC_OPENSSL_ANDROID_CONTENT}")
        string(REPLACE
            [[if (which("clang") =~ m|^$ndk/.*/prebuilt/([^/]+)/|) {]]
            [[my $clang_path = which("clang");
            require Win32 if $^O eq "MSWin32";
            $clang_path = Win32::GetLongPathName($clang_path) if $^O eq "MSWin32";
            $clang_path =~ s{\\}{/}g;
            if ($clang_path =~ m|^$ndk/.*/prebuilt/([^/]+)/|) {]]
            MSQUIC_OPENSSL_ANDROID_CONTENT
            "${MSQUIC_OPENSSL_ANDROID_CONTENT}")
        string(REPLACE
            [[if (which("llvm-ar") =~ m|^$ndk/.*/prebuilt/([^/]+)/|) {]]
            [[my $llvm_ar_path = which("llvm-ar");
                $llvm_ar_path = Win32::GetLongPathName($llvm_ar_path) if $^O eq "MSWin32";
                $llvm_ar_path =~ s{\\}{/}g;
                if ($llvm_ar_path =~ m|^$ndk/.*/prebuilt/([^/]+)/|) {]]
            MSQUIC_OPENSSL_ANDROID_CONTENT
            "${MSQUIC_OPENSSL_ANDROID_CONTENT}")
        file(WRITE "${MSQUIC_OPENSSL_ANDROID_CONF}" "${MSQUIC_OPENSSL_ANDROID_CONTENT}")

        # Android 客户端不生成测试用自签名证书。上游 Linux 源列表默认加入
        # selfsign_openssl.c，但它依赖 Android API 28 才提供的 glob/globfree，
        # 与本项目 minSdk 26 冲突；正常 TLS 证书校验由 certificates_posix.c 保留。
        set(MSQUIC_PLATFORM_CMAKE "${MSQUIC_BUILD_SOURCE}/src/platform/CMakeLists.txt")
        file(READ "${MSQUIC_PLATFORM_CMAKE}" MSQUIC_PLATFORM_CONTENT)
        string(REPLACE
            [[set(SOURCES ${SOURCES} certificates_posix.c selfsign_openssl.c)]]
            [[set(SOURCES ${SOURCES} certificates_posix.c)
        if(NOT ANDROID)
            set(SOURCES ${SOURCES} selfsign_openssl.c)
        endif()]]
            MSQUIC_PLATFORM_CONTENT
            "${MSQUIC_PLATFORM_CONTENT}")
        file(WRITE "${MSQUIC_PLATFORM_CMAKE}" "${MSQUIC_PLATFORM_CONTENT}")

        set(ONEDESK_NDK_TOOLCHAIN_BIN "${ANDROID_NDK}/toolchains/llvm/prebuilt/windows-x86_64/bin")
        cmake_path(CONVERT "$ENV{PATH}" TO_CMAKE_PATH_LIST ONEDESK_HOST_PATH NORMALIZE)
        set(ONEDESK_OPENSSL_PATH
            "${ONEDESK_NDK_TOOLCHAIN_BIN};${ONEDESK_GIT_USR_CMAKE_PATH}/bin;${ONEDESK_HOST_PATH}")
        string(SUBSTRING "${MSQUIC_BUILD_SOURCE}" 0 1 ONEDESK_MSQUIC_DRIVE)
        string(TOLOWER "${ONEDESK_MSQUIC_DRIVE}" ONEDESK_MSQUIC_DRIVE)
        string(SUBSTRING "${MSQUIC_BUILD_SOURCE}" 2 -1 ONEDESK_MSQUIC_POSIX_TAIL)
        set(ONEDESK_MSQUIC_PERL_LIB
            "/${ONEDESK_MSQUIC_DRIVE}${ONEDESK_MSQUIC_POSIX_TAIL}/submodules/quictls/util/perl")
        string(REPLACE
            "COMMAND SYSTEM=\${CMAKE_HOST_SYSTEM_NAME}\n            \${OPENSSL_CONFIG_CMD} \${OPENSSL_CONFIG_FLAGS}"
            "COMMAND \"\${CMAKE_COMMAND}\" -E env \"SYSTEM=\${CMAKE_HOST_SYSTEM_NAME}\" \"ANDROID_NDK_ROOT=\${ANDROID_NDK}\" \"ANDROID_NDK_HOME=\${ANDROID_NDK}\" \"PATH=${ONEDESK_OPENSSL_PATH}\" \"PERL5LIB=${ONEDESK_MSQUIC_PERL_LIB}\" \"${ONEDESK_PERL_EXECUTABLE}\" \${OPENSSL_CONFIG_CMD} \${OPENSSL_CONFIG_FLAGS}"
            MSQUIC_OPENSSL_CONTENT
            "${MSQUIC_OPENSSL_CONTENT}")
        string(REPLACE
            "COMMAND make install_dev -j\${NPROCS}"
            "COMMAND \"\${CMAKE_COMMAND}\" -E env \"ANDROID_NDK_ROOT=\${ANDROID_NDK}\" \"ANDROID_NDK_HOME=\${ANDROID_NDK}\" \"PATH=${ONEDESK_OPENSSL_PATH}\" \"${ONEDESK_MAKE_EXECUTABLE}\" \"PERL=${ONEDESK_PERL_EXECUTABLE} -I${ONEDESK_MSQUIC_PERL_LIB}\" \"SHELL=${ONEDESK_SHELL_EXECUTABLE}\" install_dev -j\${NPROCS}"
            MSQUIC_OPENSSL_CONTENT
            "${MSQUIC_OPENSSL_CONTENT}")
        file(WRITE "${MSQUIC_OPENSSL_CMAKE}" "${MSQUIC_OPENSSL_CONTENT}")
        file(WRITE "${ONEDESK_MSQUIC_PATCH_MARKER}" "${ONEDESK_MSQUIC_PATCH_REVISION}\n")
    endif()
    set(MSQUIC_CMAKE_SOURCE "${MSQUIC_BUILD_SOURCE}")
else()
    set(MSQUIC_CMAKE_SOURCE "${MSQUIC_ROOT}")
endif()

add_subdirectory("${MSQUIC_CMAKE_SOURCE}" "${CMAKE_CURRENT_BINARY_DIR}/msquic")

add_library(onedesk_quic SHARED onedesk_quic_jni.cpp)
target_include_directories(onedesk_quic PRIVATE "${MSQUIC_CMAKE_SOURCE}/src/inc")
target_link_libraries(onedesk_quic PRIVATE msquic log android)
