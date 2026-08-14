# Stage 1 implementation matrix validation

## Result

**PASS**

## Counts

| Metric | Count |
|---|---:|
| Source rows read | 3492 |
| Gap override rows read | 1246 |
| Output rows | 3968 |
| Requirements | 3378 |
| API-operation rows | 3409 |
| Unique API operations used | 243 |
| Rows without API | 559 |
| `no_api` rows | 314 |
| Documented `gap` rows | 245 |
| Gaps resolved to API rows | 1001 |
| Unknown operations found | 0 |
| Operations declared by OpenAPI | 244 |

Unknown operations: none.

## Validation performed

- Every source requirement is represented; the 57 universal `ALL` rules are stored once after equality checks across Wave A/B/C.
- Every multi-operation source cell is split into one row per requirement-to-operation link, with positional path and handler mapping.
- Every API operation is checked against `outputs/stage_2_3/openapi/openapi.yaml`, including its HTTP method and path.
- Every endpoint row is checked for a planned server handler, screen, FLOW and test type.
- Every source gap has exactly one reviewed unresolved override or one or more reviewed resolved API links.
- Every resolved override is checked against OpenAPI method/path and promoted to an `api` row with evidence.
- Every unresolved override remains `API status=gap` with its rationale and exact evidence reference.

## Manifest

- Matrix version: `1.1.0`
- `work/production_stage_1_baseline/traceability/wave-a.csv` — SHA-256 `dd75d8404759d3045758528f045f8066af833a5dd987429d29cccca9f355ecca`
- `work/production_stage_1_baseline/traceability/wave-b.csv` — SHA-256 `8675f380cff8ae719eb1da4407d5c1dae70f43b3b591b57e081c62859c1426f0`
- `work/production_stage_1_baseline/traceability/wave-c.csv` — SHA-256 `3514f5596218d23c22386862ba13e4eba82502dcf9da65f84f75ecdc07109f7f`
- `work/production_stage_1_baseline/traceability/gap_overrides_wave_a.csv` — SHA-256 `d477c9e097fcb12cb064a2c121bb3cce25f67812602489c4ed3f84680e1ad0ef`
- `work/production_stage_1_baseline/traceability/gap_overrides_wave_b.csv` — SHA-256 `e13d1d2afaf119adee5ac7d4859962c35ae22d423e6c5c00ef589ac72f1fc29f`
- `work/production_stage_1_baseline/traceability/gap_overrides_wave_c.csv` — SHA-256 `e7f8203f6ce5bce451a0b2a21f5f5dbf01068703b9871e2bfdb360f2cf016ea6`
- `outputs/stage_2_3/openapi/openapi.yaml` — SHA-256 `5429a8c8be079157fba504900b6a0d6197dda8c2821c8b17aa3f8bc78c2e2614`
- `work/production_stage_1_baseline/traceability/implementation_matrix.csv` — SHA-256 `4bbf9d2dd28049f5dfbbc750263c4362f0e991dd504c602206e34c9acaa8d0ea`

## Errors

- None.
