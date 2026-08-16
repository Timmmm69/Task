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
| `no_api` rows | 394 |
| Documented `gap` rows | 0 |
| Gaps resolved to API rows | 2153 |
| Gaps disposed as no-API rows | 80 |
| Unknown operations found | 0 |
| Operations declared by OpenAPI | 244 |

Unknown operations: none.

## Validation performed

- Every source requirement is represented; the 57 universal `ALL` rules are stored once after equality checks across Wave A/B/C.
- Every multi-operation source cell is split into one row per requirement-to-operation link, with positional path and handler mapping.
- Every API operation is checked against `outputs/stage_2_3/openapi/openapi.yaml`, including its HTTP method and path.
- Every endpoint row is checked for a planned server handler, screen, FLOW and test type.
- Every source gap has exactly one reviewed unresolved, no-API or resolved API disposition.
- Every resolved override is checked against OpenAPI method/path and promoted to an `api` row with evidence.
- Every no-API disposition proves its Desktop-only parent, names `Task.Desktop` as its verification owner and retains screen, FLOW and test scope.
- Every unresolved override remains `API status=gap` with its rationale and exact evidence reference.

## Manifest

- Matrix version: `1.3.0`
- `work/production_stage_1_baseline/traceability/wave-a.csv` — SHA-256 `9bfc8e14ca1175732f2decae36ab4c5271c0da2c630073aa2d7b52b25b2bd3d2`
- `work/production_stage_1_baseline/traceability/wave-b.csv` — SHA-256 `9ec40ae0fe06595352844eb94ea9479f34fb7875ab655fa7742c9365dae48d21`
- `work/production_stage_1_baseline/traceability/wave-c.csv` — SHA-256 `f1d7a650a5fda6a406461cdba308fcd1fd8a3e017618c462855efb6d97dc6eca`
- `work/production_stage_1_baseline/traceability/gap_overrides_wave_a.csv` — SHA-256 `964061c6486fae6442c044f0dc7d8470784c6674d1e4dba683d94cd517be3c6a`
- `work/production_stage_1_baseline/traceability/gap_overrides_wave_b.csv` — SHA-256 `4326c486cc30ca7e3fc88e1c683067c6d31aa823d7d56ae34f4f068eb0b8bdca`
- `work/production_stage_1_baseline/traceability/gap_overrides_wave_c.csv` — SHA-256 `225f5be9d5babd28265149d1cfa316cdaf781c86f9917562dcab186d96a14221`
- `work/production_stage_1_baseline/traceability/desktop_no_api_dispositions.csv` — SHA-256 `937a0f67dcf9c3e696eeef317d20199b27c5a62e2c355ee1f892935ba36882d6`
- `outputs/stage_2_3/openapi/openapi.yaml` — SHA-256 `5da115968490f2907ebe9aff1e7a639333280676fcc7e7c950c2276ae76f128f`
- `work/production_stage_1_baseline/traceability/implementation_matrix.csv` — SHA-256 `1accd9e49b626b5e6e3a80305754b0a5dbda57c1b8726dfa7400f6f2b8dd74d3`

## Errors

- None.
