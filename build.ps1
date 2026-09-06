[CmdletBinding()]
param(
	[ValidateSet('win', 'linux', 'osx')]
	[string]$Os = 'win',

	[ValidateSet('x64', 'arm64')]
	[string]$Platform = 'x64',

	[switch]$AddToPath
)

$projectPath = Join-Path $PSScriptRoot 'projects/cli/src/Lunapack.Cli/Lunapack.Cli.csproj'
$publishPath = Join-Path $PSScriptRoot 'publish'
$runtime = "$Os-$Platform"

function Test-PathEntry {
	param(
		[string]$PathValue,
		[string]$Entry
	)

	$comparison = if ($IsWindows) {
		[System.StringComparison]::OrdinalIgnoreCase
	} else {
		[System.StringComparison]::Ordinal
	}
	$trimCharacters = [char[]]@(
		[System.IO.Path]::DirectorySeparatorChar,
		[System.IO.Path]::AltDirectorySeparatorChar
	)
	$normalizedEntry = $Entry.TrimEnd($trimCharacters)

	foreach ($pathEntry in $PathValue -split [System.IO.Path]::PathSeparator) {
		if ([string]::Equals($pathEntry.TrimEnd($trimCharacters), $normalizedEntry, $comparison)) {
			return $true
		}
	}

	return $false
}

if (
	($IsWindows -and $Os -ne 'win') -or
	($IsLinux -and $Os -ne 'linux') -or
	($IsMacOS -and $Os -ne 'osx')
) {
	throw "Native AOT requires a $Os host to build '$runtime'."
}

if ($Os -eq 'win' -and $Platform -ne 'x64') {
	throw "Unsupported Luna runtime '$runtime'."
}

$commonArguments = @(
	$projectPath
	'-c'
	'Release'
	'--self-contained'
	'--runtime'
	$runtime
)

$restoreArguments = @(
	'restore'
	$projectPath
	'--locked-mode'
)

& dotnet @restoreArguments
if ($LASTEXITCODE -ne 0) {
	exit $LASTEXITCODE
}

$publishArguments = @(
	'publish'
	'--no-restore'
)
$publishArguments += $commonArguments
$publishArguments += '/p:PublishAot=true'
$publishArguments += @('--output', $publishPath)

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
	exit $LASTEXITCODE
}

if ($AddToPath) {
	if (Test-PathEntry -PathValue $env:PATH -Entry $publishPath) {
		Write-Host "Publish directory already exists in PATH: $publishPath"
	} else {
		$separator = [System.IO.Path]::PathSeparator
		$env:PATH = "$publishPath$separator$env:PATH"

		if ($IsWindows) {
			$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
			if (-not (Test-PathEntry -PathValue $userPath -Entry $publishPath)) {
				$updatedUserPath = if ([string]::IsNullOrWhiteSpace($userPath)) {
					$publishPath
				} else {
					"$publishPath$separator$userPath"
				}
				[Environment]::SetEnvironmentVariable('Path', $updatedUserPath, 'User')
			}
		}

		Write-Host "Added publish directory to PATH: $publishPath"
	}
}

exit 0
