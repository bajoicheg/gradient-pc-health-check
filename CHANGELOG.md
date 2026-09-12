# Changelog

## 0.13.0 — Applications using a selected file

- Adds an idle-on-open Analysis window using Windows Restart Manager for one explicitly selected ordinary local file.
- Preserves RM application/service names separately from executable metadata verified by exact PID and process creation time on a query-only handle.
- Adds choose/paste path, repeat/stop, literal search, numeric sorting, details, elapsed progress, clipboard and full current/previous-attempt HTML/JSON exports.
- Distinguishes an unavailable or cancelled query from a successful empty result; neither is presented as proof that the file can be deleted or renamed.
- Bounds list retries to four and records to 1024; validates returned counts against capacity and attempts session cleanup on every exit after successful start.
- Fixes all four source-review findings: invalid-record PID-cache poisoning, reserved DOS path components, lost native cancellation code and unrealistic zero-capacity fake responses/service count validation.
- Keeps native service rows separate even when they share a process. No file-content read/change, forced handle closure, process termination, service restart or privilege adjustment is added.
- Adds 44 behavior, six native/UI/export and 20 follow-up acceptance cases. Previous suites and the 32-case portable worker matrix remain enabled.
- Preserves arbitrary executable location/name, existing UAC/context/Temp rules, health model, packages and workflow permissions.
- Version `0.13.0`, FileVersion `0.13.0.0`. [Scope and pilot checks](docs/releases/0.13.0.md). [Observed validation](docs/releases/0.13.0-validation.md).

## 0.12.0 — TCP/UDP endpoint and process review

- Adds an idle-on-open Analysis window for local TCP4/TCP6/UDP4/UDP6 owner-PID tables, addresses, ports and native states.
- Matches process names only across consistent pre/post PID and creation-time observations; unavailable and changing identity stays explicit.
- Provides qualified two-snapshot comparisons; failed/partial tables cannot imply disappearance, and ambiguous tuples are preserved.
- Includes literal search, protocol/state/change filters, numeric sorting, details, repeat/stop, elapsed progress and full HTML/JSON exports with collection context.
- Distinguishes UDP local bindings and TCP listeners from remote conversations or externally reachable services. No reverse DNS, active probe, capture, connection/process modification or elevation.
- Bounded native buffers/retries and retained rows; UI display limit does not truncate saved evidence further.
- Adds 39 behavior and eight integration cases, including disposable IPv4/IPv6 loopback sockets. Previous tests and portable worker matrix remain enabled.
- Preserves 0.11.0 execution-context/Temp boundaries, all prior tools, remediation commands, health model, dependencies and workflow permissions.
- Version `0.12.0`, FileVersion `0.12.0.0`. [Scope, sources and pilot checks](docs/releases/0.12.0.md).

## 0.11.0 — Execution context and elevated read-only Temp preview

- Distinguishes standard, filtered-admin, elevated-admin and full-token contexts; separates process account from current-session user/profile without a physical-console fallback.
- Adds a main-window context strip, details/availability matrix and preflight action availability; unavailable operations cannot remain selected.
- Allows read-only session-user Temp preview from elevated and separate technician accounts when the target profile is known.
- Keeps deletion in a verified same-user non-elevated context, with a fresh runtime check and explicit target scope.
- Removes unrelated user-profile prerequisites from machine repair commands; keeps original UAC and portable EXE behavior.
- Records actor/rights/target per remediation action and preserves mixed contexts in UI, clipboard and HTML/JSON; missing legacy context stays unknown.
- Fixes native effective-role inspection by opening the current token with the identification-duplication right required by WindowsPrincipal.
- Rejects invalid/inaccessible cleanup roots instead of confusing Directory.Exists false with successful empty cleanup.
- Adds 30 context/preview, seven native/UI/report and eight action-boundary review cases; previous tests stay enabled.
- No new commands, credential storage, impersonation, packages, drivers or workflow-permission changes. Version `0.11.0`, FileVersion `0.11.0.0`. [Scope and pilot checks](docs/releases/0.11.0.md).

## 0.10.0 — Folder space analysis and detailed storage evidence

