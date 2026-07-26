---
name: codex-solo-workflow
description: Discipline for using Codex as the sole agent, without a separate Claude Code orchestrator. Run standard, adversarial, and PR self-reviews as a bounded review-fix-validation loop, rescue stuck work, and report in the user's language. Triggers after Codex modifies code, gets stuck on a bug, or prepares a PR.
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

## Response language

Match the user's language for findings, explanations, verification steps, suggested fixes, summaries, and the final report.

- For a Korean user, write that prose in Korean.
- Keep code identifiers, file paths, commands, `Severity` / `Confidence` field names, and their enum values in English.
- Localize human-facing report headings and field labels.

## Review pass

For a standard self-review:

1. Re-read the diff (`git diff`, or the diff against the base branch) in the reviewer role.
2. Walk the What-to-check lists (Correctness / Security / Tests / Maintainability) below.
3. Record findings in the Findings format below (Severity / Confidence / File / Issue / Why it matters / How to verify / Suggested fix).

For an adversarial self-review, use the same pass and adopt a stance of **trying to break your own change**:

- Ask "if this change is wrong, where does it break first?" before reading line by line.
- Actively hunt edge cases, boundary values, concurrency, and failure paths.
- For every optimistic assumption, try to construct a counterexample.
- Mark anything you cannot reproduce as `Confidence: Speculative`.

## Self-review loop

Run standard, adversarial, and PR self-reviews with at most **3 passes**:

1. Start at pass 1. Record the diff being reviewed, deliberately reset into the reviewer role, and run the selected review pass.
2. Classify every finding as `Apply`, `Investigate`, `Reject`, or `Defer`.
3. Verify `Investigate` findings. Apply only confirmed, in-scope fixes; record reasons for `Reject` and `Defer`.
4. Run focused tests, lint, typecheck, or build checks appropriate to the changed code. Treat a validation failure as an issue to investigate before deciding whether another pass is possible.
5. Stop successfully when no `Apply` or unresolved `Investigate` finding remains and relevant validation passes.
6. When the diff changed and the pass count is below 3, increment the count and return to step 1.
7. Otherwise stop and report remaining risks: pass 3 was exhausted, the diff did not change, the same finding repeated without new evidence, validation cannot pass, or a user decision is required.

Do not re-review an unchanged diff merely to obtain a different opinion.

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
5. If rescue changes code, enter the self-review loop before reporting completion.

## PR review mode

1. **Self-review the diff first.** What changed, what behavior is affected, which tests cover it, riskiest files, and whether secrets / generated files / lockfiles / migrations are involved.
2. **Pick the review depth** based on risk: standard for normal, adversarial for risky.
3. Run the bounded self-review loop.
4. Report unresolved findings and validation results.

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

Write `Issue`, `Why it matters`, `How to verify`, and `Suggested fix` prose in the user's language.

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
4. Do not re-review an unchanged diff to argue against a previous finding. Re-review a changed diff after an applied fix or validation-related change only within the bounded loop.

## Final report section

Localize the heading and human-facing field labels. For a Korean user, use:

```text
Codex 단독 검토
- 모드: standard | adversarial | rescue | pr-review
- 검토 횟수: <completed>/3
- 적용:
- 거부:
- 보류:
- 남은 위험:
```

For a Korean PR report, use:

```text
PR 준비 상태
- 요약:
- 위험 수준:
- 테스트:
- 모드:
- 검토 횟수: <completed>/3
- 적용한 findings:
- 거부한 findings:
- 보류한 findings:
- 남은 위험:
```
