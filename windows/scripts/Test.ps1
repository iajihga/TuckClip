[CmdletBinding()]
param(
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$windowsRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $windowsRoot 'TuckClip.Windows.slnx'
$artifactsRoot = Join-Path $windowsRoot 'artifacts'
$resultsDirectory = Join-Path $windowsRoot 'artifacts/TestResults'

if (-not (Test-Path -LiteralPath $solution -PathType Leaf)) {
    throw "Windows solution was not found: $solution"
}

$resolvedResults = [IO.Path]::GetFullPath($resultsDirectory)
$resolvedArtifacts = [IO.Path]::GetFullPath($artifactsRoot).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
if (-not $resolvedResults.StartsWith(
    $resolvedArtifacts + [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to reset a test result directory outside Windows artifacts: $resolvedResults"
}
if (Test-Path -LiteralPath $resolvedResults) {
    Remove-Item -LiteralPath $resolvedResults -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $resolvedResults | Out-Null

Push-Location $windowsRoot
try {
    if (-not $NoRestore) {
        & dotnet restore $solution --locked-mode
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed with exit code $LASTEXITCODE."
        }
    }

    & dotnet test $solution `
        --configuration Release `
        --no-restore `
        --logger 'trx;LogFilePrefix=TuckClip-Windows' `
        --results-directory $resolvedResults
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed with exit code $LASTEXITCODE."
    }

    & dotnet build $solution --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
