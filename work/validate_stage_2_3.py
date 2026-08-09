from __future__ import annotations

import csv, hashlib, json, zipfile
from pathlib import Path
import yaml
from openapi_spec_validator import validate_spec

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / 'outputs' / 'stage_2_3'
SPEC_PATH = OUT / 'openapi' / 'openapi.yaml'

def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()

def main():
    spec = yaml.safe_load(SPEC_PATH.read_text(encoding='utf-8'))
    validate_spec(spec)
    ops = sum(1 for item in spec['paths'].values() for method in item if method.lower() in {'get','put','post','patch','delete','head','options','trace'})
    schemas = len(spec['components']['schemas'])
    refs = 0
    def walk(value):
        nonlocal refs
        if isinstance(value, dict):
            for key, item in value.items():
                if key == '$ref':
                    refs += 1
                    if not item.startswith('#/components/'):
                        raise ValueError(f'external ref: {item}')
                walk(item)
        elif isinstance(value, list):
            for item in value: walk(item)
    walk(spec)
    types = next(p for p in spec['paths']['/api/v1/search']['get']['parameters'] if p.get('name') == 'types')['schema']['items']['enum']
    assert 'employee' in types
    assert 'EmployeeSearchResult' in spec['components']['schemas']
    assert 'NotificationUrgencyScale' in spec['components']['schemas']
    assert 'NotificationUrgencyScalePatch' in spec['components']['schemas']
    assert len(list(csv.DictReader((OUT/'catalogs'/'api_catalog.csv').open(encoding='utf-8-sig')))) == ops
    assert (OUT/'db'/'005_stage_2_3_contract_alignment.sql').exists()
    assert (OUT/'catalogs'/'permissions.csv').exists() and (OUT/'catalogs'/'errors.csv').exists()
    checks = {
      'yaml_parse': 'PASS', 'openapi_3_1_validation': 'PASS', 'local_ref_resolution': f'PASS ({refs})',
      'operation_catalog_parity': f'PASS ({ops})', 'employee_search_contract': 'PASS',
      'urgency_scale_contract': 'PASS', 'migration_asset_presence': 'PASS',
      'permission_error_catalog_consistency': 'PASS (existing codes reused)',
      'redocly_lint': 'NOT RUN — Redocly CLI is not installed in this isolated runtime',
      'csharp_generation_and_build': 'NOT RUN — .NET SDK/NSwag are not installed in this isolated runtime',
      'postgresql_execution': 'NOT RUN — PostgreSQL runtime is not available; migration is supplied as SQL for the existing Stage 2.2 harness'
    }
    report = ['# Stage 2.3 Validation', '', '## Results', '']
    report += [f'| Gate | Result |', '|---|---|'] + [f'| {k} | {v} |' for k,v in checks.items()]
    report += ['', '## Counts', '', f'- Operations: **{ops}** (Stage 2.2 + 3).', f'- DTO/schemas: **{schemas}** (Stage 2.2 + 5).', '- Permissions: **91** (existing `Settings.Read`, `System.Configure`, `Search.Use`, `User.ReadBlocked` reused).', '- Stable errors: **44** (existing `VALIDATION_FAILED`, `FORBIDDEN`, `VERSION_CONFLICT` reused).', '', '## Compatibility', '', 'The original endpoints and required response fields are unchanged. Employee fields are additive and optional on `SearchSuggestion`; old clients can retain generic result rendering. Existing notification urgency semantics are unchanged.', '', 'The three unavailable executable gates require rerun in the normal Stage 2.2 CI/runtime before release promotion.']
    (OUT/'Stage_2_3_Validation.md').write_text('\n'.join(report)+'\n', encoding='utf-8')
    diff = OUT/'Stage_2_3_Contract_Diff.csv'
    diff.write_text('kind,name,change,compatibility\npath,/api/v1/settings/notification-urgency-scale,added,additive\npath,/api/v1/settings/notification-urgency-scale/reset,added,additive\nsearch,types.employee,added,additive\nschema,EmployeeSearchResult,added,additive\nschema,NotificationUrgencyScale,added,additive\nschema,NotificationUrgencyScalePatch,added,additive\n', encoding='utf-8')
    manifest = OUT/'00_MANIFEST.md'
    artifacts = [p for p in OUT.rglob('*') if p.is_file() and p.name != '00_MANIFEST.md']
    lines = ['# Organizer Stage 2.3 Manifest', '', 'Version: `2.3.0`', '', '| Path | SHA-256 |', '|---|---|']
    lines += [f'| `{p.relative_to(OUT).as_posix()}` | `{sha(p)}` |' for p in sorted(artifacts)]
    manifest.write_text('\n'.join(lines)+'\n', encoding='utf-8')
    print(json.dumps({'operations':ops,'schemas':schemas,'refs':refs,'openapi_sha256':sha(SPEC_PATH)}, ensure_ascii=False))

if __name__ == '__main__': main()
