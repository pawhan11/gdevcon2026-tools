# eol-docker-custom-images

This repository builds predefined Docker images and custom GitHub runner images for CI/CD workflows that require National Instruments TestStand and LabVIEW tooling.

## Use cases

### 1. Docker images for CI builds

Pre-built Windows Docker images with LabVIEW, NI TestStand, the TestStand CLI and the VIPM2 tool, ready to use in CI pipelines. Each image includes the .NET SDK and the NI software, so builds can run without installing anything at runtime.

### 2. Custom GitHub runner image

A snapshot image (`ctc-eol-windows-2025-full`) for self-hosted GitHub Actions runners with all build tools pre-installed, eliminating setup time in workflows.

## Docker images

Built and published to GitHub Container Registry (`ghcr.io/convertertec`). There are two images, differing only in the installed TestStand version.

| Image | Tag | Base image | TestStand version |
|-------|-----|------------|-------------------|
| `ghcr.io/convertertec/ni-docker-builder` | `2025-q3` | `nationalinstruments/labview:2026q3-windows` | 2025 Q3 (25.5) |
| `ghcr.io/convertertec/ni-docker-builder` | `2026-q3` | `nationalinstruments/labview:2026q3-windows` | 2026 Q3 (26.5) |

Both images contain:

| Software | Version | Purpose |
|----------|---------|---------|
| NI LabVIEW | 2026 Q3 | From the NI base image |
| .NET SDK | 10.0.101 | Build eol projects, VIPM2 and TestStand CLI |
| NI Package Manager | 26.5.0 | Install NI TestStand packages |
| NI TestStand | 2025 Q3 or 2026 Q3 | Test sequencing and execution (core, tools, process models, .NET 4.0 runtime support) |
| TestStand CLI | Built from source | CLI for TestStand version/license management |
| VIPM2 | Built from source | CLI for LabVIEW package dependency management |
| CycloneDX CLI | 0.33.1 | Merge, convert and validate CycloneDX SBOMs (`cyclonedx.exe`, spec versions 1.0 to 1.7) |

The Dockerfiles are multi-stage: stage 1 builds `vipm2` and `TeststandCLI` as self-contained win-x64 binaries, stage 2 copies them into `C:\Convertertec\` alongside the downloaded CycloneDX CLI and puts them on `PATH`. TestStand is set active and licensed during the build using the `SERIALNUMBER` build argument.

## Custom runner image: `ctc-eol-windows-2025-full`

Based on `ctc-eol-windows-2025-bare` (Windows Server 2025). The following software is installed:

| Software | Version | Purpose |
|----------|---------|---------|
| Git for Windows | Latest | Source control with LFS support |
| PowerShell Core | 7.4 LTS | Cross-platform scripting |
| .NET SDK | 8.0.413 | Build TestStand CLI (.NET 8 target) |
| .NET SDK | 10.0.101 | Build VIPM2 (.NET 10 target) and eol projects|
| NI Package Manager | 25.5.0 | Install and manage NI software packages |
| NI TestStand 2025 | 25.5 | Test sequencing and execution |
| TestStand CLI | Built from source | CLI for TestStand version/license management |
| NI LabVIEW 2026 | 26.1 | LabVIEW development environment |
| VIPM2 | Built from source | CLI for LabVIEW package dependency management |
| CycloneDX CLI | 0.33.1 | Merge, convert and validate CycloneDX SBOMs (`cyclonedx.exe`, spec versions 1.0 to 1.7) |

## Repository structure

```
scripts/runner/          PowerShell scripts for runner image provisioning
dockerfiles/             Dockerfiles for the ni-docker-builder images
TeststandCLI/            .NET CLI tool for TestStand management
vipm2/                   .NET CLI tool for LabVIEW package management
dlls/                    NI TestStand .NET interop assemblies
.github/workflows/       GitHub Actions workflows
```

## Workflows

| Workflow | Trigger | Purpose |
|----------|---------|---------|
| `create-eol-windows2025-full.yml` | Manual | Build the custom runner snapshot image |
| `create-ni-docker-builder-2025-q3.yml` | Manual | Build and push `ni-docker-builder:2025-q3` |
| `create-ni-docker-builder-2026-q3.yml` | Manual | Build and push `ni-docker-builder:2026-q3` |

## vipm2 commands

`vipm2` works on a project TOML with a `[project]` table (`name`, `version`, `labview-version`) and a `[dependencies]` table of VIPM package name to version.

| Command | Purpose |
|---------|---------|
| `vipm2 install <toml>` | Download the listed packages and their transitive dependencies from the VIPM package network and copy their `vi.lib`/`user.lib` into the LabVIEW installation. |
| `vipm2 build <lvproj> --labview-version <yyyy>` | Run a LabVIEW build specification through the LabVIEW CLI. |
| `vipm2 sbom <toml> [--output <file>]` | Generate a CycloneDX 1.7 JSON software bill of materials. |

### SBOM generation

`vipm2 sbom` describes the software that was actually put on the build machine:

- **VIPM packages** from `[dependencies]` plus their transitive dependencies, resolved through the same NI/JKI package index that `install` uses. Each component carries the package MD5 from the index and, from the package `.spec` file, the license (SPDX id when recognisable), copyright, vendor, description and website. Components use `pkg:vipm/<name>@<version>` package URLs.
- **NI Package Manager packages** installed on the machine, read from `nipkg info-installed`. Each component carries the maintainer, MD5 and SHA-256, homepage, feed, section, architecture and EULA id, and dependencies come from the package `Depends` fields. Components use `pkg:nipkg/<name>@<version>` package URLs, matching what the JKI VIPM CLI emits for NIPM packages.

The output defaults to `sbom.json` next to the TOML. Typical usage in a project build after `vipm2 install`:

```powershell
vipm2 sbom .\dependencies.toml --output .\build\sbom.json --nipkg-pattern "ni-teststand-*" --nipkg-pattern "ni-labview-*"
```

Options:

| Option | Effect |
|--------|--------|
| `--output`, `-o` | Output file path (default `sbom.json` next to the TOML). |
| `--nipkg-pattern <glob>` | Only include installed NI packages whose name matches the pattern (`*`, `?`, `[seq]`, `[!seq]`). Repeatable. Default is every installed package. |
| `--no-nipkg` | Skip NI packages entirely. |
| `--nipkg-path <exe>` | Explicit `nipkg.exe` path (default: `PATH`, then the standard NI Package Manager directory). |
| `--no-transitive` | Only list the packages declared in `[dependencies]`. |
| `--no-spec` | Do not download `.spec` files (faster, but no license, vendor, copyright or website for VIPM packages). |
| `--product-name`, `--product-version`, `--product-supplier` | Override the root component described by the SBOM (defaults come from `[project]`). |
