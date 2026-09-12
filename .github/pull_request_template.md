## Goal and scope

What changed, why, and what is deliberately unchanged? Keep one coherent task per PR.

## Current checkpoint (update this block, not a new recap each turn)

| Field | Current evidence |
|---|---|
| Stage | planned / implementing / blocked / checking / reviewed / merged / released |
| Branch; base SHA; current head SHA | |
| Local-only files or untracked source | none / list and reviewed local snapshot location |
| Last tested SHA; workflow run/job | |
| Actual result and scope | RED / failed / running / source pass / packaged pass; counts and unperformed checks |
| Blocker category | tool evaluation / transport / authorization / conflict / CI / local environment / none |
| Next concrete step | |

Do not paste credentials, full private payloads, signed URLs or workstation evidence here. Handoff metadata is not a substitute for preserving new source files.

## Validation

Record commands, exact source SHA/worktree state, outcomes and evidence. Quick is a local iteration result, not full release acceptance. A green older commit does not validate a new head. Identify manual Windows 11/UAC/RDP/DPI tests still needed. See `docs/DEVELOPMENT.md`.

## Security / release checklist

- [ ] No credentials, secrets or unreviewed workstation evidence were committed/attached.
- [ ] Remediation/elevation changes, if any, have rationale and negative tests.
- [ ] Worker action allow-list, nonce/session binding and applicable reparse-point protections remain enforced.
- [ ] Arbitrary valid portable EXE location/name and elevated read-only Temp preview remain supported; no obsolete Program Files-only rule was restored.
- [ ] Workflow changes preserve untrusted-PR restrictions, least privilege, pinned Actions and exact-tested-SHA publication.
- [ ] All relevant final-head checks finished successfully, including developer-tools when that workflow applies.
- [ ] User/developer documentation is included in this logical change.
- [ ] Tools/docs-only maintenance does not bump the app version or replace release assets.
