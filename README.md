# G PC Health Check

Windows 11 x64 Service Desk utility for workstation diagnostics, explainable health assessment, before/after reporting and controlled remediation.

Current project version: **0.10.0**. A self-contained single-file `G-PC-Health-Check.exe`; no installation is required.

> **Security / privacy:** review diagnostic exports before sharing. Event messages, account names, file paths, process/startup commands, device identifiers, target addresses and symptom notes may contain sensitive information. Search filters are not redaction. See [`SECURITY.md`](SECURITY.md).

## New in 0.10.0 — folder sizes and disk details

**Анализ → Место по папкам…** analyzes one selected absolute folder. Start explicitly, then inspect logical subtree/own sizes, counts, immediate subfolders and the largest 200 observed files. Search covers the whole saved snapshot; the GUI displays at most 2000 matching folder rows. HTML/JSON retain all collected rows and warnings. HTML also shows size shares for the 15 largest immediate subfolders.

Metadata traversal is bounded by default to 200000 entries, 20000 folders and 120 seconds between provider calls. Hidden entries are included; reparse points, errors, limits and cancellation are explicit. The scanner does not read file contents or delete anything. Logical sizes are not physical allocation or cleanup recommendations; hard links count per name and nested totals overlap. The filesystem is not frozen. Network/cloud providers may issue their own traffic.

**Анализ → Подробности накопителей…** reads local Windows Storage physical-disk properties and explicitly associated reliability counters. Only a unique matching DeviceId is accepted. It shows available firmware, raw health/media/bus codes, temperature/device limit, consumed wear, hours and uncorrected errors. Missing data is not zero and not evidence of health. These are driver/provider reports, not complete raw SMART, a surface test or drive-letter mapping.

Both windows open idle, support repeat/stop, elapsed progress, details, clipboard and unique-folder HTML/JSON export. Stop retains completed data in memory; export separately to preserve it before another scan. No new repair, elevation, background agent, driver or external utility is added. See [`docs/releases/0.10.0.md`](docs/releases/0.10.0.md) for scope, methods and pilot requirements.

## Performance observation (0.9.0)

**Анализ → Сеанс производительности…** observes an actual workload for 30/60/120/300/600 seconds with 1/2/5-second intervals; defaults **120/2**. It opens idle, creates no stress load, and supports up to 100 timestamped symptom notes of 160 characters.

The live chart switches between CPU, physical-memory use, aggregate disk busy and queue. Actual receipt times, gaps, collection durations and warnings are retained. Statistics use available samples: median, nearest-rank P95, maximum and valid/total counts; reports add minimum and reference-threshold counts. These are not time fractions or proof of a bottleneck.

**Остановить и сохранить замеры** retains observations in memory. Export creates HTML with all four charts and a full JSON session in a unique directory. Export before starting another session. Native CPU/RAM and aggregate WMI disk readings are sequential, not atomic; missing values remain unavailable and multiple processor groups disable whole-machine CPU in this implementation. Scheduling skips missed slots rather than overlapping reads and allows 100 ms grace for the final due sample. See [`docs/releases/0.9.0.md`](docs/releases/0.9.0.md).

## Incident and process review (0.8.0)

**Анализ → События за время сбоя…** collects local Application/System events for an explicit interval up to seven days, default last hour. Query bounds are UTC. At most 1000 newest events per log are retained, with source status and visible missing messages/access failures/truncation. Literal search and log/Event ID/severity filters apply to the collected subset.

**Анализ → Подробности процессов…** shows current PID/parent PID, creation time, path, raw command, session, working set, thread/handle counts and missing fields. **Проверить владельца** checks exact PID/creation time before and after GetOwner so exited or reused identities do not inherit another process's result. Historical emitter PIDs are not automatically linked to current processes; time coincidence is not causation.

Both windows open idle, with repeat/cancel, elapsed progress, details, clipboard and full HTML/JSON exports. Commands are not executed, processes not modified and logs not cleared. See [`docs/releases/0.8.0.md`](docs/releases/0.8.0.md).

## Resource diagnostics (0.7.0)

**Анализ → Проверить доступность ресурса (DNS/TCP)…** tests one entered hostname/IP and one port after explicit outbound consent. It opens idle; changing target/port or completing a run clears consent. IPv4/IPv6/IDN are accepted; URLs, credentials, paths and ranges are rejected.

Resolver and per-address TCP attempts have separate outcomes, elapsed times, errors and explanations; successful TCP also records source IP. Defaults: DNS 5 seconds, TCP 3 seconds per address (GUI 1–10), up to eight usable distinct addresses. Presets set ports only. No application payload, TLS, HTTP, authentication or application-health check is performed.

The system resolver can use hosts/cache/suffixes. Direct TCP follows OS/VPN routing, not HTTP proxy/PAC. Repeat/cancel, current/previous attempts, clipboard and unique-folder HTML/JSON are included; different targets are not repair-before/after evidence. See [`docs/releases/0.7.0.md`](docs/releases/0.7.0.md).

## Read-only inspections (0.6.0)

