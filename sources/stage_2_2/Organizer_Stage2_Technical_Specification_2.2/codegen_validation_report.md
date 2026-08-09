# Code Generation Validation Report — Stage 2.2

## 1. Environment

- Validation date: `2026-07-26`.
- Source: `openapi/openapi.yaml`.
- Source SHA-256: `052738F7BF1B02CAB054B92827E17E3EA79EB0C8832C0F5A6E60681E4B363161`.
- .NET SDK: `8.0.423`.
- .NET runtime: `8.0.29`.
- NSwag: `14.7.1.0`.
- NJsonSchema: `11.6.1.0`.
- Target framework: `net8.0`.
- JSON library: `System.Text.Json`.
- Nullable reference types: enabled.
- Warnings as errors: enabled.

## 2. C# desktop client

- Generator: NSwag `openapi2csclient`.
- Operation mode: `SingleClientFromOperationId`.
- Client interface generation: enabled.
- HttpClient injection: enabled.
- Output: `qa/generated/desktop-csharp/OrganizerDesktopClient.g.cs`.
- Output size: `3145378` bytes.
- Output SHA-256: `96484932AED1DEB8537C5786FD3D209216F3533442D71E8F81F2F2B2744A60BE`.
- Generated async operation implementations: `241`.
- Search client contains `types`, `contactIds`, `hasFiles`, `lifecycle`, `cursor` and `limit`.
- Project: `qa/generated/desktop-csharp/Organizer.DesktopSdk.csproj`.
- Build result: `PASS`.
- Warnings: `0`.
- Errors: `0`.

## 3. C# server stubs

- Generator: NSwag `openapi2cscontroller`.
- Controller style: abstract ASP.NET Core controller.
- Operation mode: `SingleClientFromOperationId`.
- Cancellation tokens: enabled.
- Model validation attributes: enabled.
- Output: `qa/generated/server-csharp/OrganizerController.g.cs`.
- Output size: `679026` bytes.
- Output SHA-256: `55AE839A2743EDB94C81AC69F252454EC51770D64956D27A61514E2FE8185300`.
- Generated HTTP action stubs: `241`.
- Project: `qa/generated/server-csharp/Organizer.ServerStubs.csproj`.
- Build result: `PASS`.
- Warnings: `0`.
- Errors: `0`.

## 4. Logs

- Generation: `qa/reports/stage_2_2_csharp_codegen.log`.
- Desktop compilation: `qa/reports/stage_2_2_desktop_csharp_build.log`.
- Server compilation: `qa/reports/stage_2_2_server_csharp_build.log`.

Build outputs under `bin` and `obj` are validation intermediates and are excluded from final archives. Generated C# source and project files are retained.

## 5. Decision

OpenAPI supports concrete C# client and server generation. Both generated projects compile in strict warning-free mode; code generation is not simulated by a schema dump or empty interfaces.
