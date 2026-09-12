# Incident review implementation plan — 0.8.0

Goal: deliver the next already approved #26 iteration, incident-window events and richer read-only process context, without changing repairs or the health model.

Architecture: bounded synchronous collectors run off the UI thread behind a shared operation gate; typed snapshots retain per-source outcomes. Two opt-in windows attach to the existing Analysis menu. Pure filtering and reports are shared by UI/tests. .NET 8 Windows Forms, existing EventLog and Management packages only.

## Design and boundaries
- Events: fixed local Application and System logs, explicit inclusive UTC window up to seven days, newest records first. Default 1000 records per source, up to 20 seconds checked between provider calls. Application queries include only validated timestamps; all provider/text filtering is literal and local. Per-source failures, cutoff limits and unavailable messages remain visible. Search runs on the collected subset, not unseen records. No Security log, remote session, subscription, log clearing or event writing.
- Processes: bounded current Win32_Process inventory, metadata and raw command strings, not execution or shortcut/file loading. PID + exact WMI CreationDate establishes identity for a separate GetOwner request. Recheck identity before and after that request; absent, changed or unverified identity cannot inherit an old owner's result. No automatic linkage of a historical event's emitter PID to a current application.
- Both windows: open idle, repeat/cancel, elapsed progress, detail pane, typed numeric columns, search, clipboard, unique-folder UTF-8 HTML/JSON. Cancelled/partial results explicitly replace the latest attempt, never a green status. Snapshots have collection times. Local reports may contain private paths, commands and event text; warn before export. Export saves the full snapshot and current filter, not just a filtered subset.
- Preserve single EXE, any location/name, UAC, worker/action allow-list, score/coverage, prior 0.6/0.7 features. No new dependencies, external executables, automatic network probes or settings changes. Event formatting and WMI calls can exceed cooperative deadlines; do not promise hard cancellation.

## Tasks and validation
1. Add contracts, source interfaces and regression suites before implementations. Build via existing Windows EXE PR workflow. Observe clean compile followed by failed new assertions; existing suites must remain green.
2. Implement IncidentEvents collection, query generation and literal filtering. Test window validation/UTC, fixed sources, boundaries, cap/exhaustion, partial access/format errors, cancellation, deterministic ordering, filter combinations and no input mutation.
3. Implement ProcessReview inventory and identity-checked owner lookup. Test cap, denied source, missing identity, reuse before/after owner request, error/cancellation, exact identity equality and search without command execution.
4. Implement Windows adapters, menu/form and reports. Test real read-only Application query and lookup of only the test process, idle/menu idempotence, numeric columns, HTML encoding, current-filter/full-snapshot JSON and non-overwriting export. Never emit events or run repairs in tests.
5. Run all source/single-EXE/portable matrix checks; inspect diff and fix regressions test-first. Update README/release notes. Merge only the exact green head through normal PR controls, verify fresh main CI/release/attestations, then download and hash the release bytes.

Primary references reviewed: https://learn.microsoft.com/en-us/windows/win32/wes/consuming-events ; https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.eventing.reader.eventlogreader.readevent ; https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-process ; https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/getowner-method-in-class-win32-process .

Pilot remains: managed Windows 11, access denied/protected processes, missing event resources, DST/local times, log rollover, cancellation under slow providers and 100/150/200% DPI. Hosted Windows Server CI is not this pilot; no Authenticode signature is introduced.
