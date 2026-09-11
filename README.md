# G PC Health Check

Windows 11 x64 Service Desk utility for workstation diagnostics, explainable health assessment, before/after reporting, and a deliberately small set of controlled remediation actions.

Current project version: **0.4.2**. The application is a self-contained single-file `G-PC-Health-Check.exe`.

> **Security / privacy:** never attach an unreviewed `G-PC-Health-Check-E2E-*.zip` to a public Issue or Pull Request. E2E evidence can contain workstation names, usernames, domain information, hardware/OS details and recent diagnostic reports. See [`SECURITY.md`](SECURITY.md).

## What 0.4.2 adds

Version 0.4.2 improves diagnostic confidence for disk bottlenecks. A high disk queue no longer creates WARN/CRIT by itself: the assessment now requires sustained queue pressure together with high median disk busy percentage, and incomplete busy/queue telemetry reduces diagnostic coverage instead of being treated as evidence of a bottleneck.

The application:

- collects CPU, RAM, logical/physical disk, Windows/build, process, Event Log, startup, security-product, network and Windows Update signals;
- calculates diagnostic coverage so missing telemetry is not shown as “healthy”;
- shows diagnostic coverage directly on the dashboard and colors degraded coverage;
- summarizes the primary issue, CRIT/WARN counts and next Service Desk step in a dedicated triage card;
- exposes missing diagnostic signals in the System view and report outputs;
- can copy a concise Service Desk summary to the clipboard;
- shows elapsed scan time without changing the mouse cursor to a global busy state;
- samples CPU, disk busy and disk queue repeatedly and uses medians to reduce transient false positives;
- classifies disk pressure only when both queue depth and disk busy are elevated;
- groups repeated Windows events by Provider/Event ID;
- sorts process/event numeric columns using typed values rather than formatted strings;
- creates HTML/JSON before/after reports;
- offers only an explicit allow-list of automated remediation actions.

## Remediation security boundary

### Non-privileged

The following can run from a normal user context:

- diagnostics and reports;
- `CleanTemp` — only old ordinary files under `%LOCALAPPDATA%\Temp` of the interactive user;
- `FlushDns`.

`CleanTemp` does not clean `%WINDIR%\Temp`, Prefetch or other system directories, does not traverse reparse points/junctions/symlinks, and fails closed if the GUI itself is already elevated.

### Administrative

`DISM /Online /Cleanup-Image /RestoreHealth` and `SFC /scannow` require UAC. The elevated worker is allowed to run only from the exact canonical path:

`%ProgramFiles%\G\PCHealthCheck\G-PC-Health-Check.exe`

The privileged worker:

- validates the canonical executable path and rejects reparse points;
- accepts only the hardcoded action IDs `FlushDns`, `Dism`, `Sfc`;
- does **not** accept `CleanTemp`;
- rejects unknown and mixed known/unknown action lists;
- uses a one-time session ID + nonce over a local named pipe;
- keeps legacy bootstrap from user-writable locations disabled.

Detailed model: [`docs/SECURITY.md`](docs/SECURITY.md).

## Build and CI

`Windows EXE` runs on pushes to `main`, pull requests and manual dispatch. It uses read-only repository permissions and pinned GitHub Actions. The pipeline includes:

- PowerShell parser/smoke gates;
- deterministic brand-asset generation and SHA-256 provenance check;
- .NET restore and NuGet vulnerability audit including transitive dependencies;
- warnings-as-errors build;
- source and published-single-EXE self-tests;
- privileged-worker negative security tests;
- exact FileVersion verification;
- SHA-256 generation;
- pilot-bundle creation and strict UTF-8 validation.

`main` is protected by an active repository ruleset: changes require a Pull Request, `build`, `analyzer` and `supply-chain-smoke` must pass against the current base branch, deletion and non-fast-forward updates are blocked, and linear history is required.

## Release and artifact provenance

A successful `Windows EXE` **push build on `main`** triggers `Publish GitHub Release`. The release workflow re-validates that its triggering run is a successful `push` on `main`, checks out the exact tested SHA, downloads artifacts from that exact run and re-verifies the EXE checksum before publishing.

The same trusted build event also triggers `Supply Chain Attestations`. That workflow:

- independently re-validates the source build run and exact commit SHA;
- downloads the exact EXE and pilot artifacts produced by that build;
- verifies the EXE SHA-256 again;
- restores only dependency metadata on Linux with Windows targeting explicitly enabled for component detection;
- generates an SPDX 2.2 SBOM with the pinned Microsoft SBOM Tool;
- generates signed GitHub Artifact Attestations using Sigstore for EXE and pilot build provenance;
- binds the SPDX SBOM to the EXE with a signed SBOM attestation;
- retains the SBOM and its SHA-256 as a dedicated Actions artifact.

Attestations for public-repository builds can be verified with GitHub CLI, for example:

```powershell
gh attestation verify G-PC-Health-Check.exe --repo bajoicheg/g-pc-health-check
```

Existing release tags are never overwritten automatically.

## Building locally

Prerequisites: Windows 11 x64 and .NET 8 SDK.

```powershell
dotnet restore src/G.PcHealthCheck/G.PcHealthCheck.csproj
dotnet build src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -warnaserror
dotnet run --project src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -- --selftest
```

Single-file publish:

```powershell
dotnet publish src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md). Public bug reports must use synthetic or redacted data. Security vulnerabilities should be reported privately as described in [`SECURITY.md`](SECURITY.md).

## License and branding

Source code is licensed under the **Apache License 2.0**; see [`LICENSE`](LICENSE).

The G name, G-shield artwork and related branding assets are **not** granted under Apache-2.0 and remain reserved to their respective owner(s). See [`NOTICE`](NOTICE) for the branding exception.

## Code signing

0.4.0 provides cryptographic build/SBOM provenance, but the Windows PE itself is not yet Authenticode-signed. For managed enterprise deployment, validate the published checksum and GitHub attestation and use an approved software-distribution channel. Authenticode signing and publisher enforcement through AppLocker/WDAC/EDR remain recommended before broad deployment.
