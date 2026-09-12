# Network endpoints implementation plan

## Intent and design
Continue the approved analogue-derived roadmap #26 after 0.11.0. The next bounded product delivery is an idle-on-open, read-only TCP/UDP endpoint window, inspired by TCPView's documented endpoint/process presentation, implemented independently with Windows IP Helper APIs. No third-party code or binaries are bundled.

Prefer GetExtendedTcpTable/GetExtendedUdpTable OWNER_PID over parsing localized netstat output or introducing a driver. Read TCP4/TCP6/UDP4/UDP6 separately with per-source status, receipt times, bounded buffers/retries and cancellation. UDP tables describe local bindings, not remote conversations; LISTEN remote fields have no meaning. No packet capture, reverse DNS, traffic probes, connection closing, process termination, elevation or firewall changes.

Read process identities before and after the four tables. Only a matching PID and creation time in both observations may decorate an endpoint with a process name. Missing/changing/duplicate identity remains explicit; PID 0 is not the idle process. This is evidence for a sampled interval, not a continuous trace.

Compare the two most recent snapshots only for complete corresponding tables from the same host and actor/session/rights with chronological timestamps. Preserve duplicate tuples and mark ambiguous matching. Labels mean appeared/not observed/state changed between snapshots, not exact socket lifetime events. Unknown or failed collection cannot imply disappearance. Retain both raw snapshots in reports. Partial/cancelled results are labelled.

## Tasks
- [x] Add models, test-first core contracts and regressions for layouts, endian decoding, empty versus unavailable data, bounded collection, checked process attribution, comparison and export.
- [x] Verify RED in existing Windows CI while prior suites remain green.
- [x] Implement pure decoder/association/comparison and a bounded query-only Windows source. Integration tests use only disposable loopback TCP/UDP sockets.
- [x] Add Analysis menu/window, repeat/stop, literal search, protocol/state/change filters, numeric sorting, details, elapsed progress and full-snapshot HTML/JSON export. Display context captured at collection.
- [x] Directly review source, documentation and successful behavior/integration/packaged/portable tests on implementation f568da2e40d76a37075e6fc274d07639583e3d04. Not an independent review.
- [ ] Complete fresh final-head checks, merge through protected PR #33, and verify exact main/release artifacts. Record final state on the PR to avoid changing the just-tested release SHA.

## Limits
5000 retained rows per native table; 16 MiB maximum buffer and 4 retrieval attempts; 8192 process objects per observation; UI displays at most 2000 matching rows, exports preserve the full retained snapshots. No auto-refresh/background monitoring in this first delivery. Provider calls may delay cooperative cancellation. Native tests on hosted Windows Server are not a Windows 11/VPN/IPv6-link-local/UAC/DPI pilot. No changes to existing diagnostics/remediation/context semantics or workflow permissions.

## Sources reviewed 2026-09-12
- https://learn.microsoft.com/en-us/sysinternals/downloads/tcpview
- https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedtcptable
- https://learn.microsoft.com/en-us/windows/win32/api/iphlpapi/nf-iphlpapi-getextendedudptable
- https://learn.microsoft.com/en-us/windows/win32/api/tcpmib/ns-tcpmib-mib_tcprow_owner_pid
- https://learn.microsoft.com/en-us/windows/win32/api/tcpmib/ns-tcpmib-mib_tcp6row_owner_pid
- https://learn.microsoft.com/en-us/windows/win32/api/udpmib/ns-udpmib-mib_udp6row_owner_pid
