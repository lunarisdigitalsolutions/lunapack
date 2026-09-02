#!/usr/bin/env bash

set -euo pipefail

configuration="Debug"
suites=()

usage() {
	cat <<'EOF'
Usage: ./test.sh [unit|int|security ...] [--configuration Debug|Release]
EOF
}

while (($# > 0)); do
	case "$1" in
		--configuration)
			configuration="${2:-}"
			shift 2
			;;
		-h | --help)
			usage
			exit 0
			;;
		unit | int | security)
			suites+=("$1")
			shift
			;;
		*)
			echo "Unknown argument: $1" >&2
			usage >&2
			exit 2
			;;
	esac
done

case "$configuration" in
	Debug | Release) ;;
	*)
		echo "Invalid configuration '$configuration'. Expected Debug or Release." >&2
		exit 2
		;;
esac

if ((${#suites[@]} == 0)); then
	suites=(unit int security)
fi

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

for suite_name in "${suites[@]}"; do
	case "$suite_name" in
		unit) test_project='Lunapack.Cli.UnitTests/Lunapack.Cli.UnitTests.csproj' ;;
		int) test_project='Lunapack.Cli.IntegrationTests/Lunapack.Cli.IntegrationTests.csproj' ;;
		security) test_project='Lunapack.Cli.SecurityTests/Lunapack.Cli.SecurityTests.csproj' ;;
	esac

	project_path="$script_dir/projects/cli/src/$test_project"

	dotnet restore "$project_path" --locked-mode
	dotnet test --project "$project_path" --configuration "$configuration" --no-restore
done
