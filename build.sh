#!/usr/bin/env bash

set -euo pipefail

os="linux"
platform="x64"
add_to_path=false

usage() {
	cat <<'EOF'
Usage: ./build.sh [--os win|linux|osx] [--platform x64|arm64] [--add-to-path]
EOF
}

add_publish_directory_to_path() {
	local publish_directory="$1"

	case ":${PATH:-}:" in
		*:"$publish_directory":*)
			echo "Publish directory already exists in PATH: $publish_directory"
			return
			;;
	esac

	export PATH="$publish_directory${PATH:+:$PATH}"

	local shell_name
	local profile_path
	local escaped_publish_directory
	local path_line
	shell_name="$(basename "${SHELL:-bash}")"

	case "$shell_name" in
		bash) profile_path="$HOME/.bashrc" ;;
		zsh) profile_path="$HOME/.zshrc" ;;
		*) profile_path="$HOME/.profile" ;;
	esac

	printf -v escaped_publish_directory '%q' "$publish_directory"
	path_line="case \":\$PATH:\" in *:$escaped_publish_directory:*) ;; *) export PATH=$escaped_publish_directory:\$PATH ;; esac"

	if [[ ! -f "$profile_path" ]] || ! grep -Fqx "$path_line" "$profile_path"; then
		printf '\n# LunaPack local publish\n%s\n' "$path_line" >>"$profile_path"
	fi

	echo "Added publish directory to PATH in $profile_path: $publish_directory"
	echo "Start a new shell or source $profile_path to use luna."
}

while (($# > 0)); do
	case "$1" in
		--os)
			os="${2:-}"
			shift 2
			;;
		--platform)
			platform="${2:-}"
			shift 2
			;;
		--add-to-path)
			add_to_path=true
			shift
			;;
		-h | --help)
			usage
			exit 0
			;;
		*)
			echo "Unknown argument: $1" >&2
			usage >&2
			exit 2
			;;
	esac
done

case "$os" in
	win | linux | osx) ;;
	*)
		echo "Invalid OS '$os'. Expected win, linux, or osx." >&2
		exit 2
		;;
esac

case "$platform" in
	x64 | arm64) ;;
	*)
		echo "Invalid platform '$platform'. Expected x64 or arm64." >&2
		exit 2
		;;
esac

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
project_path="$script_dir/projects/cli/src/Lunapack.Cli/Lunapack.Cli.csproj"
runtime="$os-$platform"

case "$(uname -s)" in
	MINGW* | MSYS* | CYGWIN*) host_os="win" ;;
	Linux*) host_os="linux" ;;
	Darwin*) host_os="osx" ;;
	*) host_os="unknown" ;;
esac

if [[ "$host_os" != "$os" ]]; then
	echo "Native AOT requires a $os host to build '$runtime'." >&2
	exit 1
fi

if [[ "$os" == "win" && "$platform" != "x64" ]]; then
	echo "Unsupported Luna runtime '$runtime'." >&2
	exit 1
fi

dotnet restore "$project_path" --locked-mode

publish_arguments=(
	publish
	--no-restore
	"$project_path"
	-c
	Release
	--self-contained
	--runtime
	"$runtime"
	/p:PublishAot=true
	--output
	"$script_dir/publish"
)

dotnet "${publish_arguments[@]}"

if [[ "$add_to_path" == true ]]; then
	add_publish_directory_to_path "$script_dir/publish"
fi
