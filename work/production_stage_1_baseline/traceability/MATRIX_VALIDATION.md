# Stage 1 implementation matrix validation

## Result

**PASS**

## Counts

| Metric | Count |
|---|---:|
| Source rows read | 3492 |
| Gap override rows read | 1246 |
| Output rows | 4955 |
| Requirements | 3378 |
| API-operation rows | 4561 |
| Unique API operations used | 243 |
| Rows without API | 394 |
| `no_api` rows | 314 |
| Documented `gap` rows | 80 |
| Gaps resolved to API rows | 2153 |
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

- Matrix version: `1.2.0`
- `work/production_stage_1_baseline/traceability/wave-a.csv` — SHA-256 `9bfc8e14ca1175732f2decae36ab4c5271c0da2c630073aa2d7b52b25b2bd3d2`
- `work/production_stage_1_baseline/traceability/wave-b.csv` — SHA-256 `9ec40ae0fe06595352844eb94ea9479f34fb7875ab655fa7742c9365dae48d21`
- `work/production_stage_1_baseline/traceability/wave-c.csv` — SHA-256 `f1d7a650a5fda6a406461cdba308fcd1fd8a3e017618c462855efb6d97dc6eca`
- `work/production_stage_1_baseline/traceability/gap_overrides_wave_a.csv` — SHA-256 `5eaea30e2e8bcfe4760c52870f9d5afecb627d180125bebc09a98fb1ebe641ca`
- `work/production_stage_1_baseline/traceability/gap_overrides_wave_b.csv` — SHA-256 `26858f8b15e5c3d9d8971f5c54f7c8d02fe9cab3097de482073304b322051d6d`
- `work/production_stage_1_baseline/traceability/gap_overrides_wave_c.csv` — SHA-256 `ca0870e3aef8f1c11aad5a8085f0fcbc427168aa6fccaa40929ae7af67a7f479`
- `outputs/stage_2_3/openapi/openapi.yaml` — SHA-256 `36c15dff5adba0041fcfd79f5a0d203835dac5cdd4ad24122bcd92177c13220d`
- `work/production_stage_1_baseline/traceability/implementation_matrix.csv` — SHA-256 `daf8329d6c443f305885a26443db37214404f75f1ed920fec7dec1ecdda6e093`

## Errors

- None.
