<#
.SYNOPSIS
  Creates or updates the BluetoothHandsFreeToggle package in winget-pkgs.

.DESCRIPTION
  Generates x64 and Arm64 WinGet manifests with localized metadata, validates
  them, optionally tests installation from a public GitHub release, and submits
  a pull request only when -Submit is explicitly supplied. Local release
  artifacts can be used to prepare manifests before the GitHub release exists.
#>
[CmdletBinding()]
param(
    [string]$Version = "",

    [ValidateSet("Auto", "New", "Update")]
    [string]$Mode = "Auto",

    [string]$PackageIdentifier = "OlimoffDev.BluetoothHandsFreeToggle",

    [string]$Repository = "Avazbek22/BluetoothHandsFreeToggle",

    [switch]$InstallTest,

    [switch]$KeepTestInstall,

    [switch]$Submit,

    [switch]$LocalOnly,

    [switch]$NonInteractive,

    [switch]$PlanOnly,

    [string]$LocalArtifactsDirectory = "",

    [string]$PlannedReleaseDate = "",

    [string]$OutDirectory = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "release-helpers.ps1")

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$projectPath = Join-Path $repositoryRoot "BluetoothHandsFreeToggle\BluetoothHandsFreeToggle.csproj"
$manifestSchemaVersion = "1.12.0"
$commandAlias = "bhft"
$packageName = "BluetoothHandsFreeToggle"

function Write-Step {
    param([Parameter(Mandatory)][string]$Message)

    Write-Host
    Write-Host "=== $Message ===" -ForegroundColor Cyan
}

function Ensure-Command {
    param([Parameter(Mandatory)][string]$Name)

    if ($null -eq (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "Required command not found: $Name"
    }
}

function Ensure-WingetCreate {
    Ensure-Command -Name "wingetcreate"

    try {
        & wingetcreate info *> $null
        if ($LASTEXITCODE -ne 0) {
            throw "exit code $LASTEXITCODE"
        }
    }
    catch {
        throw (
            "WingetCreate is installed but could not start. Repair or update " +
            "Microsoft.WingetCreate before using -Submit. Details: " +
            $_.Exception.Message)
    }
}

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

    return $commandOutput
}

function Read-Optional {
    param(
        [Parameter(Mandatory)]
        [string]$Prompt,

        [string]$DefaultValue = ""
    )

    $suffix = if ([string]::IsNullOrWhiteSpace($DefaultValue)) {
        ""
    }
    else {
        " [$DefaultValue]"
    }

    $inputValue = Read-Host ($Prompt + $suffix)
    if ([string]::IsNullOrWhiteSpace($inputValue)) {
        return $DefaultValue
    }

    return $inputValue.Trim()
}

function Get-GitHubRelease {
    param(
        [Parameter(Mandatory)]
        [string]$RepositoryName,

        [Parameter(Mandatory)]
        [string]$ReleaseTag
    )

    $encodedTag = [Uri]::EscapeDataString($ReleaseTag)
    $apiUrl = "https://api.github.com/repos/$RepositoryName/releases/tags/$encodedTag"
    $headers = @{
        Accept = "application/vnd.github+json"
        "User-Agent" = "BluetoothHandsFreeToggle-WinGet-Publisher"
        "X-GitHub-Api-Version" = "2022-11-28"
    }

    try {
        $release = Invoke-RestMethod -Uri $apiUrl -Headers $headers -Method Get
    }
    catch {
        throw "GitHub release '$ReleaseTag' was not found in '$RepositoryName': $($_.Exception.Message)"
    }

    if ($release.draft) {
        throw "GitHub release '$ReleaseTag' is still a draft."
    }

    if ($release.prerelease) {
        throw "GitHub release '$ReleaseTag' is a prerelease and cannot be published to WinGet."
    }

    return $release
}

function Get-ReleaseInstallerEntries {
    param(
        [Parameter(Mandatory)]
        [object]$Release,

        [Parameter(Mandatory)]
        [string]$DisplayVersion
    )

    $entries = foreach ($architecture in @("x64", "arm64")) {
        $runtimeIdentifier = "win-$architecture"
        $assetName = Get-ReleaseArtifactName `
            -DisplayVersion $DisplayVersion `
            -RuntimeIdentifier $runtimeIdentifier
        $asset = @($Release.assets | Where-Object { $_.name -eq $assetName }) |
            Select-Object -First 1

        if ($null -eq $asset) {
            throw "Release asset '$assetName' was not found."
        }

        [pscustomobject]@{
            Architecture = $architecture
            Name = $assetName
            Url = [string]$asset.browser_download_url
            Digest = [string]$asset.digest
        }
    }

    return @($entries)
}

