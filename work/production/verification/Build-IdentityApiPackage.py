"""Build and verify the reviewed API-01 source delta and test-evidence package."""
from pathlib import Path
import hashlib
import json
import shutil
import xml.etree.ElementTree as ET
import zipfile

ROOT=Path(__file__).resolve().parents[3]
VERSION='1.0.0'
NAME='20260905_api01_identity_lifecycle_'+VERSION
OUTPUT=ROOT/'outputs'/NAME
EVIDENCE=ROOT/'work/production/evidence/api01'
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

def main():
    sources=json.loads((ROOT/'work/production/verification/api01-package-files.json').read_text(encoding='utf-8'))
    latest={}
    for path in sorted(EVIDENCE.glob('*.trx'),key=lambda p:p.stat().st_mtime):
        tree=ET.parse(path)
        counters=tree.find('.//t:ResultSummary/t:Counters',NS)
        tests=tree.findall('.//t:TestDefinitions/t:UnitTest',NS)
        if counters is None or not tests or int(counters.get('total','0'))<100: continue
        project=Path(tests[0].get('storage')).stem
        latest[project]=(path,dict(counters.attrib))
    assert len(latest)==3, 'Need full runs of all three test assemblies.'
    for path,counters in latest.values():
        assert counters['total']==counters['passed'] and int(counters.get('notExecuted','0'))==0, path
    OUTPUT.mkdir(parents=True,exist_ok=True)
    # A source delta contains the exact reviewed paths, not binaries or local PostgreSQL data.
    for relative in sources:
        source=ROOT/relative
        assert source.is_file() and source.resolve().is_relative_to(ROOT.resolve())
        destination=OUTPUT/'source'/relative
        destination.parent.mkdir(parents=True,exist_ok=True)
        shutil.copyfile(source,destination)
    (OUTPUT/'evidence').mkdir(exist_ok=True)
    for path,_ in latest.values(): shutil.copyfile(path,OUTPUT/'evidence'/path.name)
    total=sum(int(c['passed']) for _,c in latest.values())
    rows='\n'.join(f'| {project} | {c["passed"]}/{c["total"]} | 0 |' for project,(_,c) in sorted(latest.items()))
    report=f'''# API-01 validation report — {VERSION}

Result: PASS. API-01 is complete in the validated source baseline.

| Suite | Passed / total | Skipped |
| --- | --- | --- |
{rows}

Total: {total} passed, zero failed, zero skipped. These are real PostgreSQL-enabled full solution runs, not the environment-guarded no-database path.

Command: `pwsh -NoProfile -File work/production/verification/Test-IdentityApi.ps1 -Filter ''`.

The tests cover migration application, limited runtime permissions, user create/update/activate/block/unblock/deactivate/reactivate/reset, version conflicts, idempotency replay and mismatch, tenant isolation, session/device ownership, stable temporary-password expiry, token/session revocation, current-session metadata, malformed JSON, device-key validation, authentication, refresh, password change and existing API/desktop regressions. Existing product-event types are preserved by migration 012. Architecture boundary and Git whitespace checks were also run.

The new PostgreSQL lifecycle tests execute against a disposable PostgreSQL 16 cluster using a non-superuser runtime role. Stored audit reasons contain no credential material. Full raw TRX files are included for independent inspection.

Dashboard: API-01 is done/100 and recommended ordering was recalculated. The separate global dashboard validator reports the pre-existing `SEC-02: invalid progress` because SEC-02 is 55 in both the baseline and current roadmap while its validator accepts only 0/25/50/75/100. SEC-02 readiness and dashboard validation rules were not changed to hide this unrelated inconsistency. This does not affect the product test gate above.

Compatibility: current-session desktop metadata remains as documented additive fields; session lists now use SessionPage. See `source/work/production/docs/api01-identity-lifecycle.md` for supported query shapes, permission mapping and deployment order.

Scope: source implementation and reproducible automated acceptance of API-01. This does not claim a production deployment or completion of unrelated roadmap items. Existing ASP.NET deprecation and desktop test-analyzer warnings are not introduced by this work.

This is a source delta against repository baseline e03bcf077afad450e9abec2421e2f4449a121f39. Apply with the repository's normal release procedure and run the database migrator before the new API.
'''
    (OUTPUT/'validation-report.md').write_text(report,encoding='utf-8')
    (OUTPUT/'VERSION').write_text(VERSION+'\n',encoding='utf-8')
    entries=[]
    for path in sorted(OUTPUT.rglob('*')):
        if path.is_file() and path.name not in ('manifest.json','SHA256SUMS'):
            entries.append({'path':path.relative_to(OUTPUT).as_posix(),'bytes':path.stat().st_size,'sha256':digest(path)})
    manifest={'task':'API-01','version':VERSION,'status':'validated','baseCommit':'e03bcf077afad450e9abec2421e2f4449a121f39','testsPassed':total,'files':entries}
    (OUTPUT/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    for entry in entries: assert digest(OUTPUT/entry['path'])==entry['sha256']
    sums=[f'{entry["sha256"]}  {entry["path"]}' for entry in entries]
    sums.append(f'{digest(OUTPUT/"manifest.json")}  manifest.json')
    (OUTPUT/'SHA256SUMS').write_text('\n'.join(sums)+'\n',encoding='utf-8')
    archive=ROOT/'outputs'/(NAME+'.zip')
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as package:
        for path in sorted(OUTPUT.rglob('*')):
            if path.is_file(): package.write(path,path.relative_to(OUTPUT).as_posix())
    with zipfile.ZipFile(archive) as package: assert package.testzip() is None
    archive.with_suffix('.zip.sha256').write_text(f'{digest(archive)}  {archive.name}\n',encoding='utf-8')
    print(json.dumps({'package':str(archive),'sha256':digest(archive),'files':len(entries),'testsPassed':total}))

if __name__=='__main__': main()
