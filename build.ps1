$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    & "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /optimize+ /out:AC8SaveEditor.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /resource:editor-catalogue.json,Catalogue MoneyEditor.cs Unlocks.cs Editor.cs EditorTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    Copy-Item -LiteralPath AC8SaveEditor.exe -Destination AC8MoneyEditor.exe -Force
} finally { Pop-Location }
