#!/usr/bin/env python3
"""Builds the Wave A implementation matrix wave-a.csv.

Inputs (all read-only):
- Stage 4 validated baseline: work/stage_4_6_lite/inputs/candidate/*.csv|*.md
- Stage 3.5 UX baseline: work/stage_4_6_lite/inputs/audit_input/normative_stage3_5/*
- OpenAPI contract: outputs/stage_2_3/openapi/openapi.yaml
- Generated server stub (handler naming): outputs/stage_2_3/qa/generated/server-csharp/OrganizerController.g.cs

Output: work/production_stage_1_baseline/traceability/wave-a.csv
"""
import csv
import json
import os
import re
import sys

import yaml

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
CAND = os.path.join(ROOT, "work", "stage_4_6_lite", "inputs", "candidate")
ST35 = os.path.join(ROOT, "work", "stage_4_6_lite", "inputs", "audit_input", "normative_stage3_5")
OUT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_CSV = os.path.join(OUT_DIR, "wave-a.csv")

TRACE = os.path.join(CAND, "Stage_4_Requirements_Traceability_4.5.csv")
BR_CAT = os.path.join(CAND, "Stage_4_Business_Rules_Catalog_4.5.csv")
AC_CAT = os.path.join(CAND, "Stage_4_Acceptance_Criteria_Catalog_4.5.csv")
NFR_CAT = os.path.join(CAND, "Stage_4_NFR_Catalog_4.5.csv")
FR_SRC = os.path.join(ROOT, "work", "stage_4_6_lite", "design_input", "prd", "Stage_4_Module_PRDs_4.5.md")
SCR_SRC = os.path.join(ST35, "Stage_3_Screen_Catalog_Final_3.5.md")
FLOW_SRC = os.path.join(ST35, "Stage_3_User_Flows_Final_3.5.md")
OPENAPI = os.path.join(ROOT, "outputs", "stage_2_3", "openapi", "openapi.yaml")

WAVE_A_MODULES = {
    "MOD-001": "Авторизация и сессии",
    "MOD-002": "App shell и навигация",
    "MOD-003": "Сегодня",
    "MOD-004": "Входящие",
    "MOD-005": "Задачи",
    "MOD-006": "Подзадачи и чек-листы",
    "MOD-007": "Повторяющиеся задачи",
    "MOD-008": "Напоминания",
    "MOD-009": "Календарь",
}

MODULE_ORDER = ["ALL"] + list(WAVE_A_MODULES)
MODULE_NAMES = dict(WAVE_A_MODULES)
MODULE_NAMES["ALL"] = "Все модули (Wave A: 9 модулей)"

OP_RE = re.compile(r"([A-Z]+)\s+(\S+)\s+\(([A-Z]+_[a-zA-Z0-9_]+)\)")
SYNC_OPS = [("GET_api_v1_sync_changes", "GET /api/v1/sync/changes"),
            ("POST_api_v1_sync_bootstrap", "POST /api/v1/sync/bootstrap"),
            ("POST_api_v1_sync_ack", "POST /api/v1/sync/ack")]

# --------------------------------------------------------------------------
# Loaders
# --------------------------------------------------------------------------

def read_csv(path):
    with open(path, encoding="utf-8-sig", newline="") as f:
        return list(csv.reader(f))


def norm(text):
    return re.sub(r"\s+", " ", text or "").strip()


def load_fr_titles():
    titles = {}
    with open(os.path.normpath(FR_SRC), encoding="utf-8-sig") as f:
        text = f.read()
    for m in re.finditer(r"\| (FR-\d{3}) \| ([^|]+) \|", text):
        fid = m.group(1)
        if fid not in titles:
            titles[fid] = norm(m.group(2))
    return titles


def load_scr_names():
    names = {}
    with open(SCR_SRC, encoding="utf-8") as f:
        text = f.read()
    for m in re.finditer(r"\| (SCR-\d{3}) \| ([^|]+?) \|", text):
        sid = m.group(1)
        if sid not in names:
            names[sid] = norm(m.group(2))
    return names


def load_flow_names():
    names = {}
    with open(FLOW_SRC, encoding="utf-8") as f:
        text = f.read()
    for m in re.finditer(r"\| (FLOW-\d{3}) \| ([^|]+?) \|", text):
        fid = m.group(1)
        if fid not in names:
            names[fid] = norm(m.group(2))
    # FLOW-038 не входит в Stage 3.5: добавлен в Stage 4.5 (DEC-060,
    # «organizational urgency scale management»). Только упоминание в BR-113 (ALL).
    names.setdefault("FLOW-038", "Управление организационной шкалой срочности (Stage 4.5, DEC-060)")
    return names


