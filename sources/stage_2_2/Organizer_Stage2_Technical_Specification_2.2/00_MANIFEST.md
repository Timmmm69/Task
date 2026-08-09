# Manifest — Organizer Stage 2.2 Contract Recovery

## 1. Package

- Package: `Organizer_Stage2_Technical_Specification_2.2`.
- Version: `2.2`.
- Assembly date: `2026-07-26`.
- Files in final ZIP including this manifest: `62`.
- Payload bytes excluding this self-referential manifest: `6232774`.
- Canonical Stage 2.1 ZIP SHA-256: `A293F576D7FF781ACA75222D709F323369C950E740072921F548999C8E83A715`.
- Recovered Stage 2.1 OpenAPI SHA-256: `E3D1D1D20AFB5EB34B5CB06525CF31245769CF9C6F551146E2E843BF1C0C4A37`.
- Final Stage 2.2 OpenAPI SHA-256: `052738F7BF1B02CAB054B92827E17E3EA79EB0C8832C0F5A6E60681E4B363161`.

## 2. Main sources

1. `sources/product_concept.txt` — product requirements.
2. `sources/architecture_stage1.md` — architecture baseline.
3. `docs/06_stage_2_1_normative_corrections.md` — prior normative corrections.
4. `Stage_2_2_Contract_Recovery.md` and `Search_Contract.md` — Stage 2.2 corrections.
5. `openapi/openapi.yaml` — final machine-readable contract.
6. `catalogs/api_catalog.csv`, `catalogs/permissions.csv`, `catalogs/errors.csv` — canonical operation and policy catalogs.

## 3. Full file list

