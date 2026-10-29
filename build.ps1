$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:windir 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$release = Join-Path $PSScriptRoot 'release'
$source = Join-Path $PSScriptRoot 'OlaGameStudio.cs'
$buildSource = Join-Path $env:TEMP ('OlaGameStudio-' + [guid]::NewGuid().ToString('N') + '.cs')
$clientSecret = [Environment]::GetEnvironmentVariable('GOOGLE_OAUTH_CLIENT_SECRET')
if ([string]::IsNullOrWhiteSpace($clientSecret)) { throw 'Set GOOGLE_OAUTH_CLIENT_SECRET in the build environment to enable Google sign-in.' }
New-Item -ItemType Directory -Force -Path $release | Out-Null

try {
    $sourceText = [System.IO.File]::ReadAllText($source, [System.Text.Encoding]::UTF8)
    $escapedSecret = $clientSecret.Replace('\', '\\').Replace('"', '\"')
    $sourceText = $sourceText.Replace('__GOOGLE_OAUTH_CLIENT_SECRET__', $escapedSecret)
    [System.IO.File]::WriteAllText($buildSource, $sourceText, [System.Text.UTF8Encoding]::new($false))

    & $compiler /nologo /codepage:65001 /target:winexe "/out:$release\OlaGameStudio.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Security.dll $buildSource
    if ($LASTEXITCODE -ne 0) { throw "OLA Game Studio build failed: $LASTEXITCODE" }
}
finally { Remove-Item -LiteralPath $buildSource -Force -ErrorAction SilentlyContinue }

& $compiler /nologo /codepage:65001 /target:winexe "/out:$release\Setup_OlaGameStudio.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll (Join-Path $PSScriptRoot 'OlaGameStudioUpdater.cs')
if ($LASTEXITCODE -ne 0) { throw "Updater build failed: $LASTEXITCODE" }

Get-ChildItem $release -File | Select-Object Name, Length
