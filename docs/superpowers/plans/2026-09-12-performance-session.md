# Performance session implementation plan

## Approved continuation
Continue #26 after 0.8.0 with the previously proposed timed observation during reproduction of a workstation symptom. Deliver a separate, explicitly started session, not a stress test or background agent. One portable EXE and the existing arbitrary location/name/UAC behavior remain unchanged.

## Design
- Add Analysis > Performance session, initially idle. Duration presets 30/60/120/300/600 seconds, sampling interval 1/2/5 seconds; default 120/2. Separate start, stop-and-retain, symptom marker, chart selector, copy/export.
- CPU uses differences in GetSystemTimes (kernel includes idle); physical memory uses GlobalMemoryStatusEx. A multi-processor-group machine must not be misrepresented as whole-machine CPU: mark CPU unavailable there in this iteration. Disk uses the existing WMI performance provider, PhysicalDisk _Total PercentIdleTime and CurrentDiskQueueLength. Busy = 100 - validated idle; invalid/missing counters are not zero. Disk aggregation is not a measurement of the Windows volume alone.
- Monotonic deadlines, timestamp/collection-duration per sample, no overlapping reads and no catch-up bursts. Missed slots and provider overruns are recorded. Cooperative stop retains earlier samples. The provider can exceed its timeout; never claim hard real-time collection/cancellation.
- Live chart uses actual sample offsets, breaks at unavailable values or long gaps. Four selectable series; exports contain all four. Statistics use valid samples only: count, min, median, nearest-rank P95, max and count of threshold-crossing samples, not percentage of time or proof of bottleneck. Original Health Score/Coverage is not changed.
- At most 100 explicit symptom markers, 160 characters each, with monotonic offsets. Markers do not prove causation. Export whole session to unique local HTML/JSON, encode notes as text, no overwrite, no upload. New snapshot includes outcome and data completeness separately.

## Tasks and files
1. Add PerformanceSessionModels.cs, behavioral self-tests and temporary throwing core/report contracts; wire tests into Program. Observe Windows behavioral RED after successful compile.
2. Implement PerformanceSessionCore.cs (validation/math/scheduler/stats/marker limits), PerformanceSessionReport.cs (encoded HTML/SVG, JSON, text) and WindowsPerformanceSessionSource.cs (native CPU/RAM + read-only bounded WMI). Keep source and clock injectable for deterministic cancellation/slow-provider tests.
3. Add PerformanceSessionForm.cs with one shared gate, lifetime-safe progress, live chart, table, markers, statistics and export. Add integration self-test covering idle UI/menu, native layout and read-only source, rendered chart and export uniqueness.
4. Version 0.9.0, release notes/README/CHANGELOG. Review changes and verify existing checks, single-file and renamed-portable matrix; only merge after exact-head GREEN.
5. Verify fresh main build, release and downloaded hashes/PE version. Update #26 with what shipped and what remains.

## Validation boundaries
Synthetic scheduler/counter/error/stop tests must not depend on real load. Local Windows source integration reads only native counters and WMI; no external targets or repair commands. Hosted Windows Server is not Windows 11 user/DPI/provider/UAC acceptance. No driver, new package, stress workload, process termination or new privileged action.

## Primary API references
- https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-getsystemtimes
- https://learn.microsoft.com/en-us/windows/win32/api/sysinfoapi/ns-sysinfoapi-memorystatusex
- https://learn.microsoft.com/en-us/previous-versions/aa394308(v=vs.85)
