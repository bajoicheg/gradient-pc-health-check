# Development without losing work between sessions

This workflow addresses the connector interruptions seen while preparing 0.10.0 and 0.13.0. It reduces avoidable round trips and preserves evidence; it does not fix or disable OpenAI's internal tool evaluation. It does not expand GitHub permissions. Application version 0.13.0 is unchanged by this maintenance update.

## Two supported working environments

**Preferred:** a dedicated Windows 11 checkout with Git, PowerShell 7 and the exact .NET SDK in `global.json` (currently 8.0.425, matching existing CI). The developer/agent works locally, tests, then pushes a focused branch for review. Use an ordinary development account, not a production workstation. Authentication is handled by approved Git/GitHub tooling; do not place tokens in scripts or chat.

**Connector-only:** use the existing authorized GitHub actions and Windows CI. Preserve exact file/commit references locally and a current PR checkpoint. This chat does not automatically gain access to a Windows VM by adding these scripts. A Linux sandbox without .NET cannot execute Windows acceptance tests; successful archive/XML/text checks must be labelled as such.

No automatic SDK download, machine configuration, account changes or deployment is performed by this change. GitHub/Codex permissions and policies continue to apply.

## One-command checks

Run from the repository checkout; the script also resolves the root correctly when called from another directory:

```powershell
# Show the plan without touching the repo, running commands or contacting services.
pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Quick -PlanOnly

# Iteration: tooling tests, restore, audit, compile, all source self-tests.
pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Quick

# Final local candidate: also package/test the EXE, run the existing portable
# matrix and verify exact FileVersion and SHA-256 in a fresh output directory.
pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Full
```

Both profiles run prerequisite/PowerShell-parser checks and the development-tool regression suite first. Native command failures stop the sequence and preserve the failing stage/exit code. An audit failure, invalid JSON or absent projects/frameworks cannot become a clean audit. Argument arrays are passed without shell expression evaluation; a path containing spaces or an ampersand is not a command string.

Quick avoids packaging and repeatedly invoking the same tests from multiple renamed EXE copies during edits. Full reuses existing portable-security tests. Neither profile skips final required GitHub checks. Full is not pilot acceptance, SBOM, attestation, publication or the complete release workflow; `ReleaseReady` is always false in local results. Restore/audit can contact configured package sources; tests exercise only the project's documented synthetic/native fixtures, not real repairs. Review local test code before running on a machine with valuable data.

Each invocation uses a unique ignored `artifacts/dev/<UTC-time>-<id>/` directory. It contains per-stage stdout/stderr logs and `summary.json`, including Git SHA, branch, dirty-worktree flag, SDK, profile, timestamps, durations, stage state and exit codes. Full additionally creates `publish/G-PC-Health-Check.exe` and its checksum. The source SHA alone does not identify uncommitted changes: `Dirty=true` means the tested workspace differs from that SHA and must not be claimed as exact-commit validation.

The summary is replaced atomically before and after each step. A killed/crashed runner can leave `Running` or `NotRun`, never an inferred pass. This records progress, not automatic resume: rerun verification after restarting. Native calls have no hard local timeout; Ctrl+C/closing the shell can leave child work running. Check owned processes before a new run. CI job timeout remains the upper bound on hosted runs. The script does not run privileged repairs or forcibly terminate other processes.

Only compact summaries are uploaded by `Developer Tools`; raw logs stay local unless explicitly reviewed and shared. Even paths and source names can be sensitive. Do not upload a full worktree/environment dump as incident evidence.

## Push/review/publication sequence

