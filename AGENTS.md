# Development instructions — G PC Health Check

Read this file and `docs/DEVELOPMENT.md` before changing the project. These instructions organize legitimate development; they do not override tool restrictions, safety checks, user approvals or repository protections.

## Resume from evidence, not a conversation recap

1. Read the current main ref and the relevant open PR once. Record base SHA, head SHA, branch, task scope and the last tested SHA/run ID. Check actual changes before accepting a previous completion claim.
2. Read only the files needed for that task, at an explicit SHA. Reuse cached file bodies and returned blob SHAs; do not repeatedly retrieve the entire repository, PR body or successful full logs.
3. Work on the existing task branch when resuming. One coherent feature or maintenance task per PR. Do not create another release branch or another draft archive merely because a chat turn ended.
4. Keep one current checkpoint section in the PR body, using `.github/pull_request_template.md`; use `docs/development/handoff-template.json` for a local handoff. Update at a meaningful checkpoint, not on every poll. Remote state may differ from the checkpoint: re-read refs on resume.

## Short iteration loop

- On a prepared Windows checkout: `pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Quick`.
- Quick includes restore, transitive vulnerability audit, warnings-as-errors compilation and ALL source self-tests. It omits EXE packaging and the repeated portable EXE matrix; it does not replace final CI.
- Before proposing the final head: `pwsh -NoProfile -File tools/dev/Invoke-DevCheck.ps1 -Profile Full`. It adds fresh EXE packaging, the EXE self-test, existing portable matrix, FileVersion and SHA-256 validation.
- In a connector-only environment, inspect available commands once. If Windows/.NET are absent, use normal Windows CI for runtime evidence; static checks are not C# compilation. Do not repeatedly attempt unavailable local SDK installation.
- Keep test-first changes small, demonstrate the expected failing behavior, then implement and verify. A test-only RED commit is not a product release. Prepare documentation alongside code to avoid an extra full CI cycle just for forgotten version notes.
- Do not bump the application version or publish new assets for development-process-only changes. Existing immutable-by-policy release assets stay associated with their tested commit.

## GitHub interaction budget

- Group logically related file changes in a normal commit from the known base. Preserve transparent source content; batching is not a way to evade a blocked request.
- After an uncertain write result, read the ref/commit/PR before retrying. Never assume failure means nothing was written, and never force-push to resolve uncertainty.
- List runs for the exact SHA/event once. Save run/job IDs. Poll the relevant run at sensible intervals (e.g. 30–60 seconds while actively attending); do not request completed-job logs while a job is still running.
- Read failed-step logs once after completion. For a passing run, step summaries and the compact result artifact normally suffice; inspect full logs when needed to support exact claims.
- A tool safety denial is NOT a GitHub permission error or a compiler failure. Stop the blocked operation. Do not split, encode, rename, change endpoints/accounts or use an alternate channel to circumvent it. Preserve the checkpoint and error privately; request platform support. Do not loop identical rejected writes. A later legitimate user-authorized continuation still uses ordinary checks.
- For actual authorization failures, verify account/repository access; do not ask for a token in chat. For a rate limit, respect Retry-After. For transient read failures, bounded retries are acceptable. For writes/timeouts, reconcile state first.

## Observable progress and handoff

Tell the owner what is being done before starting. During a long active operation give factual milestone updates, normally every 2–4 minutes; do not fabricate progress or promise work after the response ends. End with committed changes, exact checks, PR/release state and the one next unresolved step. State clearly whether a binary exists and whether it is a release or only a locally built artifact.

Record separately: prepared locally / committed / tested / reviewed / merged / published / downloaded bytes verified. Include stale/partial/unknown states. Keep build logs and handoffs under ignored `artifacts/`; inspect them before sharing. No credentials, env dumps, private workstation evidence or signed download URLs in public issues/PRs. Prefer one current source snapshot and a patch against a named base, not a growing set of ambiguous archives.

## Existing product boundaries

- One portable Windows x64 EXE; arbitrary valid EXE location/name remains supported.
- Preserve UAC, worker allow-list and nonce/session binding; do not reintroduce the retired Program Files-only rule.
- Elevated read-only Temp preview is allowed. Temp deletion remains tied to a verified same-user non-elevated context.
- Missing telemetry is not zero or proof of health; query-only tools must not silently become remediation.
- Preserve read-only PR permissions, pinned Actions, independent required checks and the exact-main-build publication/attestation chain. No bypass merges or disabling failing checks.
- Hosted Windows Server tests are not interactive Windows 11/UAC/RDP/DPI acceptance. A source review by the implementing agent is not an independent reviewer.
