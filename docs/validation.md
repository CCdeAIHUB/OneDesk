# Validation

Routine validation targets:

- Current Codex system environment.
- Android.

Local validation:

```powershell
pnpm install
pnpm build
dotnet build apps/desktop/OneDesk.Desktop.csproj
```

Android validation is currently performed by GitHub Actions because the current Codex Windows environment does not provide Java/Gradle.
