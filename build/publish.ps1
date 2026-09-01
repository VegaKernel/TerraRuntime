[CmdletBinding()]
param(
    [ValidateSet("win-x64", "linux-x64")]
    [string] $RuntimeIdentifier,

    [ValidateSet("all", "native-aot", "coreclr")]
    [string] $Profile = "all",

    [ValidateSet("Release", "Debug")]
    [string] $Configuration = "Release",

    [switch] $NoClean
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

function Get-HostRuntimeIdentifier {
    if ($env:OS -eq "Windows_NT") {
        return "win-x64"
    }

    try {
        if ([System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Linux)) {
            return "linux-x64"
        }
    }
    catch {
        # RuntimeInformation may be unavailable on very old PowerShell/.NET hosts.
    }

    return $null
}

function Get-RequiredSdkVersion {
    $globalJsonPath = Join-Path $repoRoot "global.json"
    if (-not (Test-Path $globalJsonPath -PathType Leaf)) {
        return "unknown"
    }

    try {
        $globalJson = Get-Content $globalJsonPath -Raw | ConvertFrom-Json
        return [string] $globalJson.sdk.version
    }
    catch {
        return "unknown"
    }
}

function Assert-DotNetSdk {
    $dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $dotnetCommand) {
        throw "dotnet CLI was not found in PATH. Install the .NET SDK required by global.json before publishing TerraRuntime."
    }

    $requiredSdk = Get-RequiredSdkVersion
    Push-Location $repoRoot
    try {
        $versionOutput = @(& dotnet --version 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        $details = ($versionOutput | ForEach-Object { $_.ToString() }) -join " "
        throw "dotnet could not resolve the SDK required by global.json (requested: $requiredSdk). $details"
    }

    $resolvedSdk = ($versionOutput | Select-Object -First 1).ToString().Trim()
    Write-Host "PowerShell $($PSVersionTable.PSVersion) on $([System.Environment]::OSVersion.VersionString)"
    Write-Host "dotnet SDK: $resolvedSdk"
}

function Invoke-DotNet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]] $Arguments
    )

    Write-Host "> dotnet $($Arguments -join ' ')"
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet exited with code $LASTEXITCODE."
    }
}

function Prepare-OutputDirectory {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path
    )

    if (-not $NoClean -and (Test-Path $Path)) {
        Remove-Item $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

$hostRuntimeIdentifier = Get-HostRuntimeIdentifier
if ($null -eq $hostRuntimeIdentifier) {
    throw "TerraRuntime shipping publish currently supports Windows x64 and Linux x64 hosts only."
}

if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    $RuntimeIdentifier = $hostRuntimeIdentifier
}

if ($RuntimeIdentifier -ne $hostRuntimeIdentifier) {
    throw "Requested RID '$RuntimeIdentifier' does not match this host ('$hostRuntimeIdentifier'). NativeAOT and ReadyToRun shipping artifacts must be published on their target OS."
}

Assert-DotNetSdk

$nativeOutput = Join-Path $repoRoot "artifacts/native-aot/$RuntimeIdentifier"
$coreClrOutput = Join-Path $repoRoot "artifacts/coreclr/$RuntimeIdentifier"

Push-Location $repoRoot
try {
    if ($Profile -in @("all", "native-aot")) {
        Prepare-OutputDirectory $nativeOutput
        Invoke-DotNet @(
            "publish",
            "src/TerraRuntime/TerraRuntime.csproj",
            "-c", $Configuration,
            "-r", $RuntimeIdentifier,
            "-p:PublishAot=true",
            "-p:IlcTreatWarningsAsErrors=true",
            "-o", $nativeOutput
        )
    }

    if ($Profile -in @("all", "coreclr")) {
        Prepare-OutputDirectory $coreClrOutput
        Invoke-DotNet @(
            "publish",
            "src/TerraRuntime.ExtensibleHost/TerraRuntime.ExtensibleHost.csproj",
            "-c", $Configuration,
            "-r", $RuntimeIdentifier,
            "-p:PublishAot=false",
            "-p:PublishSingleFile=true",
            "-p:SelfContained=true",
            "-p:PublishReadyToRun=true",
            "-o", $coreClrOutput
        )
    }
}
finally {
    Pop-Location
}

Write-Host "TerraRuntime publish completed for $RuntimeIdentifier."
if ($Profile -in @("all", "native-aot")) {
    Write-Host "NativeAOT: $nativeOutput"
}
if ($Profile -in @("all", "coreclr")) {
    Write-Host "CoreCLR:   $coreClrOutput"
}
