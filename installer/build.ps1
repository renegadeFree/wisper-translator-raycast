# Costruisce l'applicazione e crea l'installer.
# Uso:  powershell -File installer/build.ps1 [-SkipPublish]

param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root 'publish\app'
$serverSource = Join-Path $env:LOCALAPPDATA 'WisperTranslator\tools\mtranserver.exe'

if (-not $SkipPublish) {
    Write-Host 'Pubblicazione self-contained (win-x64)...'
    & dotnet publish (Join-Path $root 'src\WisperTranslator.App\WisperTranslator.App.csproj') `
        -c Release -r win-x64 --self-contained true -p:SatelliteResourceLanguages=en `
        -o $publishDir | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish non riuscito ($LASTEXITCODE)" }
}

# Il server di traduzione locale viaggia con l'applicazione.
$serverTarget = Join-Path $publishDir 'tools\mtranserver.exe'
if (-not (Test-Path $serverTarget)) {
    if (-not (Test-Path $serverSource)) {
        throw "mtranserver.exe non trovato in $serverSource. Scaricalo con: dotnet run --project src/WisperTranslator.Cli -- mt-server download"
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $serverTarget) | Out-Null
    Copy-Item -LiteralPath $serverSource -Destination $serverTarget -Force
    Write-Host "Server di traduzione copiato in $serverTarget"
}

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw 'Inno Setup 6 non trovato: installalo da https://jrsoftware.org/isdl.php'
}

Write-Host "Compilazione dell'installer con $iscc"
& $iscc (Join-Path $PSScriptRoot 'installer.iss') | Out-Host
if ($LASTEXITCODE -ne 0) { throw "ISCC non riuscito ($LASTEXITCODE)" }

$installer = Get-ChildItem (Join-Path $root 'publish') -Filter 'WisperTranslator-Setup-*.exe' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host ''
Write-Host "Installer creato: $($installer.FullName)"
Write-Host ("Dimensione: {0:N1} MB" -f ($installer.Length / 1MB))
