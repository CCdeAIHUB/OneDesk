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
dotnet publish apps/desktop-windows/OneDesk.Windows.csproj --configuration Release --runtime win-x64 --self-contained false --output artifacts/windows/OneDesk
$env:JAVA_HOME="$env:LOCALAPPDATA\OneDeskBuildTools\jdk17\jdk-17.0.19+10"
$env:ANDROID_HOME="$env:LOCALAPPDATA\OneDeskBuildTools\android-sdk"
Push-Location apps/mobile/android
.\gradlew.bat :app:assembleDebug
Pop-Location
```

The current Codex Windows environment has a local JDK and Android SDK under `%LOCALAPPDATA%\OneDeskBuildTools`, so Android must be built locally. When an ADB device is available, install the APK and verify the connection screen, QR scanner, empty-scheme state, scheme push, full-screen rendering, and orientation behavior on the real device.

## Latest Verified Artifacts

- Windows x64 executable: `artifacts/windows/win-x64/OneDesk.exe`
- Android debug APK: `apps/mobile/android/app/build/outputs/apk/debug/app-debug.apk`
- 2026-07-17 real-device validation on `22041211AC`: native QR scanner opened, an unassigned device displayed the empty-scheme page, the desktop applied `scheme-live-console`, Android acknowledged the push and rendered the assigned 3 x 4 page in landscape immediately.
