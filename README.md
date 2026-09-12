# G PC Health Check

Windows 11 x64 Service Desk utility for workstation diagnostics, explainable findings, before/after reporting and controlled remediation.

Current project version: **0.12.0**. A self-contained single-file `G-PC-Health-Check.exe`; no installation is required. Portable copies may use any folder and filename.

> **Privacy:** review exports before sharing. Account names/SIDs, profile and file paths, commands, events, device identifiers, resource addresses and notes can be sensitive. Search is not redaction. See [`SECURITY.md`](SECURITY.md).

## New in 0.12.0 — network endpoints and processes

**Анализ → Сетевые соединения и порты (TCP/UDP)…** reads local TCP4/TCP6/UDP4/UDP6 owner-PID tables on demand. It shows local/remote IPs and ports where meaningful, native states and PIDs, and process names only when PID/creation-time evidence agrees before and after the table reads. Missing or reused process identity remains explicit; PID 0 is not guessed as the idle process.

The window offers literal process/PID/address/port search, TCP/UDP/LISTEN/ESTABLISHED/change filters, typed sorting, details, repeat/stop, elapsed progress and full HTML/JSON exports of current/previous snapshots. Comparisons require complete corresponding tables and matching host/actor/session/rights. They describe observations, not exact socket lifetimes; failed collection cannot imply disappearance. Limits: 5000 retained rows per table, 16 MiB buffers/four retries, 8192 process objects per observation, 2000 displayed matching rows.

No reverse DNS, packet capture, active network probe, connection closing, process termination, elevation or firewall changes. UDP shows local bindings, not remote conversations; LISTEN does not prove reachability through the firewall. This is separate from the existing active resource check. [Scope, sources and pilot checks](docs/releases/0.12.0.md).

## Execution context (0.11.0) — who runs the tool, whose data it reads

The main context strip distinguishes a standard user, an administrator without elevation, an elevated administrator, a full token without a linked UAC pair, and incomplete evidence. It separately displays the **process account** and **user of the process's Windows session**. Open **Права и доступные действия…** for token/session/profile facts, explanations and an availability matrix.

The action table shows availability before confirmation. Unavailable actions are not selectable; DISM/SFC explicitly show when UAC is needed. The process rechecks context before applying and records the actual account, rights and target scope for each action. Mixed normal/admin batches retain both contexts in the before/after view and HTML/JSON. A saved UI snapshot is not an authorization credential.

**Elevated read-only Temp preview is allowed.** It identifies the current session user and exact profile-based Temp path rather than falling back to the console user or technician's profile. Unknown identity stays unknown. Preview does not delete anything. Actual CleanTemp still requires a normal, non-elevated process belonging to that same verified session user; this difference is visible before applying.

Startup review still reads HKCU/personal Startup of the **process account**. Raising only the repair worker does not elevate the original GUI's subsequent diagnostics. There is no automatic privileged diagnostic broker or account impersonation. Machine repairs no longer require finding a user profile. See [`docs/releases/0.11.0.md`](docs/releases/0.11.0.md) for the matrix, implemented scope and required pilot cases.

## Analysis tools

| Menu under Анализ | Purpose | Scope / version notes |
|---|---|---|
| Предпросмотр очистки Temp… | Exact age cutoff, candidate count, logical-size estimate and largest 200 files before the existing cleanup | Metadata only; current-session profile and elevated preview since 0.11.0. [Inspection scope](docs/releases/0.6.0.md) |
| Разбор автозагрузки… | Searchable Run/RunOnce/Startup records, raw commands, account and source status | No command execution, disabling or shortcut resolution; not full Autoruns. [0.6.0](docs/releases/0.6.0.md) |
| Проверить доступность ресурса (DNS/TCP)… | Resolve one hostname/IP and connect to one chosen port after explicit outbound consent | Separate DNS/address outcomes; direct OS/VPN TCP, not HTTP proxy or TLS/application validation. [0.7.0](docs/releases/0.7.0.md) |
| События за время сбоя… | Local Application/System events for a selected incident interval, search and filters | Up to seven days and 1000 newest records per log; missing/truncated data stays visible. [0.8.0](docs/releases/0.8.0.md) |
| Подробности процессов… | Process identity, parent, path/command, memory/session and checked owner lookup | Exact PID/creation-time matching; no process changes or historical PID guesswork. [0.8.0](docs/releases/0.8.0.md) |
| Сеанс производительности… | Timed CPU/RAM/disk observation with live graphs and symptom markers | Default 120 seconds / 2 seconds; sample statistics, not time fractions or proof of a bottleneck. [0.9.0](docs/releases/0.9.0.md) |
| Место по папкам… | Own/subtree logical sizes, counts, immediate folders and largest 200 files | Default 200000 entries / 20000 folders / 120 seconds; no deletion or file-content reads. [0.10.0](docs/releases/0.10.0.md) |
| Подробности накопителей… | Physical-disk properties and explicitly associated Windows reliability counters | Missing is not zero; consumed wear, not remaining health; not full raw SMART or a surface test. [0.10.0](docs/releases/0.10.0.md) |
| Сетевые соединения и порты (TCP/UDP)… | Local owner-PID tables, checked process-name attribution and qualified snapshot differences | No probe/reverse DNS or connection/process changes; full retained-snapshot export. [0.12.0](docs/releases/0.12.0.md) |

