# Storage review implementation plan — 0.10.0

## Approved scope
Continue the #26 roadmap after 0.9.0: folder space analysis and detailed storage evidence, as proposed in the preceding product update and accepted by the user's continuation request. Two read-only tools in the existing Analysis menu; retain the single portable EXE and existing repair/UAC behavior.

## Design
Folder analysis streams metadata for one explicitly selected absolute path, aggregates logical file sizes bottom-up, and retains the largest 200 files. Default limits: 200000 entries, 20000 directories, 120 seconds checked between provider operations. Reparse points are excluded; initial root ancestors and each traversed directory are checked. This is not an atomic filesystem snapshot or a guarantee against concurrent path replacement. File contents are never opened. Hard links count per name; allocated size, ADS, compression, sparse extents and inaccessible data are not inferred. Cancel/limits/access errors preserve partial results. Decimal aggregates avoid long overflow.

Storage evidence reads local MSFT_PhysicalDisk and its explicit MSFT_PhysicalDiskToStorageReliabilityCounter association. Accept one associated counter row with matching nonempty DeviceId only. Preserve missing fields separately from zero, raw health/bus/media/operational codes, firmware, size, temperature, wear, hours and uncorrected error counters. No raw SMART claim, drive letter guessing, repair/reset methods, benchmarks or elevation. Limit 64 disks / 30-second cooperative budget; storage providers may exceed timeouts.

Both windows open idle, provide start/repeat/cancel, elapsed status, literal search, row details and local HTML/JSON export. Exports preserve all collected data independent of GUI filters; warn about sensitive paths and identifiers. Starting a new scan must not silently present old data as its result.

## Tasks and files
- [ ] Add StorageReviewModels.cs, injectable sources, throwing service/report contracts and StorageReviewSelfTest.cs. Wire tests in Program.cs. Observe clean compilation followed by failing behavior assertions on Windows CI.
- [ ] Implement FolderUsageService.cs: path/options validation, streaming traversal, limits/cancellation, immutable input, bounded top files, rollups, visible gaps and warnings. Tests include empty/zero/nested/large files, access errors, late enumeration failure, links, outside-root entries and limits.
- [ ] Implement DiskDetailsService.cs and StorageReviewWindowsSources.cs: local read-only collection and explicit association, valid raw counters and conservative explanations. Test missing/mismatched/ambiguous counters, warning/critical priorities and partial enumeration.
- [ ] Implement StorageReviewReport.cs and StorageReviewForm.cs. Test menu preservation, idle controls, export encoding/unique paths, real temporary-file read-only collection, and actual local WMI source contract without requiring hardware counters.
- [ ] Review exact diff and resolve defects with regressions. Update README, CHANGELOG and docs/releases/0.10.0.md; bump version only in feature branch.
- [ ] Require fresh Windows source/published-EXE tests, existing 32-case portable matrix and remaining CI checks. Merge exact green head without bypasses; verify main/release runs and downloaded asset bytes.

## Platform / validation
No new packages, drivers, external utilities, uploads, background agent, shell commands or workflow privileges. Linux container cannot resolve github.com or run the Windows GUI; use the connected GitHub API and remote isolated branch/Windows CI. Hosted tests are not managed Windows 11/DPI/UAC/OEM/RAID/USB acceptance. Existing v0.9.0 remains released until the new main build succeeds.

## Primary API references
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisk
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-storagereliabilitycounter
- https://learn.microsoft.com/en-us/windows-hardware/drivers/storage/msft-physicaldisktostoragereliabilitycounter
- https://learn.microsoft.com/en-us/dotnet/api/system.management.managementobject.getrelated
- https://learn.microsoft.com/en-us/dotnet/api/system.io.directoryinfo.enumeratefilesysteminfos
