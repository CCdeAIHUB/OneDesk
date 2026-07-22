# Validation

Routine validation targets:

- Current Codex system environment.
- Android.

Local validation:

```powershell
pnpm install
pnpm build
dotnet build apps/desktop/OneDesk.Desktop.csproj
dotnet build apps/desktop-windows/OneDesk.Windows.csproj
dotnet publish apps/desktop-windows/OneDesk.Windows.csproj --configuration Release --runtime win-x64 --self-contained true --output artifacts/windows/win-x64
$env:JAVA_HOME="C:\Program Files\Android\Android Studio\jbr"
$env:ANDROID_HOME="$env:LOCALAPPDATA\Android\Sdk"
Push-Location apps/mobile/android
.\gradlew.bat :app:testDebugUnitTest :app:assembleDebug --no-daemon
Pop-Location
```

The current Codex Windows environment has a local JDK and Android SDK under `%LOCALAPPDATA%\OneDeskBuildTools`, so Android must be built locally. When an ADB device is available, install the APK and verify the connection screen, QR scanner, empty-scheme state, scheme push, full-screen rendering, and orientation behavior on the real device.

## Latest Verified Artifacts

- Windows x64 executable: `artifacts/windows/win-x64/OneDesk.exe`
- Android debug APK: `apps/mobile/android/app/build/outputs/apk/debug/app-debug.apk`
- 2026-07-22 local validation: 42 .NET contract/integration tests passed; desktop Vue production build passed; Android JVM tests and APK build passed; Windows x64 self-contained publish passed. This validation includes concurrent desktop JSON read/write and Android atomic media-cache replacement regressions.
- 2026-07-18 local validation: 39 .NET contract/integration tests passed; both Vue frontends built; Windows WinForms and generic Avalonia desktop shells built; Android JVM tests and APK build passed. The APK contains MsQuic/JNI libraries for `arm64-v8a` and `x86_64`.
- iOS is source- and project-contract validated on Windows and is built as an unsigned Simulator application by the macOS release workflow. It is not reported as locally compiled on Windows.
- 2026-07-17 real-device validation on `22041211AC`: native QR scanner opened, an unassigned device displayed the empty-scheme page, the desktop applied `scheme-live-console`, Android acknowledged the push and rendered the assigned 3 x 4 page in landscape immediately.
