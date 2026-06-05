# Simple Folder Sync for Windows

Plan safe one-way or two-way folder syncs.

This is the initial Microsoft Store-oriented Windows desktop app scaffold for $(System.Collections.Hashtable.Title). It uses .NET 8 and WPF, keeps the first implementation local-first, and includes a repo-root Store-Assets folder for listing and privacy handoff material.

## Initial scope

- Source and target folder setup
- Copy/update/delete planning
- Conflict review surface
- Local-only sync notes

## Build

``powershell
dotnet build .\SimpleFolderSync\SimpleFolderSync.csproj -c Release
``

## Store notes

Before final packaging, reserve the exact Microsoft Store product name in Partner Center and update package identity values to match that reservation.