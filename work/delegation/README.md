# DeepSeek delegation

This subsystem accepts decision-complete tasks prepared by Codex and runs them through OpenCode in isolated Git worktrees.

## One-time setup

1. Install and authenticate OpenCode and GitHub CLI.
2. From the repository root run:

   `powershell -ExecutionPolicy Bypass -File work/delegation/scripts/Setup-Delegation.ps1`

3. If automatic model discovery finds zero or several candidates, run it again with the exact ID printed by `opencode models --refresh`, for example:

   `powershell -ExecutionPolicy Bypass -File work/delegation/scripts/Setup-Delegation.ps1 -ModelId provider/model`

The selected model is stored in ignored `work/delegation/local.settings.json` and is never committed.

## Usage

Open OpenCode in this repository, select `task-delegate`, and paste the entire block supplied by Codex. Alternatively run `/delegate` and paste the block as its arguments. No Git knowledge is required.

The dispatcher rejects incomplete/stale packets, dirty `main`, a third concurrent task, overlapping ownership, forbidden files, excessive diffs, failed checks, deletions, renames, binaries, and low-risk public-interface/dependency changes. Low-risk PRs use GitHub auto-merge after required CI. Medium-risk PRs remain draft for Codex review.

The OpenCode worker may edit its isolated worktree, but `git` and `gh` are shadowed by blocking command shims inside the worker process. Git commit, push, PR creation, and auto-merge are performed by the dispatcher only after independent scope validation.

Open PR worktrees and their path locks are retained under ignored `work/tmp/delegation-worktrees` until the PR is merged or closed. The dispatcher runs cleanup before each task; `Cleanup-Delegations.ps1` can also be run manually. Failed worktrees are retained for diagnosis but release their active slot.