def load_openapi():
    with open(OPENAPI, encoding="utf-8") as f:
        spec = yaml.safe_load(f)
    ops = {}
    for path, item in spec.get("paths", {}).items():
        for method, op in item.items():
            if method in ("get", "post", "put", "patch", "delete"):
                ops[op.get("operationId")] = (method.upper(), path)
    return ops


# --------------------------------------------------------------------------
# Parsed artifacts
# --------------------------------------------------------------------------

trace_rows = [r for r in read_csv(TRACE)[1:]
              if r[1] in WAVE_A_MODULES or r[1] == "ALL"]
trace_hdr = read_csv(TRACE)[0]
T = {h: i for i, h in enumerate(trace_hdr)}

br_rules = {r[0]: norm(r[2]) for r in read_csv(BR_CAT)[1:]}

ac_rows = {}
for r in read_csv(AC_CAT)[1:]:
    ac_rows[r[0]] = {"module": r[1], "frbr": norm(r[2]), "owner": norm(r[3]),
                     "scenario": norm(r[6]), "priority": norm(r[7]),
                     "test_type": norm(r[8]), "source": norm(r[9])}

nfr_rows = []
for r in read_csv(NFR_CAT)[1:]:
    nfr_rows.append({"id": r[0], "area": norm(r[1]), "requirement": norm(r[2]),
                     "target": norm(r[3]), "measurement": norm(r[4]),
                     "source": norm(r[5]), "modules": norm(r[6])})

fr_titles = load_fr_titles()
scr_names = load_scr_names()
flow_names = load_flow_names()
openapi_ops = load_openapi()

# --------------------------------------------------------------------------
# Helpers
# --------------------------------------------------------------------------

def resolve_scr(cell):
    ids = [x.strip() for x in re.split(r"[;,]", cell) if x.strip().startswith("SCR-")]
    if not ids:
        return "—"
    return "; ".join(f"{i} {scr_names[i]}" if i in scr_names else i for i in ids)


def resolve_flow(cell):
    ids = [x.strip() for x in re.split(r"[;,]", cell) if x.strip().startswith("FLOW-")]
    if not ids:
        return "—"
    return "; ".join(f"{i} {flow_names[i]}" if i in flow_names else i for i in ids)


def ac_ids_from(cell):
    return [x.strip() for x in re.split(r"[;,]", cell) if re.fullmatch(r"AC-\d+", x.strip())]


def test_types_of(ac_ids):
    out = []
    for aid in ac_ids:
        if aid in ac_rows:
            tt = ac_rows[aid]["test_type"]
            if tt and tt not in out:
                out.append(tt)
        else:
            if "(AC вне каталога)" not in out:
                out.append("(AC вне каталога)")
    return "; ".join(out)


def priorities_of(ac_ids):
    out = []
    for aid in ac_ids:
        if aid in ac_rows:
            pr = ac_rows[aid]["priority"]
            if pr and pr not in out:
                out.append(pr)
    return "; ".join(out)


def api_ops(cell):
    """Returns list of (method, path, opid) found in an API cell."""
    found = []
    for m in OP_RE.finditer(cell):
        found.append((m.group(1), m.group(2), m.group(3)))
    return found


def handler_of(opid):
    return f"OrganizerController.{opid}"


# Module-level default screens/flows (from Stage 4 traceability BR rows).
MODULE_SCR = {
    "MOD-001": "SCR-001;SCR-002;SCR-006;SCR-161",
    "MOD-002": "SCR-004;SCR-005;SCR-007;SCR-008;SCR-200;SCR-204;SCR-205;SCR-207;SCR-208;SCR-209;SCR-211;SCR-212;SCR-213",
    "MOD-003": "SCR-010;SCR-011",
    "MOD-004": "SCR-012;SCR-013;SCR-014",
    "MOD-005": "SCR-020;SCR-021;SCR-022;SCR-023;SCR-024;SCR-025;SCR-029;SCR-030;SCR-031;SCR-032;SCR-034",
    "MOD-006": "SCR-033",
    "MOD-007": "SCR-026;SCR-027",
    "MOD-008": "SCR-028",
    "MOD-009": "SCR-040;SCR-041;SCR-042;SCR-043;SCR-044;SCR-045;SCR-046;SCR-047",
}
MODULE_FLOW = {
    "MOD-001": "FLOW-001;FLOW-002;FLOW-003",
    "MOD-002": "FLOW-002;FLOW-005;FLOW-020",
    "MOD-003": "FLOW-005;FLOW-007;FLOW-008;FLOW-020;FLOW-021",
    "MOD-004": "FLOW-034",
    "MOD-005": "FLOW-004;FLOW-005;FLOW-006;FLOW-007;FLOW-008;FLOW-025;FLOW-033",
    "MOD-006": "FLOW-009",
    "MOD-007": "FLOW-010;FLOW-011;FLOW-012",
    "MOD-008": "FLOW-021",
    "MOD-009": "FLOW-031;FLOW-032",
}

