# Stage 5.1 / 5.2 Working Validation 0.3

**Date:** 2026-07-28  
**Scope:** Direction 2 Auth/resilience edge-state and adaptive-accessibility wave  
**Result:** PASS  
**Gate status:** Gate 5.1 and Gate 5.2 remain open

## Verification

| Check | Expected | Actual | Result |
|---|---|---|---|
| Production build | success | Vite success, 222 modules | PASS |
| Serena diagnostics | no errors/warnings | none | PASS |
| Endpoint TLS failure | no bypass, actionable error | verified | PASS |
| Endpoint unavailable | no stale-data claim | verified | PASS |
| Endpoint incompatibility | client update guidance | verified | PASS |
| Temporary account lock | timed recovery guidance | verified | PASS |
| Administrator block | no false self-service path | verified | PASS |
| Bootstrap cursor expired | safe full-snapshot recovery | verified | PASS |
| Bootstrap scope changed | authorized-cache rebuild | verified | PASS |
| Bootstrap progress | labelled 68% progressbar | verified | PASS |
| Storage full | cache preserved, writes blocked | verified | PASS |
| Maintenance | retry-after, read-only cache | verified | PASS |
| Reconnecting | writes blocked through checks | verified | PASS |
| Interrupted retry | honest read-only fallback | verified | PASS |
| Scope revalidation | inaccessible data removed before writes | verified | PASS |
| Authoritative recovery | writes restored only after update | verified | PASS |
| Compact viewport | document/body overflow 0 at 1280 × 720 | PASS |
| Device scaling | browser runtime at devicePixelRatio 1.5 | PASS at 150% |
| 800 px adaptive semantics | icon rail and retained accessible names | PASS |
| Long Russian strings | no visible clipping in captured edge states | PASS |
| Reduced motion | reconnect animation disabled by media query | IMPLEMENTED |
| Direction 2 visual comparison | P0/P1/P2 findings | 0 / 0 / 0 |
| Board formula errors | 0 | 0 | PASS |
| Board visual review | Dashboard and Change Log | reviewed | PASS |

## Board snapshot

| Metric | Value |
|---|---:|
| Total tasks | 56 |
| Done | 14 |
| In progress | 17 |
| Blocked | 2 |
| Overall progress | 46% |
| Stage 5.1 progress | 81% |
| Stage 5.2 progress | 73% |
| Workbook sheets | 16 |

No Gate is declared complete. `S5-0214` remains in progress because actual Windows 200% scaling, UI Automation, Narrator and contrast-tool evidence are pending.

## Artifact integrity

| File | Bytes | SHA-256 |
|---|---:|---|
| `stage_5_prototype/src/App.jsx` | 73942 | `CC983AD0651F6FFA757863D061208E478CBA8D515172BD3076E9A4C956620162` |
| `stage_5_prototype/src/styles.css` | 34010 | `E01A26322A0B61579124BEF3376ED83657E6F9E92521294C8A923A32C50CB629` |
| `stage_5_prototype/design-qa-stage5-edge-states.md` | 3234 | `86F679D60F8195C3669A43B06111A8AA15CAE65817FE52B3C0D4842F31E04316` |
| `stage_5_prototype/implementation-direction2-edge-final.png` | 98249 | `E7B31DF964506393C684D100397D0E8F1AC2402F1048F76F8F713DBF2A76F6AD` |
| `stage_5_prototype/design-qa-comparison-edge-final.png` | 702351 | `0B0E71834BB05FD3A0364A6930660FB3A8D29372F2D6BF0487452DEC524B47BD` |
| `stage_5_prototype/edge-auth-tls.png` | 79386 | `AF4E4DA2973E853FE692E91E912F9A9A9BC3F17590C5631CBA50E3F7AB66B17C` |
| `stage_5_prototype/edge-auth-locked.png` | 71073 | `34A1DDBF73F3C58FF8040FE5E7A13A250FFD4690981F6A39AC490738ECA39E12` |
| `stage_5_prototype/edge-bootstrap-cursor.png` | 77457 | `4EF0A8FCAB9FD35A08F47F08D5C196496EB79F6402C5ABCE656281B3DF09A037` |
| `stage_5_prototype/edge-reconnecting.png` | 106192 | `FF4FAAA292324BEA25FB98F3ADF17D75A68B88906FC599B1B7544F7DEE21A398` |
| `stage_5_prototype/edge-scope-recovery.png` | 104829 | `FF8FFFD6362AF86AA5F1F4EF8176A53EE9CEBE3C86CBE87B7F4DA605B3018462` |
| `stage_5_prototype/edge-maintenance-readonly.png` | 104779 | `6D3C0475FC4CEBE57C7467175FD59726650FA1C64A622FC0A4334682DEFE374A` |
| `stage_5_prototype/edge-storage-readonly.png` | 106846 | `E3EF96217A2EC2A33151087F655DE0A740B139B48ECE8468225DEFFB21C07015` |
| `stage_5_1/Interaction_State_Spec_Direction_2_0.1.md` | 6017 | `79AF98C339968F5F3228CE54A2360BCA3E858E2BA6103A02537EF1F4F6F42FEB` |
| `stage_5_2/Accessibility_Evidence_Working_0.2.md` | 2950 | `C0AA1E304FCD307A6A064724539EC2BCAE92634299BE47C558E129EA595541C3` |
| `Stage_5_Task_Board.xlsx` | 109095 | `0CCE09540D733B0FC48B616D2FA315DEED0534A85EC87A7EFEFC6450A6B803E2` |

## Remaining before Gate 5.2

- session-revoked, bootstrap download/signature failure and repeated recovery failure;
- Notification Center and remaining picker/table/tree/pagination variants;
- UI Automation, Narrator, actual Windows 200% scaling and contrast-tool evidence;
- component-to-SCR specification freeze;
- Product Owner, Windows/WPF Tech Lead and QA approval.