| Relative path | Purpose | Status | Format | Bytes | SHA-256 | Relation |
|---|---|---|---|---:|---|---|
| `00_MANIFEST.md` | Состав и контроль поставки | generated | Markdown | self | external `.manifest.sha256` | new_2.2 |
| `Search_Contract.md` | Нормативный Search API | canonical | Markdown | 6047 | `3E4458D893EA35F610299E87DF7684FDD78F6684E94885D84D32130D5F08E564` | new_2.2 |
| `Stage_2_2_Contract_Recovery.md` | Происхождение и решение Contract Recovery | canonical | Markdown | 7981 | `EEDC4DA851C317D421B7DBCCB4B53B3BD5CF0AB1A4B6C0D10DF08A04345D5B8A` | new_2.2 |
| `Stage_2_2_Fix_Registry.md` | Реестр исправлений 2.2 | canonical | Markdown | 4807 | `447D53A1CAA21C506FA0C67B7E27B960BD084518B307D98C81F953D2E9192425` | new_2.2 |
| `catalogs/api_catalog.csv` | Канонический структурированный каталог | canonical | CSV | 54670 | `DACDEFFB79A11B2BA5A95552EEBEE13D02465636BD83EE91DAA15BA2022338C7` | corrected_2.2 |
| `catalogs/background_jobs.csv` | Канонический структурированный каталог | canonical | CSV | 2478 | `29BAAF9455F4C071B837E888D240031F1F7E7A1F61CB193611550102E59DF681` | unchanged_2.1 |
| `catalogs/entities.csv` | Канонический структурированный каталог | canonical | CSV | 19248 | `C875780F5477683F5DFF7B8328D07EFA1482E75C66D22DEFA3A90889BCBB98D9` | unchanged_2.1 |
| `catalogs/errors.csv` | Канонический структурированный каталог | canonical | CSV | 7340 | `0E0170CBFD9B93ADA80FE93E8D4DAF8685DAF054A1B9CB0F56C51A29F4B6E82E` | corrected_2.2 |
| `catalogs/events.csv` | Канонический структурированный каталог | canonical | CSV | 61805 | `DF1E1381BA109FC7A7E5561E1DB8FD8B287A4155674E346B0A05CCCF86539848` | unchanged_2.1 |
| `catalogs/permissions.csv` | Канонический структурированный каталог | canonical | CSV | 9447 | `0227011C8843BF646BE102DBBF00A028757592FC106503CB1D388F37D4179671` | unchanged_2.1 |
| `catalogs/traceability.csv` | Канонический структурированный каталог | canonical | CSV | 4095 | `4F0AA5C916624838E1E1E8A4D5D6D56987CAD45E3FFFC8DBE4F9D0DAA8BBEDC7` | corrected_2.2 |
| `codegen_validation_report.md` | Итоговый validation report | supporting | Markdown | 2265 | `B6CBFF2DCC9A86F4E5A1546D5D63E90F14158D0B2F2CD438C3F02AEE8E520F5A` | new_2.2 |
| `contract_diff_against_traceability.csv` | Method+path contract diff | generated | CSV | 40494 | `73AB4618BE4F5F0859CFBE6806F9A497E60CF737989FE4D83ABC98315B1EF23F` | new_2.2 |
| `db/001_initial_schema.sql` | Нормативный PostgreSQL artifact | canonical | SQL | 76548 | `7D9403692A000BD0A234AD93F74F23C13158D7608B6F76CFBC3EC619A998C0B1` | unchanged_2.1 |
| `db/002_seed_authorization.sql` | Нормативный PostgreSQL artifact | canonical | SQL | 16742 | `C447C670F5C98331D59166E8874785AFAD8E5B5F474BF1E4BCDB07173BE55A95` | unchanged_2.1 |
| `db/003_audit_corrections.sql` | Нормативный PostgreSQL artifact | canonical | SQL | 15326 | `CFC556B7749A39F81F5BC9C188AA1DB7164A5BD90C59978AF50CE2ADC52E8148` | unchanged_2.1 |
| `db/004_stage_2_1_foundation.sql` | Нормативный PostgreSQL artifact | canonical | SQL | 51394 | `3AAFC2CDFAC72EFE4831B68D9E6FBB84D0FC8DF8C64A0323FBE51FA080722634` | unchanged_2.1 |
| `docker-compose.yml` | Supporting package artifact | supporting | YAML | 441 | `A21386296B5493491539D6A93E9A2DB724E8BAAE9EF57E6D59AEB435DFF6BB5A` | unchanged_2.1 |
| `docs/00_README.md` | Нормативная или историческая документация Этапа 2 | canonical | Markdown | 6536 | `3D8F10368BC5A3C78ED107812114BB85D14503D17196199E2C0CB453392672F0` | corrected_2.2 |
| `docs/01_core_domain_and_data.md` | Нормативная или историческая документация Этапа 2 | canonical | Markdown | 58491 | `A912E89A5CF6CAD39CDC1BAFB3EF7008ECBD18D143955AAD223A69E3B2855258` | unchanged_2.1 |
| `docs/02_api_and_concurrency.md` | Нормативная или историческая документация Этапа 2 | canonical | Markdown | 67374 | `311E968BB49F995660D3DC60E24390392BA554490568D1FA8C3B0B1B22B465D2` | unchanged_2.1 |
| `docs/03_runtime_operations_and_testing.md` | Нормативная или историческая документация Этапа 2 | canonical | Markdown | 99975 | `8199B30E1C7415E667935D2B5379BA50C0C5BC08015DB69A3C87A526887C6A47` | unchanged_2.1 |
| `docs/04_adr_and_independent_audit.md` | Нормативная или историческая документация Этапа 2 | canonical | Markdown | 13958 | `4DE7715D1DB53572D94E1DA4507761869F304286B4512AE25F2EA2DEB0AA241B` | unchanged_2.1 |
| `docs/05_physical_schema_reference.md` | Нормативная или историческая документация Этапа 2 | canonical | Markdown | 105099 | `5ED78788230BC53FF06D13848E3E2FBACE6E1CD116369DAF9607CCC80E72F3D6` | unchanged_2.1 |
| `docs/06_stage_2_1_normative_corrections.md` | Нормативная или историческая документация Этапа 2 | canonical | Markdown | 20841 | `9F6000C7EAB64F7A529B9D5DDBFD51BEB1B4EC99F957BB02DBB9B2DEA1CC4A0A` | unchanged_2.1 |
| `docs/07_stage_2_1_validation.md` | Нормативная или историческая документация Этапа 2 | supporting | Markdown | 6059 | `48289F84FFF6714321F45C1318E0A5F51EE087D71214199B30BE3013C04FEF52` | unchanged_2.1 |
| `docs/08_stage_2_1_fix_registry.md` | Нормативная или историческая документация Этапа 2 | supporting | Markdown | 17882 | `6908357A0F1FC5139A2F94AF53DFC28C6A97A23B036CE72C7D49B44F89410A50` | unchanged_2.1 |
| `dto_field_catalog.csv` | Field-level каталог DTO | generated | CSV | 186674 | `72492E6E272DBA0F113E9351B416F626439593D50012AF43DD389C46FDDE9B2B` | new_2.2 |
| `openapi/openapi.yaml` | Нормативный OpenAPI 3.1 | canonical | YAML | 959722 | `052738F7BF1B02CAB054B92827E17E3EA79EB0C8832C0F5A6E60681E4B363161` | corrected_2.2 |
| `openapi_validation_report.md` | Итоговый validation report | supporting | Markdown | 2989 | `39FDBCC33A555DCB6C9612A5C2E401C6E0971B88D58B7657409D52D48E910B5C` | new_2.2 |
| `qa/build_openapi.py` | Validator or reproducibility script | supporting | Python | 103468 | `946D20A515A5091CE2448386DE284D039A0AE492E010B3F047A0D5132F39B9A2` | corrected_2.2 |
| `qa/concurrency_tests.py` | Validator or reproducibility script | supporting | Python | 7604 | `1FF3A403771E3435B0ED1D283A75F49608BE7515C3C9F20E6265D87FD6966DA5` | unchanged_2.1 |
| `qa/database_contract_tests.sql` | Validator or reproducibility script | supporting | SQL | 13272 | `976AA6230314B788102761A42FCC8879E3A1355ED22727AF4A08B052EB78613E` | unchanged_2.1 |
| `qa/generate_manifest.py` | Validator or reproducibility script | supporting | Python | 955 | `E9CD0E8BE8457F30AD97B066CBFC57F6953A63E4389CB3A5175DF6C84E735835` | unchanged_2.1 |
| `qa/generate_permission_seed.py` | Validator or reproducibility script | supporting | Python | 1936 | `9B914D3E3BFFB72EC1FFB12FB2E374398D6D0A47E70D22518899C235425D5876` | unchanged_2.1 |
| `qa/generate_server_stub.py` | Validator or reproducibility script | supporting | Python | 2606 | `BD1C9994FEF6CF46A912055CB764A8C0CC977812DE17496C7A508CFAEAC3A854` | unchanged_2.1 |
| `qa/generate_stage_2_2_manifest.py` | Validator or reproducibility script | supporting | Python | 7712 | `4F2AB07B06D680B9C65F46080C2AB7A5490918B24799DA5ED0CB6F6E8BBE3A40` | new_2.2 |
| `qa/generate_traceability.py` | Validator or reproducibility script | supporting | Python | 10062 | `F896246CABCD59C73134E87F1B7F3F6C9F5921B627DFFFF8120C0D59CFA93F4F` | unchanged_2.1 |
| `qa/generated/desktop-csharp/Organizer.DesktopSdk.csproj` | Generated C# desktop SDK | generated | MSBuild XML | 408 | `4D045ABC7169419B6E81E40515EFAE48D9A041DF9B74D67AE9CE9ADE50BC9D5D` | new_2.2 |
| `qa/generated/desktop-csharp/OrganizerDesktopClient.g.cs` | Generated C# desktop SDK | generated | C# | 3145378 | `96484932AED1DEB8537C5786FD3D209216F3533442D71E8F81F2F2B2744A60BE` | new_2.2 |
| `qa/generated/server-csharp/Organizer.ServerStubs.csproj` | Generated C# server stubs | generated | MSBuild XML | 446 | `521119236EEC7CFF07EA139727487FA7A6E18630F3D682A2EC6E26692FBEA99B` | new_2.2 |
| `qa/generated/server-csharp/OrganizerController.g.cs` | Generated C# server stubs | generated | C# | 679026 | `55AE839A2743EDB94C81AC69F252454EC51770D64956D27A61514E2FE8185300` | new_2.2 |
| `qa/reports/concurrency_validation.log` | Validation evidence | supporting | Log | 594 | `FC2B9F92F78870CDC7868D6495149EB73914B61531E68E5D5BA28EC90CB5E4D9` | unchanged_2.1 |
| `qa/reports/postgresql_schema_inventory.json` | Validation evidence | supporting | JSON | 188 | `025816F8E8C796AB2C9CF2991EEE5565445DB9360F1B536D93269ACA4F57A1E2` | unchanged_2.1 |
| `qa/reports/postgresql_validation.log` | Validation evidence | supporting | Log | 19828 | `3003FE26529F0F2BFDE79B58DA1FC7F2371E9BD2DBEDD0FAE3FC2183EF979B9C` | unchanged_2.1 |
| `qa/reports/stage_2_2_contract_gate.log` | Validation evidence | supporting | Log | 666 | `412AD753670B0797B4AA76EC187392B70FEC274F17A68CBC63CDE0806200B1AB` | new_2.2 |
| `qa/reports/stage_2_2_csharp_codegen.log` | Validation evidence | supporting | Log | 2328 | `33E1FF2C90039760D16C016CAC9D2472DA5A23FEFEEBE57C40EC367A465DD189` | new_2.2 |
| `qa/reports/stage_2_2_desktop_csharp_build.log` | Validation evidence | supporting | Log | 716 | `F84B6F63DF4F3185ED8A2B47EC6AF80427BB1DCBB5F2404BBAAAFA94E5EEBA77` | new_2.2 |
| `qa/reports/stage_2_2_legacy_artifact_gate.log` | Validation evidence | supporting | Log | 180 | `8196761026ECF031B383E8EC6DD25A4A17962050DA57AB7A1D09DEB431DD669C` | new_2.2 |
| `qa/reports/stage_2_2_redocly_lint.log` | Validation evidence | supporting | Log | 1578 | `25732F5320A0C57F2587771F670DB85A01FF5A8E4ECF65134987BDA5454B64F3` | new_2.2 |
| `qa/reports/stage_2_2_server_csharp_build.log` | Validation evidence | supporting | Log | 718 | `4CC86D8961C31E01ABC129B17EAD4EF786FFE009F11E04A88B07361286B9B33E` | new_2.2 |
| `qa/requirements.txt` | Validator or reproducibility script | supporting | Text | 67 | `1822CC34C44B7782FC0C043536AA09C001FA605B5552E5383870E37017AD9EBC` | unchanged_2.1 |
| `qa/run_validation.ps1` | Validator or reproducibility script | supporting | PowerShell | 10482 | `AD9CDA67F0CCB9B43F6767B8E4CDD8C25AFE1C7638683EE03550D2553EA7A755` | unchanged_2.1 |
| `qa/source_integrity_gate.py` | Validator or reproducibility script | supporting | Python | 8390 | `BA2A65D6F2A3E698DFA9096874F589258BA26F24B22321330970DF6B676D2A76` | new_2.2 |
| `qa/stage_2_2_contract_gate.py` | Validator or reproducibility script | supporting | Python | 20227 | `7E897A64A438D41568CAD88EB64DF1C99D5D82A71030FC6CC85FBC448C2E30D1` | new_2.2 |
| `qa/stage_2_2_validation.json` | Validator or reproducibility script | supporting | JSON | 318 | `4C2C2F88191F22EFDEAE21491D5F8D7ED92109A81D417D140DF6CAC59D7C0FBD` | new_2.2 |
| `qa/sync_artifacts.py` | Validator or reproducibility script | supporting | Python | 10954 | `DFFC2DAF98D1184CE2507B4B17511663E8200495ADFFA4A3D15010C90B992832` | unchanged_2.1 |
| `qa/validate_artifacts.py` | Validator or reproducibility script | supporting | Python | 12684 | `C266B9D867879594C8B409998F905242429B42ECD36C7DAC325C25CBDBAC3750` | corrected_2.2 |
| `source_integrity_report.md` | Итоговый validation report | supporting | Markdown | 10856 | `91F9039F9DDB03925D66C35EFAC3137EE9C92168A0C24E79054C9AD530E76BAC` | new_2.2 |
| `sources/architecture_stage1.md` | Первичный источник требований | canonical | Markdown | 164400 | `309492241DCD63BAC970B467E729FB878AE265CCA6F592CF43C3BB4AC0CDBE16` | unchanged_2.1 |
| `sources/product_concept.txt` | Первичный источник требований | canonical | Text | 48315 | `74482A0D463A4831228F96847496152D93887AECE6F498E2CD1DEC0054D5F7F4` | unchanged_2.1 |
| `sources/stage_2_1_acceptance_criteria.txt` | Первичный источник требований | canonical | Text | 19684 | `3898CFCF31C3C766CAFEDE3861F813FD5E81DD34EB7B92AA1DF795164C25843F` | unchanged_2.1 |

## 4. Exclusions

- Stale Stage 2.1 TypeScript generated client and server-contract outputs.
- Superseded Stage 2.1 OpenAPI/codegen reports, `MANIFEST.json`, endpoint dump and JSON validation summary.
- `bin`, `obj`, dependency caches, compiler binaries and temporary files.
- Unrelated STOK Git repository content.

## 5. Self-reference and archive hash

The SHA-256 of this manifest cannot be embedded in itself without changing the file. The final manifest hash is delivered in `Organizer_Stage2_Technical_Specification_2.2.manifest.sha256`. The final ZIP hash is delivered in `Organizer_Stage2_Technical_Specification_2.2.zip.sha256`; both sidecars are outside the ZIP.
