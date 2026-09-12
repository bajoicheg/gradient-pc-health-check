# Changelog

## 0.5.2 — Portable administrative actions

- Removed fixed-directory and fixed-EXE-name restrictions from both GUI dispatch and privileged-worker startup, as explicitly requested by the product owner.
- DISM/SFC now use the current EXE from Downloads, other directories and renamed copies; no installation/copy bootstrap is required.
- Preserves UAC, explicit action confirmation, administrative-token requirements, fixed commands, worker session/pipe/nonce checks and standard-user-only Temp cleanup.
- Uses the current absolute EXE path as a separate ProcessStartInfo field, with a Windows system working directory; spaces/Unicode/renamed files are not shell command strings.
- Added 20 portable-elevation regression cases and a 32-invocation published-EXE matrix across four actual local path/name layouts.
- Replaced the obsolete noncanonical-path rejection test with portable request-validation checks; retained forbidden-action, invalid pipe/nonce and disabled-bootstrap tests.
- Updated pilot metadata, START-HERE, current security/pilot docs, README and generated release notes to remove the installation requirement.
- This intentionally relaxes the previous application-level location boundary. Windows access/launch policy still applies; no Authenticode signature or elimination of writable-directory risks is claimed.
- Application version: `0.5.2`; FileVersion: `0.5.2.0`. Details: `docs/releases/0.5.2.md`.

## 0.5.1 — Consistent system-volume diagnostics

- Unified system-volume selection across assessment, main dashboard, clipboard summary and HTML/before-after reporting.
- Resolves the local Windows volume instead of assuming C: or falling back to another disk when telemetry is missing.
- Normalizes equivalent drive-root forms without mutating collected records.
- Rejects ambiguous duplicate identities and invalid capacity/free-space measurements as unavailable evidence; missing data does not preselect cleanup or fabricate a health-score penalty.
- Preserves a measured zero free space as a critical finding and unavailable comparison values/deltas as missing.
- Shows the Windows-volume identity in the System tab, HTML System section and comparison label.
- Added 24 synthetic regressions. No collector, worker, IPC, elevation, remediation allow-list, dependency or CI-permission changes.
- Application version: `0.5.1`; FileVersion: `0.5.1.0`. Local-diagnostics scope and pilot checks: `docs/releases/0.5.1.md`.

## 0.5.0 — Common workstation problems

- Added a dedicated window for local network/IP/DNS configuration, printing/Spooler state and Plug-and-Play device errors, accessible from the single EXE menu.
- Every finding includes evidence, an explicit status and guided resolution; WARN precedes UNKNOWN, INFO and OK consistently in the UI, clipboard, HTML and JSON.
- Supports IPv6-only and multiple-adapter configurations, distinguishes APIPA/link-local-only configuration and missing DNS, and does not claim resource reachability from configuration alone.
- Distinguishes missing printer telemetry, unknown Spooler, default-printer offline/error signals and unused/disconnected printers; positive fault observations retain priority over missing data.
- Retains NULL PnP error codes in the WMI query and treats unavailable presence/error codes as unknown rather than confirmed hardware failure or health.
- Added repeat/cancel collection, current/previous snapshots, local HTML/JSON export and a copyable Service Desk summary.
- Added an allow-listed Windows Settings menu and separate confirmation for the existing FlushDns action, gated on usable local IP/DNS configuration.
- Added 36 diagnostic scenarios, eight priority/unknown-data regressions and presentation/serialization/URI/menu checks.
- Fixed the ambiguous System.Management/System.IO EnumerationOptions reference detected by Windows CI.
- Existing Health Score, assessment thresholds, worker, remediation allow-list, IPC, elevation and CI workflows are unchanged.
- Application version: `0.5.0`; FileVersion: `0.5.0.0`. Details and pilot acceptance: `docs/releases/0.5.0.md`.

## 0.4.4 — Primary guidance and critical-first support summary

- The triage next step uses the primary finding's recommendation; an unrelated recommended action no longer overrides it.
- Missing primary guidance falls back to manual investigation before system changes.
- Clipboard observations use the same stable severity/penalty ordering as triage before the eight-observation limit.
- Longer observation lists explicitly state the number omitted and refer to the complete HTML/JSON report.
- Added 19 synthetic regression scenarios for competing findings, missing guidance, ordering, truncation, unchanged action state and null arguments.
- No changes to diagnostics collection, assessment thresholds, score, remediation scope, privileged worker, IPC, elevation or CI workflows.
- Application version: `0.4.4`. Details: `docs/releases/0.4.4.md`.

