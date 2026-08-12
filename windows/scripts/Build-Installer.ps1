[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$')]
    [string]$Tag,

    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime,

    [string]$OutputDirectory,
    [string]$VpkExecutable,

    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$vpkVersion = '1.2.0'
$windowsRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = Split-Path -Parent $windowsRoot
$publishScript = Join-Path $PSScriptRoot 'Publish-Portable.ps1'
$artifactsRoot = Join-Path $windowsRoot 'artifacts'
$publishDirectory = Join-Path $artifactsRoot "publish/$Runtime"
$velopackDirectory = Join-Path $artifactsRoot "velopack/$Runtime"
$toolDirectory = Join-Path $artifactsRoot 'tools/vpk'
$version = $Tag.Substring(1)

if ($Tag.Length -gt 80) {
    throw 'Release tags cannot exceed 80 characters.'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'dist'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

$architecture = $Runtime.Substring(4)
$packageId = if ($Runtime -eq 'win-x64') {
    'io.github.iajihga.TuckClip.WinX64'
} else {
    'io.github.iajihga.TuckClip.WinArm64'
}
$installerPath = Join-Path $OutputDirectory "TuckClip-$Tag-Windows-$architecture-Setup.exe"
$packagePath = Join-Path $OutputDirectory "$packageId-$version-$Runtime-full.nupkg"
$feedPath = Join-Path $OutputDirectory "releases.$Runtime.json"

foreach ($target in @($installerPath, $packagePath, $feedPath)) {
    if (Test-Path -LiteralPath $target) {
        throw "Refusing to replace an existing Windows release asset: $target"
    }
}

function Reset-ControlledDirectory {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$AllowedRoot
    )

    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $resolvedRoot = [IO.Path]::GetFullPath($AllowedRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $requiredPrefix = $resolvedRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to reset a directory outside the controlled artifacts root: $resolvedPath"
    }

    if (Test-Path -LiteralPath $resolvedPath) {
        Remove-Item -LiteralPath $resolvedPath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $resolvedPath | Out-Null
}

& $publishScript `
    -Tag $Tag `
    -Runtime $Runtime `
    -OutputDirectory $OutputDirectory `
    -NoRestore:$NoRestore

Reset-ControlledDirectory -Path $velopackDirectory -AllowedRoot $artifactsRoot

if ([string]::IsNullOrWhiteSpace($VpkExecutable)) {
    Reset-ControlledDirectory -Path $toolDirectory -AllowedRoot $artifactsRoot
    & dotnet tool install --tool-path $toolDirectory vpk --version $vpkVersion
    if ($LASTEXITCODE -ne 0) {
        throw "Installing vpk $vpkVersion failed with exit code $LASTEXITCODE."
    }
    $VpkExecutable = Join-Path $toolDirectory 'vpk.exe'
}

if (-not (Test-Path -LiteralPath $VpkExecutable -PathType Leaf)) {
    throw "vpk was not found: $VpkExecutable"
}

& $VpkExecutable pack `
    --outputDir $velopackDirectory `
    --channel $Runtime `
    --runtime $Runtime `
    --packId $packageId `
    --packVersion $version `
    --packDir $publishDirectory `
    --packAuthors 'TuckClip contributors' `
    --packTitle TuckClip `
    --icon (Join-Path $windowsRoot 'src/TuckClip.Windows/Assets/TuckClip.ico') `
    --mainExe TuckClip.exe `
    --instLicense (Join-Path $repositoryRoot 'LICENSE') `
    --noPortable true `
    --delta none
if ($LASTEXITCODE -ne 0) {
    throw "Velopack packaging failed with exit code $LASTEXITCODE."
}

$generatedInstallers = @(Get-ChildItem -LiteralPath $velopackDirectory -Filter '*-Setup.exe' -File)
if ($generatedInstallers.Count -ne 1) {
    throw "Expected one Velopack installer, found $($generatedInstallers.Count)."
}
$generatedPackage = Join-Path $velopackDirectory "$packageId-$version-$Runtime-full.nupkg"
$generatedFeed = Join-Path $velopackDirectory "releases.$Runtime.json"
foreach ($generated in @($generatedPackage, $generatedFeed)) {
    if (-not (Test-Path -LiteralPath $generated -PathType Leaf)) {
        throw "Velopack did not create the expected update asset: $generated"
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
[IO.File]::Move($generatedInstallers[0].FullName, $installerPath)
[IO.File]::Move($generatedPackage, $packagePath)
[IO.File]::Move($generatedFeed, $feedPath)

foreach ($asset in @($installerPath, $packagePath, $feedPath)) {
    if ((Get-Item -LiteralPath $asset).Length -eq 0) {
        throw "Velopack created an empty release asset: $asset"
    }
}

Write-Host "Created $installerPath"
Write-Host "Created $packagePath"
Write-Host "Created $feedPath"
