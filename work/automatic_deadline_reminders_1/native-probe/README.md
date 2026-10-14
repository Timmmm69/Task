# Native deadline acceptance harness

Build `Probe.csproj -c Release`, then run `Task.Desktop.Tests.exe personal <isolated-evidence-directory>` or `corporate <isolated-evidence-directory>`. Click «Запустить проверку». Corporate needs TASK_POSTGRES_TEST_ADMIN_CONNECTION for a loopback PostgreSQL 16 fixture; it creates and drops a GUID database. Never point it at a customer database. Personal uses only the supplied isolated directory.

This harness calls production task persistence, reconciliation, existing worker/store and actual Windows presenters. Corporate replaces HTTP read transport with a proxy carrying the real authorized store response; it does not replace reminder or notification delivery. Two corporate agent passes must submit exactly once. Evidence records Windows acceptance separately from visual confirmation. Observe a real banner separately; the harness cannot certify it.
