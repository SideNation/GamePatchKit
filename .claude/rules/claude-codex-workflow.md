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

## Commands

Pick the mode from the table, then run the matching command. For background runs, retrieve the result with `/codex:status` and `/codex:result`.

| Mode        | Command                                    |
| ----------- | ------------------------------------------ |
| standard    | `/codex:review --background`             |
| adversarial | `/codex:adversarial-review --background` |
| rescue      | `/codex:rescue`                          |

Claude must not blindly apply Codex output. Verify the patch, run relevant tests, and explain what was accepted or rejected — especially for `rescue`, where Codex may have produced a second implementation rather than a review.

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
4. Do not re-invoke Codex on the same change to argue against a previous finding. One additional pass is allowed only when new information (a new commit, new failing test) is available.

## Reporting language

Match the user's language. If the user writes in Korean, write the final report and findings summary in Korean. Keep code, command names, and severity labels (`Blocker`, `High`, etc.) in English.

## Review acceptance criteria

A change is done only when:

- relevant tests pass, or failure is clearly unrelated and documented
- lint/typecheck pass if applicable
- new behavior is covered by tests when practical
- no secrets or generated files were modified accidentally
- Codex review findings have been triaged when Codex was invoked

## Report format to user

Use this structure at the end of work:

```text
Summary
- ...

Changed files
- ...

Validation
- ...

Codex review
- Mode: standard | adversarial | rescue | not run
- Applied:
- Rejected:
- Remaining risks:
```

## Repository-specific commands

Fill these in for the target project (install / dev / test / focused test / lint / typecheck / build). Omit any that do not apply.

## Do not do

Claude shares the baseline hard constraints with Codex (see `claude-codex-workflow-review.md` "Hard constraints"): no secret/env edits, no generated-file edits, no unjustified dependencies, no broad rewrites for narrow tasks, no silencing of tests/lint/types without explanation.

Claude-specific additions:

- Do not use Codex as an excuse to skip Claude's own verification.
- Do not merge or commit unless explicitly requested.
- Do not re-invoke Codex on the same change just to overturn a prior finding.
