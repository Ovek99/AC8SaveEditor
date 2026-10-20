# AC8 Save Editor

Run **AC8SaveEditor.exe**. The previous filename **AC8MoneyEditor.exe** is kept as an identical copy for existing shortcuts. The catalogue is embedded in the executable; no data folder or Python installation is required to run it.

Download the executable ZIP from the repository's **Releases** page. To compile it yourself, see [docs/BUILDING.md](docs/BUILDING.md).

**WARNING: DLC unlocks do not work. Loading an edited save may cause the game to wipe your save data. Make a separate manual backup before editing; do not rely solely on the editor's automatic backups. Use at your own risk.**

## Use

1. Close the game and open your Campaign.sav.
2. Change spendable MRP. Tick an item to unlock it, or **untick an existing unlock to relock it**. The State column shows Unlock pending or Relock pending. Toggling back cancels that change.
3. Use Search, Category and Tags to narrow the list. **Unlock shown** stages the displayed items as unlocked; **Relock shown** stages them as locked. Both can cancel opposite pending edits. Changing filters keeps your pending selections. **Clear pending** cancels all staged unlocks and relocks, restoring their loaded states without resetting the money field.
4. Choose Save or Save as. Pending selections are applied only when saving. Every file replaced gets a unique timestamped `.bak` beside it. Items you do not select remain unchanged except for the dependent ownership/loadout cleanup described below.

Save as defaults to Campaign.edited.sav. Install the edited copy as Campaign.sav in the game's save directory if you saved to a different filename. Cloud sync may replace a local edit. To restore a backup, close the game and copy the `.bak` back to Campaign.sav.

## Unlocks and tags

The browser includes 1,649 entries: 882 skins, 285 emblems, 36 aircraft definitions, 108 per-aircraft special-weapon grants, 60 parts, 29 medals, 83 assault records, 75 music tracks, 32 free missions, 15 training missions, 13 menu features (including Ace difficulty), six hangars and 25 hangar situations.

- **DLC0000 / DLC0001 / DLC0002:** DLC content, including skins associated with a DLC aircraft even when their own table row says Live.
- **Online:** explicitly marked online in the game data. These entries can be added to the campaign's corresponding local ownership lists.
- **Shared:** aircraft definitions marked CampaignAndOnline. This is a scope tag, not evidence of separate online ownership.
- **Unclassified:** the skin table says None rather than a mode. These definitions remain selectable; their scope is not guessed.
- **Mission-only / Experimental:** loaner aircraft or unusual content such as Hangar 00. These are included for experimentation and may not behave like normal campaign unlocks.

**DLC unlocks are known not to work.** Tags remain visible so those entries can be identified and relocked. Editing local ownership does not grant account entitlements. The editor writes Campaign.sav only; a separate online profile is not changed. File-integrity checks do not establish in-game safety.

## What saving changes

- Spendable CurrentMRP and its checksum, plus the unlocks you select. Lifetime TotalMRP is left alone.
- Relevant ownership and "new item" lists are updated together. Matching condition/action tracking rules are marked activated, without fabricating platform achievement completion.
- New aircraft get four copies, an empty statistics record, their default skin, and their first default special weapon. Existing aircraft counts, statistics and pilot/loadout data are preserved. Selecting a special weapon also grants its aircraft if needed.
- Special weapons are stored per aircraft and kept in the game's enum order. Parts and aircraft also get matching aircraft-tree reference IDs; these differ from the tree table's small NodeID values.
- Free mission unlocks do not fabricate mission completion, ranks or scores.

## Relocking

- Ownership and companion "new item" entries are removed together. Matching unlock tracking flags are reset. Parts/aircraft tree references and related new-icon entries are removed as appropriate.
- Relocking a skin resets matching equipped skin selections to the game's default-skin sentinel. Relocked parts are unequipped; matching emblem slots are cleared. Relocked special weapons are removed from that aircraft's ownership lists and affected equipped weapon slots are cleared.
- Relocking an aircraft removes its copies and special weapons but preserves its statistics record. Presets and last-sortied selections that reference it are switched to the first remaining owned aircraft, with affected loadouts reset. Default skins are not automatically relocked; select them separately if desired.
- At least one aircraft and one hangar must remain unlocked. A plan that simultaneously unlocks a special weapon and relocks its required aircraft is rejected before writing.
- Relocking Ace difficulty switches the saved difficulty to Normal if it was Ace. Relocked songs are removed from playlist song arrays.
- Natural unlock conditions are not erased: the game may grant earned items again. Mission completion, historical scores and platform achievements are not undone. Relocking is **not** an exact undo or a guaranteed repair; restore a pre-edit backup to recover the original state.

Unchanged properties and unknown IDs are preserved. The parser requires an exact byte-for-byte no-op round trip before accepting a file, validates the original checksum, updates nested property sizes and PackedData length after additions, and validates the edited output before writing. Existing-file replacements use a temporary file and an atomic replacement with a backup.

The interface accepts money's full unsigned 64-bit range; the game's practical limit is unverified.

## Build and verification

Requires Windows with .NET Framework 4.x, included on typical Windows 10/11 installations. Rebuild with:

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

The generated editor-catalogue.json is included and embedded at build time. No game extraction or catalogue regeneration is needed to compile. Sources: MoneyEditor.cs (file envelope, checksum and atomic writer), Unlocks.cs (lossless property editing), Editor.cs (Windows interface), EditorTests.cs (integrity tests). Full prerequisites, packaging commands and test-fixture assumptions are documented in [docs/BUILDING.md](docs/BUILDING.md).

Create the distributable ZIP and SHA-256 checksum with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

For a read-only regression test against a save:

```powershell
Start-Process .\AC8SaveEditor.exe -ArgumentList '--self-test', '"C:\path\Campaign.sav"' -Wait -PassThru
```

Exit code 0 means success. Results go to test-results-Campaign.txt (based on the source filename). Tests use in-memory data and a temporary directory, leaving the source untouched. They cover exact no-op round trips, money limits, unlock/relock/regrant across all categories, companion fields and tracking reset, equipped-item cleanup, aircraft fallback, protection against removing the last aircraft/hangar, conflicting changes, retained statistics/unknown data, idempotence, corrupt-checksum rejection, disk output and exact backups. A separate `--ui-test` mode validates filtering, unlock/relock checkbox state, cancellation and bulk controls and renders preview images without writing a save. These checks do not establish in-game safety.

See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) for metadata provenance. The checksum salt/formula was corroborated against the [RivaTesu checksum source](https://github.com/RivaTesu/ac8-save-editor/blob/main/src/Ac8Save/Checksum.cs) and matched both development saves. English item text and weapon associations use its exported metadata, cross-referenced with installed tables. Game archives, extracted binary assets, access keys and personal saves are not included. This is an unofficial fan tool, not affiliated with or endorsed by Bandai Namco or Epic Games.
