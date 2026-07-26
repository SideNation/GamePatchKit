---
name: codex-collab-workflow
description: Coordinate Claude Code with Codex via the Codex Claude Code plugin. Claude runs standard/adversarial reviews and rescue itself by delegating to /codex:rescue (review-only reviews run read-only; rescue runs write-capable); the dedicated /codex:review reviewer is user-triggered. Covers review, adversarial review, rescue, and PR-readiness. Triggers when Claude has just modified code, is stuck on a bug, or is preparing a PR.
allowed-tools:
  - Skill(codex:rescue *)
---
# Codex collaboration skill

Single entry point for invoking Codex from Claude Code. Pick a mode, run it, then triage findings using `packages/plugins/workflow/rules/claude-codex-workflow.md`.

## Codex review paths

`/codex:rescue` is the only Codex command Claude can invoke. It forwards a free-form task to Codex, and its behavior follows the prompt:

- **Review via rescue (Claude-invocable).** Prompt it to **"review only — do not edit"** and Codex runs **read-only** and returns findings (per codex-rescue, review/diagnosis tasks run without `--write`). This is how Claude runs reviews itself.
- **Rescue / fix via rescue (Claude-invocable).** Describe the problem and Codex runs **write-capable**, returning a patch or diagnosis.

The **dedicated reviewer** — `/codex:review` and `/codex:adversarial-review` — uses Codex's purpose-built review harness (review-gate, `/codex:status`, `/codex:result`). These are `disable-model-invocation: true`, so **Claude cannot run them**; present the command for the **user** to run when the full review pipeline is wanted.

## Model

- model: `gpt-5.6-terra`
- Pass `--model <model>` on `/codex:rescue` (the dedicated review commands take no `--model` — their model comes from Codex config).
- If the user requests a different model, use that value instead.

## Mode selection

1. **Stuck / repeated failures / unclear root cause** → Rescue.
2. **About to open or finalize a PR** → PR review.
3. **High-risk change** (security, auth, DB, migration, payments, concurrency, public API, large refactor, >3 files) → Adversarial review.
4. **Ordinary change worth a second pair of eyes** → Standard review.
5. **No code changed and user did not ask** → do not invoke.

## Standard review

Claude runs it via rescue (read-only):

```text
/codex:rescue --model <model>
```

Rescue prompt: *"Review only — do not edit. Review the current diff (working tree, or vs the base branch) for correctness, security, tests, and maintainability. Report each finding as Severity / Confidence / File / Issue / Why it matters / How to verify / Suggested fix."*

Full dedicated reviewer (user runs): `/codex:review --background`, then `/codex:status`, `/codex:result`.

## Adversarial review

Same as standard, but the rescue prompt takes an adversarial stance: *"Review only — do not edit. Try to break this change: hunt edge cases, boundary values, concurrency, and failure paths; for every optimistic assumption construct a counterexample; mark anything you cannot reproduce as Speculative."*

Full dedicated reviewer (user runs): `/codex:adversarial-review --background`, then `/codex:status`, `/codex:result`.

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
```

Then Claude invokes directly (foreground — output returns directly):

```text
/codex:rescue --model <model>
```

Verify the patch before applying — read it, check for broad rewrites, check tests, check secrets, run focused validation. Apply only the safe parts.

## PR review mode

1. **Self-review the diff first.** What changed, what behavior is affected, which tests cover it, riskiest files, and whether secrets / generated files / lockfiles / migrations are involved.
2. **Pick depth** based on risk: standard or adversarial.
3. Run the chosen review via rescue (read-only), or present the dedicated `/codex:review` command for the user.
4. Triage findings (below). Verify by reading code, reproduce when possible, run focused tests after changes.

## Triage

Follow the policy in `packages/plugins/workflow/rules/claude-codex-workflow.md`. Per finding:

```text
Finding | Decision | Reason
```

Decisions: `Apply` · `Investigate` · `Reject` · `Defer`. Reject style-only or speculative comments unless backed by code or tests.

## Final report

```text
Codex review
- Mode: standard | adversarial | rescue | pr-review
- Path: rescue (read-only) | rescue (fix) | dedicated /codex:review (user-run)
- Model: <model>   (rescue path only)
- Applied:
- Rejected:
- Remaining risks:
```
