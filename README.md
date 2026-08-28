# LunaPack

![GitHub release](https://img.shields.io/github/v/release/lunarisdigitalsolutions/lunapack)
![NuGet downloads](https://img.shields.io/nuget/dt/Lunaris.Lunapack.Luna?label=NuGet%20downloads)
![npm downloads](https://img.shields.io/npm/dy/%40lunarisdigitalsolutions%2Flunapack?style=flat&label=npm%20downloads)
![Container downloads](https://img.shields.io/github/downloads/lunarisdigitalsolutions/lunapack/total?label=Container%20downloads)

![LunaPack Logo](docs/branding/logos/logo-white-bg.png)

LunaPack is a command-line tool and manifest format for applying versioned sets
of files to software repositories. A pack can contain managed files, parameters,
references to other packs, and opt-in lifecycle scripts. Luna records requested
packs and resolved ownership so updates and removals remain explicit.

Current code supports local and Git-backed sources, discovery, validation,
inspection, installation, update, audit, and removal. LunaPack does not provide
a hosted registry or sandbox pack scripts.

Luna ships for Windows x64, Linux x64, Linux Arm64, macOS x64, and macOS Arm64
through release archives, npm, and a .NET tool package. A Linux x64 OCI image
is available from GitHub Container Registry.

Start with the [public documentation](docs/developer/index.md), including
[installation](docs/developer/installation.md) and the
[CLI reference](docs/developer/cli/commands.md).

## Repository Structure

```text
.agents/                 Local agent skills and guidance
.github/                 GitHub automation, prompts, and skills
.husky/                  Git hooks for documentation checks
docs/                    Product, internal, and developer documentation
openspec/                OpenSpec configuration, changes, and specifications
projects/                CLI solution, schemas, and local sample packs
package.json             Pinned documentation tooling and scripts
```

## Documentation

Start at [the documentation index](docs/index.md).

| Area      | Reader                                      | Start Here                                                   |
| --------- | ------------------------------------------- | ------------------------------------------------------------ |
| Product   | Product and business stakeholders           | [Product overview](docs/product/prd/000-product-overview.md) |
| Internal  | LunaPack maintainers                        | [Internal documentation](docs/internal/index.md)             |
| Developer | CLI users, pack authors, and pack consumers | [Developer documentation](docs/developer/index.md)           |

## Documentation Tooling

Install pinned development dependencies and Husky hooks after cloning:

```bash
npm ci
```

`npm install` runs the `prepare` script, which configures Husky. To restore hooks after cloning with lifecycle scripts disabled, run:

```bash
npm run prepare
```

The pre-commit hook runs Markdownlint for staged files under `docs/`, CSharpier and .NET using-directive cleanup for staged C# files, and Prettier for staged JSON, CSS, SCSS, HTML, YAML, and YML files. Prettier uses the repository's `prettier-config-standard` configuration. Review and stage any fixes before committing.

```bash
npm run lint:docs
npm run format:docs
npm run format:prettier
```

## CLI Development

The implemented CLI foundation requires the SDK pinned in `global.json`. Run these commands from the repository root:

```bash
dotnet build projects/cli/src/Lunapack.slnx
dotnet test --solution projects/cli/src/Lunapack.slnx
```

Keep public usage guidance in `docs/developer`, maintainer-only technical
decisions in `docs/internal`, and product direction in `docs/product`. See
[Contributing](CONTRIBUTING.md), [Support](SUPPORT.md), and
[Security](SECURITY.md) before opening a report or pull request.
