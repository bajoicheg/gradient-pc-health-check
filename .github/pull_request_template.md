## Summary

Describe what changed and why.

## Testing

Describe the tests you ran and their results.

## Security / release checklist

- [ ] I did not commit or attach credentials, secrets, internal URLs/IPs, usernames, domain names, or raw workstation evidence.
- [ ] I did not attach an unreviewed `G-PC-Health-Check-E2E-*.zip` bundle.
- [ ] If remediation/elevation behavior changed, I explained the privilege boundary and added/updated negative tests.
- [ ] The elevated worker still accepts only the intended hardcoded action allow-list.
- [ ] Canonical-path, nonce/session, and reparse-point protections are preserved or intentionally changed with rationale.
- [ ] If GitHub Actions/release logic changed, I considered untrusted fork PRs and `GITHUB_TOKEN` permissions.
- [ ] I did not add an unpinned third-party GitHub Action.
- [ ] User-facing documentation is updated where required.
