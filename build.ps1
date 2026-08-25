[CmdletBinding()]
param(
	[ValidateSet('win', 'linux', 'osx')]
	[string]$Os = 'win',

	[ValidateSet('x64', 'arm64')]
	[string]$Platform = 'x64',

	[switch]$Publish
)

$projectPath = Join-Path $PSScriptRoot 'projects/cli/src/Lunapack.Cli/Lunapack.Cli.csproj'
$runtime = "$Os-$Platform"

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

if ($Publish) {
	$publishArguments += @('--output', (Join-Path $PSScriptRoot 'publish'))
}

& dotnet @publishArguments
exit $LASTEXITCODE
