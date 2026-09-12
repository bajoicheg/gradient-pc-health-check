# Resource Diagnostics Implementation Plan

**Goal:** Deliver the next approved item in issue #26: a user-initiated DNS/system-name-resolution and single-port TCP diagnostic session, inside the portable EXE.

**Architecture:** A pure target parser, injectable asynchronous network adapter, session orchestrator, encoded report renderer and separate Windows Form. The existing main Health Score, remediation worker and read-only inspection collectors are unchanged.

**Tech Stack:** C# 12 / .NET 8, WinForms, System.Net DNS/TcpClient, System.Text.Json. No new packages or external executables.

**Spec:** Issue #26, next scoped iteration 1, and the owner's instruction to continue its feature list. Work from v0.6.0 / 988b35a1adc138146eabb5df960d2f15679b54ea.

## Constraints and acceptance
- One explicitly entered hostname or IP and one TCP port. No ranges, automatic destinations, scheduled scans or HTTP/TLS/authentication requests.
- DNS uses the configured system resolver; hosts/cache/search suffixes may contribute. Do not claim a direct DNS-server query.
- TCP uses the resolved addresses, at most eight by default, once each. No application payload; close sockets after each attempt.
- Show per-stage outcomes, elapsed milliseconds, exact socket errors, attempted remote addresses and successful local source address.
- DNS default 5 s, each TCP attempt 3 s, at most 8 addresses. Expose truncation and cancellation; never retain a cancelled attempt as overall success.
- Opening the window makes no network requests. Explicit checkbox and Check button opt into outbound diagnostics. No stored targets or cloud submissions.
- Preserve portable arbitrary EXE path/name and UAC behavior. No networking reset or policy change.

## Task 1: model, parser, session tests
Files: ResourceProbeModels.cs, ResourceProbeService.cs, ResourceProbeSelfTest.cs, Program.cs.
1. Add typed target/options/snapshot/step records and IResourceProbeNetwork interface.
2. Write synthetic target, stage, timeout, cancellation, duplicate/truncation and error-mapping tests. Compile against explicit unimplemented service contracts and observe RED in Windows CI.
3. Implement validation before any network call, asynchronous per-stage cancellation, bounded sequential per-address attempts and explicit session outcomes. Run the same tests to GREEN.

## Task 2: transport and presentation
Files: ResourceProbeNetwork.cs, ResourceProbeForm.cs, ResourceProbeReport.cs, ResourceProbeIntegrationSelfTest.cs.
1. Test actual TcpClient against a loopback-only TcpListener and require zero payload, prompt close and local-address evidence. Never probe external hosts in self-tests.
2. Add menu/form test with no automatic scan; test invalid targets and consent gating.
3. Render current/previous snapshots with escaped HTML and JSON schema 1; distinguish unrelated targets from a meaningful repeat. Export unique files, never overwrite earlier reports.
4. Keep cancellation/error status visible with its timestamp and old results clearly identified until the new snapshot completes. Close cancels current I/O.

## Task 3: release validation
1. Update version/README/changelog and focused release notes with exact test evidence and pilot limits.
2. Review changed files against the scope. Require Windows build, source and packaged EXE tests, portable matrix, analyzer and supply-chain smoke on the final head.
3. Merge by checked-head squash without changing protection. Verify fresh main build, release target SHA and artifact bytes/checksums before delivering.
4. Keep corporate VPN/proxy/DNS and Windows 11 interactive checks distinct from synthetic/loopback CI evidence.

## Primary API references
- https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.tcpclient.connectasync?view=net-8.0
- https://learn.microsoft.com/en-us/dotnet/api/system.net.dns.gethostaddressesasync
- https://learn.microsoft.com/en-us/powershell/module/nettcpip/test-netconnection
