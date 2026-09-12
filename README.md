# G PC Health Check

Windows 11 x64 Service Desk utility for workstation diagnostics, explainable health assessment, before/after reporting and controlled remediation.

Current project version: **0.9.0**. A self-contained single-file `G-PC-Health-Check.exe`; no installation is required.

> **Security / privacy:** review diagnostic exports before sharing. Event messages, account names, file paths, process/startup commands, target addresses and symptom notes may contain sensitive information. Search filters are not redaction. See [`SECURITY.md`](SECURITY.md).

## New in 0.9.0 — observe performance during a symptom

Open **Анализ → Сеанс производительности…**. Select a duration (30/60/120/300/600 seconds) and interval (1/2/5 seconds), then start and reproduce the slowdown. Defaults: **120 seconds / 2 seconds**. The window opens idle and creates no stress workload. Use **Отметить симптом** to record a note at the click time. Up to 100 notes of 160 characters are allowed; this is not recovery of the historical onset of a freeze.

The live chart switches between CPU, physical-memory use, aggregate disk busy and disk queue. It uses actual sample receipt times and breaks at missing values or long gaps. A typed table retains all completed samples, collection durations and warnings. Median, nearest-rank P95, maximum and valid/total sample counts are visible; reports additionally include minimum and reference-threshold sample counts. These are sample statistics, not percentages of time or proof of a bottleneck.

**Остановить и сохранить замеры** stops cooperatively and retains completed measurements in memory. Export writes a separate HTML report with all four charts and a full JSON session to a unique directory. Clipboard/export retain warnings and notes; no files are overwritten or uploaded. Export before starting another session to preserve the previous result.

CPU uses GetSystemTimes deltas, RAM uses GlobalMemoryStatusEx, and disks use the local WMI PhysicalDisk `_Total` idle complement and current queue. These readings are sequential, not atomic. Aggregate disk values are not the Windows volume alone. Multiple processor groups make whole-machine CPU unavailable in this implementation rather than silently reporting one group's usage. Missing/invalid values remain unavailable. The provider can exceed requested timeouts; missed slots and overruns are visible. A due final sample allows 100 ms of scheduling grace while preserving its actual time.

The original Health Score and remediation are unchanged. See [`docs/releases/0.9.0.md`](docs/releases/0.9.0.md) for methods, tests and pilot limits. This completes the timed-observation iteration from #26, not the later hardware, general folder-analysis or per-process performance-attribution items.

## Incident and process review (0.8.0)

**Анализ → События за время сбоя…** collects local Application/System events for a chosen interval of up to seven days, default last hour. Local times become explicit UTC query bounds. The GUI keeps at most 1000 newest records per log, with separate source status and visible access failures, missing messages and truncation. Search applies literally to collected data; log, Event ID and severity filters do not recover records outside the collected subset.

**Анализ → Подробности процессов…** shows a current read-only snapshot: PID/parent PID, start time, path, raw command, session, working set, thread/handle counts and unavailable fields. **Проверить владельца** verifies PID and exact creation time before and after GetOwner. Reused/exited/unverified identities cannot inherit another process's owner result. Historical event emitter PIDs are not automatically associated with current processes, and temporal coincidence is not causal proof.

Both windows open idle and offer repeat/cancel, elapsed progress, details, clipboard and full-snapshot HTML/JSON exports. They never execute commands, modify processes, write events or clear logs. See [`docs/releases/0.8.0.md`](docs/releases/0.8.0.md).

## Resource diagnostics (0.7.0)

**Анализ → Проверить доступность ресурса (DNS/TCP)…** tests one entered hostname/IP and one port after explicit outbound-request consent. The window opens idle; changing target/port and completing a run clears consent. IPv4/IPv6 and internationalized names are supported; URLs, credentials, paths and ranges are rejected.

System name resolution and each TCP attempt have separate outcomes, elapsed times, socket codes and explanations. Successful TCP also records the local source IP. Defaults: DNS 5 seconds, TCP 3 seconds per address (GUI 1–10), at most eight distinct usable addresses. Missing, mixed and cancelled outcomes remain explicit. Port presets set numbers only; TCP sends no application payload and does not validate TLS, HTTP, authentication or application health.

DNS uses the system resolver, potentially including hosts/cache/search suffixes. Direct TCP follows OS routing/VPN, not HTTP proxy/PAC. Repeat/cancel, current/previous attempts, clipboard and unique-folder HTML/JSON exports are included. Different targets are not presented as repair-before/after evidence. See [`docs/releases/0.7.0.md`](docs/releases/0.7.0.md).

## Read-only inspections (0.6.0)

