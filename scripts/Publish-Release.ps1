<#
.SYNOPSIS
  Builds the StereoKeeper release files and, on request, publishes the GitHub release.

.DESCRIPTION
  Runs the tests, publishes self-contained single-file executables for win-x64 and win-arm64,
  checks their version resources and writes SHA256SUMS.txt to artifacts\release\<tag>.
  The release notes come from docs\release-notes\<tag>.md and must exist before a release.

  -CreateGitHubRelease also creates the tag and the GitHub release with the files and notes
  (requires the GitHub CLI signed in). Add -Draft to keep the release unpublished for a review.

.EXAMPLE
  .\scripts\Publish-Release.ps1 -ValidateConfigurationOnly
.EXAMPLE
  .\scripts\Publish-Release.ps1 -CreateGitHubRelease -Draft
#>
[CmdletBinding()]
param(
    [ValidateNotNullOrEmpty()]
    [string]$Configuration = "Release",

    [ValidateNotNullOrEmpty()]
    [string]$ReleaseBranch = "master",

    [switch]$SkipRepositoryChecks,

    [switch]$ValidateConfigurationOnly,

    [switch]$CreateGitHubRelease,

    [switch]$Draft
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "release-helpers.ps1")

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$solutionPath = Join-Path $repositoryRoot "StereoKeeper.sln"
$projectPath = Join-Path $repositoryRoot "StereoKeeper\StereoKeeper.csproj"
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot "artifacts"))
$releaseRoot = [IO.Path]::GetFullPath((Join-Path $artifactsRoot "release"))
$runtimeIdentifiers = @("win-x64", "win-arm64")
$releaseNotesRoot = Join-Path $repositoryRoot "docs\release-notes"

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    Write-Host "> $FilePath $($Arguments -join ' ')" -ForegroundColor DarkGray
    $commandOutput = @(& $FilePath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE

    foreach ($outputLine in $commandOutput) {
        Write-Host $outputLine
    }

    if ($exitCode -ne 0) {
        throw "Command '$FilePath' failed with exit code $exitCode."
    }
}

function Get-NativeOutput {
    param(
        [Parameter(Mandatory)]
        [string]$FilePath,

        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $output = & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$FilePath' failed with exit code $LASTEXITCODE."
    }

    return ($output -join "`n").Trim()
}

