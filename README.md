# Simple Folder Sync for Windows

Plan safe one-way or two-way folder syncs.

This is the initial Microsoft Store-oriented Windows desktop app scaffold for $(System.Collections.Hashtable.Title). It uses .NET 8 and WPF, keeps the first implementation local-first, and includes a repo-root Store-Assets folder for listing and privacy handoff material.

## Initial scope

- Source and target folder setup
- Copy/update/delete planning
- Conflict review surface
- Local-only sync notes

## Build

```powershell
dotnet build .\SimpleFolderSync\SimpleFolderSync.csproj -c Release
```

## Store prep

- App name: Simple Folder Sync
- Reserved Store name: Simple Folder Sync
- Partner Center Store ID: 9P2QL9F40TXX
- Reserved Package Identity Name: m3Coding.SimpleFolderSync
- Reserved Publisher: CN=AFF85DD5-3D92-42A5-BA39-3AF6D41B1837
- Package Family Name (PFN): m3Coding.SimpleFolderSync_8srffngrg4x08
- Price: $1.99 USD
- Trial: 15-day fully functional trial
- Full status and remaining days are shown on the About page.
- Logging: runtime logs are written to `%LOCALAPPDATA%\\SimpleFolderSync\\Logs`.
- AppX manifest template: `Store-Assets/AppxManifest.xml` (use this as baseline for packaging).

- Keep `Store-Assets/StoreSubmissionChecklist.md` up to date.
- Copy icons from:
  - `Store-Assets/StoreLogo.png` (300x300)
  - `Store-Assets/Square310x310Logo.png` (310x310)
  - `Store-Assets/Wide310x150Logo.png` (310x150)
  - `Store-Assets/Square150x150Logo.png` (150x150)
  - `Store-Assets/Square44x44Logo.png` (44x44)

## Release artifacts

- `screenshots\\simple-folder-sync-trial.png`
- `screenshots\\simple-folder-sync-full-license.png`
- `Store-Assets\\Screenshots\\screenshot-trial.png`
- `Store-Assets\\Screenshots\\screenshot-full.png`
