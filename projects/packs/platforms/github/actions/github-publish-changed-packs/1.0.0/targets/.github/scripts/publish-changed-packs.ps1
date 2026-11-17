[CmdletBinding()]
param(
    [string] $ChangedFilesJson = '',
    [Parameter(Mandatory)]
    [ValidateSet('generic', 'acr', 'ghcr')]
    [string] $Target,
    [switch] $IncludeAllPacks
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Get-Location).Path)
$packsRoot = Join-Path $repositoryRoot 'packs'
if (-not (Test-Path -LiteralPath $packsRoot -PathType Container)) {
    throw "Pack root '$packsRoot' does not exist."
}

$packsRootItem = Get-Item -LiteralPath $packsRoot -Force
if (($packsRootItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "Pack root '$packsRoot' must not be a symbolic link."
}

$publishablePacks = [System.Collections.Generic.SortedSet[string]]::new(
    [System.StringComparer]::Ordinal
)

function Add-PublishablePack {
    param([Parameter(Mandatory)][string] $PackName)

    if (
        [string]::IsNullOrWhiteSpace($PackName) -or
        $PackName -in @('.', '..') -or
        $PackName.Contains('/') -or
        $PackName.Contains('\')
    ) {
        throw "Invalid pack directory name: '$PackName'."
    }

    $packDirectory = Join-Path $packsRoot $PackName
    if (-not (Test-Path -LiteralPath $packDirectory -PathType Container)) {
        return
    }

    $packDirectoryItem = Get-Item -LiteralPath $packDirectory -Force
    if (
        ($packDirectoryItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
    ) {
        return
    }

    $manifestPath = Join-Path $packDirectory 'pack.yml'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        return
    }

    $manifestItem = Get-Item -LiteralPath $manifestPath -Force
    if (($manifestItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        return
    }

    $null = $publishablePacks.Add("packs/$PackName")
}

if ($IncludeAllPacks) {
    foreach ($packDirectory in Get-ChildItem -LiteralPath $packsRoot -Directory -Force) {
        Add-PublishablePack -PackName $packDirectory.Name
    }
}
elseif (-not [string]::IsNullOrWhiteSpace($ChangedFilesJson)) {
    if (-not $ChangedFilesJson.TrimStart().StartsWith('[')) {
        throw 'Changed-files output must be a JSON array.'
    }

    try {
        $changedFiles = @(ConvertFrom-Json -InputObject $ChangedFilesJson)
    }
    catch {
        throw "Unable to parse changed-files JSON: $($_.Exception.Message)"
    }

    foreach ($changedFile in $changedFiles) {
        if ($changedFile -isnot [string] -or [string]::IsNullOrWhiteSpace($changedFile)) {
            throw 'Changed-files output contains an invalid path.'
        }

        $normalizedPath = $changedFile.Replace('\', '/')
        if ($normalizedPath.StartsWith('/') -or $normalizedPath -match '^[A-Za-z]:') {
            throw "Changed file path must be relative: '$changedFile'."
        }

        $pathParts = @($normalizedPath -split '/' | Where-Object { $_.Length -gt 0 })
        if ($pathParts | Where-Object { $_ -in '.', '..' }) {
            throw "Changed file path contains a traversal segment: '$changedFile'."
        }

        if ($pathParts.Count -lt 3 -or $pathParts[0] -cne 'packs') {
            continue
        }

        Add-PublishablePack -PackName $pathParts[1]
    }
}

if ($publishablePacks.Count -eq 0) {
    Write-Host 'No changed pack directories contain a publishable pack.yml.'
    return
}

foreach ($packDirectory in $publishablePacks) {
    Write-Host "Publishing $packDirectory to $Target."
    & luna publish $packDirectory --target $Target
    $publishExitCode = $LASTEXITCODE
    if ($publishExitCode -ne 0) {
        throw "luna publish failed for '$packDirectory' with exit code $publishExitCode."
    }
}
