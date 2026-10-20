param([string]$Destination = (Join-Path $PSScriptRoot 'AC8SaveEditor.zip'))
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$archivePath = [IO.Path]::GetFullPath($Destination)
$archiveDirectory = Split-Path -Parent $archivePath
if (-not (Test-Path -LiteralPath $archiveDirectory)) {
    New-Item -ItemType Directory -Path $archiveDirectory -Force | Out-Null
}
$files = @('AC8SaveEditor.exe', 'README.md', 'WARNING.txt') | ForEach-Object { Join-Path $PSScriptRoot $_ }
Compress-Archive -LiteralPath $files -DestinationPath $archivePath -Force
$hash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = $archivePath + '.sha256'
$checksumLine = $hash + '  ' + [IO.Path]::GetFileName($archivePath)
[IO.File]::WriteAllText($checksumPath, $checksumLine + [Environment]::NewLine, [Text.Encoding]::ASCII)
Write-Output "Package: $archivePath"
Write-Output "Checksum: $checksumPath"
