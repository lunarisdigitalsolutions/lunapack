#Requires -PSEdition Core

[CmdletBinding()]
param(
    [ValidatePattern('^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$')]
    [string]$Version
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Invoke-Git {
    [OutputType([string[]])]
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    $output = @(& git @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $message = "git $($Arguments -join ' ') failed with exit code $exitCode."
        if ($output.Count -gt 0) {
            $message += "`n$($output -join [Environment]::NewLine)"
        }

        throw $message
    }

    return $output
}

function Confirm-Action {
    param(
        [Parameter(Mandatory)]
        [string]$Action
    )

    while ($true) {
        [string]$response = Read-Host "$Action [y/n]"
        switch ($response.Trim().ToLowerInvariant()) {
            'y' { return $true }
            'yes' { return $true }
            'n' { return $false }
            'no' { return $false }
            default { Write-Information 'Enter y or n.' }
        }
    }
}

function Select-ReleaseChangeType {
    while ($true) {
        [string]$response = Read-Host 'Change type: breaking change [major/ma], new functionality [minor/mi], or small change [patch/p]'
        switch ($response.Trim().ToLowerInvariant()) {
            'major' { return 'major' }
            'ma' { return 'major' }
            'minor' { return 'minor' }
            'mi' { return 'minor' }
            'patch' { return 'patch' }
            'p' { return 'patch' }
            default { Write-Information 'Enter major (ma), minor (mi), or patch (p).' }
        }
    }
}

function Compare-SemanticVersion {
    param(
        [Parameter(Mandatory)]
        [string]$Left,
        [Parameter(Mandatory)]
        [string]$Right
    )

    $leftParts = $Left.Split('.')
    $rightParts = $Right.Split('.')
    for ($index = 0; $index -lt 3; $index++) {
        $comparison = [System.Numerics.BigInteger]::Parse($leftParts[$index]).CompareTo(
            [System.Numerics.BigInteger]::Parse($rightParts[$index]))
        if ($comparison -ne 0) {
            return $comparison
        }
    }

    return 0
}

function Get-NextSemanticVersion {
    param(
        [Parameter(Mandatory)]
        [string]$Baseline,
        [Parameter(Mandatory)]
        [ValidateSet('major', 'minor', 'patch')]
        [string]$ChangeType
    )

    $parts = $Baseline.Split('.')
    $major = [System.Numerics.BigInteger]::Parse($parts[0])
    $minor = [System.Numerics.BigInteger]::Parse($parts[1])
    $patch = [System.Numerics.BigInteger]::Parse($parts[2])
    switch ($ChangeType) {
        'major' { return "$($major + 1).0.0" }
        'minor' { return "$major.$($minor + 1).0" }
        'patch' { return "$major.$minor.$($patch + 1)" }
    }
}

function Get-MinVerVersion {
    param(
        [Parameter(Mandatory)]
        [ValidateSet('major', 'minor', 'patch')]
        [string]$ChangeType
    )

    $output = @(& dotnet minver . --tag-prefix v --auto-increment $ChangeType --verbosity error 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "MinVer failed with exit code $exitCode.`n$($output -join [Environment]::NewLine)"
    }

    if ($output.Count -eq 0) {
        throw 'MinVer completed without returning a version.'
    }

    $minVerVersion = ([string]$output[0]).Trim()
    if ($minVerVersion -notmatch "^(?<version>$semanticVersionPattern)(?:-|\+|`$)") {
        throw "MinVer returned an invalid semantic version: '$minVerVersion'."
    }

    return $Matches['version']
}

function Get-UnreleasedReleaseNotes {
    param(
        [Parameter(Mandatory)]
        [System.Text.RegularExpressions.Match]$UnreleasedSection
    )

    $unreleasedContent = $UnreleasedSection.Groups['content'].Value.Trim()
    $unreleasedTemplate = 'Update this section before creating a release tag.'
    if ($unreleasedContent.StartsWith($unreleasedTemplate, [System.StringComparison]::Ordinal)) {
        return $unreleasedContent.Substring($unreleasedTemplate.Length).Trim()
    }

    return $unreleasedContent
}

function Test-UnreleasedHasReleaseNotes {
    param(
        [Parameter(Mandatory)]
        [System.Text.RegularExpressions.Match]$UnreleasedSection
    )

    return -not [string]::IsNullOrWhiteSpace((Get-UnreleasedReleaseNotes -UnreleasedSection $UnreleasedSection))
}

function Test-GitPathChanged {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    & git diff --quiet -- $Path
    $exitCode = $LASTEXITCODE
    switch ($exitCode) {
        0 { return $false }
        1 { return $true }
        default { throw "Unable to check for changes in '$Path'; git diff exited with code $exitCode." }
    }
}

function Add-ChangelogSection {
    param(
        [Parameter(Mandatory)]
        [string]$Path,
        [Parameter(Mandatory)]
        [string]$Changelog,

        [Parameter(Mandatory)]
        [int]$InsertionIndex,

        [Parameter(Mandatory)]
        [string]$ReleaseVersion,

        [Parameter(Mandatory)]
        [System.Text.RegularExpressions.Match]$UnreleasedSection,

        [switch]$CopyUnreleased
    )

    $newLine = if ($Changelog.Contains("`r`n")) { "`r`n" } else { "`n" }
    $date = Get-Date -Format 'yyyy-MM-dd'
    $unreleasedTemplate = 'Update this section before creating a release tag.'
    $releaseNotes = '<!-- Add consumer-facing release notes before rerunning New-Release.ps1. Exclude internal maintenance work. -->'
    if ($CopyUnreleased) {
        $releaseNotes = Get-UnreleasedReleaseNotes -UnreleasedSection $UnreleasedSection
        if ([string]::IsNullOrWhiteSpace($releaseNotes)) {
            $releaseNotes = '<!-- Add consumer-facing release notes before rerunning New-Release.ps1. Exclude internal maintenance work. -->'
        }
    }

    $section = "## Version $ReleaseVersion - $date$newLine$newLine$releaseNotes$newLine$newLine"
    if ($CopyUnreleased) {
        $updatedChangelog = $Changelog.Substring(0, $UnreleasedSection.Index) +
            "## Unreleased$newLine$newLine$unreleasedTemplate$newLine$newLine" +
            $section +
            $Changelog.Substring($InsertionIndex)
    }
    else {
        $updatedChangelog = $Changelog.Insert($InsertionIndex, $section)
    }

    [System.IO.File]::WriteAllText(
        $Path,
        $updatedChangelog,
        [System.Text.UTF8Encoding]::new($false))
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$changelogPath = Join-Path $repositoryRoot 'CHANGELOG.md'
$semanticVersionPattern = '(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)\.(?:0|[1-9]\d*)'
$releaseHeadingPattern = "(?m)^## Version (?<version>$semanticVersionPattern) - .+$"
$unreleasedSectionPattern = '(?ms)^## Unreleased\r?\n(?<content>.*?)(?=^## Version )'

Push-Location $repositoryRoot
try {
    $isInsideWorkTree = ([string](Invoke-Git -Arguments @('rev-parse', '--is-inside-work-tree'))).Trim()
    if ($isInsideWorkTree -ne 'true') {
        throw 'Release script must run inside a Git worktree.'
    }

    $branch = ([string](Invoke-Git -Arguments @('branch', '--show-current'))).Trim()
    if ($branch -ne 'main') {
        throw "Release script requires the main branch; current branch is '$branch'."
    }

    $stagedFiles = @(
        Invoke-Git -Arguments @('diff', '--cached', '--name-only') |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($stagedFiles.Count -gt 0) {
        throw 'Release script requires an empty Git index. Commit or unstage existing changes first.'
    }

    Invoke-Git -Arguments @('fetch', 'origin', 'main') | Out-Null
    $localMain = ([string](Invoke-Git -Arguments @('rev-parse', 'main'))).Trim()
    $remoteMain = ([string](Invoke-Git -Arguments @('rev-parse', 'origin/main'))).Trim()
    if ($localMain -ne $remoteMain) {
        throw 'Local main must match origin/main before creating a release.'
    }

    $changelog = Get-Content -LiteralPath $changelogPath -Raw
    $releaseMatches = [regex]::Matches($changelog, $releaseHeadingPattern)
    if ($releaseMatches.Count -eq 0) {
        throw 'CHANGELOG.md contains no stable release headings.'
    }

    $unreleasedSection = [regex]::Match($changelog, $unreleasedSectionPattern)
    if (-not $unreleasedSection.Success) {
        throw 'CHANGELOG.md must contain an Unreleased section before the first release heading.'
    }

    if ($PSBoundParameters.ContainsKey('Version')) {
        $releaseVersion = $Version
    }
    else {
        $changeType = Select-ReleaseChangeType
        $minVerVersion = Get-MinVerVersion -ChangeType $changeType
        $latestChangelogVersion = $releaseMatches[0].Groups['version'].Value

        if ((Compare-SemanticVersion -Left $minVerVersion -Right $latestChangelogVersion) -gt 0) {
            $releaseVersion = $minVerVersion
        }
        else {
            $releaseVersion = Get-NextSemanticVersion -Baseline $latestChangelogVersion -ChangeType $changeType
            Write-Warning "MinVer produced '$minVerVersion', which is not newer than changelog version '$latestChangelogVersion'. Using changelog-based version '$releaseVersion'."
        }

        Write-Information "Derived release version $releaseVersion."
    }

    $releaseTag = "v$releaseVersion"
    $matchingReleaseIndex = -1
    for ($index = 0; $index -lt $releaseMatches.Count; $index++) {
        if ($releaseMatches[$index].Groups['version'].Value -eq $releaseVersion) {
            $matchingReleaseIndex = $index
            break
        }
    }

    if ($matchingReleaseIndex -lt 0) {
        $latestChangelogVersion = $releaseMatches[0].Groups['version'].Value
        if ((Compare-SemanticVersion -Left $releaseVersion -Right $latestChangelogVersion) -le 0) {
            throw "Version '$releaseVersion' must be greater than current changelog version '$latestChangelogVersion'."
        }

        if (-not (Confirm-Action -Action "Add a CHANGELOG.md section for version $($releaseVersion)?")) {
            Write-Information 'Release cancelled without changing CHANGELOG.md.'
            return
        }

        $copyUnreleased = $false
        if (Test-UnreleasedHasReleaseNotes -UnreleasedSection $unreleasedSection) {
            $copyUnreleased = Confirm-Action -Action "Copy Unreleased changelog entries to version $releaseVersion and clear Unreleased?"
        }

        Add-ChangelogSection -Path $changelogPath -Changelog $changelog -InsertionIndex $releaseMatches[0].Index -ReleaseVersion $releaseVersion -UnreleasedSection $unreleasedSection -CopyUnreleased:$copyUnreleased
        Write-Information "Added CHANGELOG.md section for version $releaseVersion. Fill in its release notes, then rerun this script."
        return
    }

    if ($matchingReleaseIndex -ne 0) {
        throw "Version '$releaseVersion' must be the first release heading in CHANGELOG.md."
    }

    $releaseSectionEnd = if ($releaseMatches.Count -gt 1) { $releaseMatches[1].Index } else { $changelog.Length }
    $releaseSection = $changelog.Substring(
        $releaseMatches[0].Index + $releaseMatches[0].Length,
        $releaseSectionEnd - ($releaseMatches[0].Index + $releaseMatches[0].Length))
    if ([string]::IsNullOrWhiteSpace($releaseSection) -or $releaseSection.Contains('<!-- Add consumer-facing release notes before rerunning New-Release.ps1. Exclude internal maintenance work. -->')) {
        throw "CHANGELOG.md release section '$releaseVersion' needs release notes before it can be published."
    }

    if ($releaseMatches.Count -gt 1) {
        $previousChangelogVersion = $releaseMatches[1].Groups['version'].Value
        if ((Compare-SemanticVersion -Left $releaseVersion -Right $previousChangelogVersion) -le 0) {
            throw "Version '$releaseVersion' must be greater than previous changelog version '$previousChangelogVersion'."
        }
    }

    & git show-ref --verify --quiet "refs/tags/$releaseTag"
    $tagCheckExitCode = $LASTEXITCODE
    if ($tagCheckExitCode -eq 0) {
        throw "Release tag '$releaseTag' already exists locally."
    }

    if ($tagCheckExitCode -ne 1) {
        throw "Unable to check whether release tag '$releaseTag' exists locally."
    }

    $remoteTags = @(Invoke-Git -Arguments @('ls-remote', '--tags', 'origin', "refs/tags/$releaseTag"))
    if ($remoteTags.Count -gt 0) {
        throw "Release tag '$releaseTag' already exists on origin."
    }

    if (Test-GitPathChanged -Path 'CHANGELOG.md') {
        if (-not (Confirm-Action -Action "Create commit 'release: Release version $releaseVersion' containing CHANGELOG.md?")) {
            Write-Information 'Release cancelled before commit.'
            return
        }

        Invoke-Git -Arguments @('commit', '--only', 'CHANGELOG.md', '-m', "release: Release version $releaseVersion") | Out-Null

        if (-not (Confirm-Action -Action 'Push main to origin?')) {
            Write-Information "Release commit created locally. Push main before creating tag '$releaseTag'."
            return
        }

        Invoke-Git -Arguments @('push', 'origin', 'main') | Out-Null
    }
    else {
        Write-Information 'CHANGELOG.md is unchanged; skipping release commit and main push.'
    }

    if (-not (Confirm-Action -Action "Create annotated tag '$releaseTag'?")) {
        Write-Information "Main was pushed. Create and push tag '$releaseTag' to finish the release."
        return
    }

    Invoke-Git -Arguments @('tag', '-a', $releaseTag, '-m', "Release version $releaseVersion") | Out-Null

    if (-not (Confirm-Action -Action "Push tag '$releaseTag' to origin?")) {
        Write-Information "Tag '$releaseTag' exists locally. Push it with: git push origin $releaseTag"
        return
    }

    Invoke-Git -Arguments @('push', 'origin', $releaseTag) | Out-Null
    Write-Information "Released version $releaseVersion with tag $releaseTag."
}
finally {
    Pop-Location
}
