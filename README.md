# G PC Health Check

Windows 11 x64 Service Desk utility for workstation diagnostics, explainable health assessment, before/after reporting, and controlled remediation.

Current project version: **0.5.2**. The application is a self-contained single-file `G-PC-Health-Check.exe`.

> **Security / privacy:** never publish unreviewed diagnostic reports. Evidence may contain workstation/user/domain names, hardware details, device names and network addresses. See [`SECURITY.md`](SECURITY.md).

## What 0.5.2 fixes

**DISM/SFC can now be launched from any EXE location and under any EXE filename**, including Downloads, a portable tools directory and a renamed copy. Neither installation nor a fixed Program Files path is required. The previous path/name checks have been removed from both the GUI dispatch path and the worker entry point.

Start the application normally, choose the required actions and confirm them. Administrative actions launch the same current executable with Windows UAC; user Temp cleanup remains in the original standard-user process. Moving or renaming the EXE must be done while the application is closed. Windows access permissions, UAC and enterprise execution policies still apply; a network path must be accessible to the administrative identity as well.

The worker still accepts only its fixed action IDs and validates the session, pipe name, nonce and administrative token. It does not copy/install itself or use an external shell/helper. The legacy bootstrap is not used. The UAC child uses the current absolute EXE path and a Windows system working directory, so filenames with spaces or Unicode are not turned into shell commands.

This intentionally removes the previous application-level deployment-path restriction at the product owner's request. It is not a claim that writable launch locations are equivalent to protected deployment, and it does not add Authenticode signing.

Verification covers 20 portable-elevation regressions plus 32 process invocations across four executable path/name layouts. The latter runs self-tests and invalid-request checks, not real DISM/SFC. Actual Windows 11 UAC/alternate-admin/remediation acceptance remains a pilot step. See [`docs/releases/0.5.2.md`](docs/releases/0.5.2.md).

## Diagnostic capabilities

The main window collects CPU, RAM, logical/physical disks, Windows/build, processes, Event Log, startup, security-product, network and Windows Update signals. CPU and disk values use short-series medians; disk-pressure findings require elevated busy percentage and queue depth together. Event findings account for repeated Provider/Event ID groups rather than raw counts alone.

The dashboard exposes score, coverage, significant findings and the next Service Desk step. Missing telemetry does not mean healthy. Unknown signals reduce coverage without fabricating a health-score penalty. Primary guidance and clipboard observations prioritize the most important finding. System-volume selection is shared by assessment, dashboard and reports and does not assume C:.

The menu **Типовые проблемы: сеть, печать, устройства** opens additional local IP/DNS configuration, printer/Spooler and Plug-and-Play checks. Each result includes evidence, status and guided next steps; WARN precedes UNKNOWN, INFO and OK. Repeat/cancel collection, previous/current snapshots, clipboard and HTML/JSON exports are available. Fixed Windows Settings shortcuts assist manual work. Optional DNS-cache cleanup requires separate confirmation and reuses the existing action.

IP/DNS configuration is not proof of resource reachability. Printer-driver status is not proof of successful printing. Command success is not proof the user's symptom is resolved. The extra common-problem checks do not silently change the original score model.

See [`docs/ASSESSMENT-MODEL.md`](docs/ASSESSMENT-MODEL.md), [`docs/COMMON-PROBLEMS.md`](docs/COMMON-PROBLEMS.md), [`docs/KNOWN-LIMITATIONS.md`](docs/KNOWN-LIMITATIONS.md) and [`CHANGELOG.md`](CHANGELOG.md). The common-problem catalogue is a product-priority backlog, not measured incident-frequency statistics.

## Remediation boundaries

Diagnostics and reports do not require elevation. `CleanTemp` removes only old ordinary files from the interactive user's `%LOCALAPPDATA%\Temp`; it does not clean Windows Temp or Prefetch and does not traverse reparse points/junctions/symbolic links. It refuses to run inside an already elevated GUI. `FlushDns` normally runs without the administrative worker.

`DISM /Online /Cleanup-Image /RestoreHealth` and `SFC /scannow` require administrative rights. From a standard-user GUI they use UAC for the same EXE, independent of its name or directory. A combined batch keeps `CleanTemp` out of the elevated worker and runs it in the parent user context. Cancelled UAC does not execute the batch.

The worker accepts only `FlushDns`, `Dism`, `Sfc`, rejects `CleanTemp` and unknown/mixed action lists, and returns results over a session/nonce-bound local named pipe. No automatic network reset, DHCP release, DNS/proxy/VPN/GPO/EDR change, Spooler restart, print-job deletion, driver installation, device enabling or automatic reboot is added. Details: [`docs/SECURITY.md`](docs/SECURITY.md).

## Build, CI and release provenance

The `Windows EXE` workflow runs on main pushes, pull requests and manual dispatch with read-only repository permissions and pinned Actions. It validates PowerShell scripts, deterministic brand assets, NuGet vulnerabilities including transitive dependencies, warnings-as-errors build, source and published-EXE self-tests, the portable worker matrix, FileVersion, SHA-256 and pilot-package UTF-8/metadata.

Changes are integrated through pull requests with required build/analyzer/supply-chain checks. Main push builds trigger release publication and supply-chain attestations only after success; both validate the exact tested SHA/run, download that run's artifacts and re-verify the EXE checksum. Existing release tags are not overwritten.

The supply-chain workflow generates an SPDX SBOM and GitHub Artifact Attestations for EXE/pilot provenance and the EXE SBOM. See [`docs/SUPPLY-CHAIN.md`](docs/SUPPLY-CHAIN.md).

```powershell
gh attestation verify G-PC-Health-Check.exe --repo bajoicheg/g-pc-health-check
```

PR builds are not published releases. Hosted Windows Server CI is not a substitute for managed Windows 11 GUI/DPI/UAC, real hardware/provider and remediation E2E. WMI cancellation is cooperative, not a guaranteed hard timeout. The pilot procedure is in [`docs/E2E-TEST-PLAN.md`](docs/E2E-TEST-PLAN.md).

## Building locally

Prerequisites: Windows 11 x64 and .NET 8 SDK.

```powershell
dotnet restore src/G.PcHealthCheck/G.PcHealthCheck.csproj
dotnet build src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -warnaserror
dotnet run --project src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -- --selftest
dotnet publish src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

## Contributing, licensing and signing

See [`CONTRIBUTING.md`](CONTRIBUTING.md). Use synthetic/redacted public reports and the private reporting procedure in [`SECURITY.md`](SECURITY.md) for vulnerabilities.

Source is Apache License 2.0; see [`LICENSE`](LICENSE). The G name, shield and related branding are reserved to their owners and not granted under that license; see [`NOTICE`](NOTICE).

The PE is not Authenticode-signed. Verify the release checksum/provenance and use an approved distribution channel. Managed installation remains an option, not an application requirement; build attestations do not replace a Windows publisher signature.
