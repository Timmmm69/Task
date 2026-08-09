# Task — Stage 5.3 Wave B Design Coverage Matrix 0.1

**Status:** working design-coverage package; Gate 5.3 is not closed.  
**Scope:** Projects/members/lifecycle, Files/FileLocations/SMB diagnostics, CRM, comments/watchers/history.  
**Source basis:** `outputs/stage_5_plan/Stage_5_Visual_Design_Plan_1.0.md`; `work/stage_5_2/Component_Inventory_0.1.csv`; the mapped flow and state contracts in `work/stage_5_2/`.

The companion CSV is the executable record-level matrix. It covers 47 relevant SCR records, 12 related FLOW records, and 14 reusable STATE contracts: 73/73 records have a representative frame/state, action path, permission/redaction rule, failure or destructive variant, component evidence, and implementation backlog reference.

## Boundary and N/A decisions

* `Task.Watch` has no separate SCR or new data field in the supplied inventory. The required evidence is a capability-filtered watcher control on the existing Task card (`SCR-024`) using `PeoplePicker`, `PermissionState`, and `SemanticStatus`; its precise backlog item is `WB-WATCH-01`. It must not create a standalone Watcher surface or invent watcher API payloads.
* Shared comments/history are covered both at their object tabs (`SCR-035`, `SCR-036`, `SCR-067`, `SCR-068`) and as reusable surfaces (`SCR-201`, `SCR-202`). The variants are intentional reuse, not duplicate new UI.
* Physical file content and Windows/SMB access are not changed by catalog lifecycle, relinking, or diagnostics. `FILE_ACCESS_DENIED` is a valid post-action OS result, distinct from metadata visibility and server capability.

## Traceability summary

| Area | SCR | FLOW | State evidence | Representative storyboard sections |
|---|---:|---:|---|---|
| Projects and members | 13 | 3 | load, empty, forbidden, conflict, archived/trashed, offline | P1–P4 |
| Files and SMB diagnostics | 12 | 4 | partial/redacted path, no location, OS deny, network unavailable, unsafe path, conflict | F1–F5 |
| CRM | 10 | 2 | PII redaction, validation, conflict, unavailable, offline | C1–C4 |
| Comments/watchers/history | 8 | included in object journeys | tombstone, permission recheck, redacted audit, offline | S1–S3 |
| Lifecycle | 4 | 3 | archive, trash, restore conflict, purge legal hold | L1–L4 |

## Prototype backlog — exact scope

| ID | Implement in prototype | Acceptance evidence |
|---|---|---|
| WB-PROJ-01…10 | list, overview/tabs, members, role dialog, project settings/lifecycle | project creation, member role change, complete/archive storyboard paths can be walked without a network call |
| WB-FILE-01…12 | catalog/item detail, location manager, open resolution, recovery, relink, SMB diagnostics, native picker handoff | each diagnosis names metadata vs OS/SMB access and offers only permitted recovery |
| WB-CRM-01…10 | contacts/companies list/cards/editors, channels, interactions, links, menu | PII/hidden relation uses neutral redaction; no external communication side-effect is implied |
| WB-COM-01…03 | object and reusable comment threads | edit-own/moderate distinction, tombstone, offline read-only shown |
| WB-HIST-01…03 | object/project/reusable history | structured entries and current-access redaction, never raw JSON |
| WB-WATCH-01 | watcher section/control on `SCR-024` only | `Task.Watch` capability filters picker/action; server recheck result is explained by `SCR-204` |
| WB-LIFE-01…06 | project/object lifecycle, confirmation, archive, trash, restore, purge | completion and archive are distinct; purge requires typed confirmation and states metadata irreversibility |
| WB-PERM-01; WB-CMD-01; WB-ERR-01 | unavailable reason, generic context menu, error details | keyboard-equivalent menu; no raw policy graph; safe trace id only |

## Non-negotiable frame annotations

Every backlog frame inherits the source component contracts: visible focus, keyboard reachability, accessible name/role/state, non-colour status indication, deterministic dialog focus trap, and a concise live status for result/error. At 200% scaling, tables reflow to a single-column detail order rather than clipping path, permission, or destructive-action explanation.

The storyboard specification is `Wave_B_Annotated_Storyboard_Spec_0.1.md`; it defines the representative state for each backlog group. This package intentionally provides storyboard/component evidence, not a claim that the production prototype or Gate 5.3 is complete.
