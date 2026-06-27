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
```

Android validation is currently performed by GitHub Actions because the current Codex Windows environment does not provide Java/Gradle.