**Анализ → Предпросмотр очистки Temp…** previews the existing old-user-Temp cleanup rule: scope/cutoff, count, logical-size estimate, largest 200 candidates, skipped links and access errors. Traversal is bounded at 100000 entries/30 seconds between provider calls. It deletes nothing; actual cleanup remains separately confirmed and rechecks eligibility.

**Анализ → Разбор автозагрузки…** shows searchable supported Run/RunOnce/Startup records, exact commands/references and account/source status. Commands are not expanded/executed, shortcuts not resolved and records not disabled. Registration proves neither enabled state nor startup impact/maliciousness. This is not full Autoruns coverage.

Both provide repeat/cancel, details, clipboard and whole-snapshot HTML/JSON irrespective of search. No third-party utility or source is bundled. See [`docs/releases/0.6.0.md`](docs/releases/0.6.0.md).

## Portable administrative actions (since 0.5.2)

**DISM/SFC can run from any EXE location/name**, including Downloads and renamed copies. No fixed Program Files path, installation or copy/bootstrap is required. Start normally, select and confirm actions; administrative work requests UAC for the same executable. Move/rename only while closed. Windows permissions and enterprise execution policies still apply, including network-path access for the administrative identity.

The worker validates fixed action IDs, session, pipe, nonce and administrative token. User Temp cleanup remains in the non-elevated parent. The obsolete bootstrap stays disabled. Removing a path restriction does not eliminate writable-directory risk or add a publisher signature.

## Main diagnostics and remediation

The dashboard collects CPU/RAM, logical/physical disks, Windows/build, processes, events, startup, security-product, network and update signals. Short-series CPU/disk medians reduce transients. Disk pressure requires elevated busy and queue together; event findings consider repeated Provider/Event ID groups. Score, coverage, findings, next steps and before/after reports are separate concepts. Missing telemetry is not health or a fabricated fault. System-volume selection is shared without assuming C:.

**Типовые проблемы: сеть, печать, устройства** adds local IP/DNS, printing/Spooler and PnP evidence with guided steps, repeat/cancel and export. Fixed Settings shortcuts aid investigation; optional DNS flush needs separate confirmation. Configuration is not reachability, printer status is not successful printing, command success is not symptom resolution.

`CleanTemp` removes old ordinary files under the user's `%LOCALAPPDATA%\Temp`, not Windows Temp/Prefetch or encountered reparse points; it refuses an elevated GUI. `FlushDns` normally runs without the administrative worker. DISM RestoreHealth and SFC /scannow need administrative rights. Combined batches keep CleanTemp in the parent; worker IDs remain FlushDns/Dism/Sfc and reject unknown/mixed requests or CleanTemp. Cancelled UAC does not execute the batch.

No automatic network reset, DHCP release, DNS/proxy/VPN/GPO/EDR change, Spooler restart, job deletion, driver installation, startup disabling or reboot is added. See [`docs/SECURITY.md`](docs/SECURITY.md), [`docs/ASSESSMENT-MODEL.md`](docs/ASSESSMENT-MODEL.md), [`docs/COMMON-PROBLEMS.md`](docs/COMMON-PROBLEMS.md), [`docs/KNOWN-LIMITATIONS.md`](docs/KNOWN-LIMITATIONS.md) and [`CHANGELOG.md`](CHANGELOG.md).

## Build, CI and provenance

`Windows EXE` runs on main pushes, PRs and manual dispatch with read-only repository permissions and pinned Actions. Gates cover PowerShell parsing, deterministic branding, transitive NuGet audit, warnings-as-errors build, source/single-EXE tests, portable worker tests, FileVersion, SHA-256 and pilot metadata/UTF-8. Successful main builds trigger release and supply-chain attestations for the exact tested SHA/run and rechecked artifact hash; existing releases are not overwritten. See [`docs/SUPPLY-CHAIN.md`](docs/SUPPLY-CHAIN.md).

```powershell
gh attestation verify G-PC-Health-Check.exe --repo bajoicheg/g-pc-health-check
```

PR builds are not releases. Hosted Windows Server tests, native/event/WMI integration and loopback TCP are not a substitute for corporate Windows 11 workloads, OEM/USB/RAID, VPN/proxy/DNS, GUI/DPI/UAC and actual remediation. Provider calls may exceed timeouts and cancellation is cooperative. See [`docs/E2E-TEST-PLAN.md`](docs/E2E-TEST-PLAN.md) and version notes.

## Building locally

Prerequisites: Windows 11 x64 and .NET 8 SDK.

```powershell
dotnet restore src/G.PcHealthCheck/G.PcHealthCheck.csproj
dotnet build src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -warnaserror
dotnet run --project src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -- --selftest
dotnet publish src/G.PcHealthCheck/G.PcHealthCheck.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

## Contributing, licensing and signing

See [`CONTRIBUTING.md`](CONTRIBUTING.md); use synthetic/redacted public reports and [`SECURITY.md`](SECURITY.md) for private vulnerabilities. Source is Apache License 2.0 ([`LICENSE`](LICENSE)); G branding is reserved ([`NOTICE`](NOTICE)). The EXE is not Authenticode-signed. Verify checksums/provenance and use an approved distribution channel; build attestations do not replace a Windows publisher signature.
