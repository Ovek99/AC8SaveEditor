# Compile from source

## Requirements

- Windows 10/11 or another Windows installation with .NET Framework 4.x and its bundled C# compiler.
- Windows PowerShell 5.1 or PowerShell 7.
- Git only if cloning the repository rather than downloading its source ZIP.

No Visual Studio, modern .NET SDK, NuGet packages, Python, game installation or extracted game archives are required to compile the editor. `editor-catalogue.json` is already generated and is embedded into the executable.

## Build

```powershell
git clone https://github.com/Ovek99/AC8SaveEditor.git
Set-Location AC8SaveEditor
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The script runs `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe` against the four C# source files, references framework libraries, and embeds the catalogue. It produces:

- `AC8SaveEditor.exe`: standalone Windows application.
- `AC8MoneyEditor.exe`: identical compatibility copy for older shortcuts.

If the compiler path is missing, install/repair the .NET Framework developer tooling for your Windows installation, or adapt the compiler path in build.ps1 to a compatible .NET Framework compiler. The current script targets 64-bit Windows installations.

## Create a distributable ZIP

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1
```

This rebuilds the editor and creates `AC8SaveEditor.zip` containing the executable, README and warning. It also writes `AC8SaveEditor.zip.sha256`. To use another destination:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\package.ps1 -Destination .\dist\AC8SaveEditor.zip
```

Build output and private save files are ignored by Git. The published ZIP belongs in GitHub release assets, not source control.

## Test with your own compatible save

Make a separate backup first. Tests do not write to the input save; they work in memory and a temporary directory.

```powershell
$save = 'C:\path\to\Campaign.sav'
$process = Start-Process .\AC8SaveEditor.exe -ArgumentList '--self-test', ('"' + $save + '"') -Wait -PassThru
$process.ExitCode
```

Exit code zero means success; the report is `test-results-Campaign.txt`, named after your input file. This regression suite expects a representative version-38 campaign save containing aircraft and pilot records, including more than one aircraft with pilot data. A brand-new/empty save may not satisfy those test-fixture assumptions. Personal saves are not supplied with this repository.

For UI checks and local preview images:

```powershell
Start-Process .\AC8SaveEditor.exe -ArgumentList '--ui-test', ('"' + $save + '"') -WindowStyle Hidden -Wait -PassThru
```

The UI test checks filtering and pending unlock/relock state and renders PNG previews. Its fixture assumes both owned and locked online-tagged emblems. These are development checks, not evidence that an edited save is safe to load in the game.

## Source layout

| File | Purpose |
|---|---|
| MoneyEditor.cs | GVAS envelope, checksum validation and atomic file replacement with backups |
| Unlocks.cs | Lossless tagged-property parser and unlock/relock operations |
| Editor.cs | Windows Forms interface and staged edits |
| EditorTests.cs | File-integrity and regression checks |
| editor-catalogue.json | Embedded item catalogue and weapon ordering |
| build.ps1 | Compile standalone executables |
| package.ps1 | Compile, package and calculate the ZIP checksum |

The embedded catalogue is sufficient for builds. Historical archive-extraction tools and their game-data inputs are deliberately not part of this source package. See THIRD-PARTY-NOTICES.md for metadata provenance.
