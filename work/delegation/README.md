# DeepSeek delegation

This subsystem supports two ways to delegate decision-complete tasks prepared by Codex.

## One-time setup

1. Install and authenticate OpenCode and GitHub CLI.
2. From the repository root run:

   `powershell -ExecutionPolicy Bypass -File work/delegation/scripts/Setup-Delegation.ps1`

3. If automatic model discovery finds zero or several candidates, run it again with the exact ID printed by `opencode models --refresh`, for example:

   `powershell -ExecutionPolicy Bypass -File work/delegation/scripts/Setup-Delegation.ps1 -ModelId provider/model`

The selected model is stored in ignored `work/delegation/local.settings.json` and is never committed.

## Default usage: copy and paste into DeepSeek

1. Ask Codex to continue Task development. Project `AGENTS.md` requires it to identify suitable low/medium tasks itself.
2. When a task is suitable, Codex returns one complete `DELEGATION_PACKET`.
3. Copy that whole packet into a normal new OpenCode chat with your chosen DeepSeek model.
4. DeepSeek returns its result; give the result back to Codex for review and integration.

You do not need to ask Codex separately whether to delegate or to build a DeepSeek prompt. You do not need to use the terminal in this default mode.

## Optional automated usage

If you choose the `task-delegate` OpenCode agent instead of a normal DeepSeek chat, paste the entire block supplied by Codex there. The dispatcher then creates the isolated worktree, invokes `task-worker`, validates scope, creates the PR, and routes it according to risk. This mode requires the one-time terminal setup described above.

The dispatcher rejects incomplete/stale packets, dirty `main`, a third concurrent task, overlapping ownership, forbidden files, excessive diffs, failed checks, deletions, renames, binaries, and low-risk public-interface/dependency changes. Low-risk PRs are merged by a trusted GitHub workflow after required CI. Medium-risk PRs remain draft for Codex review.

The OpenCode worker may edit its isolated worktree, but `git` and `gh` are shadowed by blocking command shims inside the worker process. Git commit, push, PR creation, and auto-merge are performed by the dispatcher only after independent scope validation.

Open PR worktrees and their path locks are retained under ignored `work/tmp/delegation-worktrees` until the PR is merged or closed. The dispatcher runs cleanup before each task; `Cleanup-Delegations.ps1` can also be run manually. Failed worktrees are retained for diagnosis but release their active slot.

## GitHub Free limitation

GitHub does not provide branch protection or native auto-merge for a private repository on the current Free plan. The repository remains private. The trusted merge workflow verifies the packet, branch, risk route, mergeable state, and successful current-head CI before merging. DeepSeek has no GitHub credentials and its local `git`/`gh` commands are blocked. An owner can still manually bypass this process by pushing to `main`; upgrading to GitHub Pro is required for server-enforced branch protection.
