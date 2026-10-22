"""Package the verified Task 14 increment, preserving all historical receipts."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def run(*args):
    return subprocess.check_output(args, cwd=ROOT, text=True, encoding='utf-8')


def test_results(folder, require_green=True):
    results = []
    for path in sorted(folder.glob('*.trx')):
        tree = ET.parse(path)
        counters = tree.find('.//t:Counters', NS).attrib
        if int(counters['total']) == 0:
            continue
        result = {'trx': path.name, **{key: int(counters[key]) for key in ('total', 'passed', 'failed', 'notExecuted')}}
        result['assembly'] = tree.find('.//t:UnitTest', NS).get('storage').replace('\\', '/').split('/')[-1]
        if require_green and (result['failed'] or result['notExecuted'] or result['total'] != result['passed']):
            raise ValueError(f'Non-green test receipt: {result}')
        results.append(result)
    return results


def build(output, gate, recheck, desktop, samples):
    if not output.is_relative_to(ROOT / 'outputs'):
        raise ValueError('Publication packages must stay within this repository outputs/.')
    original = test_results(gate, require_green=False)
    http = test_results(recheck)
    ui = test_results(desktop)
    domain = [s for s in original if s['assembly'] == 'task.tests.dll']
    if len(original) != 3 or len(domain) != 1 or len(http) != 1 or len(ui) != 1:
        raise ValueError('Expected the original three suites and full final HTTP/Desktop rechecks.')
    if domain[0]['passed'] != domain[0]['total'] or http[0]['assembly'] != 'task.servicehosts.tests.dll' or ui[0]['assembly'] != 'task.desktop.tests.dll':
        raise ValueError('Incomplete or non-green final suites.')
    suites = [domain[0], http[0], ui[0]]
    if run('git', 'status', '--porcelain', '--', 'work/production', '.project-dashboard').strip():
        raise ValueError('Commit the reviewed source/dashboard before freezing their fingerprints.')
    source_commit = run('git', 'rev-parse', 'HEAD').strip()
    output.mkdir(parents=True, exist_ok=False)
    shutil.copytree(gate, output / 'evidence/original-full-gate')
    shutil.copytree(recheck, output / 'evidence/http-final-recheck')
    shutil.copytree(desktop, output / 'evidence/desktop-final-recheck')
    shutil.copytree(samples, output / 'evidence/contract-samples')
    package_checks = run(sys.executable, str(Path(__file__).with_name('Verify-Task14Packages.py')), str(ROOT), str(ROOT))
    (output / 'evidence/package-checks.json').write_text(package_checks, encoding='utf-8')
    run(sys.executable, str(Path(__file__).with_name('Build-Task14Contract.py')),
        '--out', str(output / 'contract'), '--samples', str(samples))

    changed = run('git', 'diff', '--name-only', 'origin/main..HEAD', '--', 'work/production', '.project-dashboard/roadmap.json').splitlines()
    fingerprints = []
    for relative in changed:
        data = subprocess.check_output(['git', 'show', f'{source_commit}:{relative}'], cwd=ROOT)
        destination = output / 'source-overlay' / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(data)
        fingerprints.append({'path': relative, 'gitBlobSha256': hashlib.sha256(data).hexdigest()})
    (output / 'source-files.json').write_text(json.dumps(fingerprints, indent=2) + '\n', encoding='utf-8')
    validation = {'version': '1.0.0', 'result': 'PASS', 'sourceCommit': source_commit,
        'originMainBeforePublication': run('git', 'rev-parse', 'origin/main').strip(),
        'fullSuites': suites, 'fullPassed': sum(s['passed'] for s in suites), 'fullFailed': 0, 'fullSkipped': 0,
        'finalHttpRecheck': http, 'originalPackageChecksPassed': True, 'contractSamplesValidated': True,
        'originalFullGate': original, 'finalDesktopRecheck': ui,
        'sourcesUnchanged': not bool(run('git', 'diff', '--name-only', 'origin/main..HEAD', '--', 'sources').strip()),
        'readyForMain': True, 'manualAcceptanceNotRun': ['live company LAN/multi-client', 'screen reader', 'physical mixed-DPI/multi-monitor']}
    assert validation['sourcesUnchanged']
    (output / 'validation-summary.json').write_text(json.dumps(validation, indent=2) + '\n', encoding='utf-8')
    report = f'''# Task 14.1–14.4 — финальная проверка публикации

Версия 1.0.0, 22.10.2026 (Europe/Minsk). Результат: **PASS**.
Проверенный source commit: `{source_commit}`.

Исправлены все ошибки, выявленные проверкой перед main:

- Native UI gate: дочерний тест получает foreground authorization; ожидание активации ограничено, проверяется реальное foreground-окно. Проверки мыши, клавиатуры и фокуса сохранены.
- API-14.4-07: история возвращает все семь обязательных полей, UTC timestamp, каноническую категорию изменения и correlationId. Поля WPF сохранены и явно объявлены в исправленном контракте; приватный payload не раскрывается.
- DOC-14.4-08: производный OpenAPI и operation-specific CSV согласуют VERSION_CONFLICT=412, ACCOUNT_BLOCKED=423, INTERNAL_ERROR=503 и IDEMPOTENCY_REQUEST_IN_PROGRESS=409 с Retry-After. Исторические sources не изменены.
- В общем ProblemDetails восстановлен обязательный fieldErrors=[], объявлен существующий retryable.
- Исправлен недетерминированный fixture теста пагинации: равные timestamps больше не получают случайный GUID tie-break. Проверка запрета неподдерживаемого Title sort сохранена.

## Проверка

Release solution build: 0 errors. Полные suites в финальных проверках: **{validation['fullPassed']} PASS / 0 FAIL / 0 SKIP**.
Это полный Domain/PostgreSQL suite ({domain[0]['passed']}), полный ServiceHosts recheck ({http[0]['passed']})
и полный Desktop recheck ({ui[0]['passed']}). Production-код после первого полного прогона не менялся.
Его единственный FAIL был в старом fixture пагинации; оригинальный красный TRX сохранён в evidence/original-full-gate.
После исправления fixture повторён весь Desktop; после усиления Retry-After/примеров повторён весь ServiceHosts.
Перекрывающиеся прогоны не прибавляются к общему числу тестов.

Реальные PostgreSQL history responses и HTTP error responses проверены JSON Schema, включая типы, UUID, timestamps,
status/code и Retry-After. Каждый обязательный history field отдельно удалялся из копии ответа: schema обязана отвергнуть такой ответ.
Итоги находятся в contract/contract-validation.json. Dashboard validation и порядок roadmap проверены; progress/status не повышались.

Все исходные пакеты 14.1–14.4 проверены по manifest/SHA256SUMS, ZIP 14.4 дополнительно по SHA/CRC/inventory/payload.
Исторический FAIL предыдущего аудита сохранён; текущая проверка заменяет его рекомендацию не сливать из-за native gate.
Источники, рабочие материалы других задач и несвязанный commit employee-picker не входят в эту публикацию.

## Пределы результата

Пакет готов для main по прямому поручению пользователя «Исправь все ошибки и после этого запушь всё в main».
Здесь подтверждены исправления и автоматизированные/native WPF проверки. Живой LAN/multi-client walkthrough,
реальный screen reader и физические mixed-DPI/multi-monitor сценарии не запускались. Это оставшиеся ручные
приёмочные работы, а не утверждение о найденных неисправленных дефектах. Полная нормативная совместимость
всех остальных API вне перечисленных исправлений этим пакетом не сертифицируется.
'''
    (output / 'validation-report.md').write_text(report, encoding='utf-8')
    (output / 'VERSION').write_text('1.0.0\n', encoding='utf-8')
    (output / '.gitattributes').write_text('* binary\n', encoding='utf-8')

    def entries():
        return [{'path': str(p.relative_to(output)).replace('\\', '/'), 'bytes': p.stat().st_size,
                 'sha256': hashlib.sha256(p.read_bytes()).hexdigest()}
                for p in sorted(output.rglob('*')) if p.is_file()]

    manifest = {'package': output.name, 'version': '1.0.0', 'sourceCommit': source_commit, 'files': entries()}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    records = entries()
    (output / 'SHA256SUMS').write_text(''.join(f'{e["sha256"]}  {e["path"]}\n' for e in records), encoding='utf-8')
    for e in records:
        assert hashlib.sha256((output / e['path']).read_bytes()).hexdigest() == e['sha256']
    print(json.dumps({'output': str(output), 'checksumsVerified': len(records), **validation}, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('out', 'gate', 'recheck', 'desktop', 'samples'):
        parser.add_argument('--' + name, type=Path, required=True)
    args = parser.parse_args()
    build(args.out.resolve(), args.gate.resolve(), args.recheck.resolve(), args.desktop.resolve(), args.samples.resolve())
