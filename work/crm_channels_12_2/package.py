"""Package the committed 12.2 increment and verify every archived file."""
from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import hashlib
import json
import shutil
import subprocess
import xml.etree.ElementTree as ET

repo = Path.cwd()
output = Path(r"C:\Users\novik\Таск\outputs\20261018_crm_channels_addresses_12.2.0")
evidence = repo / "work/crm_channels_12_2"
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}


def git(*args):
    return subprocess.check_output(["git", *args], cwd=repo)


def sha(data):
    return hashlib.sha256(data).hexdigest()


def write_json(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


assert not output.exists(), "Do not overwrite an existing final package"
assert git("branch", "--show-current").decode().strip() == "feature/crm-contacts-workspace"
assert not git("diff", "--name-only").strip() and not git("diff", "--cached", "--name-only").strip()
commit = git("rev-parse", "HEAD").decode().strip()
base = git("rev-parse", "HEAD^").decode().strip()
assert base.startswith("935e479")
files = git("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").decode("utf-8").splitlines()
assert all(p == ".project-dashboard/roadmap.json" or p.startswith((
    "work/production/src/Task.Desktop/", "work/production/tests/Task.Desktop.Tests/",
    "work/production/tests/Task.ServiceHosts.Tests/", "work/crm_channels_12_2/")) for p in files)
runs = {}
for path in sorted((evidence / "tests").glob("*.trx")):
    root = ET.parse(path)
    counters = root.find(".//t:Counters", ns).attrib
    runs[path.name] = {k: int(counters[k]) for k in ("total", "passed", "failed", "notExecuted")}
assert runs["desktop-final.trx"] == dict(total=1510, passed=1510, failed=0, notExecuted=0)
assert runs["api-final.trx"] == dict(total=644, passed=644, failed=0, notExecuted=0)
assert runs["baseline-focus.trx"]["failed"] == 1
assert (evidence / "native-final/contact-address-compact.png").exists()
output.mkdir()
for name in files:
    target = output / "source" / name
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(git("show", f"HEAD:{name}"))
shutil.copytree(evidence / "tests", output / "evidence/tests")
shutil.copytree(evidence / "native-final", output / "evidence/native")
shutil.copy2(evidence / "build.log", output / "evidence/build.log")
(output / "VERSION").write_text("12.2.0\n", encoding="utf-8")
(output / "source.diff").write_bytes(git("show", "--binary", "--format=fuller", "HEAD"))
validation = {
    "version": "12.2.0", "commit": commit, "base": base, "branch": "feature/crm-contacts-workspace",
    "result": "PASS_WITH_MANUAL_ACCEPTANCE_LIMITATIONS", "release_build": "PASS", "runs": runs,
    "native_wpf": "PASS: existing card, channel/address entry, confirmed results, delete confirmation, read-only, 800x600 scrolling",
    "initial_failures": "TaskContextActionsUiTests/company.Activate failed on current and clean 935e479; final complete run passed; original TRX retained",
    "not_verified": ["Real PostgreSQL: Docker daemon unavailable; no TASK_POSTGRES_TEST_ADMIN_CONNECTION", "Installed client with live Corporate API, physical offline and accessibility acceptance"],
    "api_limits": ["No address PATCH/DELETE routes", "Server-managed isVerified never sent", "No guaranteed unique primary or duplicate-value constraint"],
    "stage_12_3": "Implementation foundation ready; no company/interaction implementation included",
    "publication": "Local commit only; origin/main not modified",
}
write_json(output / "validation-report.json", validation)
table = "\n".join(f"| {name} | {r['passed']} | {r['failed']} | {r['notExecuted']} |" for name, r in runs.items())
report = f"""# Task 12.2 — каналы связи и адреса, 12.2.0

Реализация закоммичена в `feature/crm-contacts-workspace`: `{commit}`.
Основа 12.1: `{base}`. Публикации в main не выполнялось.

В существующую карточку добавлены способы связи и адреса. Каналы поддерживают
просмотр, добавление, изменение, удаление с подтверждением, подпись и isPrimary;
адреса — просмотр и добавление всех контрактных полей. Данные появляются после
серверного подтверждения и GET карточки, выделение контакта сохраняется.
Доступны русские подписи семи типов каналов и четырёх типов адресов.

| Сущность | Поля формы | Ограничения |
|---|---|---|
| Канал | channelType, value, label, isPrimary | value 1–1000; label ≤100; isVerified не отправляется |
| Адрес | addressType, formattedAddress, countryCode, region, city, street, postalCode, isPrimary | formattedAddress 1–1000; countryCode 2; region/city ≤200; street ≤500; postalCode ≤40 |

Операции: GET contacts/{{id}}; POST contacts/{{id}}/channels;
PATCH/DELETE contacts/{{id}}/channels/{{childId}}; POST contacts/{{id}}/addresses.
Каждая мутация использует актуальный strong parent If-Match. POST/PATCH получают
idempotency key согласно ProductApiRoutes, DELETE ключа не требует. Parent ETag
должен быть strong и больше прежней версии; GET не может подтвердить старую версию.
Основные поля и дочерние операции разделяют блокировку записи. Refresh списка
не подменяет версию открытого черновика. После 412, отмены, недоступности или
неопределённого результата требуется отменить форму и перечитать контакт.
Предусмотрены loading/empty/forbidden/error/unavailable/offline/read-only/conflict/retry;
потеря чтения очищает данные, поздние ответы другого контакта игнорируются.
Сервер проверяет Contact.Update и object-level permissions; клиент не обходит их.

API-источники: ProductApiContracts.cs, PostgresProductApiRelations.cs (Related/Bump,
CrmRelation), PostgresProductApiQueries.cs (Enrich), migration 010, нормативные
CommunicationChannelCreate/Patch и AddressCreate в OpenAPI 2.2.
OpenAPI содержит isVerified, но сервер запрещает его запись — клиент поле не посылает.
Серверный PATCH родителя не возвращает связи: успешный CAS сохраняет их неизменённую
подтверждённую проекцию. Подробная процедура и поля — source/work/crm_channels_12_2/README.md.

Адреса ограничены существующим POST: редактирование и удаление не предлагаются.
Новых справочников, интеграций, карт, геокодирования, компаний и взаимодействий нет.
Флаг основного объекта не означает серверную гарантию единственности; одинаковые
значения каналов не запрещаются новой клиентской политикой. Ошибки 409/412/422
обрабатываются без ложного успеха. Значения не попадают в технические ошибки/логи.
БД, production API, permissions, lifecycle и личная модель не изменены.

## Проверки

Release solution build PASS. Финальный Desktop: **1510 PASS, 0 FAIL, 0 SKIP**.
API: **644 PASS, 0 FAIL, 0 SKIP**. Новые тесты включают все типы, методы и поля,
CAS/idempotency, перечитывание и его отказ, stale/weak ETag, 403/404/409/412/422,
валидацию, read-only/offline, отмену, смену контакта, сохранение выделения и черновика.
Нативная WPF-проверка: формы, подтверждение удаления, server-confirmed списки,
read-only, отсутствие binding errors, 1100×1000 и 800×600; PNG визуально проверены.
Dashboard: затронуты только PROD-05/DESK-03, progress/status сохранены;
dashboard:order и dashboard:validate PASS.

| Прогон | PASS | FAIL | SKIP |
|---|---:|---:|---:|
{table}

Первоначальный общий прогон имел сбой TaskContextActionsUiTests на company.Activate();
он воспроизведён на чистой версии 12.1/935e479, в финальном общем прогоне прошёл.
Исходные TRX сохранены, сбой не скрыт исключением теста.
Real PostgreSQL не проверен: Docker daemon недоступен, connection variable не задана.
Live Corporate/установленный клиент, физическое отключение сети и ручная accessibility
приёмка остаются открытыми. Автотесты с незапущенной БД за её проверку не выдаются.

Основа для разработки 12.3 готова. Полная CRM и выпуск релиза не объявлены завершёнными.
Пакет содержит source overlay, diff, исходные TRX, PNG, версию, manifest и SHA-256;
контрольные суммы всех файлов и содержимое ZIP проверяются package.py.
"""
(output / "validation-report.md").write_text(report, encoding="utf-8")
manifest = {"version": "12.2.0", "commit": commit, "files": [
    {"path": p.relative_to(output).as_posix(), "bytes": p.stat().st_size, "sha256": sha(p.read_bytes())}
    for p in sorted(output.rglob("*")) if p.is_file()
]}
write_json(output / "manifest.json", manifest)
for entry in manifest["files"]:
    assert sha((output / entry["path"]).read_bytes()) == entry["sha256"]
archive = output.parent / (output.name + ".zip")
assert not archive.exists()
with ZipFile(archive, "w", ZIP_DEFLATED) as z:
    for p in sorted(output.rglob("*")):
        if p.is_file():
            z.write(p, p.relative_to(output).as_posix())
with ZipFile(archive) as z:
    assert z.testzip() is None
    assert set(z.namelist()) == {e["path"] for e in manifest["files"]} | {"manifest.json"}
    for entry in manifest["files"]:
        assert sha(z.read(entry["path"])) == entry["sha256"]
archive_sha = sha(archive.read_bytes())
Path(str(archive) + ".sha256").write_text(f"{archive_sha}  {archive.name}\n", encoding="ascii")
write_json(Path(str(archive) + ".validation.json"), {
    "version": "12.2.0", "commit": commit, "archive_sha256": archive_sha,
    "manifest_files_verified": len(manifest["files"]), "archive_crc": "PASS", "all_archived_hashes": "PASS",
})
print(json.dumps({"output": str(output), "commit": commit, "verified_files": len(manifest["files"]), "sha256": archive_sha}, ensure_ascii=True))
