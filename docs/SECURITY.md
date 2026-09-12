# Security design — 0.5.2

## Current boundaries

Diagnostics do not modify the OS. Remediation requires an explicit selection and confirmation. DISM/SFC require an administrative token; a standard-user GUI launches the same current EXE via Windows UAC (`runas`). The worker accepts only `FlushDns`, `Dism`, `Sfc` and validates session, pipe name and nonce before executing commands. Unknown/mixed action lists and `CleanTemp` are rejected by the worker.

## Portable execution: intentional change in 0.5.2

The former exact Program Files directory/filename restriction has been removed from both GUI dispatch and worker startup following the product owner's explicit request. Downloads, other local directories and renamed EXE copies are not blocked by application policy. Installation or copying to a fixed path is not required. A fully qualified current executable path is used directly, not a command shell or user-supplied remediation executable.

The UAC child uses the Windows system directory as its working directory. This avoids inheriting a writable working directory, but does not claim to eliminate executable replacement, loading risks from the EXE directory, or the interval before elevation. Directory-based deployment trust is no longer an enforced application boundary. Windows permissions, UAC and enterprise application-control policies remain in force; network/mapped locations can have different accessibility under another administrative identity.

Do not move, rename or replace the running executable. Close the GUI first, then rename/move and reopen. A UNC path string is supported by the launch contract; real access from a separate admin identity must be checked in the environment.

The obsolete `--bootstrap-worker` mode still returns 48. It is not used by portable elevation and does not copy/install any executable. Retired exit code 26 no longer represents a path gate.

## User Temp cleanup

`CleanTemp` runs only in the original standard-user process and only under the interactive user's `%LOCALAPPDATA%\Temp`. It does not clean Windows Temp or Prefetch, remove entire directories or traverse reparse points/junctions/symbolic links. An already elevated GUI rejects user Temp cleanup and requests a normal relaunch.

For a combined selection, DISM/SFC run in the worker; user Temp cleanup remains in the original parent process after the worker returns. Cancelling UAC prevents the combined batch from starting. Removing the EXE path gate does not put user-controlled directory traversal into the privileged worker.

## IPC and commands

The GUI generates a GUID session and a random 256-bit nonce, creates `GPcHealthCheck-<GUID>` and launches the current EXE with the fixed worker arguments. The worker validates the session/pipe/nonce/action list, checks administrative rights when required, connects before long-running commands and returns JSON through the pipe. The parent requires the expected nonce and session in the result.

The pipe DACL permits the GUI user's SID and the local Administrators group, allowing UAC credentials from a separate Service Desk admin. This is the existing result-channel design; the nonce is not a code-signature mechanism. Commands use Windows system executable paths and fixed arguments; no arbitrary executable, script, deletion root or output path is accepted through worker actions. Existing timeouts remain unchanged.

## Excluded actions

No automatic Winsock/network reset, DHCP release, GPO/EDR changes, SoftwareDistribution/Prefetch/Windows Temp cleanup, reboot, process termination, startup/service disabling, print-job deletion, driver installation or remotely downloaded remediation rule is added. DISM/SFC are not automatically selected merely because they are now portable. A successful command still requires symptom verification and a repeated scan.

## CI and releases

The build has read-only repository permissions and pinned Actions. It runs PowerShell parsing/smoke, deterministic brand checks, dependency vulnerability audit, warnings-as-errors build, source/single-EXE self-tests, portable path/name process tests, invalid worker-request checks, exact version/checksum and pilot validation.

The old noncanonical-path rejection test has been replaced deliberately, not silently skipped: the worker must reach session validation from any path. Invalid session/pipe/nonce/unknown/mixed/CleanTemp requests and the disabled bootstrap remain tested. The published-EXE matrix uses four actual local path/name layouts, including Downloads-style directories, Unicode, spaces and brackets. It performs no DISM/SFC or other remediation.

Protected-main integration uses PR checks. Release publication and supply-chain attestations run only from a successful main push build, verify its exact SHA/run, and use that run's artifacts. SPDX SBOM and provenance are generated separately from the Windows binary's signature.

## Remaining validation and signing

Interactive UAC, a different administrative identity, real Windows 11 providers and actual remediation require the documented pilot. CI runs on hosted Windows Server and is not this pilot. Authenticode signing is not configured. Portable launch is a usability decision, not evidence that the unsigned executable or its writable location is intrinsically trusted. Validate checksums/provenance and distribute through an approved channel.
