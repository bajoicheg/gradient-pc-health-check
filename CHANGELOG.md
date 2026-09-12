# Changelog

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
