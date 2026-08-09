
# Stage 2.3 Code Generation Report

## C# desktop client

- Generator: NSwag `openapi2csclient` 14.7.1.0.
- Operation mode: `SingleClientFromOperationId`.
- JSON: `System.Text.Json`; nullable reference types and C# required members enabled.
- Generated async operations: **244**.
- Generated source: `qa/generated/desktop-csharp/OrganizerDesktopClient.g.cs`.
- SHA-256: `27B1D7079BF5B8C6BF41B10D53DD632988906E1A36E27E9052A2A53007729E65`.
- Compilation: .NET SDK 8.0.423, `net8.0`, **0 warnings, 0 errors**.

## C# server stubs

- Generator: NSwag `openapi2cscontroller` 14.7.1.0.
- Abstract ASP.NET Core controller with cancellation tokens and validation attributes.
- Generated actions: **244**.
- Generated source: `qa/generated/server-csharp/OrganizerController.g.cs`.
- SHA-256: `6014C9A0D0EAA8B7CBE28F197FAAC4513D8FB327D67C7180FB8267BBCBC6A579`.
- Compilation: .NET SDK 8.0.423, `net8.0`, **0 warnings, 0 errors**.

## Dependent TypeScript artifacts

- Server schema: `openapi-typescript` 7.9.1.
- Desktop SDK: `openapi-typescript-codegen` 0.29.0; **277 files**.
- Compiler: TypeScript 5.8.3 strict/noEmit.
- Compilation: **PASS**.

The generated artifacts contain `EmployeeSearchResult`, urgency-scale DTOs, all three new settings operations, `If-Match`, `ETag`, and `Idempotency-Key`.
