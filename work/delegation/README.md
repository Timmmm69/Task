# DeepSeek delegation

There are two delegation modes. The normal manual DeepSeek chat is the primary workflow; the automated dispatcher is an optional PR workflow with stricter technical controls.

## Default usage: copy, implement and push from a manual DeepSeek chat

1. Codex independently identifies a meaningful, decision-complete low or medium task. It does not manufacture microtasks when a coherent implementation package is possible.
2. Codex returns one self-contained Russian `DELEGATION_PACKET` for one task and continues the chat numbering. Parallel packets are allowed only when their owned files and semantics do not overlap.
3. Copy the whole packet into a normal new OpenCode chat with the chosen DeepSeek model. DeepSeek writes production code, not merely tests or documentation.
4. The packet gives DeepSeek the exact scope, contract, acceptance criteria, checks and stop conditions. It also gives the publication procedure: fetch, verify a clean tree, commit only owned files, fetch plus rebase `origin/main`, repeat checks and push `HEAD:main`.
5. DeepSeek pushes a successful low or medium task directly to `main`. On a conflict, dirty tree, failed check or scope deviation it must not push and must report the blocker.
6. Tell Codex that the chat is ready. Codex fetches `origin/main`, independently reviews the diff and runs the relevant gate; medium changes are always reviewed after push.

Codex must not ask the user to choose branches, worktrees or commands, or to compose a prompt manually. The user-facing status states what entered `main`, what was verified, product impact and the next step.

## Task boundaries

- Low: up to 3 files and 150 lines; no public interfaces, dependencies, deletions or renames.
- Medium: up to 8 files and 400 lines; one isolated module or scenario with approved interfaces.
- High: architecture, API/DTO, database/migrations, permissions/security, synchronization, deployment, backup/updates, unclear defects and large refactors. Codex owns these or first splits them into safe low/medium work.

## Optional automated dispatcher

Choose `task-delegate` only when the user explicitly wants the automated PR workflow. It requires a one-time local setup:

1. Install and authenticate OpenCode and GitHub CLI.
2. From the repository root run:

   `powershell -ExecutionPolicy Bypass -File work/delegation/scripts/Setup-Delegation.ps1`

3. If automatic model discovery finds zero or several candidates, run it again with the exact ID printed by `opencode models --refresh`, for example:

   `powershell -ExecutionPolicy Bypass -File work/delegation/scripts/Setup-Delegation.ps1 -ModelId provider/model`

The selected model is stored in ignored `work/delegation/local.settings.json` and is never committed.

The dispatcher creates the isolated worktree, invokes `task-worker`, validates scope, creates the PR and routes it by risk. Its `merge: automatic` / `merge: codex-review` metadata applies only in this dispatcher workflow, not to a manual direct push.

The dispatcher rejects incomplete/stale packets, dirty `main`, a third concurrent task, overlapping ownership, forbidden files, excessive diffs, failed checks, deletions, renames, binaries, and low-risk public-interface/dependency changes. Low-risk PRs are merged by a trusted GitHub workflow after required CI. Medium-risk PRs remain draft for Codex review.

The OpenCode worker may edit its isolated worktree, but `git` and `gh` are shadowed by blocking command shims inside the worker process. Git commit, push, PR creation and auto-merge are performed by the dispatcher only after independent scope validation.

Open PR worktrees and their path locks are retained under ignored `work/tmp/delegation-worktrees` until the PR is merged or closed. The dispatcher runs cleanup before each task; `Cleanup-Delegations.ps1` can also be run manually. Failed worktrees are retained for diagnosis but release their active slot.

## GitHub Free limitation

GitHub does not provide branch protection or native auto-merge for a private repository on the current Free plan. The repository remains private. The trusted merge workflow verifies the packet, branch, risk route, mergeable state and successful current-head CI before merging. The dispatcher worker has no GitHub credentials and its local `git`/`gh` commands are blocked. This restriction applies only to the dispatcher worker; a manual DeepSeek chat follows the direct-push procedure above. Upgrading to GitHub Pro is required for server-enforced branch protection.