COLUMNS = ["Requirement", "Type", "Module", "Module name", "Requirement title",
           "API operationId", "API path (method)", "Permission",
           "Server handler (planned)", "Screen (Stage 3.5)", "FLOW (Stage 3.5)",
           "Acceptance criteria (AC)", "Test type", "Priority", "Source"]

rows_out = []
missing_opids = []
checked_opids = 0


def add_row(req, rtype, module, title, opid, path, permission, handler,
            scr_cell, flow_cell, ac_cell, test_type, priority, source,
            module_name=None):
    rows_out.append([req, rtype, module, module_name or MODULE_NAMES.get(module, module),
                     title, opid, path, permission, handler,
                     resolve_scr(scr_cell), resolve_flow(flow_cell),
                     ac_cell, test_type, priority, source])


def api_cell_to_cols(api_cell):
    """Returns (opid, path, handler) from an API cell; verifies opid in OpenAPI."""
    global checked_opids
    ops = api_ops(api_cell)
    if not ops:
        return ("—", "—", "—")
    opids, paths, handlers = [], [], []
    for method, path, opid in ops:
        checked_opids += 1
        if opid in openapi_ops:
            om, op = openapi_ops[opid]
            paths.append(f"{om} {op}")
        else:
            missing_opids.append(opid)
            paths.append(f"{method} {path}")
        opids.append(opid)
        handlers.append(handler_of(opid))
    return ("; ".join(opids), "; ".join(paths), "; ".join(handlers))


# --------------------------------------------------------------------------
# Compose rows: FRs and other per-module requirement rows (traceability CSV)
# --------------------------------------------------------------------------

req_rows = []  # (module_order, seq, row)
for row in trace_rows:
    req, module = row[T["Requirement"]], row[T["Module"]]
    rtype = req.split("-")[0]
    req_rows.append((MODULE_ORDER.index(module), req, row, rtype))
req_rows.sort(key=lambda x: (x[0], re.match(r"([A-Z]+)-(\d+)", x[1]).group(2).zfill(4)))

requirement_lookup = {}  # requirement id -> composed row dict (for AC inheritance)

for _, req, row, rtype in req_rows:
    module = row[T["Module"]]
    api_cell = row[T["API"]]
    ac_cell = row[T["AC"]]
    ac_ids = ac_ids_from(ac_cell)
    if module == "ALL":
        scr_cell = row[T["SCR"]] or "—"
        flow_cell = row[T["FLOW"]] or "—"
    else:
        scr_cell = row[T["SCR"]] or MODULE_SCR[module]
        flow_cell = row[T["FLOW"]] or MODULE_FLOW[module]

    if rtype == "FR":
        title = fr_titles.get(req, "—")
        if "Desktop-only" in api_cell:
            opid = path = handler = "— (Desktop-only, без нового API)"
        elif "Business rule" in api_cell:
            opid = path = handler = "—"
        else:
            opid, path, handler = api_cell_to_cols(api_cell)
        permission = norm(row[T["Permission"]]) or "—"
    elif rtype == "BR":
        title = br_rules.get(req, "—")
        opid = path = handler = "—"
        permission = "—"
    elif rtype == "SYNC":
        title = "Синхронизация модуля: bootstrap/incremental sync, ack, cursor, scope invalidation"
        opids = [o for o, _ in SYNC_OPS]
        paths = []
        for o, _p in SYNC_OPS:
            checked_opids += 1
            if o in openapi_ops:
                paths.append(f"{openapi_ops[o][0]} {openapi_ops[o][1]}")
            else:
                missing_opids.append(o)
        opid = "; ".join(opids)
        path = "; ".join(paths)
        handler = "; ".join(handler_of(o) for o in opids)
        permission = "—"
    elif rtype == "AUDIT":
        title = "Аудит и история действий модуля (append-only, redaction-aware)"
        opid = "— (domain command + audit/history endpoints)"
        path = "—"
        handler = "— (domain handlers + audit pipeline)"
        permission = "—"
    else:  # DATA, PERM, ERR
        label = {"DATA": "Данные модуля: DTO/агрегаты и их поля по OpenAPI",
                 "PERM": "Права модуля: capabilities и scopes по OpenAPI",
                 "ERR": "Ошибки модуля: стабильные коды, HTTP-статусы и recovery по OpenAPI"}[rtype]
        title = label
        opid = "— (операции модуля)"
        path = "—"
        handler = "—"
        permission = "—"

    test_type = test_types_of(ac_ids)
    priority = priorities_of(ac_ids)
    source = norm(row[T["Source"]]) or "—"
    add_row(req, rtype, module, title, opid, path, permission, handler,
            scr_cell, flow_cell, ac_cell, test_type, priority, source)
    requirement_lookup[req] = {
        "module": module, "title": title, "opid": opid, "path": path,
        "permission": permission, "handler": handler,
        "scr": scr_cell, "flow": flow_cell, "source": source,
    }

