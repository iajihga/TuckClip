[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$')]
    [string]$Tag,

    [Parameter(Mandatory)]
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime,

    [string]$OutputDirectory,

    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$windowsRoot = Split-Path -Parent $PSScriptRoot
$repositoryRoot = Split-Path -Parent $windowsRoot
$project = Join-Path $windowsRoot 'src/TuckClip.Windows/TuckClip.Windows.csproj'
$artifactsRoot = Join-Path $windowsRoot 'artifacts'
$publishDirectory = Join-Path $artifactsRoot "publish/$Runtime"
$artifactName = "TuckClip-$Tag-Windows-$($Runtime.Substring(4))-portable"
$stagingDirectory = Join-Path $artifactsRoot "package/$artifactName"
$packageDirectory = Join-Path $stagingDirectory 'TuckClip'
$version = $Tag.Substring(1)

if ($Tag.Length -gt 80) {
    throw 'Release tags cannot exceed 80 characters.'
}

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repositoryRoot 'dist'
}
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)

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

function Get-PeMachine {
    param([Parameter(Mandatory)] [string]$Executable)

    $stream = [IO.File]::OpenRead($Executable)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($reader.ReadUInt16() -ne 0x5A4D) {
            throw "Executable does not start with the MZ signature: $Executable"
        }

        $stream.Position = 0x3C
        $peOffset = $reader.ReadInt32()
        if ($peOffset -lt 0x40 -or $peOffset -gt ($stream.Length - 6)) {
            throw "Executable has an invalid PE header offset: $Executable"
        }

        $stream.Position = $peOffset
        if ($reader.ReadUInt32() -ne 0x00004550) {
            throw "Executable does not contain a PE signature: $Executable"
        }

        return $reader.ReadUInt16()
    }
    finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$zipPath = Join-Path $OutputDirectory "$artifactName.zip"
$checksumPath = "$zipPath.sha256"
if ((Test-Path -LiteralPath $zipPath) -or (Test-Path -LiteralPath $checksumPath)) {
    throw "Refusing to replace an existing portable asset or checksum: $zipPath"
}

Reset-ControlledDirectory -Path $publishDirectory -AllowedRoot $artifactsRoot
Reset-ControlledDirectory -Path $stagingDirectory -AllowedRoot $artifactsRoot

Push-Location $windowsRoot
try {
    if (-not $NoRestore) {
        & dotnet restore $project --locked-mode
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore --locked-mode failed with exit code $LASTEXITCODE."
        }
    }

    & dotnet publish $project `
        --configuration Release `
        --runtime $Runtime `
        --self-contained true `
        --no-restore `
        --output $publishDirectory `
        -p:Version=$version `
        -p:DebugSymbols=false `
        -p:DebugType=None `
        -p:ContinuousIntegrationBuild=true
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$executable = Join-Path $publishDirectory 'TuckClip.exe'
if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    throw "Published application is missing TuckClip.exe: $publishDirectory"
}

$requiredPublishFiles = @(
    'TuckClip.dll',
    'TuckClip.deps.json',
    'TuckClip.runtimeconfig.json',
    'coreclr.dll',
    'hostfxr.dll'
)
foreach ($requiredFile in $requiredPublishFiles) {
    $requiredPath = Join-Path $publishDirectory $requiredFile
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Self-contained publish is missing $requiredFile for $Runtime."
    }
}

$expectedMachine = if ($Runtime -eq 'win-x64') { 0x8664 } else { 0xAA64 }
$actualMachine = Get-PeMachine -Executable $executable
if ($actualMachine -ne $expectedMachine) {
    throw ('Unexpected PE machine 0x{0:X4}; expected 0x{1:X4} for {2}.' -f $actualMachine, $expectedMachine, $Runtime)
}

$debugSymbols = @(Get-ChildItem -LiteralPath $publishDirectory -Filter '*.pdb' -Recurse -File)
foreach ($debugSymbol in $debugSymbols) {
    Remove-Item -LiteralPath $debugSymbol.FullName -Force
}

$remainingDebugSymbols = @(
    Get-ChildItem -LiteralPath $publishDirectory -Filter '*.pdb' -Recurse -File
)
if ($remainingDebugSymbols.Count -ne 0) {
    throw "Portable package still contains PDB files after symbol cleanup."
}

New-Item -ItemType Directory -Force -Path $packageDirectory | Out-Null
Get-ChildItem -LiteralPath $publishDirectory -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $packageDirectory -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.en.md') -Destination $packageDirectory

[IO.Compression.ZipFile]::CreateFromDirectory(
    $stagingDirectory,
    $zipPath,
    [IO.Compression.CompressionLevel]::Optimal,
    $false)

$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($archive.Entries.Count -eq 0) {
        throw "Portable ZIP is empty."
    }

    $requiredArchiveEntries = @(
        'TuckClip/TuckClip.exe',
        'TuckClip/LICENSE',
        'TuckClip/README.md',
        'TuckClip/README.en.md'
    )
    foreach ($requiredEntry in $requiredArchiveEntries) {
        if (-not ($archive.Entries.FullName -contains $requiredEntry)) {
            throw "Portable ZIP does not contain $requiredEntry."
        }
    }

    $unexpectedEntries = @($archive.Entries | Where-Object {
        $_.FullName -ne 'TuckClip/' -and
        -not ($_.FullName.StartsWith('TuckClip/', [StringComparison]::Ordinal))
    })
    if ($unexpectedEntries.Count -ne 0) {
        throw "Portable ZIP contains entries outside the TuckClip directory."
    }
}
finally {
    $archive.Dispose()
}

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumLine = "$hash  $([IO.Path]::GetFileName($zipPath))`n"
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

Write-Host "Created $zipPath"
Write-Host "Created $checksumPath"