function Assert-ReleaseRepositoryState {
    $currentBranch = Get-NativeOutput "git" @("-C", $repositoryRoot, "branch", "--show-current")
    if ($currentBranch -ne $ReleaseBranch) {
        throw "Release builds must run from '$ReleaseBranch'. Current branch: '$currentBranch'."
    }

    $changes = @(& git -C $repositoryRoot status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to inspect the Git working tree."
    }

    if ($changes.Count -gt 0) {
        throw "Release builds require a clean working tree.`n$($changes -join "`n")"
    }

    Invoke-NativeCommand "git" @("-C", $repositoryRoot, "fetch", "--quiet", "origin", $ReleaseBranch)

    $headCommit = Get-NativeOutput "git" @("-C", $repositoryRoot, "rev-parse", "HEAD")
    $remoteCommit = Get-NativeOutput "git" @("-C", $repositoryRoot, "rev-parse", "origin/$ReleaseBranch")
    if ($headCommit -ne $remoteCommit) {
        throw "Local '$ReleaseBranch' must exactly match 'origin/$ReleaseBranch' before publishing."
    }
}

function Assert-SafeReleasePath {
    $expectedPrefix = $artifactsRoot.TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

    if (-not $releaseRoot.StartsWith($expectedPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Release output must remain inside '$artifactsRoot'."
    }
}

function Assert-GitHubReleaseIsNew {
    param([Parameter(Mandatory)][string]$Tag)

    if (-not (Get-Command "gh" -ErrorAction SilentlyContinue)) {
        throw "The GitHub CLI (gh) is required for -CreateGitHubRelease."
    }

    & gh auth status *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "The GitHub CLI is not signed in. Run 'gh auth login' first."
    }

    & gh release view $Tag *> $null
    if ($LASTEXITCODE -eq 0) {
        throw "The GitHub release '$Tag' already exists."
    }

    $remoteTag = Get-NativeOutput "git" @("-C", $repositoryRoot, "ls-remote", "--tags", "origin", "refs/tags/$Tag")
    if (-not [string]::IsNullOrWhiteSpace($remoteTag)) {
        throw "The tag '$Tag' already exists on origin."
    }
}

$versionInfo = Get-ProjectReleaseInfo -ProjectPath $projectPath
$releaseNotesPath = Join-Path $releaseNotesRoot "$($versionInfo.ReleaseTag).md"
$releaseTitle = "StereoKeeper $($versionInfo.ReleaseTag)"

if (-not (Test-Path -LiteralPath $releaseNotesPath -PathType Leaf) -or
    [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $releaseNotesPath -Raw))) {
    throw "Release notes are missing: write '$releaseNotesPath' first."
}

if ($Draft -and -not $CreateGitHubRelease) {
    throw "-Draft only applies together with -CreateGitHubRelease."
}

if ($ValidateConfigurationOnly) {
    Write-Host "Release configuration is valid:" -ForegroundColor Green
    Write-Host "  Build version   : $($versionInfo.BuildVersion)"
    Write-Host "  Display version : $($versionInfo.DisplayVersion)"
    Write-Host "  Package version : $($versionInfo.PackageVersion)"
    Write-Host "  Release tag     : $($versionInfo.ReleaseTag)"
    Write-Host "  Release title   : $releaseTitle"
    Write-Host "  Release notes   : $releaseNotesPath"
    return
}

if ($SkipRepositoryChecks) {
    if ($CreateGitHubRelease) {
        throw "-CreateGitHubRelease publishes from '$ReleaseBranch' only and cannot skip the repository checks."
    }

    Write-Warning "Repository branch and cleanliness checks were explicitly skipped."
}
else {
    Assert-ReleaseRepositoryState
}

if ($CreateGitHubRelease) {
    Assert-GitHubReleaseIsNew -Tag $versionInfo.ReleaseTag
}

Assert-SafeReleasePath

$displayVersion = $versionInfo.DisplayVersion
$releaseDirectory = Join-Path $releaseRoot $versionInfo.ReleaseTag
$stagingDirectory = Join-Path $releaseRoot ".staging"

if (Test-Path -LiteralPath $releaseRoot) {
    Remove-Item -LiteralPath $releaseRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null

Invoke-NativeCommand "dotnet" @(
    "test",
    $solutionPath,
    "--configuration", $Configuration,
    "--nologo"
)

$releaseArtifacts = foreach ($runtimeIdentifier in $runtimeIdentifiers) {
    $publishDirectory = Join-Path $stagingDirectory $runtimeIdentifier

    Invoke-NativeCommand "dotnet" @(
        "publish",
        $projectPath,
        "--configuration", $Configuration,
        "--runtime", $runtimeIdentifier,
        "--self-contained", "true",
        "--output", $publishDirectory,
        "--nologo",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true",
        "-p:DebugType=None",
        "-p:DebugSymbols=false",
        "-p:ContinuousIntegrationBuild=true"
    )

    $publishedExecutable = Join-Path $publishDirectory "StereoKeeper.exe"
    if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
        throw "The $runtimeIdentifier single-file executable was not produced."
    }

    $artifactName = Get-ReleaseArtifactName `
        -DisplayVersion $displayVersion `
        -RuntimeIdentifier $runtimeIdentifier
    $artifactPath = Join-Path $releaseDirectory $artifactName
    Copy-Item -LiteralPath $publishedExecutable -Destination $artifactPath
    $artifact = Get-Item -LiteralPath $artifactPath

    if ($artifact.VersionInfo.FileVersion -ne $versionInfo.PackageVersion) {
        throw "Unexpected FileVersion in '$artifactName': $($artifact.VersionInfo.FileVersion)."
    }

    if ($artifact.VersionInfo.ProductVersion -ne $displayVersion) {
        throw "Unexpected ProductVersion in '$artifactName': $($artifact.VersionInfo.ProductVersion)."
    }

    $artifact
}

$checksumLines = foreach ($artifact in $releaseArtifacts) {
    $hash = Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $($artifact.Name)"
}

$checksumPath = Join-Path $releaseDirectory "SHA256SUMS.txt"
[IO.File]::WriteAllLines($checksumPath, $checksumLines, [Text.Encoding]::ASCII)

Remove-Item -LiteralPath $stagingDirectory -Recurse -Force

Write-Host
Write-Host "Release $($versionInfo.ReleaseTag) is ready:" -ForegroundColor Green
foreach ($artifact in $releaseArtifacts) {
    Write-Host "  $($artifact.Name) ($([Math]::Round($artifact.Length / 1MB, 2)) MiB)"
}
Write-Host "  SHA256SUMS.txt"
Write-Host
Get-Content -LiteralPath $checksumPath

if ($CreateGitHubRelease) {
    $releaseArguments = @(
        "release", "create", $versionInfo.ReleaseTag
    ) + @($releaseArtifacts | ForEach-Object { $_.FullName }) + @(
        $checksumPath,
        "--target", $ReleaseBranch,
        "--title", $releaseTitle,
        "--notes-file", $releaseNotesPath
    )

    if ($Draft) {
        $releaseArguments += "--draft"
    }
    else {
        $releaseArguments += "--latest"
    }

    Write-Host
    Invoke-NativeCommand "gh" $releaseArguments
    $state = if ($Draft) { "draft" } else { "published" }
    Write-Host "GitHub release $($versionInfo.ReleaseTag) is $state." -ForegroundColor Green
}
