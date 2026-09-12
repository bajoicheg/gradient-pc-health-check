# Execution context and elevated Temp preview — implementation plan

## Approved intent
The product owner approved the proposed context/availability improvement and questioned the elevated read-only Temp preview restriction. Continue from released 0.10.0, main c953561412d79bb403d09f71e15e0630a4aec0ca, in feature/0.11.0-execution-context. No further feature expansion is intended.

## Design
Keep normal diagnostics in the GUI's actual token, and elevate only the existing DISM/SFC worker when needed. Expose process identity, effective administrative role, elevation state, limited administrator membership, process session and the user/profile belonging to that same session. Never substitute the physical console user for an RDP session or the technician's profile for an unresolved subject. Preview may read the session user's Temp with any current privilege; deletion requires a verified same-user, non-elevated context. Read-only access failures remain facts, not diagnoses.

User explicitly delegated design decisions. Prefer explicit context over three manually selected role modes. Do not automatically restart the GUI or request elevation merely because some diagnostic fields are inaccessible. Existing portable path/name freedom, repair commands, worker nonce/pipe/session checks, user confirmation, health score and prior features remain.

## Tasks
- [ ] Add nullable context evidence and a pure action-availability policy with test-first coverage for standard/limited/full tokens, different-user launches, unknown session/profile and elevated preview.
- [ ] Collect Windows token facts with query-only APIs; resolve the current process session through WTS, not Win32_ComputerSystem.UserName. Resolve profiles without loading hives, impersonation or changing ACLs.
- [ ] Enable elevated Temp preview with exact subject/root evidence. Keep CleanTemp out of elevated execution and forbid ambiguous/different-user cleanup. Re-evaluate the actual context immediately before execution.
- [ ] Display context and availability before applying changes; save actual execution context for each repair action, including mixed user/admin batches. Preserve unknown evidence for old results.
- [ ] Include collection context in main and Temp outputs; make current-process HKCU scope explicit. Do not claim existing tool snapshots have been retroactively re-collected.
- [ ] Validate original and new tests on Windows, packaged EXE and portable path/name matrix. Review the diff; then merge through protected PR and verify the exact main/release artifacts.

## Validation boundaries
No Windows runtime is available in the editing container; use existing Windows CI. Native tests read the current token/session and disposable files. Test mixed contexts synthetically without invoking DISM/SFC/cleanup or interactive UAC. Real Windows 11 standard/local-admin/UAC-other-account/RDP/denied-WTS/profile and DPI acceptance remains a pilot requirement. No workflow permission changes, code signing or endpoint deployment are part of this iteration.

## Primary references
- https://learn.microsoft.com/en-us/windows/desktop/api/Winnt/ne-winnt-token_elevation_type
- https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsquerysessioninformationw
- https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/ne-wtsapi32-wts_info_class
- https://learn.microsoft.com/en-us/windows/win32/api/userenv/nf-userenv-getuserprofiledirectoryw