**Анализ → Предпросмотр очистки Temp…** previews only the existing old-user-Temp cleanup rule: exact scope/cutoff, candidate count, logical-size estimate, 200 largest candidates, skipped links and access errors. Enumeration is bounded at 100,000 entries/30 seconds between provider calls; partial results are explicit. Nothing is deleted. Actual cleanup remains a separately confirmed action that checks current file eligibility again.

**Анализ → Разбор автозагрузки…** shows searchable Run/RunOnce and Startup-folder records, raw commands/references, account/scope and per-source status. Commands are not expanded/executed, shortcuts are not resolved and entries are not disabled. Registration does not prove enabled state, impact or maliciousness. This is a defined subset, not full Autoruns coverage.

Both windows support repeat/cancel, full details, clipboard and whole-snapshot HTML/JSON exports irrespective of search filters. See [`docs/releases/0.6.0.md`](docs/releases/0.6.0.md). No third-party utilities or source are bundled.

## Portable administrative actions (since 0.5.2)

**DISM/SFC can run from any EXE location/name**, including Downloads and renamed copies. No fixed Program Files path, installation or copy/bootstrap is required. Start normally, choose actions and confirm; administrative actions request UAC for the same executable. Move/rename only while closed. Windows access and enterprise launch policies still apply, including network-path access for the administrative identity.

The worker validates fixed action IDs, session, pipe, nonce and the administrative token. User Temp cleanup remains in the non-elevated parent. The obsolete bootstrap stays disabled. Removing a deployment-path restriction does not eliminate writable-directory risks or add a publisher signature.

## Main diagnostics and remediation

The dashboard collects CPU, RAM, logical/physical disk, Windows/build, processes, events, startup, security-product, network and update signals. CPU/disk values use short-series medians; disk-pressure findings require elevated busy and queue together. Event findings consider repeated Provider/Event ID groups. Score, coverage, findings, next steps and before/after reports are visible; missing telemetry is not health or a fabricated fault. System-volume selection is shared across assessment, dashboard and reports without assuming C:.

**Типовые проблемы: сеть, печать, устройства** adds local IP/DNS configuration, printing/Spooler and PnP evidence with guided steps, repeat/cancel and export. Fixed Windows Settings shortcuts help investigation. Optional DNS-cache cleanup needs separate confirmation. Configuration is not reachability, driver status is not successful printing and command success is not symptom resolution.

`CleanTemp` removes old ordinary files under the interactive user's `%LOCALAPPDATA%\Temp`, not Windows Temp/Prefetch or encountered reparse points. It refuses an elevated GUI. `FlushDns` normally runs without the administrative worker. DISM RestoreHealth and SFC /scannow require administrative rights. Combined batches keep CleanTemp in the parent user context; worker commands remain exactly FlushDns/Dism/Sfc. Unknown/mixed requests and CleanTemp are rejected by that worker. Cancelled UAC does not execute the batch.

No automatic network reset, DHCP release, DNS/proxy/VPN/GPO/EDR change, Spooler restart, job deletion, driver installation, startup disabling or reboot is added. See [`docs/SECURITY.md`](docs/SECURITY.md), [`docs/ASSESSMENT-MODEL.md`](docs/ASSESSMENT-MODEL.md), [`docs/COMMON-PROBLEMS.md`](docs/COMMON-PROBLEMS.md), [`docs/KNOWN-LIMITATIONS.md`](docs/KNOWN-LIMITATIONS.md) and [`CHANGELOG.md`](CHANGELOG.md).

## Build, CI and provenance

`Windows EXE` runs on main pushes, PRs and manual dispatch using read-only repository permissions and pinned Actions. Gates cover PowerShell parsing, deterministic branding, transitive NuGet audit, warnings-as-errors build, source/single-EXE tests, portable worker tests, FileVersion, SHA-256 and pilot metadata/UTF-8. PR checks precede merge; successful main builds trigger release publication and supply-chain attestations for the exact tested SHA/run and rechecked artifact hash. Existing releases are not overwritten. See [`docs/SUPPLY-CHAIN.md`](docs/SUPPLY-CHAIN.md).

```powershell
gh attestation verify G-PC-Health-Check.exe --repo bajoicheg/g-pc-health-check
```

PR builds are not releases. Hosted Windows Server tests, local read-only counters/event/WMI integration and loopback TCP do not replace managed Windows 11 real-workload, VPN/proxy/DNS, GUI/DPI/UAC and remediation testing. Provider cancellation cannot guarantee immediate termination of every OS call. See [`docs/E2E-TEST-PLAN.md`](docs/E2E-TEST-PLAN.md) and version notes.

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
