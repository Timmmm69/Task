# Personal backup and recovery

Personal stores its local DB outside the installation: `%LOCALAPPDATA%\Task\Personal`.
Updating/replacing Task binaries must preserve that directory.

Use **Backup** in Personal to choose a `.taskbackup` destination outside the Personal
directory. The app reports success only after snapshot/integrity/hash checks and a
complete package write. Catalog records/paths are included; referenced physical
files must be backed up separately. Corporate credentials/settings are excluded.

Use **Восстановить** to choose a Personal backup. Save or cancel open editors first.
Confirm the replacement; the current DB is kept as a safety copy under
`Personal\recovery\restore-<id>`. Invalid/truncated/incompatible copies cannot replace
the current database. Successful restore reopens Personal and displays the safety path.

When Personal cannot open or validate its DB, Task shows recovery without resetting
the database. Choose **Восстановить backup**, **Перейти в Corporate**, or **Выход**.
Keep the original DB and any sidecars for diagnosis. If a prior restore was interrupted,
**Вернуть safety copy** is also available. A validated backup can still be restored
when a prior recovery marker or safety copy is damaged. Recovery copies are retained;
there is no automatic cleanup/retention policy in this release.

If another Task owns this Personal store, use Corporate or Exit in the second
process. Closing the owner or terminating its process releases the OS lock.

On disk-full/access/write errors, Task reports that data was not saved. Keep the
editor open, free sufficient space/check directory access, then retry Save.
No successful Save or backup is reported until its operation actually completes.

Corporate PostgreSQL backup operations remain in `backup-restore-runbook.md` and
are independent of this workflow.