- Adds idle-on-open Analysis tools for an explicitly chosen folder and local physical-disk details.
- Streams metadata into logical folder totals, own versus subtree sizes, file counts and the 200 largest files; supports typed sorting, literal search and full-snapshot export.
- Makes access errors, skipped reparse points, cancellation and bounded traversal explicit; does not infer physical allocation or recommend deletion from size.
- Uses the documented physical-disk/reliability-counter association with nonempty matching DeviceId and rejects ambiguous/mismatched evidence.
- Shows available firmware, health/media/bus codes, temperature/device limit, consumed wear, power-on hours and uncorrected error counters without inventing absent values.
- Adds two GUI windows, progress/stop, row details, clipboard and non-overwriting HTML/JSON. Reports retain all collected rows irrespective of search and show immediate-folder size shares.
- Adds 53 behavior and eight integration cases, complementing prior suites and the portable worker matrix.
- Preserves arbitrary EXE location/name, UAC, original Health Score/Coverage, existing repairs, dependencies and workflow permissions. No disk operations or file-content reads/deletion are added.
- Version `0.10.0`, FileVersion `0.10.0.0`. [Scope and validation limits](docs/releases/0.10.0.md).

## 0.9.0 — Timed performance observation

- Adds an explicitly started CPU/RAM/disk session with duration and interval controls, default 120 seconds / 2 seconds.
- Adds live actual-time graphs, missing-data gaps, a full sample table, collection-time/warning evidence and timestamped symptom notes.
- Adds sample-based min/median/nearest-rank P95/max and reference-threshold counts, without treating these as time fractions or diagnoses.
- Uses native CPU/physical-memory counters and read-only aggregate WMI disk idle/queue data; unknown values and unsupported whole-machine CPU remain unavailable.
- Supports cooperative stop retaining completed observations, no overlapping/catch-up reads, explicit skipped slots, and local full-session HTML/JSON/clipboard outputs.
- Corrects final-sample loss on small timer jitter with a bounded 100-ms grace and avoids median overflow from finite nonnegative values.
- Adds 45 behavior, 10 integration and five timing/review cases; prior tests remain enabled.
- Keeps portable arbitrary EXE location/name, UAC, original health model, previous tools, dependencies and remediation allow-list unchanged.
- Version `0.9.0`, FileVersion `0.9.0.0`. Scope, measurement semantics and validation limits: [version notes](docs/releases/0.9.0.md).

## 0.8.0 — Incident-window events and process details

- Adds local Application/System event review for an explicit interval, literal search and journal/Event ID/severity filters.
- Adds current process metadata, exact PID/creation-time checked owner lookup, details and full local exports.
- Makes partial data and provider failures visible; historical emitter PIDs are not automatically attributed to current processes.
- Adds 44 behavior and seven integration cases. [Version notes](docs/releases/0.8.0.md).

## 0.7.0 — Explicit DNS/TCP resource diagnostics

- Adds one-host/one-port diagnostics with separate resolver and per-address TCP outcomes, timing, source IP and error evidence.
- Requires explicit outbound-probe consent; supports IPv4/IPv6/IDN, bounded timeouts, repeat/cancel and local exports.
- Distinguishes cancellation, mixed results and missing data; does not claim TLS/application health from TCP success.
- Adds 55 behavior, six integration and four cancellation/evidence cases. [Version notes](docs/releases/0.7.0.md).

## 0.6.0 — Read-only cleanup and startup review

- Adds metadata-only preview of existing user-Temp cleanup candidates and the largest eligible files.
- Adds searchable supported Run/RunOnce/Startup records with exact commands/references and source status.
- Includes cancellation, missing-data warnings, full details and non-overwriting HTML/JSON exports.
- Adds 36 behavior and eight UI/export cases. [Version notes](docs/releases/0.6.0.md).

## 0.5.2 and earlier

The complete existing history is preserved verbatim in [Changelog through 0.5.2](docs/releases/CHANGELOG-THROUGH-0.5.2.md), including portable elevation, system-volume fixes, common-problem diagnostics and earlier security/supply-chain work.
