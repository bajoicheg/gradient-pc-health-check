# G PC Health Check

Windows 11 x64 Service Desk utility for workstation diagnostics, explainable health assessment, before/after reporting, and controlled remediation.

Current project version: **0.7.0**. The application is a self-contained single-file `G-PC-Health-Check.exe`.

> **Security / privacy:** never publish unreviewed diagnostic reports. Evidence may contain workstation/user/domain names, file paths, startup commands, target hostnames and network addresses. See [`SECURITY.md`](SECURITY.md).

## New in 0.7.0 — diagnose one network resource

Open **Анализ → Проверить доступность ресурса (DNS/TCP)…**. Enter one hostname or IPv4/IPv6 address and one TCP port, explicitly allow outbound diagnostic connections, then select **Проверить / повторить**. Opening the window makes no network requests. Changing the target/port clears consent; another run also requires confirmation through the checkbox. Internationalized names are normalized; URLs, credentials, paths and ranges are rejected.

The session separates system name resolution from each TCP attempt, displays resolved addresses, connection time, successful local source IP, socket error codes and explanations. Port presets only set numbers; they do not test HTTPS, SMB, RDP or SMTP protocols. DNS uses the current system resolver (including possible cache/hosts/search suffixes), not a nominated DNS server. Direct TCP follows OS routing/VPN; an HTTP proxy is not used.

Defaults: DNS timeout 5 seconds; TCP 3 seconds per address (GUI range 1–10); at most eight distinct usable addresses, in resolver order. Mixed results, skipped addresses and cancellation remain explicit. TCP success is not proof of TLS, HTTP, authentication or application health. No application payload is sent. Repeat/cancel, current/previous attempts, clipboard and unique-folder HTML/JSON exports are included. Different targets are not presented as a before/after repair comparison.

The session never resets networking, changes DNS/VPN/proxy or runs repairs. Existing Health Score and Coverage are unchanged. See [`docs/releases/0.7.0.md`](docs/releases/0.7.0.md) for tests, sources and pilot limits. This implements the next scoped network item from issue #26, not its later event, performance, hardware or full TCPView-style process inventory items.

## Read-only inspections (0.6.0)

Open **Анализ → Предпросмотр очистки Temp…** for a metadata-only preview of the existing old-user-Temp cleanup scope: candidate count, logical size estimate, exact cutoff, 200 largest candidates, skipped links and access errors. Enumeration is bounded at 100,000 entries/30 seconds between provider calls; partial results are explicitly identified. Nothing is deleted. Actual cleanup remains a separately confirmed main-window action that rechecks current file eligibility.

Open **Анализ → Разбор автозагрузки…** for searchable read-only Run/RunOnce and Startup-folder evidence: names, raw commands/file references, account/scope and per-source collection status. Commands are not expanded or executed, shortcut targets are not resolved, and no entries are disabled. Presence is not proof of enabled state, performance impact or maliciousness. This is a defined subset, not full Autoruns coverage.

Both inspection windows offer repeat/cancel, full row details, clipboard summary and local HTML/JSON exports. Cancelled or failed scans do not silently replace earlier snapshots. Exports retain the whole saved snapshot regardless of the current search filter. See [`docs/releases/0.6.0.md`](docs/releases/0.6.0.md). Third-party utilities and code are not bundled.

## Portable administrative actions (since 0.5.2)

**DISM/SFC can be launched from any EXE location and under any EXE filename**, including Downloads and renamed copies. No installation or fixed Program Files path is required. Start normally, choose actions and confirm them; administrative actions request UAC for the same executable. Move/rename the file only while the application is closed. Windows access and enterprise execution policies still apply; network paths must also be accessible to the administrative identity.

The worker validates fixed action IDs, session, pipe, nonce and administrative token. User Temp cleanup remains outside the elevated worker. The obsolete copy/install bootstrap is not used. Portability removes the old deployment-path restriction; it does not eliminate writable-directory risks or add Authenticode signing.

## Diagnostic capabilities

