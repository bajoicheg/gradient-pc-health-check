# G PC Health Check 0.5.2 — Windows 11 pilot

## Changed expectation

DISM/SFC are now permitted from any location and EXE name. **Do not use the old test that confirms SFC expecting a Downloads path refusal: that refusal no longer exists.** First test UAC cancellation from a standard-user session. Actual SFC/DISM execution belongs on an approved test workstation only.

Do not disable AV/EDR, Windows services or execution policy. This pilot does not require installing the app in Program Files.

## 1. Verify the release and launch normally

Use the EXE/checksum from one release. Verify the SHA-256 before opening it; after renaming, use the new filename with `Get-FileHash` and compare the same hash value.

```powershell
Get-FileHash -LiteralPath '.\G-PC-Health-Check.exe' -Algorithm SHA256
Get-Content -LiteralPath '.\G-PC-Health-Check.exe.sha256'
```

Start the GUI with a normal double click. Confirm that diagnosis does not request UAC, the interface stays responsive and missing telemetry is explicit. Check main metrics, system-volume identity, triage, clipboard and HTML. Windows installed on a non-C volume remains a separate acceptance scenario from 0.5.1.

## 2. User Temp scenario

```powershell
powershell.exe -NoLogo -NoProfile -File .\e2e\Prepare-CleanTempScenario.ps1 -IncludeJunctionTest
```

Select only user Temp cleanup, confirm it and check that UAC does not appear. Windows Temp/Prefetch and junction targets must not be cleaned. Repeated diagnostics and before/after reporting must finish.

```powershell
powershell.exe -NoLogo -NoProfile -File .\e2e\Verify-CleanTempScenario.ps1
```

Expected: the dedicated old sentinel is deleted; fresh and junction-target sentinels are preserved. The helper's CI smoke is only a simulated sentinel deletion, not actual application remediation.

## 3. Portable/renamed UAC cancellation

Close the GUI between cases. Copy the same verified EXE to Downloads, a directory with spaces/Unicode, another local volume when available and an optional managed-install directory. Include a renamed EXE such as `Проверка ПК (1).exe`; compare hashes after copying.

For each case, launch normally from a standard-user session, select only SFC and confirm the app's action dialog. Expected: UAC is reached without a directory/name refusal. **Cancel UAC**; SFC must not run, the GUI must explain cancellation and remain usable. A second GUI or an orphan worker must not remain. If the test process is already elevated, there may be no UAC prompt; do not use that session for this cancellation test.

## 4. Approved positive UAC test

On an approved Windows 11 test machine, repeat with SFC and approve UAC, including a separate Service Desk admin identity in one case. Verify that the elevated worker is the same renamed EXE at the same actual location, the parent receives results and the before/after report contains the command exit code. Run DISM only when appropriate for the test machine; it is not necessary simply to verify path portability.

If launching from a network/mapped path, independently check that the administrative identity can read it. Windows policy/access failures are not bypassed by the application. A synthetic UNC launch-contract test does not prove network access in a real domain.

## 5. Combined actions and elevated-GUI guard

From a normal GUI, select SFC plus user Temp cleanup. UAC belongs to the worker; `CleanTemp` must not be included in its action list and must run in the parent user's context after the worker returns. Cancelled UAC must not partially execute the combined selection.

Separately start the GUI elevated and select only `CleanTemp`: it must still refuse privileged user-Temp traversal and request a normal relaunch. No directory/name restriction is reintroduced for DISM/SFC.

## 6. Common-problem and interface regression

Check main/common-problem windows at 100/150/200% DPI, keyboard navigation and long device names. Repeat local network/printer/PnP checks, cancellation and HTML/JSON export. Confirm that configuration is not presented as proof of reachability or a resolved user symptom. Do not enable devices, change corporate network settings or restart Spooler automatically.

## 7. Evidence and cleanup

Pass the actual executable path, including a renamed filename:

```powershell
powershell.exe -NoLogo -NoProfile -File .\e2e\Collect-E2EEvidence.ps1 -SourceExe '.\Проверка ПК (1).exe'
powershell.exe -NoLogo -NoProfile -File .\e2e\Analyze-E2EEvidence.ps1 -EvidenceZip '<path-to-zip>'
powershell.exe -NoLogo -NoProfile -File .\e2e\Cleanup-E2EScenario.ps1
```

The evidence tooling retains a legacy optional comparison against a Program Files copy. An absent or older installed copy can therefore produce an analyzer warning/mismatch; it is not a requirement to install the portable release and must not substitute for comparing the actual source EXE with its release checksum. Record portable UAC/rename outcomes explicitly in the pilot notes.

Evidence may contain host/user/domain/device/network details and must not be posted publicly without review. Cleanup removes only dedicated E2E test paths.

## Acceptance boundary

Automated release gates: successful exact PR/main builds, existing regressions, 20 portable-elevation scenarios, 32 published-EXE process checks, version/checksum and pilot validation. The process matrix intentionally does not execute OS remediation or interact with UAC.

Workstation acceptance: normal diagnosis, old/fresh/junction Temp checks, renamed Downloads UAC-cancel, approved alternate-admin positive UAC and combined-action results. Record each as PASS/FAIL/NOT RUN. A published CI-tested release does not itself certify this manual pilot or Authenticode signing.
