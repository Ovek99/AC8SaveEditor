# AC8 Save Editor v1.0.0 — Experimental

**WARNING: DLC unlocks do not work. Loading an edited save may wipe your save data. Make a separate manual backup before editing; do not rely solely on automatic backups. Use at your own risk.**

Standalone Windows campaign save editor with money editing, searchable unlock/relock selections, DLC/Online tags, dependent loadout cleanup, checksum validation and automatic backups.

## Download and run

1. Download and extract `AC8SaveEditor.zip`.
2. Close the game and make a separate backup of your save.
3. Run `AC8SaveEditor.exe`, open `Campaign.sav`, stage changes and save.

Requires Windows with .NET Framework 4.x. No additional mods or Python installation is required.

The `.sha256` asset gives the ZIP's SHA-256 checksum. The executable is unsigned.

## Included

- Money editing and 1,649 selectable item/feature entries.
- Individual and filtered bulk unlocking/relocking.
- Aircraft and per-aircraft weapons, skins, parts, emblems, medals, assault records, music, free/training missions, hangars and features.
- Preserved aircraft statistics and unknown save data; cleanup of affected equipped items when relocking.
- Source and compile/package instructions in the repository's README and docs/BUILDING.md.

## Known limitations

DLC unlocks do not work. Campaign edits do not establish separate online-profile or account ownership. Some definitions are mission-only, unclassified or experimental. The game may regrant naturally earned items. Relocking is not an exact undo or guaranteed repair; restore a pre-edit backup when necessary.

File-integrity regression checks passed on two development saves, including unlock/relock operations, checksum updates, preservation checks and backups. UI checks passed. These results do not establish in-game safety, so this is an experimental prerelease.
