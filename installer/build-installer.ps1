#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the AcerFALDv2 Windows installer (AcerFALDv2-Setup-<version>.exe).

.DESCRIPTION
    Compiles installer\AcerFALDv2.iss with Inno Setup and reports the payload
    and installer SHA-256 hashes so they can be pasted into release notes.

    The payload is the release copy of the portable build in the repository
    root (AcerFALDv2\AcerFALDv2.exe). Build or refresh that folder first:

        dotnet publish prototype\AcerFALDv2\AcerFALDv2.csproj `
            -c Release -r win-x64 --self-contained true -o AcerFALDv2

.PARAMETER Version
    Version stamped into the installer. Defaults to the payload exe's file
    version, so the installer always matches the build it contains.

.PARAMETER IsccPath
    Full path to ISCC.exe. Auto-detected from the usual Inno Setup 6 locations
    when omitted.

.EXAMPLE
    .\build-installer.ps1

.EXAMPLE
    .\build-installer.ps1 -Version 2.3.0
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$IsccPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$installerDir = $PSScriptRoot
$repoRoot     = Split-Path -Parent $installerDir
$payloadDir   = Join-Path $repoRoot 'AcerFALDv2'
$scriptFile   = Join-Path $installerDir 'AcerFALDv2.iss'
$outputDir    = Join-Path $repoRoot 'dist'

function Fail([string]$Message) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

# --- Locate the Inno Setup compiler -----------------------------------------
if (-not $IsccPath) {
    $candidates = @()
    if ($env:LOCALAPPDATA) {
        $candidates += (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    }
    $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)')
    if ($programFilesX86) {
        $candidates += (Join-Path $programFilesX86 'Inno Setup 6\ISCC.exe')
    }
    if ($env:ProgramFiles) {
        $candidates += (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    }

    $IsccPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath)) {
    Fail @'
Inno Setup 6 was not found.

Install it, then re-run this script:

    winget install --id JRSoftware.InnoSetup --exact

or pass -IsccPath "C:\path\to\ISCC.exe".
'@
}

# --- Validate the payload ----------------------------------------------------
$exePath = Join-Path $payloadDir 'AcerFALDv2.exe'
$readme  = Join-Path $payloadDir 'README.txt'
$config  = Join-Path $payloadDir 'faldsync.json'

foreach ($required in @($exePath, $readme, $config)) {
    if (-not (Test-Path -LiteralPath $required)) {
        Fail "Payload file missing: $required`nPublish the portable build into $payloadDir first."
    }
}

if (-not $Version) {
    $Version = (Get-Item -LiteralPath $exePath).VersionInfo.FileVersion
    if (-not $Version) {
        Fail 'Could not read a file version from the payload exe; pass -Version x.y.z.'
    }
    $parts = $Version.Split('.')
    while ($parts.Count -gt 3 -and $parts[$parts.Count - 1] -eq '0') {
        $parts = $parts[0..($parts.Count - 2)]
    }
    $Version = ($parts -join '.')
}

if ($Version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') {
    Fail "Version '$Version' is not a valid numeric version (expected x.y.z)."
}

# --- Build -------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir | Out-Null
}

$payloadSize   = (Get-Item -LiteralPath $exePath).Length
$payloadHash   = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash

function Format-Size([long]$Bytes) {
    [string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0:N0} bytes', $Bytes)
}

Write-Host ''
Write-Host 'AcerFALDv2 installer build' -ForegroundColor Cyan
Write-Host ("  payload    : {0}" -f $exePath)
Write-Host ("  size       : {0}" -f (Format-Size $payloadSize))
Write-Host ("  sha256     : {0}" -f $payloadHash)
Write-Host ("  version    : {0}" -f $Version)
Write-Host ("  compiler   : {0}" -f $IsccPath)
Write-Host ("  output dir : {0}" -f $outputDir)
Write-Host ''

$previousEap = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
$isccOutput = & $IsccPath '/Qp' "/DMyAppVersion=$Version" "/O$outputDir" $scriptFile 2>&1
$isccExitCode = $LASTEXITCODE
$ErrorActionPreference = $previousEap

if ($isccExitCode -ne 0) {
    $isccOutput | ForEach-Object { Write-Host $_ }
    Fail "ISCC exited with code $isccExitCode."
}

$setupName = "AcerFALDv2-Setup-$Version.exe"
$setupPath = Join-Path $outputDir $setupName
if (-not (Test-Path -LiteralPath $setupPath)) {
    Fail "Compiler reported success but $setupPath does not exist."
}

$setup = Get-Item -LiteralPath $setupPath
$setupHash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash

Write-Host 'Build complete' -ForegroundColor Green
Write-Host ("  installer  : {0}" -f $setup.FullName)
Write-Host ("  size       : {0}" -f (Format-Size $setup.Length))
Write-Host ("  sha256     : {0}" -f $setupHash)
Write-Host ''