1. Refresh main and the existing task PR; record both SHAs. Reuse the feature branch on continuation.
2. Add focused tests and demonstrate RED. Implement with Quick checks while editing. Preserve code in version control; a chat recap is not the source of truth.
3. Complete documentation in the same logical change. Run Full for the final candidate where Windows is available. Push once per useful checkpoint, not per file.
4. Read workflow state for the exact head. Retain run/job IDs; poll summaries at 30–60 second intervals while attending. Request logs for completed failed jobs rather than repeatedly fetching nonexistent logs from running jobs.
5. Review diff and full required checks. Merge only with the expected head SHA after all relevant jobs finish. A green earlier SHA or skipped test is not evidence for the final candidate.
6. On a product release, verify the new main build, publication and exact downloaded asset hashes separately. Do not relabel a PR/local EXE as a release. For tools/docs-only changes, leave the app version and existing release assets alone.

The existing `Windows EXE`, `analyzer` and `supply-chain-smoke` checks and release/attestation workflows are unchanged. `Developer Tools` runs only when its tools, SDK pin or workflow change, plus manual dispatch; it regression-tests the helpers and executes Full on Windows. Do not make this path-filtered workflow a blanket required check for unrelated PRs. All existing full product checks still run. No general docs-only skip, weakened audit, caching of test verdicts or authentication changes are introduced.

## Failure classification — do not call everything a GitHub problem

| Observation | Classification and next action |
|---|---|
| Tool says it cannot determine the safety status | Platform/tool evaluation. Stop that operation; keep exact text, UTC/timezone, action, PR/base/head and available request ID privately. Escalate to platform support. No workaround via another endpoint/account/encoding. |
| GitHub authentication/authorization error | Verify the connected account, repository selection and organization approval. A generic 404 is not sufficient proof of missing rights. Never request credentials in chat. |
| Rate limit | Respect Retry-After/reset; do not speed up polling or rotate accounts. |
| Timeout/network interruption during a write | Inspect current ref/commit/PR before retry; avoid duplicate commits/comments or overwriting another contributor. |
| Ref moved/conflict | Refresh base/head, reconcile the actual diff and retest. No force push or protection bypass. |
| CI build/test failure | Inspect the failed step once. Distinguish intended test-first RED from a regression. Fix the cause, not the gate. |
| Logs unavailable while a job is running | Keep the job ID and wait for completion; not a code or permission diagnosis. |
| No local Windows/SDK | Use hosted Windows verification or an approved developer machine. Static checks remain static. Do not keep repeating failed SDK installation. |

For a genuine transient transport/read failure, a bounded retry with backoff is reasonable. A policy/safety refusal is different and must not be treated as a transient network error. Repeated identical denials need support, not more permissions or repeated 'continue' prompts. Independent permissible review/documentation can proceed, with its limited status stated honestly.

## Handoff when a session ends or a blocker appears

Use one checkpoint block in the PR body (template supplied), with current task, branch/base/head, local-only files, tests keyed by SHA/run, blocker and the next concrete step. For local continuity copy `docs/development/handoff-template.json` into ignored `artifacts/` and fill it. A handoff is metadata, not a backup: ensure tracked AND new source files are preserved in the working tree or an explicitly reviewed snapshot. A plain `git diff HEAD` does not contain untracked new files.

If a remote write is denied, save permitted local work and its exact baseline; label it uncompiled/unverified where appropriate. Do not manufacture a new patch archive each turn with an unclear predecessor. Never publish private error payloads or signed download URLs. After reconnecting, read actual remote state and reconcile; do not blindly apply a stale patch.

Progress reports should name the active stage and last completed verification. During attended long work report meaningful milestones roughly every 2–4 minutes. End with changed/committed/tested/merged/released status, not just 'continuing'. No claim of background work between messages.

## Primary references

- https://learn.microsoft.com/en-us/dotnet/core/tools/global-json
- https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-list-package
- https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax
- https://help.openai.com/en/articles/6614161-how-can-i-contact-support

SDK pinning controls compiler selection, not package reproducibility by itself. Changes to dependencies, caches, focused test selection or the release workflow need their own measured/reviewed update; this iteration does not claim a measured percentage speedup or a permanent fix for connector denials.