function Get-InstallerSha256 {
    param(
        [Parameter(Mandatory)]
        [object]$InstallerEntry
    )

    if ($InstallerEntry.PSObject.Properties.Name -contains "LocalPath" -and
        -not [string]::IsNullOrWhiteSpace([string]$InstallerEntry.LocalPath)) {
        return (Get-FileHash -LiteralPath $InstallerEntry.LocalPath -Algorithm SHA256).Hash
    }

    if ($InstallerEntry.Digest -match '^sha256:([a-fA-F0-9]{64})$') {
        return $Matches[1].ToUpperInvariant()
    }

    $temporaryFile = Join-Path ([IO.Path]::GetTempPath()) (
        "bhft-winget-" + [Guid]::NewGuid().ToString("N") + ".exe")

    try {
        Write-Host "Downloading $($InstallerEntry.Name) to calculate SHA-256..."
        Invoke-WebRequest `
            -Uri $InstallerEntry.Url `
            -OutFile $temporaryFile `
            -UseBasicParsing
        return (Get-FileHash -LiteralPath $temporaryFile -Algorithm SHA256).Hash
    }
    finally {
        if (Test-Path -LiteralPath $temporaryFile) {
            Remove-Item -LiteralPath $temporaryFile -Force
        }
    }
}

function Get-LocalInstallerEntries {
    param(
        [Parameter(Mandatory)]
        [string]$Directory,

        [Parameter(Mandatory)]
        [string]$DisplayVersion,

        [Parameter(Mandatory)]
        [string]$ReleaseTag
    )

    $resolvedDirectory = [IO.Path]::GetFullPath($Directory)
    if (-not (Test-Path -LiteralPath $resolvedDirectory -PathType Container)) {
        throw "Local artifact directory was not found: $resolvedDirectory"
    }

    $entries = foreach ($architecture in @("x64", "arm64")) {
        $runtimeIdentifier = "win-$architecture"
        $assetName = Get-ReleaseArtifactName `
            -DisplayVersion $DisplayVersion `
            -RuntimeIdentifier $runtimeIdentifier
        $localPath = Join-Path $resolvedDirectory $assetName
        if (-not (Test-Path -LiteralPath $localPath -PathType Leaf)) {
            throw "Local release artifact was not found: $localPath"
        }

        [pscustomobject]@{
            Architecture = $architecture
            Name = $assetName
            Url = "https://github.com/$Repository/releases/download/$ReleaseTag/$assetName"
            Digest = ""
            LocalPath = $localPath
        }
    }

    return @($entries)
}

function Test-PackageExists {
    param([Parameter(Mandatory)][string]$Identifier)

    & winget show --id $Identifier --exact --source winget --disable-interactivity *> $null
    return $LASTEXITCODE -eq 0
}

function Resolve-PublishMode {
    param(
        [Parameter(Mandatory)]
        [string]$RequestedMode,

        [Parameter(Mandatory)]
        [bool]$PackageExists,

        [Parameter(Mandatory)]
        [string]$Identifier
    )

    if ($RequestedMode -eq "Auto") {
        if ($PackageExists) {
            return "Update"
        }

        return "New"
    }

    if ($RequestedMode -eq "New" -and $PackageExists) {
        throw "Package '$Identifier' already exists. Use Update mode."
    }

    if ($RequestedMode -eq "Update" -and -not $PackageExists) {
        throw "Package '$Identifier' does not exist yet. Use New mode."
    }

    return $RequestedMode
}

function Write-Utf8File {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [string]$Content
    )

    $utf8WithoutBom = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText(
        $Path,
        $Content.Trim() + [Environment]::NewLine,
        $utf8WithoutBom)
}

function ConvertTo-YamlBlock {
    param([Parameter(Mandatory)][string]$Value)

    return (($Value.Trim() -split "`r?`n") |
        ForEach-Object { "  $_" }) -join [Environment]::NewLine
}

function ConvertTo-YamlSingleQuoted {
    param([Parameter(Mandatory)][string]$Value)

    return "'" + $Value.Replace("'", "''") + "'"
}

function Get-WinGetLocaleMetadata {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "WinGet locale metadata was not found: $Path"
    }

    $metadataObject = Get-Content -LiteralPath $Path -Raw -Encoding UTF8 |
        ConvertFrom-Json
    $metadata = @{}
    foreach ($localeProperty in $metadataObject.PSObject.Properties) {
        $entry = @{}
        foreach ($entryProperty in $localeProperty.Value.PSObject.Properties) {
            $entry[$entryProperty.Name] = $entryProperty.Value
        }
        $metadata[$localeProperty.Name] = $entry
    }
    $expectedLocales = @(
        "en-US", "ru-RU", "zh-CN", "zh-TW", "de-DE", "fr-FR",
        "es-ES", "es-419", "pt-BR", "pt-PT", "ja-JP", "ko-KR",
        "pl-PL", "tr-TR", "it-IT", "th-TH", "uk-UA", "cs-CZ",
        "nl-NL", "sv-SE", "da-DK", "nb-NO", "fi-FI", "hu-HU",
        "ro-RO", "el-GR", "bg-BG", "id-ID", "ms-MY", "vi-VN")

    $actualLocales = @($metadata.Keys | Sort-Object)
    $missingLocales = @($expectedLocales | Where-Object { $_ -notin $actualLocales })
    $unexpectedLocales = @($actualLocales | Where-Object { $_ -notin $expectedLocales })
    if ($missingLocales.Count -gt 0 -or $unexpectedLocales.Count -gt 0) {
        throw @"
WinGet locale metadata must contain exactly the 30 application locales.
Missing   : $($missingLocales -join ', ')
Unexpected: $($unexpectedLocales -join ', ')
"@
    }

    foreach ($locale in $expectedLocales) {
        $entry = $metadata[$locale]
        foreach ($property in @(
                "ShortDescription", "Description", "InstallationNotes", "ReleaseNotes")) {
            if (-not $entry.ContainsKey($property) -or
                [string]::IsNullOrWhiteSpace([string]$entry[$property])) {
                throw "WinGet locale '$locale' has an empty $property value."
            }
        }
        if (([string]$entry["ShortDescription"]).Length -gt 256) {
            throw "WinGet locale '$locale' has a ShortDescription longer than 256 characters."
        }

        $tags = @($entry["Tags"])
        if ($tags.Count -lt 1 -or $tags.Count -gt 16) {
            throw "WinGet locale '$locale' must contain between 1 and 16 tags."
        }
        if (@($tags | Select-Object -Unique).Count -ne $tags.Count) {
            throw "WinGet locale '$locale' contains duplicate tags."
        }
        foreach ($tag in $tags) {
            if ([string]::IsNullOrWhiteSpace([string]$tag) -or
                ([string]$tag).Length -gt 40) {
                throw "WinGet locale '$locale' contains an invalid tag: '$tag'."
            }
        }
    }

    return $metadata
}

function Write-LocalizedManifests {
    param(
        [Parameter(Mandatory)]
        [string]$ManifestRoot,

        [Parameter(Mandatory)]
        [string]$Identifier,

        [Parameter(Mandatory)]
        [string]$PackageVersion,

        [Parameter(Mandatory)]
        [string]$ReleaseTag,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [Collections.IDictionary]$Metadata
    )

    foreach ($locale in $Metadata.Keys) {
        $entry = $Metadata[$locale]
        # The WinGet 1.12 locale schema rejects numeric UN M49 regions such as
        # es-419, so the Latin American Spanish metadata uses es-MX there only.
        $packageLocale = if ($locale -eq "es-419") { "es-MX" } else { $locale }
        $shortDescription = ConvertTo-YamlBlock -Value $entry.ShortDescription
        $description = ConvertTo-YamlBlock -Value $entry.Description
        $installationNotes = ConvertTo-YamlBlock -Value $entry.InstallationNotes
        $releaseNotes = ConvertTo-YamlBlock -Value $entry.ReleaseNotes
        $tagLines = @($entry.Tags | ForEach-Object {
                "- " + (ConvertTo-YamlSingleQuoted -Value ([string]$_))
            }) -join [Environment]::NewLine

        $defaultFields = if ($locale -eq "en-US") {
@"
PublisherUrl: https://github.com/Avazbek22
PublisherSupportUrl: https://github.com/$Repository/issues
PackageUrl: https://github.com/$Repository
LicenseUrl: https://github.com/$Repository/blob/master/LICENSE
Copyright: Copyright (c) 2026 Avazbek Olimov
Moniker: $commandAlias
Documentations:
- DocumentLabel: Project documentation
  DocumentUrl: https://github.com/$Repository#readme
"@
        }
        else {
            ""
        }
        $manifestType = if ($locale -eq "en-US") { "defaultLocale" } else { "locale" }
        $schemaType = if ($locale -eq "en-US") { "defaultLocale" } else { "locale" }

        $manifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.$schemaType.$manifestSchemaVersion.schema.json

PackageIdentifier: $Identifier
PackageVersion: $PackageVersion
PackageLocale: $packageLocale
Publisher: Olimoff Dev
PackageName: $Name
License: MIT
$defaultFields
ShortDescription: >-
$shortDescription
Description: >-
$description
InstallationNotes: >-
$installationNotes
ReleaseNotes: >-
$releaseNotes
ReleaseNotesUrl: https://github.com/$Repository/releases/tag/$ReleaseTag
Tags:
$tagLines
ManifestType: $manifestType
ManifestVersion: $manifestSchemaVersion
"@

        Write-Utf8File `
            -Path (Join-Path $ManifestRoot "$Identifier.locale.$packageLocale.yaml") `
            -Content $manifest
    }
}

function New-InitialManifests {
    param(
        [Parameter(Mandatory)]
        [string]$ManifestRoot,

        [Parameter(Mandatory)]
        [string]$Identifier,

        [Parameter(Mandatory)]
        [string]$PackageVersion,

        [Parameter(Mandatory)]
        [string]$ReleaseDate,

        [Parameter(Mandatory)]
        [object[]]$InstallerEntries
    )

    New-Item -ItemType Directory -Path $ManifestRoot -Force | Out-Null

    $installerLines = foreach ($installerEntry in $InstallerEntries) {
        $sha256 = Get-InstallerSha256 -InstallerEntry $installerEntry
@"
- Architecture: $($installerEntry.Architecture)
  InstallerUrl: $($installerEntry.Url)
  InstallerSha256: $sha256
"@
    }

    $versionManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.$manifestSchemaVersion.schema.json

PackageIdentifier: $Identifier
PackageVersion: $PackageVersion
DefaultLocale: en-US
ManifestType: version
ManifestVersion: $manifestSchemaVersion
"@

    $installerManifest = @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.$manifestSchemaVersion.schema.json

PackageIdentifier: $Identifier
PackageVersion: $PackageVersion
MinimumOSVersion: 10.0.19041.0
InstallerType: portable
Commands:
- $commandAlias
ReleaseDate: $ReleaseDate
Installers:
$($installerLines -join [Environment]::NewLine)
ManifestType: installer
ManifestVersion: $manifestSchemaVersion
"@

    Write-Utf8File `
        -Path (Join-Path $ManifestRoot "$Identifier.yaml") `
        -Content $versionManifest
    Write-Utf8File `
        -Path (Join-Path $ManifestRoot "$Identifier.installer.yaml") `
        -Content $installerManifest
}

function Invoke-ManifestValidation {
    param([Parameter(Mandatory)][string]$ManifestRoot)

    Invoke-NativeCommand `
        -FilePath "winget" `
        -Arguments @("validate", "--manifest", $ManifestRoot) |
        Out-Null
}

function Test-PackageInstalled {
    param(
        [Parameter(Mandatory)]
        [string]$Identifier,

        [Parameter(Mandatory)]
        [string]$Name
    )

    & winget list --id $Identifier --exact --disable-interactivity *> $null
    if ($LASTEXITCODE -eq 0) {
        return $true
    }

    # Local portable manifests use an internal registration identifier until
    # the package is available in the public WinGet source.
    & winget list --name $Name --exact --disable-interactivity *> $null
    return $LASTEXITCODE -eq 0
}

function Invoke-ManifestInstallTest {
    param(
        [Parameter(Mandatory)]
        [string]$ManifestRoot,

        [Parameter(Mandatory)]
        [string]$Identifier,

        [Parameter(Mandatory)]
        [string]$Name,

        [Parameter(Mandatory)]
        [bool]$KeepInstalled
    )

    if (Test-PackageInstalled -Identifier $Identifier -Name $Name) {
        throw "Package '$Identifier' is already installed. Refusing to overwrite it during the test."
    }

    Invoke-NativeCommand `
        -FilePath "winget" `
        -Arguments @(
            "install",
            "--manifest", $ManifestRoot,
            "--accept-package-agreements",
            "--accept-source-agreements",
            "--disable-interactivity"
        ) |
        Out-Null

    if (-not (Test-PackageInstalled -Identifier $Identifier -Name $Name)) {
        throw "Local installation completed, but '$Identifier' was not registered by WinGet."
    }

    if (-not $KeepInstalled) {
        Invoke-NativeCommand `
            -FilePath "winget" `
            -Arguments @(
                "uninstall",
                "--name", $Name,
                "--exact",
                "--disable-interactivity"
            ) |
            Out-Null
    }
}

function Submit-Manifests {
    param(
        [Parameter(Mandatory)]
        [string]$ManifestRoot,

        [Parameter(Mandatory)]
        [string]$Identifier,

        [Parameter(Mandatory)]
        [string]$PackageVersion,

        [Parameter(Mandatory)]
        [string]$PublishMode
    )

    $pullRequestTitle = if ($PublishMode -eq "New") {
        "New package: $Identifier version $PackageVersion"
    }
    else {
        "Update $Identifier to $PackageVersion"
    }

    Write-Host "> wingetcreate submit --prtitle `"$pullRequestTitle`" $ManifestRoot" -ForegroundColor DarkGray
    $submissionOutput = wingetcreate submit --prtitle $pullRequestTitle $ManifestRoot 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "WingetCreate submission failed.`n$submissionOutput"
    }

    Write-Host $submissionOutput
    $pullRequestMatch = [regex]::Match(
        $submissionOutput,
        'https://github\.com/microsoft/winget-pkgs/pull/\d+')

    if ($pullRequestMatch.Success) {
        return $pullRequestMatch.Value
    }

    return $null
}

