# Contributing

Thank you for helping improve QHH Desktop Storage Box.

## Requirements

- Windows 10 or Windows 11 x64
- .NET SDK selected by `global.json`
- Inno Setup 6 only for producing installer packages

## Local checks

```powershell
dotnet restore QHHDesktopStorageBox.sln
dotnet build QHHDesktopStorageBox.sln --configuration Release --no-restore
dotnet test QHHDesktopStorageBox.sln --configuration Release --no-build
```

## Architecture rules

- Keep the UI native WPF.
- Keep all file moves, deletes, renames, and restore operations in Core services.
- Do not block the UI thread with file scans, SQLite writes, icon extraction, or
  thumbnail generation.
- Mapping and smart boxes must never move, copy, or delete source files.
- Add focused tests for file moves, name conflicts, deletion, persistence, search,
  and update verification changes.

## Pull requests

Keep changes scoped, explain the regression risk, and list the exact verification
commands that were run. UI changes should remain readable at 100% and 150% DPI.

## License

Contributions are accepted under the repository's existing noncommercial
licenses. Keep the original author attribution `Thewitchcat` intact.
