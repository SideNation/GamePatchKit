---
name: codex-collab-workflow
description: Coordinate Claude Code with Codex via the Codex Claude Code plugin. Run standard, adversarial, and PR reviews as a bounded review-fix-validation loop through /codex:rescue, or use rescue for stuck and repeated-failure cases. Return review and rescue prose in the user's language. Triggers after Claude modifies code, gets stuck on a bug, or prepares a PR.
allowed-tools:
  - Skill(codex:rescue *)
---
# Codex collaboration skill

Use `/codex:rescue` as the only Codex entry point. Pick a mode, run the workflow, and triage findings using [claude-codex-workflow.md](../../rules/claude-codex-workflow.md).

## Codex paths

- **Review:** Prompt `/codex:rescue` with **"Review only — do not edit"**. Keep this path read-only and let Claude apply accepted findings.
- **Fix or diagnosis:** Describe the failure without the review-only instruction. This path may write code; verify every resulting change.

## Model

- model: `gpt-5.6-terra`
- Pass `--model <model>` on every `/codex:rescue` invocation.
- If the user requests a different model, use that value instead.

## Response language

Determine the user's language before every Codex invocation and include it explicitly in the prompt. Do not rely on Codex inferring it from prior conversation context.

- Write findings, explanations, verification steps, suggested fixes, and summaries in the user's language.
- For a Korean user, write that prose in Korean.
- Keep code identifiers, file paths, commands, `Severity` / `Confidence` field names, and their enum values in English.
- Repeat the language instruction on every review pass and rescue call.

## Mode selection

1. **Stuck / repeated failures / unclear root cause** → Rescue.
2. **About to open or finalize a PR** → PR review.
3. **High-risk change** (security, auth, DB, migration, payments, concurrency, public API, large refactor, >3 files) → Adversarial review.
4. **Ordinary change worth a second pair of eyes** → Standard review.
5. **No code changed and user did not ask** → do not invoke.

## Review prompts

For a standard review, invoke:

```text
/codex:rescue --model <model>
```

Prompt:

> Review only — do not edit. Review the current diff against the base branch, or the working-tree diff when no base is available, for correctness, security, tests, and maintainability. Report each finding as Severity / Confidence / File / Issue / Why it matters / How to verify / Suggested fix.
>
> Response language: `<user language>`. Write all finding explanations, verification steps, suggested fixes, and the summary in that language. Keep code identifiers, file paths, commands, and Severity / Confidence labels and values in English.

For an adversarial review, append:

> Try to break this change. Hunt edge cases, boundary values, concurrency, and failure paths. Construct a counterexample for every optimistic assumption. Mark anything you cannot reproduce as Speculative.

## Review loop

Run standard, adversarial, and PR reviews with at most **3 passes**:

1. Start at pass 1. Record the diff being reviewed and run the selected review prompt through `/codex:rescue`.
2. Classify every finding as `Apply`, `Investigate`, `Reject`, or `Defer`.
3. Verify `Investigate` findings. Apply only confirmed, in-scope fixes; record reasons for `Reject` and `Defer`.
4. Run focused tests, lint, typecheck, or build checks appropriate to the changed code. Treat a validation failure as an issue to investigate before deciding whether another pass is possible.
5. Stop successfully when no `Apply` or unresolved `Investigate` finding remains and relevant validation passes.
6. When the diff changed and the pass count is below 3, increment the count and return to step 1.
7. Otherwise stop and report remaining risks: pass 3 was exhausted, the diff did not change, the same finding repeated without new evidence, validation cannot pass, or a user decision is required.

Never use write-capable rescue inside this review loop. Do not re-review an unchanged diff merely to obtain a different opinion.

## Rescue

For a fix or diagnosis (not review) — Codex runs write-capable. Write a concise handoff first:

```text
Goal:
Current failure:
Relevant files:
Attempts already made:
Tests run:
Constraints:
What I need from Codex:
Response language: <user language>
```

Then Claude invokes directly (foreground — output returns directly):

```text
/codex:rescue --model <model>
```

Read the resulting diff, check for broad rewrites, secrets, and generated files, then run focused validation. If rescue changes code, enter the review loop before reporting completion.

## PR review mode

1. **Self-review the diff first.** What changed, what behavior is affected, which tests cover it, riskiest files, and whether secrets / generated files / lockfiles / migrations are involved.
2. **Pick depth** based on risk: standard or adversarial.
3. Run the bounded review loop.
4. Report unresolved findings and validation results.

## Triage

Follow the policy in `packages/plugins/workflow/rules/claude-codex-workflow.md`. Per finding:

```text
Finding | Decision | Reason
```

Decisions: `Apply` · `Investigate` · `Reject` · `Defer`. Reject style-only or speculative comments unless backed by code or tests.

## Final report

Localize the heading and human-facing field labels to the user's language. For a Korean user, use:

```text
Codex 검토
- 모드: standard | adversarial | rescue | pr-review
- 경로: rescue review-loop | rescue fix
- 모델: <model>
- 검토 횟수: <completed>/3
- 적용:
- 거부:
- 보류:
- 남은 위험:
```
