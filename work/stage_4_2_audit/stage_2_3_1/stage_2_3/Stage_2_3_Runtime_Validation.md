
# Stage 2.3 Runtime Validation

## Environment

| Tool | Version |
|---|---|
| .NET SDK | 8.0.423 |
| .NET runtime | 8.0.29 |
| NSwag | 14.7.1.0 |
| NJsonSchema | 11.6.1 |
| Docker client / engine | 29.6.1 / 29.6.1 |
| Docker Desktop | 4.80.0 |
| PostgreSQL | 16.10 |
| Node.js | 22.23.1 |
| npm | 10.9.8 |
| Redocly CLI | 2.40.0 |
| openapi-typescript | 7.9.1 |
| openapi-typescript-codegen | 0.29.0 |
| TypeScript | 5.8.3 |
| Python | 3.12.13 |
| openapi-spec-validator | 0.7.2 |

## Executed gates

The following commands were executed from `work/stage_2_3_validation/stage_2_3`:

```powershell
docker run --rm -v "${root}:/work:ro" -w /work node:22-alpine sh -lc "npx --yes @redocly/cli@2.40.0 lint openapi/openapi.yaml --format=stylish"

nswag openapi2csclient /input:openapi/openapi.yaml /output:qa/generated/desktop-csharp/OrganizerDesktopClient.g.cs /classname:OrganizerClient /namespace:Organizer.DesktopSdk /operationGenerationMode:SingleClientFromOperationId /generateClientInterfaces:true /injectHttpClient:true /disposeHttpClient:false /jsonLibrary:SystemTextJson /generateNullableReferenceTypes:true /useRequiredKeyword:true /generateOptionalPropertiesAsNullable:true

nswag openapi2cscontroller /input:openapi/openapi.yaml /output:qa/generated/server-csharp/OrganizerController.g.cs /classname:OrganizerController /namespace:Organizer.ServerStubs /controllerBaseClass:Microsoft.AspNetCore.Mvc.ControllerBase /controllerStyle:Abstract /useActionResultType:true /operationGenerationMode:SingleClientFromOperationId /generateModelValidationAttributes:true /useCancellationToken:true /jsonLibrary:SystemTextJson /generateNullableReferenceTypes:true /useRequiredKeyword:true /generateOptionalPropertiesAsNullable:true

dotnet build qa/generated/desktop-csharp/Organizer.DesktopSdk.csproj -c Release --nologo
dotnet build qa/generated/server-csharp/Organizer.ServerStubs.csproj -c Release --nologo

docker run --rm -v "${root}:/work" -w /work node:22-alpine sh -lc "npx --yes openapi-typescript@7.9.1 openapi/openapi.yaml -o qa/generated/server-contract/schema.d.ts && npx --yes openapi-typescript-codegen@0.29.0 --input openapi/openapi.yaml --output qa/generated/desktop-sdk --client fetch --useOptions --useUnionTypes && npx --yes --package typescript@5.8.3 tsc --project qa/generated/tsconfig.json"

docker compose -p organizer_stage_2_3_clean up -d
docker compose -p organizer_stage_2_3_upgrade up -d
```

PostgreSQL Scenario A applied migrations `001` through `005`, loaded a realistic organization/employee fixture, reran `005` and `002`, ran database contract tests, and confirmed that an invalid interval gap fails with SQLSTATE class 23 behavior. Scenario B built an exact Stage 2.2 state, loaded data, applied `005`, reran seed/migration, and proved that employee data was unchanged.

## Result

Every mandatory runtime gate passed after the fixes. Full console evidence is under `qa/reports/stage_2_3_runtime/`.
