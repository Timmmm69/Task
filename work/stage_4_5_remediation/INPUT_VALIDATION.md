# Stage 4.5 input validation

All ZIP inputs were hash-checked where a supplied hash exists, CRC-tested, fully read, extracted into the isolated work area, and reopened.

| Input | Archive | SHA-256 | Entries | Manifest entries | Result |
|---|---|---|---:|---|---|
| candidate_4_3 | Organizer_Stage4_PRD_Candidate_4.3.zip | `952BC37316AAAAC9F1C18EA8DD8FFC1214E1490730DDB5C5AD31ADA84017691F` | 18 | 00_MANIFEST.md | PASS |
| audit_4_4 | Organizer_Stage4_4_Reaudit_Report.zip | `A568C8437E37703CBB46E8F9DC15BC7812004E7E81FCCA48B911A7CCEF0FB003` | 17 | 00_MANIFEST.md | PASS |
| remediation_input | Organizer_Stage4_5_Remediation_Input.zip | `18377B19BF48159F322C228CCB938C5089751A12180CD048F5CBC82B492479B4` | 11 | 00_MANIFEST.md | PASS |
| stage2_3_1 | Organizer_Stage2_Technical_Specification_2.3_Final.zip | `75EFC3E83F09FBCC41AE7DA68A96F2EC0EBDFC74E61F62615F4DA3478AFE5019` | 377 | stage_2_3/00_MANIFEST.md | PASS |
| stage3_5 | Organizer_Stage3_Final_Baseline_3.5.zip | `6C2447E935DD413488E482F7DB3C481C8DC6E53AEB57A07D1DF23D3ADA85381E` | 13 | 00_MANIFEST.md | PASS |

No source archive was modified.