## 0.4.3 — Diagnostic completeness

- Any missing weighted diagnostic signal now produces an explicit `WARN / Данные`, even when numeric coverage remains in the `HIGH` band (85–100%).
- Missing telemetry does not reduce the health score or suppress a confirmed `CRIT` finding.
- Full physical-disk health coverage requires a known status for every discovered disk; one healthy disk no longer hides another disk with unavailable health.
- Unknown/blank disk health remains missing data, not evidence of a failed disk.
- The optional CleanTemp reason explicitly states when system-disk free space was not measured, instead of claiming that space is sufficient.
- Added 22 synthetic regression scenarios, including individual missing signals, multi-disk health, CRIT priority, recovery after a repeated scan, support-summary consistency and unchanged automated-remediation scope.
- Updated the assessment-model documentation, including the combined disk busy/queue rule introduced in 0.4.2.
- No change to the privileged worker, IPC, canonical installation path, elevation behavior or remediation allow-list.
- Application version: `0.4.3`.

## 0.4.2 — Disk-pressure confidence

- Disk queue depth no longer produces WARN/CRIT on its own.
- Disk-pressure findings now require both elevated median queue depth and elevated median disk busy percentage.
- Added `DiskBusyWarnPercent` (80%) and `DiskBusyCriticalPercent` (95%) alongside existing queue thresholds.
- `InspectIo` is suggested only for the combined busy+queue signal.
- Diagnostic coverage now requires both disk busy and queue telemetry for the disk-load signal.
- Clamps sampled `% Disk Time` to 0–100 before taking the median.
- Clipboard and before/after HTML output now expose both disk busy and queue values.
- Added regression tests for queue-only false positives, WARN/CRIT combined pressure and incomplete disk telemetry.
- Application version: `0.4.2`.

## 0.4.1 — Service Desk triage clarity

