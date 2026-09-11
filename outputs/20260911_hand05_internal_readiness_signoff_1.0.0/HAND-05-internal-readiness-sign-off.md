# HAND-05 — единый внутренний readiness sign-off

- **Решение:** INTERNAL READINESS — **APPROVED (PASS)**.
- Дата: 11 сентября 2026 года.
- Версия evidence package: `1.0.0` (`outputs/20260911_hand05_internal_readiness_signoff_1.0.0`).
- Объект проверки: release candidate `outputs/20260911_hand03_release_candidate_1.0.0`
  (RC `1.0.0`, source revision `6e158c8ce25c8b4040dfa8eaff7eacff6f1bbf39`).
- Подписант: внутренний release-владелец (Codex). Это машинно воспроизводимый внутренний
  sign-off, а не криптографическая подпись и не приёмка заказчиком.

## 1. Границы решения

Этот документ подтверждает только то, что разработчик может закрыть без данных,
инфраструктуры и approvals заказчика:

- функциональность, UX, безопасность, эксплуатация, disposition внутренних findings и
  состав release package — в объёме шести release gates roadmap;
- воспроизводимость и проверяемость RC 1.0.0.

Не подтверждается (переносится в post-handoff действия заказчика, раздел 6):

- корпоративные secrets manager, CA, DNS, подсети, хранилища и alert webhook;
- корпоративный code-signing сертификат, RFC 3161 timestamp и производственная
  переподпись desktop-компонента;
- реальные учётные записи, SMB ACL и данные заказчика;
- named approvals (Product owner, Design owner, Desktop tech lead, QA) и формальная
  приёмка заказчика.

## 2. Сверка release gates

Все шесть gates roadmap закрыты пунктами `done` со `progress = 100`
(машинная проверка: `Test-Hand05Readiness.ps1`, раздел `gates`).

| Gate | Требуемые пункты | Состояние |
|------|------------------|-----------|
| GATE-FUNCTIONAL | PROD-01..05, DESK-03, API-04, QA-03 | 8/8 done |
| GATE-DATA | DATA-01, DATA-02, DATA-04, OPS-03 | 4/4 done |
| GATE-SECURITY | SEC-01, SEC-02, SEC-03, SEC-05 | 4/4 done |
| GATE-DEPLOY | OPS-01, OPS-02, OPS-04, OPS-05 | 4/4 done |
| GATE-QUALITY | QA-01, QA-02, QA-03, QA-04, DESK-05 | 5/5 done |
| GATE-HANDOFF | HAND-01, HAND-02, HAND-03, HAND-04, HAND-05 | 5/5 done (HAND-05 закрывается этим документом) |

Примечание по GATE-SECURITY: SEC-03 подтверждён как source contract gate в CI и на чистом
стенде; production sign-off SEC-03 с реальным корпоративным bundle остаётся действием
заказчика (раздел 6, пункт 1).

## 3. Disposition внутренних findings

Сводный реестр `HAND-05-findings-disposition.csv` фиксирует все findings внутренней
проверки production-трека. Открытых и блокирующих findings нет.

| ID | Severity | Поверхность | Disposition |
|----|----------|-------------|-------------|
| SEC05-F-001 | High | Login/API: анонимный login resource exhaustion | `fixed` — исправлено и проверено (SEC-05) |
| QA04-F-001 | Medium | Search: устаревший validation hint | `accepted` |
| QA04-F-002 | Low | Projects: владелец показан UUID | `deferred` |
| QA04-F-003 | Low | Calendar: плотный перенос длинных названий | `accepted` |
| QA04-F-004 | Low | DPI-200: первая строка статуса сокращена | `deferred` |

Дополнительные проверки без открытых findings: SEC-04 hosted Trivy — 0 CRITICAL /
0 fixable HIGH по четырём deployable образам; NuGet audit без NU1900/NU1903–NU1905;
DESK-05 (86/86 нативных проверок) и QA-03 без блокирующих замечаний.

## 4. Сверка состава release package

Состав RC 1.0.0 подтверждён по `manifest.json` HAND-03 и пересчётом SHA-256 всех
65 payload-файлов (`Test-Hand05Readiness.ps1`, раздел `release_checklist`):

