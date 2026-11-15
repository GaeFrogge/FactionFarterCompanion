# Faction Farter Companion

Windows companion for the Faction Farter addon for WoW Forever. The app reads the addon's SavedVariables and, when you sync, sends the character name, faction, and scored record IDs, zones and UTC hour buckets to https://ff.gaefrogge.com/. Target names and detailed history stay local. Auto-sync is optional. No account or login is required.

## Build on Windows

Run `build.cmd`. It uses the Windows .NET Framework C# compiler and builds both the app and its installer in `build\`. The build requires no package downloads. Run `build\FactionFarterCompanion.exe` to try the app directly, or use the setup executable to install it.

## Review the source

- `Companion.cs` reads WoW saves, shows local stats, and handles leaderboard uploads.
- `Setup.cs` installs the embedded app, manages shortcuts and Windows uninstall registration, and launches the app only if selected.
- `App.manifest` declares standard-user execution and Windows compatibility.

The companion stores its preferences and automatically generated device key under the current Windows user's registry. Keep that key private.

## Sync and updates

Sync sends up to 250 pending records per character at a time. The companion saves a local acknowledgement after the server accepts the batch. Previously acknowledged records are skipped, and a changed or shortened save is flagged for review. Auto-sync is optional and off by default. The first sync after upgrading may resend existing records; the server deduplicates them.

On startup the app checks the public version manifest at https://ff.gaefrogge.com/companion-version.json. Updates are installed manually from the Companion page.