if ($Submit -and $LocalOnly) {
    throw "Use either -Submit or -LocalOnly, not both."
}
if ($Submit -and -not [string]::IsNullOrWhiteSpace($LocalArtifactsDirectory)) {
    throw "Submission requires an already published GitHub release. Remove -LocalArtifactsDirectory."
}
if ($InstallTest -and -not [string]::IsNullOrWhiteSpace($LocalArtifactsDirectory)) {
    throw "The WinGet install test requires public installer URLs. Run it after publishing the GitHub release."
}

Ensure-Command -Name "winget"
if ($Submit) {
    Ensure-WingetCreate
}

$projectReleaseInfo = Get-ProjectReleaseInfo -ProjectPath $projectPath
$displayVersion = if ([string]::IsNullOrWhiteSpace($Version)) {
    if ($NonInteractive) {
        $projectReleaseInfo.DisplayVersion
    }
    else {
        Read-Optional `
            -Prompt "Release version" `
            -DefaultValue $projectReleaseInfo.DisplayVersion
    }
}
else {
    $Version.Trim()
}

$packageVersion = ConvertTo-NormalizedVersion -Version $displayVersion
$releaseTag = "v$displayVersion"
$usesLocalArtifacts = -not [string]::IsNullOrWhiteSpace($LocalArtifactsDirectory)
if ($usesLocalArtifacts) {
    $installerEntries = Get-LocalInstallerEntries `
        -Directory $LocalArtifactsDirectory `
        -DisplayVersion $displayVersion `
        -ReleaseTag $releaseTag
    $releaseDate = if ([string]::IsNullOrWhiteSpace($PlannedReleaseDate)) {
        [DateTime]::UtcNow.ToString(
            "yyyy-MM-dd",
            [Globalization.CultureInfo]::InvariantCulture)
    }
    else {
        $parsedReleaseDate = [DateTime]::MinValue
        if (-not [DateTime]::TryParseExact(
                $PlannedReleaseDate,
                "yyyy-MM-dd",
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::None,
                [ref]$parsedReleaseDate)) {
            throw "Planned release date must use yyyy-MM-dd format."
        }
        $parsedReleaseDate.ToString(
            "yyyy-MM-dd",
            [Globalization.CultureInfo]::InvariantCulture)
    }
}
else {
    $release = Get-GitHubRelease -RepositoryName $Repository -ReleaseTag $releaseTag
    $installerEntries = Get-ReleaseInstallerEntries `
        -Release $release `
        -DisplayVersion $displayVersion
    $releaseDate = ([DateTimeOffset]$release.published_at).UtcDateTime.ToString(
        "yyyy-MM-dd",
        [Globalization.CultureInfo]::InvariantCulture)
}
$localeMetadataPath = Join-Path $PSScriptRoot "winget-locales.json"
$localeMetadata = Get-WinGetLocaleMetadata -Path $localeMetadataPath
$packageExists = Test-PackageExists -Identifier $PackageIdentifier
$publishMode = Resolve-PublishMode `
    -RequestedMode $Mode `
    -PackageExists $packageExists `
    -Identifier $PackageIdentifier

$resolvedOutputDirectory = if ([string]::IsNullOrWhiteSpace($OutDirectory)) {
    Join-Path $repositoryRoot "artifacts\winget\$releaseTag"
}
else {
    [IO.Path]::GetFullPath($OutDirectory)
}

Write-Step -Message "WinGet publication plan"
Write-Host "Mode              : $publishMode"
Write-Host "Package identifier: $PackageIdentifier"
Write-Host "Display version   : $displayVersion"
Write-Host "Package version   : $packageVersion"
Write-Host "Release tag       : $releaseTag"
Write-Host "Release date      : $releaseDate"
Write-Host "Localized metadata: $($localeMetadata.Count) locales"
Write-Host "Installer source  : $(if ($usesLocalArtifacts) { 'local release artifacts' } else { 'public GitHub release' })"
Write-Host "Output directory  : $resolvedOutputDirectory"
foreach ($installerEntry in $installerEntries) {
    Write-Host (
        "Installer          : {0} -> {1}" -f
        $installerEntry.Architecture,
        $installerEntry.Url)
}

if ($PlanOnly) {
    return
}

if (Test-Path -LiteralPath $resolvedOutputDirectory) {
    Remove-Item -LiteralPath $resolvedOutputDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutputDirectory -Force | Out-Null

$identifierParts = $PackageIdentifier.Split('.')
$manifestRoot = Join-Path $resolvedOutputDirectory (
    "manifests\{0}\{1}\{2}\{3}" -f
    $identifierParts[0].Substring(0, 1).ToLowerInvariant(),
    $identifierParts[0],
    $identifierParts[1],
    $packageVersion)

Write-Step -Message "Generating $publishMode manifests"
New-InitialManifests `
    -ManifestRoot $manifestRoot `
    -Identifier $PackageIdentifier `
    -PackageVersion $packageVersion `
    -ReleaseDate $releaseDate `
    -InstallerEntries $installerEntries
Write-LocalizedManifests `
    -ManifestRoot $manifestRoot `
    -Identifier $PackageIdentifier `
    -PackageVersion $packageVersion `
    -ReleaseTag $releaseTag `
    -Name $packageName `
    -Metadata $localeMetadata

Write-Step -Message "Validating manifests"
Invoke-ManifestValidation -ManifestRoot $manifestRoot

$shouldRunInstallTest = $InstallTest
if (-not $NonInteractive -and -not $InstallTest -and -not $usesLocalArtifacts) {
    $installTestAnswer = Read-Optional `
        -Prompt "Run local install and uninstall test? y/N" `
        -DefaultValue "N"
    $shouldRunInstallTest = $installTestAnswer -match '^(y|yes|д|да)$'
}

if ($shouldRunInstallTest) {
    Write-Step -Message "Testing local installation"
    Invoke-ManifestInstallTest `
        -ManifestRoot $manifestRoot `
        -Identifier $PackageIdentifier `
        -Name $packageName `
        -KeepInstalled $KeepTestInstall
}

$shouldSubmit = $Submit

if (-not $shouldSubmit) {
    Write-Step -Message "Completed locally"
    Write-Host "Manifest path: $manifestRoot"
    return
}

Write-Step -Message "Submitting manifests"
$pullRequestUrl = Submit-Manifests `
    -ManifestRoot $manifestRoot `
    -Identifier $PackageIdentifier `
    -PackageVersion $packageVersion `
    -PublishMode $publishMode

Write-Step -Message "Completed"
if ([string]::IsNullOrWhiteSpace($pullRequestUrl)) {
    Write-Host "PR submitted. Check the WingetCreate output above for its URL."
}
else {
    Write-Host "PR: $pullRequestUrl"
}
Write-Host "Manifest path: $manifestRoot"