Read-only tools open idle and provide explicit collection, progress, cancellation, details and local reports. Export before replacing an in-memory result. HTML/JSON preserve the complete collected snapshot, not only a search filter. Permissions, source limits and unavailable values remain meaningful; no provider is guaranteed to return promptly. Folder sizes are logical, nested totals overlap and hard links count per name. Network/cloud paths and native name resolution may generate OS traffic.

## Main diagnostics and common problems

The dashboard collects CPU/RAM, logical/physical disks, Windows/build, processes, events, startup, security-product, network and update signals. Short-series medians reduce transient load findings. Disk pressure requires elevated busy and queue together; event findings consider repeated Provider/Event ID groups. Score, diagnostic coverage, findings and next steps are separate: unavailable telemetry is neither health nor a fabricated hardware fault. System-volume selection does not assume C:.

**Типовые проблемы: сеть, печать, устройства** adds local IP/DNS configuration, printing/Spooler and PnP evidence with guided steps, repeat/cancel and export. Fixed Windows Settings shortcuts aid investigation; optional DNS flush needs separate confirmation. Configuration is not reachability, printer status is not successful printing and a command exit code is not symptom resolution.

The original health thresholds and previous tools are preserved. See [`docs/ASSESSMENT-MODEL.md`](docs/ASSESSMENT-MODEL.md), [`docs/COMMON-PROBLEMS.md`](docs/COMMON-PROBLEMS.md), [`docs/KNOWN-LIMITATIONS.md`](docs/KNOWN-LIMITATIONS.md) and [`CHANGELOG.md`](CHANGELOG.md).

## Portable remediation and execution boundaries

**DISM/SFC work from any EXE folder/name**, including Downloads and renamed copies. Start normally, select and confirm actions; a separate process requests UAC for the same executable. An already administrative GUI uses its existing rights. Move/rename the file only while closed. Windows access and enterprise launch policies still apply.

`CleanTemp` removes only old ordinary files in the confirmed session user's **profile\\AppData\\Local\\Temp**, not Windows Temp/Prefetch. It requires a non-elevated process with matching user SIDs/profiles, rechecks the context and refuses encountered reparse points. An invalid or inaccessible root is not reported as a successful empty cleanup. Preview remains read-only and can run elevated; its scope is not an automatic discovery of every redirected/custom TMP folder.

`FlushDns` runs under the current token when selected alone, with no automatic elevated retry. When included with DISM/SFC it travels through their worker. Combined batches keep CleanTemp in the original normal parent. DISM RestoreHealth and SFC /scannow retain fixed paths/arguments and administrative requirements. The worker still accepts only FlushDns/Dism/Sfc and validates session, pipe and nonce; unknown/mixed requests and CleanTemp are rejected. Obsolete bootstrap remains disabled.

No automatic network reset, DHCP release, DNS/proxy/VPN/GPO/EDR change, service restart, print-job removal, driver installation, startup disabling or reboot is added. No credentials are stored, no user profile is loaded and no current token is elevated or impersonated by context discovery. See [`docs/SECURITY.md`](docs/SECURITY.md) and [0.11.0 context rules](docs/releases/0.11.0.md).

## Build, CI and provenance

`Windows EXE` uses read-only repository permissions and pinned Actions. Its gates include PowerShell parsing, deterministic branding, transitive NuGet audit, warnings-as-errors build, source and single-EXE self-tests, portable worker tests, exact FileVersion, SHA-256 and pilot metadata/UTF-8. Successful main builds trigger release publication and supply-chain attestations for the exact tested SHA/run. Existing releases are not overwritten. See [`docs/SUPPLY-CHAIN.md`](docs/SUPPLY-CHAIN.md).

```powershell
gh attestation verify G-PC-Health-Check.exe --repo bajoicheg/g-pc-health-check
```

PR builds are not releases. Hosted Windows Server tests are not a substitute for real corporate Windows 11 standard/admin/other-account/RDP contexts, redirected profiles, OEM disks, VPN/proxy/DNS, GUI/DPI, interactive UAC and actual remediation. See [`docs/E2E-TEST-PLAN.md`](docs/E2E-TEST-PLAN.md) and version-specific notes.

## Building locally

Prerequisites: Windows 11 x64 and .NET 8 SDK.

```powershell
dotnet restore src/G.PcHealthCheck/G.PcHealthCheck.csproj
dotnet build src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -warnaserror
dotnet run --project src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -- --selftest
dotnet publish src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

## Contributing, licensing and signing

See [`CONTRIBUTING.md`](CONTRIBUTING.md); use synthetic/redacted public evidence and [`SECURITY.md`](SECURITY.md) for private vulnerabilities. Source is Apache License 2.0 ([`LICENSE`](LICENSE)); G branding is reserved ([`NOTICE`](NOTICE)). The EXE is not Authenticode-signed. Verify checksums/provenance and use an approved distribution channel; build attestations do not replace a Windows publisher signature.
