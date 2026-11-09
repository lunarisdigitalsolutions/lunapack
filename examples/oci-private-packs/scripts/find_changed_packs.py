#!/usr/bin/env python3

from pathlib import Path, PurePosixPath
import argparse
import os
import subprocess
import sys


def git_output(*arguments: str) -> bytes:
    result = subprocess.run(
        ["git", *arguments],
        check=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    return result.stdout


def changed_paths(before: str, after: str) -> list[bytes]:
    if not after.strip():
        raise ValueError("the pushed revision is required")
    if not before or set(before) == {"0"}:
        return git_output("ls-tree", "--name-only", "-r", "-z", after, "--", "packs")
    return git_output(
        "diff", "--no-renames", "--name-only", "-z", before, after, "--", "packs"
    )


def changed_pack_directories(repository: Path, paths: bytes) -> list[str]:
    pack_directories: set[str] = set()
    for changed_path in paths.split(b"\0"):
        if not changed_path:
            continue
        path = PurePosixPath(os.fsdecode(changed_path))
        if len(path.parts) < 3 or path.parts[0] != "packs":
            continue
        directory = Path("packs", path.parts[1])
        pack_directory = repository / directory
        manifest = pack_directory / "pack.yml"
        if (
            pack_directory.is_dir()
            and not pack_directory.is_symlink()
            and manifest.is_file()
            and not manifest.is_symlink()
        ):
            pack_directories.add(directory.as_posix())
    return sorted(pack_directories, key=os.fsencode)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Write changed existing direct-child pack folders as NUL-delimited paths."
    )
    parser.add_argument("before", help="previous commit SHA, or empty/all-zero for first push")
    parser.add_argument("after", help="pushed commit SHA")
    arguments = parser.parse_args()

    try:
        paths = changed_paths(arguments.before, arguments.after)
        packs = changed_pack_directories(Path.cwd(), paths)
    except subprocess.CalledProcessError as error:
        detail = error.stderr.decode("utf-8", errors="replace").strip()
        message = f"Git could not compare the requested revisions (exit {error.returncode})."
        if detail:
            message = f"{message} {detail}"
        print(message, file=sys.stderr)
        return 1
    except (OSError, ValueError) as error:
        print(f"Unable to determine changed pack folders: {error}", file=sys.stderr)
        return 1

    sys.stdout.buffer.write(b"".join(os.fsencode(pack) + b"\0" for pack in packs))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
