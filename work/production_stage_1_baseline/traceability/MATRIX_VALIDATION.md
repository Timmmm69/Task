# Stage 1 implementation matrix validation

## Result

**PASS**

## Counts

| Metric | Count |
|---|---:|
| Source rows read | 3492 |
| Output rows | 3962 |
| Requirements | 3378 |
| API-operation rows | 2408 |
| Unique API operations used | 243 |
| Rows without API | 1554 |
| `no_api` rows | 314 |
| Documented `gap` rows | 1240 |
| Unknown operations found | 0 |
| Operations declared by OpenAPI | 244 |

Unknown operations: none.

## Validation performed

- Every source requirement is represented; the 57 universal `ALL` rules are stored once after equality checks across Wave A/B/C.
- Every multi-operation source cell is split into one row per requirement-to-operation link, with positional path and handler mapping.
- Every API operation is checked against `outputs/stage_2_3/openapi/openapi.yaml`, including its HTTP method and path.
- Every endpoint row is checked for a planned server handler, screen, FLOW and test type.
- Every row without an operation uses `API status=no_api` with a reason or `API status=gap` with an exact source-row reference.

## Manifest

- Matrix version: `1.0.0`
- `work/production_stage_1_baseline/traceability/wave-a.csv` — SHA-256 `9bfc8e14ca1175732f2decae36ab4c5271c0da2c630073aa2d7b52b25b2bd3d2`
- `work/production_stage_1_baseline/traceability/wave-b.csv` — SHA-256 `9ec40ae0fe06595352844eb94ea9479f34fb7875ab655fa7742c9365dae48d21`
- `work/production_stage_1_baseline/traceability/wave-c.csv` — SHA-256 `f1d7a650a5fda6a406461cdba308fcd1fd8a3e017618c462855efb6d97dc6eca`
- `outputs/stage_2_3/openapi/openapi.yaml` — SHA-256 `36c15dff5adba0041fcfd79f5a0d203835dac5cdd4ad24122bcd92177c13220d`
- `work/production_stage_1_baseline/traceability/implementation_matrix.csv` — SHA-256 `3342fb465c4c54a427ce5fd515e68879236c69bd3d5cbe39a22731703b754b76`

## Errors

- None.