# --------------------------------------------------------------------------
# AC rows (placed after their primary owner; module end otherwise)
# --------------------------------------------------------------------------

def module_default_scr(module):
    return MODULE_SCR.get(module, "—")


def module_default_flow(module):
    return MODULE_FLOW.get(module, "—")


ac_list = sorted((kv for kv in ac_rows.items()
                  if kv[1]["module"] in WAVE_A_MODULES or kv[1]["module"] == "ALL"),
                 key=lambda kv: (MODULE_ORDER.index(kv[1]["module"]), int(kv[0].split("-")[1])))
ac_orphans = 0
for aid, info in ac_list:
    owner = info["owner"] or info["frbr"].split(";")[0].strip()
    parent = requirement_lookup.get(owner)
    if parent is None:
        parent = next((requirement_lookup[k] for k in requirement_lookup
                       if k == info["frbr"].split(";")[0].strip()), None)
    if parent is None:
        # primary owner not in Wave A rows -> place at module end without API inheritance
        parent = {"module": info["module"], "title": "—", "opid": "—", "path": "—",
                  "permission": "—", "handler": "—",
                  "scr": module_default_scr(info["module"]),
                  "flow": module_default_flow(info["module"]), "source": "—"}
        ac_orphans += 1
    scr_cell = parent["scr"] if parent["scr"] and parent["scr"] != "—" else module_default_scr(info["module"])
    flow_cell = parent["flow"] if parent["flow"] and parent["flow"] != "—" else module_default_flow(info["module"])
    add_row(aid, "AC", info["module"], info["scenario"] or "—",
            parent["opid"] if parent["opid"] != "—" else "—",
            parent["path"] if parent["path"] != "—" else "—",
            parent["permission"], parent["handler"],
            scr_cell, flow_cell, "",
            info["test_type"], info["priority"], info["source"] or "—")

# --------------------------------------------------------------------------
# NFR rows (cross-module, Wave A scope)
# --------------------------------------------------------------------------

for nfr in nfr_rows:
    add_row(nfr["id"], "NFR", "ALL", f"[{nfr['area']}] {nfr['requirement']}",
            "—", "—", "—", "—", "—", "—", "",
            nfr["measurement"] or "—", "—", f"{nfr['source']} (NFR catalog {nfr['id']})",
            module_name="Все модули (Wave A: 9 модулей)")

# --------------------------------------------------------------------------
# Write output
# --------------------------------------------------------------------------

with open(OUT_CSV, "w", encoding="utf-8-sig", newline="") as f:
    w = csv.writer(f, quoting=csv.QUOTE_MINIMAL, lineterminator="\n")
    w.writerow(COLUMNS)
    w.writerows(rows_out)

summary = {
    "output": os.path.relpath(OUT_CSV, ROOT),
    "columns": len(COLUMNS),
    "rows": len(rows_out),
    "by_type": {},
    "by_module": {},
    "fr_titles_found": len([1 for r in trace_rows if r[0].split("-")[0] == "FR" and r[0] in fr_titles]),
    "fr_total": len([1 for r in trace_rows if r[0].split("-")[0] == "FR"]),
    "ac_orphans": ac_orphans,
    "operationIds_referenced": checked_opids,
    "operationIds_missing_from_openapi": missing_opids,
}
for row in rows_out:
    summary["by_type"][row[1]] = summary["by_type"].get(row[1], 0) + 1
    summary["by_module"][row[2]] = summary["by_module"].get(row[2], 0) + 1
print(json.dumps(summary, ensure_ascii=False, indent=2))
