[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$')]
    [string]$Tag,

    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime,

    [string]$OutputDirectory,
    [string]$InnoCompiler,

    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$windowsRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = Split-Path -Parent $windowsRoot
$publishScript = Join-Path $PSScriptRoot 'Publish-Portable.ps1'
$installerScript = Join-Path $windowsRoot 'installer/TuckClip.iss'
$publishDirectory = Join-Path $windowsRoot "artifacts/publish/$Runtime"
$version = $Tag.Substring(1)
$numericVersion = ($version -split '-', 2)[0]

if ($Tag.Length -gt 80) {
    throw 'Release tags cannot exceed 80 characters.'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'dist'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$architecture = $Runtime.Substring(4)
$installerPath = Join-Path $OutputDirectory "TuckClip-$Tag-Windows-$architecture-Setup.exe"
$checksumPath = "$installerPath.sha256"
if ((Test-Path -LiteralPath $installerPath) -or (Test-Path -LiteralPath $checksumPath)) {
    throw "Refusing to replace an existing installer asset or checksum: $installerPath"
}

if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
    $compilerCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($null -ne $compilerCommand) {
        $InnoCompiler = $compilerCommand.Source
    }
}

if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
    $compilerCandidates = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace(${env:ProgramFiles(x86)})) {
        $compilerCandidates.Add((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'))
    }
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramFiles)) {
        $compilerCandidates.Add((Join-Path $env:ProgramFiles 'Inno Setup 7/ISCC.exe'))
        $compilerCandidates.Add((Join-Path $env:ProgramFiles 'Inno Setup 6/ISCC.exe'))
    }
    $InnoCompiler = $compilerCandidates |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1
}

if ([string]::IsNullOrWhiteSpace($InnoCompiler) -or
    -not (Test-Path -LiteralPath $InnoCompiler -PathType Leaf)) {
    throw 'Inno Setup 6.5 or newer was not found. Pass -InnoCompiler or add ISCC.exe to PATH.'
}

& $publishScript `
    -Tag $Tag `
    -Runtime $Runtime `
    -OutputDirectory $OutputDirectory `
    -NoRestore:$NoRestore

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$arguments = @(
    '/Qp',
    "/DVersion=$version",
    "/DNumericVersion=$numericVersion",
    "/DRuntime=$Runtime",
    "/DSourceDir=$publishDirectory",
    "/O$OutputDirectory",
    $installerScript
)

& $InnoCompiler @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Expected installer was not created: $installerPath"
}
if ((Get-Item -LiteralPath $installerPath).Length -eq 0) {
    throw "Inno Setup created an empty installer: $installerPath"
}

$hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumLine = "$hash  $([IO.Path]::GetFileName($installerPath))`n"
$checksumBytes = [Text.UTF8Encoding]::new($false).GetBytes($checksumLine)
$checksumStream = [IO.File]::Open(
    $checksumPath,
    [IO.FileMode]::CreateNew,
    [IO.FileAccess]::Write,
    [IO.FileShare]::None)
try {
    $checksumStream.Write($checksumBytes, 0, $checksumBytes.Length)
}
finally {
    $checksumStream.Dispose()
}

Write-Host "Created $installerPath"
Write-Host "Created $checksumPath"
