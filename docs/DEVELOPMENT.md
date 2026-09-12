# Development without losing work between sessions

This workflow addresses the connector interruptions seen while preparing 0.10.0 and 0.13.0. It reduces avoidable round trips and preserves evidence; it does not fix or disable OpenAI's internal tool evaluation or expand GitHub permissions. Application version 0.13.0 is unchanged by this maintenance update.

## Working environments

**Preferred:** a dedicated Windows 11 checkout with Git, PowerShell 7 and the exact .NET SDK in `global.json` (8.0.425, matching existing CI). Work locally, test, then push a focused branch for review. Use an ordinary development account, not a production workstation. Authentication belongs to approved Git/GitHub tooling; no tokens in scripts or chat.

**Connector-only:** use the existing authorized GitHub actions and Windows CI. Preserve file/commit references locally and a current PR checkpoint. Adding scripts does not give this chat access to a Windows VM. A Linux sandbox without .NET cannot execute Windows acceptance tests; archive/XML/text checks remain static checks.

These scripts do not install SDKs, configure machines, authenticate to GitHub, change permissions or deploy the product.

## One-command checks

The script resolves its repository root even when called from another directory:

```powershell
# Print the plan; no native commands or network calls.
pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Quick -PlanOnly

# Iteration: tooling tests, restore, audit, compile, all source self-tests.
pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Quick

# Local candidate: also package/test the EXE, run the existing portable
# matrix and verify FileVersion/SHA-256 in a fresh output directory.
pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Full
```

Both profiles first check prerequisites, parse PowerShell scripts and run the development-tool regressions. Native nonzero exits stop the sequence and preserve the failed stage/code; stderr alone does not imply a failed command. Arguments are passed individually without shell evaluation. Audit command errors, warnings, vulnerabilities and malformed reports are rejected. SDK 8 JSON format 1 omits framework arrays when no vulnerable packages were found; that observed success shape is accepted only with a project path and both expected vulnerability/transitive flags, and has a regression fixture.

Quick skips four expensive stages during edits: publish, published-EXE self-test, portable EXE matrix, package/version/hash verification. All source self-tests and the vulnerability audit remain. Full adds those four stages, using the existing portable-security script. Neither replaces final required GitHub checks. Full is not the complete release workflow, pilot acceptance, SBOM, attestation or publication; local `ReleaseReady` is always false.

Restore/audit may contact configured package sources. Existing tests use the documented synthetic/native fixtures, not real repairs. Review the test code before running on a machine with valuable data.

Every run writes to a unique ignored `artifacts/dev/<UTC-time>-<id>/` directory. Per-stage stdout/stderr and `summary.json` record Git SHA, branch, dirty-worktree flag, SDK, profile, timestamps, durations, stage states and exit codes. Full adds `publish/G-PC-Health-Check.exe` and its checksum. `Dirty=true` means the tested workspace differs from its recorded SHA; it must not be claimed as exact-commit validation.

The summary is replaced atomically before/after each step. A killed runner may leave `Running` or `NotRun`, never an inferred pass. This is progress preservation, not automatic resume: rerun verification after restarting. Native calls have no hard local timeout. Ctrl+C or shell closure may leave child work running; inspect owned processes before another run. CI job timeouts remain in force. The runner does not forcibly terminate other processes.

Only compact summaries are uploaded by Developer Tools; raw logs remain local unless explicitly reviewed/shared. Even paths and source names can be sensitive. Do not upload entire worktrees or environment dumps.

## Push, review and publication

1. Refresh main and the task PR; record base/head. Reuse the branch on continuation.
2. Add focused tests and demonstrate RED. Implement with Quick checks. Preserve both modified AND new source files; a chat recap is not the source of truth.
3. Complete docs alongside code. Run Full for the final local candidate where Windows is available. Push per useful checkpoint, not per file.
4. Read run state for the exact head/event. Retain run/job IDs; poll summaries at 30–60 second intervals while attending. Read completed failed-job logs rather than repeatedly requesting logs not ready yet.
5. Review diff and all relevant final-head checks. Merge using the expected head SHA. A green older SHA, skipped job or local Full result does not validate publication.
6. For a product release, separately verify main build, publication and downloaded asset hashes. Tools/docs-only work leaves app version and existing release assets unchanged.

Existing Windows EXE, analyzer, supply-chain-smoke, release and attestation workflows are unchanged. The path-filtered Developer Tools job runs only for its tools, SDK pin or workflow changes (and manual dispatch). It checks helpers, executes Full and then Quick on Windows, and uploads compact summaries. Do not make it a blanket required check for unrelated PRs. It is intentionally additional validation when development infrastructure changes, not an extra job for every application change. No docs-only skips, cached test verdicts or weakened audits are introduced.

## Classify failures

| Observation | Next action |
|---|---|
| Tool cannot determine safety status | Stop that operation. Preserve exact text, UTC/timezone, action, PR/base/head and request ID privately; escalate to platform support. No bypass by another endpoint, account, encoding or payload disguise. |
| GitHub authentication/authorization error | Verify account, repository selection and organization approval. Generic 404 alone is not proof of missing rights. Never ask for tokens in chat. |
| Rate limit | Respect Retry-After/reset; no faster polling or account rotation. |
| Write timeout/network interruption | Read current ref/commit/PR before retrying; no duplicate commits/comments or overwritten work. |
| Ref conflict | Refresh, reconcile actual diff and retest; no force push/protection bypass. |
| CI build/test failure | Read the failed step once; separate intended test-first RED from regressions. Fix the cause, not the gate. |
| Logs absent while job runs | Keep its ID, wait for completion. Not a code/permission diagnosis. |
| No local Windows/SDK | Use hosted Windows or an approved developer machine. Do not repeat failed local SDK installation; static checks remain static. |

Bounded retries with backoff are appropriate for transient reads/transport failures, not repeated safety denials. A blocked write requires state reconciliation/support, not more permissions or repeated 'continue'. Independent permissible review/docs may continue, honestly labelled as such.

## Handoff and progress

Keep one current checkpoint in the PR body: task, branch/base/head, local-only/untracked files, tests keyed by SHA/run, blocker and next concrete step. Use the PR template; optionally copy `docs/development/handoff-template.json` into ignored `artifacts/` locally. Metadata is not a backup. `git diff HEAD` does not include untracked new files; preserve them in the checkout or an explicitly reviewed snapshot.

On a denied write, save permitted local work and its exact baseline, clearly labelled uncompiled/unverified when applicable. Avoid chains of new patch archives with unclear predecessors. Never publish private payloads or signed download URLs. Resume by reading and reconciling actual remote state, not blindly applying an old patch.

During long attended work report factual milestones roughly every 2–4 minutes. Finish with prepared/committed/tested/reviewed/merged/released status and the next unresolved step, not just 'continuing'. Never imply background development after the response ends.

## References and limits

- https://learn.microsoft.com/en-us/dotnet/core/tools/global-json
- https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-list-package
- https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax
- https://help.openai.com/en/articles/6614161-how-can-i-contact-support

The SDK pin controls compiler selection, not dependency reproducibility by itself. Caching, narrower application test selection, dependency locking and release-pipeline refactoring need separate measured/reviewed changes. No percentage speedup or permanent fix to connector evaluation is claimed.
