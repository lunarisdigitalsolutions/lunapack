# Release and package process

Maintainers prepare releases with `scripts/New-Release.ps1`. The script derives
a Semantic Version change from reviewed changelog content, validates the CLI,
creates a release commit and `v<version>` tag, and leaves publication to the
tag-triggered GitHub workflow.

The workflow must build every advertised runtime from the same source revision
and version, validate package contents, then prepare matching GitHub archive,
npm, NuGet, and container artifacts. Publication credentials belong only to the
release job. Contributors must not publish artifacts from pull requests.

Before tagging, maintainers run locked restores, Release builds, all tests,
Native AOT publishing, schema and pack validation, documentation and website
builds, dependency and secret scans, package inspection, and distribution smoke
tests. Release notes and retained security records document consumer changes
and remaining risk.
