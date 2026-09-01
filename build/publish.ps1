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

if ([string]::IsNullOrWhiteSpace($RuntimeIdentifier)) {
    if ($IsWindows) {
        $RuntimeIdentifier = "win-x64"
    }
    elseif ($IsLinux) {
        $RuntimeIdentifier = "linux-x64"
    }
    else {
        throw "TerraRuntime shipping publish currently supports Windows x64 and Linux x64 hosts only."
    }
}

$hostRuntimeIdentifier = if ($IsWindows) {
    "win-x64"
}
elseif ($IsLinux) {
    "linux-x64"
}
else {
    $null
}

if ($null -eq $hostRuntimeIdentifier -or $RuntimeIdentifier -ne $hostRuntimeIdentifier) {
    throw "Requested RID '$RuntimeIdentifier' does not match this host. NativeAOT and ReadyToRun shipping artifacts must be published on their target OS."
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