- `releaseVersion 1.0.0`, `classification internal-release-candidate`, Git binding
  `sourceRevision`/`productionTree`/`sourceDateEpoch`;
- server `0.6.0`: пять OCI-образов (task-api, task-worker, task-backup-agent,
  task-database-migrator, task-container-validation) с evidence двух независимых сборок
  и `image-map.json`;
- desktop `1.0.0` win-x64: self-contained release, Authenticode с pinned publisher
  thumbprint `0CA60C002A379AC5CF8FAD09FF9E5A02512457B8`, подписанные release/channel
  manifests, installer/update/rollback tools;
- source `task-production.tar` из зафиксированного Git tree;
- compliance: SPDX 2.3 SBOM и 46/46 строк заявленных лицензий NuGet;
- целостность: `SHA256SUMS`, `manifest.json`, detached CMS-подпись и
  `signature/signer.cer`; независимый `validation-report.md: PASS` и автономный
  `Verify-Release.ps1`.

Внутренний RC подписан self-signed validation-сертификатом и доказывает целостность и
единство подписанта, но не удостоверяет корпоративного издателя; корпоративная
переподпись — post-handoff действие (раздел 6, пункт 7).

Смежные передачи сверены: HAND-02 runbook воспроизведён на чистом стенде
(`install/install.json: PASS` фазы A–D и контракт SEC-03; `operate/checks.json: PASS`
мониторинг, backup/PITR/restore, ротация, клиент update/rollback); HAND-04 руководства
пользователя/администратора и demo-маршрут ссылаются на проверенный QA-03 профиль
`qa03-deterministic-v1`.

## 5. Явно вне scope передачи

- **API-05 (audit trail и capabilities администратора)** не входит ни в release gates,
  ни в состав RC 1.0.0; остаётся отдельным внутренним открытым пунктом roadmap и
  не блокирует передачу пакета.
- Narrator/screen-reader walkthrough не входит в acceptance scope продукта Task
  (QA-04 проверяет UIA-контракты, а не заменяет собой экранный диктор).
- Физический mixed-monitor DPI и парк конкретных устройств — deployment smoke заказчика.
- Формальная приёмка, named approvals и live-сеть заказчика — после передачи.

## 6. Post-handoff действия заказчика

Машиночитаемый список: `HAND-05-post-handoff-actions.csv`. Кратко:

1. Корпоративный secrets manager и host agent; production bundle по контракту SEC-03.
2. Корпоративный CA: две server-auth листовки (edge + PostgreSQL), revocation, trust на
   клиентах.
3. DNS-имена `TASK_SERVER_NAME` и PostgreSQL.
4. Три Docker-подсети и firewall-параметры из `ops02.parameters.example.json`.
5. Хранилища: отдельные диски PGDATA/local backup, NFS/SMB off-host backup, immutable
   snapshots, два escrow-хранилища ключей.
6. Alert webhook URL + `TASK_ALERT_OWNER` (OPS-04).
7. Code-signing сертификат + RFC 3161 timestamp + контролируемый HTTPS origin;
   пересборка desktop и повторный gate HAND-03 перед production-публикацией (OPS-05).
8. Реальные учётные записи, SMB ACL и перенос данных заказчика.
9. Production-фазы A–H по `OPS-operations-runbook.md` на инфраструктуре заказчика
   с сохранением protected evidence.
10. Pilot клиента на поддерживаемом корпоративном Windows image (install, update,
    rollback, relaunch).
11. Penetration pass после стабилизации развёртывания (SEC-05, «Explicit non-closure»).
12. Формальная приёмка: QA-03 demo-маршрут на изолированном стенде заказчика и named
    approvals (Product owner, Design owner, Desktop tech lead, QA).

## 7. Воспроизведение

Из корня репозитория:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File work/production/verification/Test-Hand05Readiness.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File work/production/verification/Build-Hand05Package.ps1
```

`Test-Hand05Readiness.ps1` fail-closed пересчитывает: 6/6 gates по roadmap, disposition
findings, состав RC (65/65 SHA-256, SBOM/лицензии, подписанные компоненты, validation
report), evidence HAND-02 и QA-04 sign-off. `-SkipReleaseHashes` допустим только для
быстрой перепроверки уже зафиксированного evidence.
