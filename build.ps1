$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$release = Join-Path $PSScriptRoot 'release'
New-Item -ItemType Directory -Force -Path $release | Out-Null

& $compiler /nologo /codepage:65001 /target:winexe "/out:$release\OlaGameStudio.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Security.dll (Join-Path $PSScriptRoot 'OlaGameStudio.cs')
if ($LASTEXITCODE -ne 0) { throw "OLA Game Studio build failed: $LASTEXITCODE" }

& $compiler /nologo /codepage:65001 /target:winexe "/out:$release\Setup_OlaGameStudio.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll (Join-Path $PSScriptRoot 'OlaGameStudioUpdater.cs')
if ($LASTEXITCODE -ne 0) { throw "Updater build failed: $LASTEXITCODE" }

Get-ChildItem $release -File | Select-Object Name, Length