- Added a dedicated triage card above recommendations: primary finding, CRIT/WARN counts, diagnostic coverage and the next Service Desk step.
- Prioritizes critical findings before warnings and prefers an explicitly recommended/preselected remediation as the next step when one exists.
- Highlights CRIT/WARN finding rows for faster visual scanning.
- Shows diagnostic coverage and missing signals directly in the System tab and HTML reports.
- Adds the same triage context to clipboard summaries.
- Fixed clipboard summary system-drive lookup (`C:` rather than `C:\`), so free-space data is no longer lost.
- Added healthy, critical-disk, incomplete-telemetry and system-drive regression self-tests.
- Application version: `0.4.1`.

## 0.4.0 — Service Desk product UX

First product-focused release after the public rename/supply-chain baseline.

- Removed the form-wide `WaitCursor`; progress is communicated through the existing progress bar/status and disabled controls, preventing a sticky busy cursor over DataGridView regions after async diagnostics.
- Added a visible diagnostic coverage metric to the main dashboard with HIGH/MEDIUM/LOW coloring.
- Added elapsed scan duration to the completion status.
- Added `Копировать сводку` for a concise clipboard-ready Service Desk summary with host/user, health score, coverage, key metrics, significant findings and recommended actions.
- Added a self-test for the support-summary contract.
- Updated protected-branch documentation to include required `supply-chain-smoke`.
- Application version: `0.4.0`.

## 0.3.8 — rename to G PC Health Check

Brand-neutral rename baseline before 0.4.0; diagnostic and remediation behavior is unchanged from 0.3.7.

- Product name changed to `G PC Health Check`.
- Executable renamed to `G-PC-Health-Check.exe`.
- .NET namespace/project path changed from the legacy brand to `G.PcHealthCheck` / `src/G.PcHealthCheck`.
- Canonical privileged path changed to `%ProgramFiles%\G\PCHealthCheck\G-PC-Health-Check.exe`.
- Local report/evidence paths, named-pipe prefix, CI artifacts, pilot bundles, Release assets, SBOM metadata and repository URLs now use `G` / `g`.
- Current repository name is expected to be `g-pc-health-check`.
- No runtime diagnostic/remediation logic change.
- Application version: `0.3.8`.

## 0.3.7 — Supply-chain attestation hotfix

Hotfix for the public build-provenance pipeline; application runtime behavior is unchanged from 0.3.6/0.3.5.

- Fixed `Supply Chain Attestations` dependency-metadata restore on the Linux runner by using `-p:EnableWindowsTargeting=true` for the Windows-targeting .NET project.
- The Linux job performs only dependency metadata restore for SBOM component detection; the Windows application is still built and security-tested exclusively by the trusted `Windows EXE` workflow.
- Keeps the same exact-run/SHA verification, SPDX 2.2 SBOM, Sigstore build provenance and signed SBOM design introduced in 0.3.6.
- Version 0.3.6 itself built and released successfully, but its first supply-chain workflow stopped at `NETSDK1100` before SBOM generation or attestation. 0.3.7 supersedes it for the complete attested release path.
- Application version: `0.3.7`.

## 0.3.6 — Initial SBOM and artifact-attestation rollout

Supply-chain hardening for the public repository; runtime diagnostics and remediation behavior are unchanged from 0.3.5.

- Added `Supply Chain Attestations`, triggered only after a successful `Windows EXE` **push** build on `main`.
- The workflow re-validates the triggering build through the GitHub API and binds all work to its exact tested commit SHA and run ID.
- EXE and pilot artifacts are downloaded only from that exact build and the EXE SHA-256 is re-verified before attestation.
- Added an SPDX 2.2 SBOM generated by the pinned Microsoft SBOM Tool `4.1.5` from the exact checked-out source and release artifact set.
- Added GitHub Artifact Attestations / Sigstore build provenance for the EXE and pilot ZIP.
- Added a signed SBOM attestation binding the SPDX document to the released EXE.
- All GitHub Actions remain pinned to full immutable commit SHAs; the attestation workflow has no `contents: write` permission.
- SBOM JSON and its SHA-256 are retained as a dedicated Actions artifact for 90 days.
- The Windows build and `v0.3.6` Release succeeded, but the first attestation run failed before SBOM generation because Linux restore of the Windows-targeting project required `EnableWindowsTargeting=true`; fixed in 0.3.7.
- Application version: `0.3.6`.

## 0.3.5 — Service Desk UX, remediation boundary and Releases

Изменения по результатам первого запуска 0.3.4 на реальном Windows 11 ПК и security review PR #11:

- `CleanTemp` всегда присутствует в списке действий; автоматически рекомендуется и выбирается только при недостатке места на системном диске.
- `CleanTemp` перенесён за пределы privileged boundary: очищается только `%LOCALAPPDATA%\Temp` текущего пользователя, без elevation; `%WINDIR%\Temp` больше не затрагивается.
- elevated worker больше не принимает `CleanTemp`; при ручном запуске GUI elevated пользовательская очистка fail closed. Это устраняет privileged traversal user-controlled directory tree и связанный junction/TOCTOU риск.
- при совместном выборе `CleanTemp` и DISM/SFC privileged действия выполняются через worker, после чего `CleanTemp` запускается исходным standard-user parent-процессом.
- `Применить выбранное` активно только при выборе автоматизируемого действия и показывает количество выбранных действий; ручные рекомендации нельзя ошибочно применить.
- PID, CPU, RAM и I/O сортируются по числовому значению; Event ID и Count — как числа; timestamp — как дата/время без фиктивного `DateTime.MinValue` при отсутствии значения.
- `START-HERE.txt` pilot-пакета записывается UTF-8 BOM и проверяется strict UTF-8 decoder-ом.
- исправлен PowerShell parser gate CI (`${target}:` вместо невалидного `$target:`).
- корпоративный G-shield и зелёная application icon генерируются детерминированно из versioned source; GUI использует встроенный реальный shield.
- временный self-modifying workflow `Finalize 0.3.5 branding` удалён.
- `Publish GitHub Release` переведён с автоматического `workflow_run` на ручной `workflow_dispatch` с обязательным `run_id` успешного `Windows EXE` push-run на `main`.
- release workflow checkout-ит точный tested SHA, скачивает артефакты только указанного run и повторно проверяет SHA-256 и точную FileVersion перед публикацией.
- версия приложения: `0.3.5`.

Administrative remediation (`DISM`, `SFC`) по-прежнему разрешена только из точного canonical Program Files path; bootstrap/elevation из Downloads отключён.
