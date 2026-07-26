---
name: codex-solo-workflow
description: Discipline for using Codex as the sole agent, without a separate Claude Code orchestrator. Codex reviews, rescues, and PR-checks its own work. Covers standard self-review, adversarial self-review, rescue (stuck/failing), and PR-readiness review. Triggers when Codex has just modified code, is stuck on a bug, or is preparing a PR.
---

# Codex solo workflow

Review-and-rescue discipline for when **Codex is the only agent** — there is no separate reviewer to delegate to, so Codex reviews its **own** changes. Pick a mode, follow the steps, then triage. The review spec (severity labels, findings format, what-to-check lists, hard constraints) is inline below — apply it to your own changes as if reviewing someone else's.

> Self-review is weak to confirmation bias — you wrote the code you are judging. Counter it: re-read the diff **as if seeing it for the first time**, switch into the reviewer role deliberately, and run the tests so findings rest on evidence, not on your own optimism.

## Mode selection

1. **Stuck / repeated failures / unclear root cause** → Rescue mode.
2. **About to open or finalize a PR** → PR review mode.
3. **High-risk change** (security, auth, DB, migration, payments, concurrency, public API, large refactor, >3 files) → Adversarial self-review.
4. **Ordinary change worth a second pass** → Standard self-review.
5. **No code changed and user did not ask** → do not review.

## Standard self-review

1. Re-read the diff (`git diff`, or the diff against the base branch) in the reviewer role.
2. Walk the What-to-check lists (Correctness / Security / Tests / Maintainability) below.
3. Record findings in the Findings format below (Severity / Confidence / File / Issue / Why it matters / How to verify / Suggested fix).
4. Triage (below), apply only the safe fixes, then run the relevant tests.

## Adversarial self-review

Same as standard, but adopt a stance of **trying to break your own change**:

- Ask "if this change is wrong, where does it break first?" before reading line by line.
- Actively hunt edge cases, boundary values, concurrency, and failure paths.
- For every optimistic assumption, try to construct a counterexample.
- Mark anything you cannot reproduce as `Confidence: Speculative`.

## Rescue mode

When stuck, re-plan instead of improvising another attempt. First write the handoff to yourself:

```text
Goal:
Current failure:
Relevant files:
Attempts already made:
Tests run:
Constraints:
What I need to solve:
```

Then:

1. Choose a **second implementation strategy** — do not repeat the approach that failed.
2. Write the smallest test that reproduces the failure first.
3. Make it pass with the smallest safe change; avoid broad rewrites.
4. Re-read the patch before applying — check for broad rewrites, secrets, generated files, and silenced tests/lint/types.

## PR review mode

1. **Self-review the diff first.** What changed, what behavior is affected, which tests cover it, riskiest files, and whether secrets / generated files / lockfiles / migrations are involved.
2. **Pick the review depth** based on risk: standard for normal, adversarial for risky.
3. Run the chosen self-review above.
4. Triage findings (below). Verify by reading code, reproduce when possible, run focused tests after changes.

## Review severity

- `Blocker`: must fix before merge; likely data loss, security issue, build break, or major regression.
- `High`: likely bug or serious missing test.
- `Medium`: plausible edge case, API compatibility issue, or maintainability risk.
- `Low`: minor issue; include only if clearly useful.

## Findings format

For each finding, use:

```text
Severity: Blocker | High | Medium | Low
Confidence: Confirmed | Likely | Speculative
File:
Issue:
Why it matters:
How to verify:
Suggested fix:
```

`Confidence` lets you triage findings as `Apply` / `Investigate` / `Reject` / `Defer` without re-deriving certainty:

- `Confirmed`: reproducible from the diff or runnable evidence.
- `Likely`: strong inference from code, but not directly reproduced.
- `Speculative`: hypothesis only; must be marked as such.

## What to check

### Correctness

- off-by-one errors
- null/undefined handling
- bad assumptions about data shape
- incorrect async behavior
- race conditions
- timezone/date bugs
- API contract breaks
- error handling gaps

### Security

- secret leakage
- injection risk
- unsafe shell execution
- auth/authz bypass
- path traversal
- unsafe deserialization
- SSRF/XSS/CSRF where relevant
- logging sensitive data

### Tests

- missing test for changed behavior
- tests that assert implementation instead of behavior
- fragile snapshots
- tests that pass without exercising the bug
- missing negative/edge cases

### Maintainability

Only report maintainability issues when they affect future correctness or clear extensibility.

## Hard constraints

- Do not modify secrets, environment files, credentials, or deployment tokens.
- Do not edit generated files unless explicitly requested.
- Do not add new production dependencies without justification.
- Do not perform broad rewrites for narrow tasks.
- Mark speculative review comments as `Confidence: Speculative`; never present them as confirmed findings.
- Do not silence tests, lint, or type errors without explaining the underlying reason.

## Triage

For each self-review finding, record it and pick a decision:

```text
Finding | Decision | Reason
```

- `Apply`: likely real issue; implement the fix.
- `Investigate`: plausible but needs verification.
- `Reject`: style-only, speculative, not applicable, or contradicted by code/tests.
- `Defer`: real but outside the requested scope.

Never apply style-only changes unless the user requested cleanup.

### Conflict resolution

When a finding conflicts with your own implementation decision:

1. Re-read the relevant code and tests before deciding.
2. If the finding is factually wrong (you misread the code), classify it `Reject` and record why.
3. If it is a judgment-based trade-off, surface both views to the user and let the user decide — do not silently override.
4. Do not re-review the same change just to argue against a previous finding; re-review only when new information (a new commit, a new failing test) appears.

## Final report section

Include in the response back to the user:

```text
Codex solo review
- Mode: standard | adversarial | rescue | pr-review
- Applied:
- Rejected:
- Remaining risks:
```

For PR mode, use this fuller form instead:

```text
PR readiness
- Summary:
- Risk level:
- Tests:
- Mode:
- Findings applied:
- Findings rejected:
- Remaining risks:
```
