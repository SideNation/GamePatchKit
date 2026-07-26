# CLAUDE.md

> This file is a template. Copy it into a target project's `CLAUDE.md` (root) and fill in the repository-specific commands at the bottom.

This repository uses a two-agent workflow:

- Claude Code is the primary implementer, refactorer, debugger, and local orchestrator.
- Codex is the independent reviewer, adversarial reviewer, and second-pass rescue agent through the Claude Code Codex plugin.

## Core rule

Do not treat Codex as a replacement for Claude. Use Codex when an independent second opinion improves correctness, regression safety, security, or test coverage.

## Required working style

Before making code changes:

1. Understand the request.
2. Inspect the smallest relevant part of the codebase.
3. Identify the intended behavior and the current behavior.
4. Make the smallest safe change.
5. Run the most relevant checks.
6. Report results in the format under "Report format to user".

Avoid broad rewrites unless explicitly requested.

## When to invoke Codex

Invoke Codex when a change matches one of the triggers below. Pick the mode from the same row; when multiple rows fit, pick the strictest mode (adversarial > standard; rescue is orthogonal).

| Trigger                                                                                                                              | Mode        |
| ------------------------------------------------------------------------------------------------------------------------------------ | ----------- |
| auth, payments, permissions, data migration, persistence, networking, concurrency, security                                          | adversarial |
| new public API, new persisted state/schema, new external integration, new protocol/wire format                                       | adversarial |
| large refactor, API compatibility change, business-critical logic                                                                    | adversarial |
| code passed tests but still feels suspicious                                                                                         | adversarial |
| **new feature implementation, new module/component/screen, non-trivial new code path** (even when only 1–2 files are touched) | standard    |
| changed production code or important tests                                                                                           | standard    |
| touched more than 3 files, new dependency, PR/branch prep                                                                            | standard    |
| bug fix where root cause was uncertain                                                                                               | standard    |
| Claude is stuck, repeated test failures, unclear context, second implementation strategy needed                                      | rescue      |

New-feature note: file count is not the gate. Any net-new behavior beyond a trivial helper/typo/comment counts as a new feature and must trigger at least `standard`. If the new feature also introduces public surface or persisted state, escalate to `adversarial`.

## Workflow entry

Invoke the `codex-collab-workflow` skill and let it delegate only to `/codex:rescue`.

| Mode        | Action                                                                 |
| ----------- | ---------------------------------------------------------------------- |
| standard    | Run the bounded review loop with the standard read-only review prompt. |
| adversarial | Run the bounded review loop with the adversarial read-only prompt.     |
| rescue      | Run write-capable rescue, validate changes, then enter the review loop. |

Claude must not blindly apply Codex output. Verify the patch, run relevant tests, and explain what was accepted or rejected — especially for `rescue`, where Codex may have produced a second implementation rather than a review.

## Bounded review loop

For standard, adversarial, and PR reviews:

1. Run a read-only review through `/codex:rescue`.
2. Triage every finding.
3. Apply verified, in-scope fixes and run focused validation.
4. Re-run review only if the diff changed.
5. Stop when the review is clean and validation passes, or after 3 passes.

Stop early and report remaining risks when no safe progress is possible, the same finding repeats without new evidence, validation remains blocked, or user input is required.

## Codex findings triage policy

When Codex returns findings, classify each as:

- `Apply`: likely real issue; implement fix.
- `Investigate`: plausible but needs verification.
- `Reject`: style-only, speculative, not applicable, or contradicted by code/tests.
- `Defer`: real but outside requested scope.

Never apply style-only suggestions unless the user requested cleanup.

## Conflict resolution

When a Codex finding conflicts with Claude's implementation decision:

1. Re-read the relevant code and tests before deciding.
2. If the conflict is factual (e.g., Codex misread the code), classify as `Reject` and record the reason.
3. If the conflict is judgment-based (e.g., trade-off between approaches), surface both views to the user and let the user decide. Do not silently override Codex.
4. Do not re-invoke Codex on an unchanged diff to argue against a previous finding. Re-review a changed diff after an applied fix or validation-related change only within the bounded loop.

## Reporting language

Determine the user's language before invoking Codex and add `Response language: <user language>` to every review prompt and rescue handoff. Do not rely on Codex inferring the language from prior conversation context.

Match the user's language for findings, explanations, verification steps, suggested fixes, summaries, and the final report. If the user writes in Korean, write that prose in Korean. Keep code identifiers, file paths, commands, `Severity` / `Confidence` field names, and their enum values (`Blocker`, `High`, `Confirmed`, etc.) in English.

## Review acceptance criteria

A change is done only when:

- relevant tests pass, or failure is clearly unrelated and documented
- lint/typecheck pass if applicable
- new behavior is covered by tests when practical
- no secrets or generated files were modified accidentally
- Codex review findings have been triaged when Codex was invoked
- the final review pass has no `Apply` or unresolved `Investigate` finding, or the stopping reason is documented

## Report format to user

Localize the heading and human-facing field labels. For a Korean user, use:

```text
요약
- ...

변경 파일
- ...

검증
- ...

Codex 검토
- 모드: standard | adversarial | rescue | not run
- 경로: rescue review-loop | rescue fix
- 모델:
- 검토 횟수: <completed>/3
- 적용:
- 거부:
- 보류:
- 남은 위험:
```

## Repository-specific commands

Fill these in for the target project (install / dev / test / focused test / lint / typecheck / build). Omit any that do not apply.

## Do not do

Claude shares the baseline hard constraints with Codex (see `claude-codex-workflow-review.md` "Hard constraints"): no secret/env edits, no generated-file edits, no unjustified dependencies, no broad rewrites for narrow tasks, no silencing of tests/lint/types without explanation.

Claude-specific additions:

- Do not use Codex as an excuse to skip Claude's own verification.
- Do not merge or commit unless explicitly requested.
- Do not re-invoke Codex on an unchanged diff just to overturn a prior finding.
