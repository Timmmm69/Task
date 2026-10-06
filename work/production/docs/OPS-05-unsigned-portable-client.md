# Unsigned portable Windows client

The optional portable path uses `deployment/desktop/Build-UnsignedPortableClient.ps1`
and `Test-UnsignedPortableClient.ps1`. It publishes the existing WPF application as
Windows x64, self-contained, with no installer, service or code-signing certificate.
The signed release and updater described in OPS-05-windows-client-release.md retain
their publisher-signature requirements. Do not feed unsigned packages to that updater.

From the repository root, build into a new directory under outputs/:

```powershell
pwsh -NoProfile -File work/production/deployment/desktop/Build-UnsignedPortableClient.ps1 -Version 1.0.0 -OutputDirectory "$PWD/outputs/my-portable-client"
pwsh -NoProfile -File work/production/deployment/desktop/Test-UnsignedPortableClient.ps1 -PackageDirectory "$PWD/outputs/my-portable-client"
```

Extract the ZIP and launch `Task/Task.Desktop.exe`. Windows may display Unknown
Publisher or SmartScreen warnings; there is no programmatic bypass. Package hashes
detect corruption, but establish publisher identity only when received through a
separately trusted distribution channel.

Application files and user data must remain in separate directories. By default,
Personal storage is `%LOCALAPPDATA%/Task/Personal/tasks.db`, with additive migrations
and retained recovery copies. Corporate settings and protected credentials use the
desktop data root. `TASK_DESKTOP_DATA_DIRECTORY` is an explicit data-root override;
never point it at the portable application directory. Never distribute a data root.

For manual update:

1. Receive the new package and its expected SHA-256 through a trusted channel.
   Verify the archive and payload inventory before launching it.
2. Create and verify a Personal backup. Keep it outside the application directory.
3. Close all Task processes. Retain the previous application package separately.
4. Replace only the application files, keeping the same independent data directory.
5. Launch the new executable and verify Personal objects, search, recurrence and
   a new backup. Verify Corporate authentication against the intended HTTPS server.

On migration or startup failure, preserve the database and recovery copies. Follow
personal-backup-recovery.md. An older executable may reject a newer schema; do not
assume binary rollback also rolls back user data. Do not overwrite the only database.

Unsigned application delivery does not change HTTPS policy, TLS validation,
server-certificate validation, login, credential protection or Corporate read-only
behavior while disconnected. Never disable server security to accommodate this package.
