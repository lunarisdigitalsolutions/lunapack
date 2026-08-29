[CmdletBinding()]
param(
	[Parameter(Position = 0)]
	[ValidateSet('unit', 'int', 'security')]
	[string[]]$Suite = @('unit', 'int', 'security'),

	[ValidateSet('Debug', 'Release')]
	[string]$Configuration = 'Debug'
)

$testProjects = @{
	unit = 'Lunapack.Cli.UnitTests/Lunapack.Cli.UnitTests.csproj'
	int = 'Lunapack.Cli.IntegrationTests/Lunapack.Cli.IntegrationTests.csproj'
	security = 'Lunapack.Cli.SecurityTests/Lunapack.Cli.SecurityTests.csproj'
}

foreach ($suiteName in $Suite) {
	$projectPath = Join-Path $PSScriptRoot "projects/cli/src/$($testProjects[$suiteName])"

	& dotnet restore $projectPath --locked-mode
	if ($LASTEXITCODE -ne 0) {
		exit $LASTEXITCODE
	}

	& dotnet test --project $projectPath --configuration $Configuration --no-restore
	if ($LASTEXITCODE -ne 0) {
		exit $LASTEXITCODE
	}
}
