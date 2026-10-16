# Baseline characterization (version 8.1.0)

The nine tests in `ProjectRelatedTasksBaselineTests.cs` were executed against
`deb16b3` before changing production code. See `evidence/baseline.trx` in the final
package. They intentionally assert the old behavior and do not belong in the
new production test project.

Reproduce from a clean checkout of this repository (PowerShell):

```powershell
git worktree add --detach work/project-related-tasks-baseline deb16b3
Copy-Item work/project_related_tasks_8_1/baseline/ProjectRelatedTasksBaselineTests.cs work/project-related-tasks-baseline/work/production/tests/Task.Desktop.Tests/Projects/
dotnet test work/project-related-tasks-baseline/work/production/tests/Task.Desktop.Tests/Task.Desktop.Tests.csproj -c Release --filter FullyQualifiedName~Baseline_ --logger 'trx;LogFileName=baseline.trx' --results-directory work/project-related-tasks-baseline/evidence
```

Fixtures use 100 items per simulated page and manually released completion
sources. No wall-clock latency or production throughput is inferred. Timeouts
only detect a stuck test. The original project and member fakes complete
immediately, making the task-page gate the only outstanding work.
