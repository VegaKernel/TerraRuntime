param(
    [string]$Version = "1458"
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$Cache = Join-Path $Root ".cache/terraria-$Version"
$Tools = Join-Path $Root ".tools"
$Out = Join-Path $Root "decompiled/$Version"
$Zip = Join-Path $Cache "terraria-server-$Version.zip"
$Url = "https://terraria.org/api/download/pc-dedicated-server/terraria-server-$Version.zip"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet SDK is required"
}

New-Item -ItemType Directory -Force -Path $Cache, $Tools, (Join-Path $Root "decompiled") | Out-Null

if (-not (Test-Path $Zip)) {
    Write-Host "Downloading Terraria dedicated server $Version..."
    Invoke-WebRequest -Uri $Url -OutFile $Zip
}

$Extracted = Join-Path $Cache "extracted"
Remove-Item -Recurse -Force $Extracted -ErrorAction SilentlyContinue
Expand-Archive -Path $Zip -DestinationPath $Extracted -Force

$Assembly = Get-ChildItem -Path $Extracted -Recurse -File -Filter TerrariaServer.exe |
    Where-Object { $_.FullName -match '[\\/]Windows[\\/]TerrariaServer\.exe$' } |
    Select-Object -First 1
if (-not $Assembly) {
    $Assembly = Get-ChildItem -Path $Extracted -Recurse -File -Filter TerrariaServer.exe | Select-Object -First 1
}
if (-not $Assembly) {
    throw "TerrariaServer.exe was not found in the downloaded archive"
}

$Ilspy = Join-Path $Tools "ilspycmd.exe"
if (-not (Test-Path $Ilspy)) {
    dotnet tool install ilspycmd --tool-path $Tools
}

Remove-Item -Recurse -Force $Out -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $Out | Out-Null

& $Ilspy -p -o $Out $Assembly.FullName
if ($LASTEXITCODE -ne 0) {
    throw "ilspycmd failed with exit code $LASTEXITCODE"
}

$Hash = (Get-FileHash -Algorithm SHA256 $Assembly.FullName).Hash.ToLowerInvariant()
@"
Terraria dedicated server version: $Version
Download URL: $Url
Decompiled assembly: $($Assembly.FullName)
Assembly SHA-256: $Hash
Decompiler: ilspycmd

This directory is intentionally ignored by git and is for local reference only.
"@ | Set-Content -Encoding UTF8 (Join-Path $Out "REFERENCE_SOURCE.txt")

Write-Host "Reference tree created at: $Out"