The main window collects CPU, RAM, logical/physical disks, Windows/build, processes, Event Log, startup, security-product, network and Windows Update signals. CPU/disk values use short-series medians; disk-pressure findings require elevated busy percentage and queue depth together. Event findings consider repeated Provider/Event ID groups rather than raw counts alone.

The dashboard exposes score, coverage, findings and next steps. Missing telemetry is not treated as healthy or fabricated as a confirmed fault. System-volume selection is shared across assessment, dashboard and reports and does not assume C:.

**Типовые проблемы: сеть, печать, устройства** adds local IP/DNS configuration, printer/Spooler and Plug-and-Play checks with evidence, guided steps, repeat/cancel and export. Fixed Windows Settings shortcuts assist investigation. Optional DNS-cache cleanup requires separate confirmation.

Configuration is not proof of reachability, printer status is not proof of printing, and command success is not proof the symptom is resolved. Additional inspection/resource windows do not silently change the original health model. See [`docs/ASSESSMENT-MODEL.md`](docs/ASSESSMENT-MODEL.md), [`docs/COMMON-PROBLEMS.md`](docs/COMMON-PROBLEMS.md), [`docs/KNOWN-LIMITATIONS.md`](docs/KNOWN-LIMITATIONS.md) and [`CHANGELOG.md`](CHANGELOG.md).

## Remediation boundaries

Diagnostics and reports do not require elevation. `CleanTemp` removes only old ordinary files from the interactive user's `%LOCALAPPDATA%\Temp`, not Windows Temp/Prefetch or encountered reparse points. It refuses to run in an elevated GUI. `FlushDns` normally runs without the administrative worker.

DISM RestoreHealth and SFC /scannow require administrative rights. A standard-user GUI requests UAC for the current EXE irrespective of name/location. Combined batches keep `CleanTemp` in the parent user context. The worker accepts only `FlushDns`, `Dism`, `Sfc`, rejects `CleanTemp` and unknown/mixed requests, and returns session/nonce-bound results over a local named pipe. Cancelled UAC does not execute the batch.

No automatic network reset, DHCP release, DNS/proxy/VPN/GPO/EDR change, Spooler restart, job deletion, driver installation, startup disabling or reboot is added. See [`docs/SECURITY.md`](docs/SECURITY.md).

## Build, CI and provenance

`Windows EXE` runs on main pushes, PRs and manual dispatch with read-only repository permissions and pinned Actions. Gates cover PowerShell parsing, deterministic branding, transitive NuGet audit, warnings-as-errors build, source/single-EXE self-tests, portable worker checks, FileVersion, SHA-256 and pilot metadata/UTF-8.

PR checks precede merge. Successful main builds trigger release publication and supply-chain attestations: exact tested SHA/run, that run's artifacts and rechecked EXE hash. Existing releases are not overwritten. SPDX SBOM and EXE/pilot provenance are described in [`docs/SUPPLY-CHAIN.md`](docs/SUPPLY-CHAIN.md).

```powershell
gh attestation verify G-PC-Health-Check.exe --repo bajoicheg/g-pc-health-check
```

PR builds are not releases. Hosted Windows Server tests, including loopback-only network integration, do not replace managed Windows 11 VPN/proxy/DNS, GUI/DPI/UAC and real remediation testing. Provider cancellation is not a guarantee that every OS-internal operation immediately stops. See [`docs/E2E-TEST-PLAN.md`](docs/E2E-TEST-PLAN.md) and version notes.

## Building locally

Prerequisites: Windows 11 x64 and .NET 8 SDK.

```powershell
dotnet restore src/G.PcHealthCheck/G.PcHealthCheck.csproj
dotnet build src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -warnaserror
dotnet run --project src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -- --selftest
dotnet publish src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

## Contributing, licensing and signing

See [`CONTRIBUTING.md`](CONTRIBUTING.md). Use synthetic/redacted public reports and [`SECURITY.md`](SECURITY.md) for private vulnerabilities. Source is Apache License 2.0; see [`LICENSE`](LICENSE). G branding is reserved; see [`NOTICE`](NOTICE).

The EXE is not Authenticode-signed. Verify checksums/provenance and use an approved distribution channel. Installation remains optional; build attestations do not replace a Windows publisher signature.
