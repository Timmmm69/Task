# OPS — единый operations runbook (HAND-02)

Статус: сводный runbook развёртывания и обслуживания Task. Это точка входа для инженера,
получающего систему без доступа к инфраструктуре заказчика. Документ не заменяет канонические
runbook'и: он задаёт порядок исполнения и ссылается на них. Подробные шаги, пороги и stop
conditions — в указанных документах; при расхождении версий приоритет у ссылочного документа
и проверенного пакета в `outputs/`.

Дата: 2026-09-11. Основание: gap-анализ `docs/OPS-operations-gap-analysis.md`.

## 1. Карта системы и проверенные версии

### 1.1. Канонические документы

| Документ | Область |
|---|---|
| `work/production/docs/task-container-deployment-foundation.md` | образы, роли БД, порядок запуска, hardening |
| `work/production/docs/task-database-migrator.md` | миграции, exit codes, safety contract |
| `work/production/docs/SEC-03-production-secrets-tls.md` | production topology, secret bundle, первое развёртывание, ротация |
| `work/production/docs/jwt-key-management.md` | JWT key ring и pepper |
| `work/production/docs/OPS-02-production-like-network-tls-foundation.md` | DNS, сети, firewall, TLS-приёмка |
| `work/production/docs/backup-restore-runbook.md` | backup/PITR, provisioning, recovery |
| `work/production/docs/backup-deployment-acceptance.md` | приёмочные drill'ы на оборудовании заказчика |
| `work/production/docs/OPS-04-monitoring-alerting.md` | monitoring/alerting и процедуры реагирования |
| `work/production/docs/OPS-05-windows-client-release.md` | signed installer, update channel, rollback |

### 1.2. Проверенные пакеты (использовать только эти версии)

| Компонент | Пакет | Что содержит |
|---|---|---|
| Серверные образы | `outputs/20260911_task_container_release_0.6.0` | OCI-архивы четырёх сервисов, `image-map.json`, `release.json`, provenance, SHA-256 |
| Backup/restore | `outputs/20260907_task_backup_restore_1.0.0` | операторный образ, acceptance suite, три clean-room восстановления, image digests |
| Сеть/TLS | `outputs/20260907_ops02_network_tls_1.0.0` | параметрический контракт, clean-room runs, firewall acceptance |
| Секреты/TLS | `outputs/20260907_sec03_production_secrets_tls_1.0.0` | контракт source gate; production sign-off — на реальном bundle |
| Мониторинг | `outputs/20260908_ops04_monitoring_alerting_1.0.0` | alerts, синтетическая доставка, acceptance |
| Клиент | `outputs/20260910_ops05_windows_client_release_1.0.0` | signed install/update/rollback gates |

Правило: в `production.env` и параметрах допустимы только immutable ссылки `image@sha256:<64 hex>`.
«Допересобрать» образ локально для продакшена запрещено (`task-container-deployment-foundation.md`).
Для локального Docker-рантайма образы выбираются по OCI index digest из `image-map.json`, причём
имя репозитория в ссылке должно совпадать с тегом, под которым образ загружен (например
`task-release/task-api@sha256:<index-digest>`); manifest-дайджесты из `release.json` — для registry.

## 2. Предусловия: customer inputs и владельцы

Пункты, которые может выполнить только заказчик (в synthetic clean-room заменяются
лабораторными аналогами, но никогда не считаются выполненными):

| Параметр/ресурс | Владелец | Документ |
|---|---|---|
| Корпоративный secrets manager и host agent | security/операции | SEC-03; без него SEC-03 остаётся blocked |
| Корпоративный CA, две server-auth листовки (edge + PostgreSQL), revocation, trust на клиентах | PKI-администратор | SEC-03 |
| DNS-имена `TASK_SERVER_NAME` (edge) и имя PostgreSQL; корпоративный DNS | DNS-администратор | OPS-02/SEC-03 |
| Три непересекающихся Docker-подсети `TASK_DATABASE_SUBNET`, `TASK_APPLICATION_EDGE_SUBNET`, `TASK_FRONTEND_SUBNET` | сетевой администратор | OPS-02, `production.env.example` |
| `employeeCidrs`, `managementCidrs`, `managementTcpPorts`, `externalInterface`, `httpsBindAddress` | сетевой/security | `ops02.parameters.example.json` |
| Хранилища: отдельный диск PGDATA, отдельный диск local backup, NFS/SMB off-host backup, immutable/offline NAS snapshots, два escrow-хранилища ключей | storage-администратор | backup-restore-runbook |
| Alert webhook URL (+токен) и `TASK_ALERT_OWNER` | дежурная смена заказчика | OPS-04 |
| Code-signing сертификат + RFC 3161 timestamp + контролируемый HTTPS origin для релиза клиента | release/security | OPS-05 |

## 3. Сквозная процедура развёртывания

### Фаза A — параметры и секреты

1. Зафиксировать значения из раздела 2 в рабочей копии `ops02.parameters.json`
   (из `deployment/ops02/ops02.parameters.example.json`) и `production.env`
   (из `deployment/security/production.env.example`); оба файла — вне репозитория,
   на защищённом носителе. Секреты в `.env` не кладутся.
2. Прогнать контракт: `Test-Ops02Foundation.ps1` — остановиться при любом отклонении значения.
3. Выпустить листовки через корпоративный CA:
   `New-TaskTlsCertificateRequest.ps1 -Purpose edge|database` → CSR → CA → импорт листовок
   в secrets manager (SEC-03 «First deployment», шаги 1–4).
4. Сгенерировать в manager'е: пароли `task_migration`/`task_runtime`, JWT P-256 key ring
   (`jwt-key-management.md`), pepper ≥32 символов; отрендерить bundle по структуре SEC-03
   `TASK_SECRET_ROOT/` с владельцами и режимами.
5. Для чистого стенда без заказчика (шаги 4–5 плана HAND-02): вместо пп. 2–4 использовать
   `New-Ops02CleanRoomAssets.ps1` (синтетический CA, DNS, ключи) и НЕ устанавливать
   synthetic root на реальные клиенты; результат помечать как clean-room, не production.

### Фаза B — окружение: DNS, сети, firewall

1. Резолвинг `TASK_SERVER_NAME → httpsBindAddress` корпоративным DNS (в clean-room —
   отдельный resolver из `New-Ops02CleanRoomAssets.ps1`; на application host порт 53 закрыт).
2. Проверить отсутствие пересечения трёх подсетей с host/VPN/LAN; зафиксировать
   `docker compose config` (OPS-02, раздел 4).
3. Firewall: `Set-Ops02Firewall.ps1 -Action Plan` → ревизия → `-Action Apply` только с
   консоли/break-glass канала (риск lockout). Требуется iptables-бэкенд и цепочка
   `DOCKER-USER`; native nftables/Windows-контейнеры — stop condition. Откат — только
   `-Action Remove` (OPS-02, раздел 5).

### Фаза C — база данных и миграции

1. Поднять только `postgres` из `deployment/security/compose.production.yaml`
   (admin password — из bundle, `POSTGRES_PASSWORD_FILE`).
2. Создать/ротировать роли `task_migration` и `task_runtime`:
   `deployment/containers/sql/initialize-validation-roles.sql` (SEC-03, шаг 5).
3. Миграции one-shot контейнером (`--profile tools`):
   `status` → ожидать exit 6 → `apply` → `grant-runtime.sql` → `status` до `Ready` (exit 0).
   Подробно: `task-database-migrator.md`; перед деструктивной миграцией — проверенный backup.
4. API никогда не получает migration-роль и не применяет миграции сам.

### Фаза D — приложение и TLS-приёмка

1. Запустить `task-api`, `task-worker`, `tls-proxy` (все образы — immutable digest'и).
   Единственный опубликованный порт — `TASK_HTTPS_BIND_IP:443`; 5432/8080 — без host port.
2. Проверить readiness: `/health/ready` через `https://TASK_SERVER_NAME`, HSTS, цепочку,
   `sslmode=verify-full` до PostgreSQL (plaintext должен отклоняться).
3. Прогнать `Test-ProductionSecretsTls.ps1` с реальным bundle и `-Endpoint`;
   на clean-room стенде — `Test-Ops02CleanRoom.ps1` (DinD, синтетический CA).
4. Доверие на клиентах: корпоративный root по управляемой политике; `-SkipCertificateCheck`
   и accept-all callbacks запрещены.

### Фаза E — мониторинг

1. `Bootstrap-Ops04Monitoring.sh` (нужен работающий PostgreSQL и `TASK_SECRET_ROOT`):
   независимые credentials для exporter, только `CONNECT` + `pg_monitor`.
2. Поднять `deployment/monitoring/compose.yaml`, затем пересоздать production stack вместе с
   `deployment/monitoring/compose.production.yaml` (API/PostgreSQL в сети `task-monitoring`,
   worker пишет `task-worker-metrics`); backup stack — с `compose.backup.yaml`.
3. Все targets up; во время тестового окна допустим только `TaskAlertRouteNotProduction`.
4. Боевая приёмка: webhook URL/token в `$TASK_SECRET_ROOT/monitoring/`,
   `TASK_ALERT_DELIVERY_MODE=production`, named owner, синтетическая пара
   firing/resolved на приёмнике заказчика (OPS-04, «Configure customer contact»).

### Фаза F — backup

1. Provisioning по `backup-restore-runbook.md`: `TASK_BACKUP_IMAGE` = проверенный digest
   (`outputs/20260907_task_backup_restore_1.0.0/evidence/image-id.txt`); PGDATA read-only,
   локальный репозиторий на отдельном устройстве, off-host NFS/SMB с другого хоста,
   UID/GID 1001:1001, `TASK_RECOVERY_KEYS` 0700 (три ключа 0400), `TASK_RECOVERY_INPUT`,
   immutable snapshots под отдельным storage-администратором, два escrow-хранилища.
2. `docker compose -f deployment/backup/compose.yaml up -d`; на существующем кластере —
   DBA ревизия `initialize.sql`, peer mapping, WAL-конфигурации (entrypoint не ретрофитит).
3. Проверка: `runner.py health` = exit 0 (base ≤26 ч, check ≤10 мин, drill ≤8 дней).
4. `TASK_BACKUP_VALIDATION=1` — только одноразовые test fixtures, в продакшене запрещён.

### Фаза G — клиенты Windows

1. На signing workstation: `Build-TaskDesktopRelease.ps1` (thumbprint из защищённого
   хранилища, timestamp, rollout, compatibility floors, HTTPS origin).
2. Независимая сверка `packageSha256`/подписей, malware scan, публикация ZIP/channel на
   контролируемом HTTPS origin.
3. Установка: подписанный `Install-TaskDesktop.ps1` + publisher thumbprint; обновление —
   `Invoke-TaskDesktopUpdate.ps1`; откат — `Rollback-TaskDesktop.ps1`.
4. `Task:ClientRelease:MinimumClientVersion` повышать только после публикации
   подписанного релиза; `RecommendedClientVersion` не ниже minimum (OPS-05).
5. Pilot evidence на поддерживаемом корпоративном Windows image (launch/login, update,
   restart, rollback, relaunch).

### Фаза H — итоговая приёмка

Собрать в защищённый evidence-каталог: параметры (без секретов), resolved compose,
firewall exports, port inventory (только 443), readiness receipts, `checks.json`
SEC-03, alert-пару заказчика, backup health + первый verify, pilot клиента. Секреты,
ключи, URL'ы webhook и приватные пути в evidence не попадают.

## 4. Обслуживание (календарь)

| Период | Действие | Источник |
|---|---|---|
| Непрерывно | WAL-архивация в оба репозитория; check receipts каждые 5 мин | backup-restore-runbook |
| Ежедневно, 01:00 UTC (параметр `TASK_BACKUP_HOUR_UTC`) | полная копия | backup-restore-runbook |
| Еженедельно | автоматическая PITR verification | backup-restore-runbook |
| При любом изменении конфигурации | явный `backup` после публикации стабильного recovery-input | backup-restore-runbook |
| ≥30 дней до expiry сертификатов | начать ротацию; алерты на 45/30/14/7 дней | SEC-03 |
| Ежеквартально (минимум) | ротация паролей БД; немедленно после подозрения на утечку | SEC-03 |
| По процедуре | JWT-ротация current/previous с выдержкой ≥ lifetime + skew | jwt-key-management |
| Ежеквартально + после изменений storage/ключей/PG/роста данных | drill восстановления из protected snapshot и из обоих escrow | backup-deployment-acceptance |
| После изменений monitoring/alerting | синтетическая пара firing/resolved | OPS-04 |
| После изменений сети/compose/proxy/сертификатов/PG TLS/firewall | повторный прогон OPS-02 gates | OPS-02 |

### 4.1. Обновление серверных образов и откат

1. Новый набор = проверенный релиз образов (Build-ContainerRelease → verify-release) или
   утверждённые заказчиком digest'и; ad hoc пересборка запрещена.
2. До обновления: успешный backup и verify, запись активных версий.
3. Миграции первыми: `status` → `apply` (при необходимости) → `grant-runtime.sql` →
   `status` = `Ready`. При деструктивной миграции — заранее согласованный план
   восстановления из backup (rollback-команды у мигратора нет).
4. Замена сервисов по одному: worker → api → tls-proxy; после каждого — readiness.
5. Откат: вернуть прежние digest'и в `production.env`, пересоздать сервисы; если была
   деструктивная миграция — восстановление из backup по инцидентной процедуре (раздел 6).
6. Версию `MinimumClientVersion` поднимать только после публикации соответствующего
   подписанного клиентского релиза.

### 4.2. Ротации

- Сертификаты edge/PostgreSQL и откат — SEC-03 «Rotation» (никогда не переиспользовать
  ключ; не добавлять bypass клиенту или `VerifyFull` в инциденте).
- Пароли БД — dual-principal или короткое окно по SEC-03; migration/runtime — всегда разные.
- Backup-ключи — только новый репозиторий/путь с новой генерацией, старый хранить до
  истечения retention (`backup-restore-runbook.md`, «Status, failures and maintenance»).

## 5. Инциденты и recovery

1. Обнаружение: алерты OPS-04 (или журналы, если monitoring сам повреждён).
2. Объявить инцидент, остановить запись на границе сервиса, зафиксировать UTC target,
   label, версии, ожидаемое окно потерь; сохранить evidence.
3. Восстановление БД — изолированный recovery operator: `plan` → `restore` (только
   read-only выбранный репозиторий + ключи из escrow), затем SQL/business проверки и
   restricted API smoke (`backup-restore-runbook.md`, «Point-in-time incident recovery»).
4. Cutover — только по двухстороннему решению владельца инцидента (компании-правила DNS
   и клиентского sync-reset).
5. Откаты: клиент — `Rollback-TaskDesktop.ps1`; сертификат — по SEC-03; firewall — только
   `Set-Ops02Firewall.ps1 -Action Remove`.
6. Плановые drill'ы (не инциденты) — `acceptance.py baseline/drill` по
   `backup-deployment-acceptance.md`: три проверки (secondary, protected snapshot,
   escrow B), receipt'ы, RPO ≤900 с и service-ready RTO ≤14400 с.

## 6. Сквозные stop conditions

Останавливаться (не «обходить») при: mutable image-ссылке; секрете в checkout/env/CLI;
отсутствии secrets manager; опубликованных 5432/8080; отсутствии `DOCKER-USER`;
пересечении подсетей; недоверенном CA/SAN-несовпадении; failed readiness или `status`
мигратора; отказе firewall rollback; неудачном backup/restore (риск RPO не глушится
удалением WAL); алерте, оставленном только в test mode.

## 7. Статус воспроизведения (шаги 4–5 плана HAND-02)

Оба прогона выполнены на чистом стенде (Windows 11 + Docker Desktop linux/amd64, PowerShell 7.6)
без доступа к инфраструктуре заказчика:

1. **Установка** (фазы A–D) — `deployment/ops-runbook/Invoke-OpsCleanRoomRepro.ps1`;
   сводка — `work/production/evidence/ops-runbook/install/install.json` (PASS):
   foundation-контракт, синтетические сети/DNS/firewall-plan, PostgreSQL TLS-бутстрап,
   миграции до v14, runtime grants, API/Worker/TLS-proxy с readiness, HSTS, отрицательные
   TLS-пробы, ротация сертификата, port inventory, контракт SEC-03.
2. **Обслуживание и recovery** (фазы E–G + инцидентный drill) —
   `deployment/ops-runbook/Invoke-OpsOperateRecoverRepro.ps1`;
   сводка — `work/production/evidence/ops-runbook/operate/checks.json` (PASS):
   - E: конфигурация мониторинга (19 алертов, 10 scrape jobs) и синтетическая доставка
     firing-алерта в production mode (token auth, redaction, receipts);
   - F: backup provisioning, полная копия, PITR target, три независимых восстановления
     (readonly-secondary, protected-snapshot, escrow-b) со schema/fixture-проверками;
   - Обслуживание: повторный OPS-02 прогон с ротацией edge-сертификата, отклонением
     wrong-SAN и возвратом к доверенной листовке;
   - G: подписанный релиз клиента, установка, mandatory update, отклонение tamper и
     uncontrolled downgrade, проверенный rollback;
   - Инцидент: PITR-restore из escrow B, бизнес-smoke восстановленного Task.Api
     (login/task/audit/catalog/capabilities), измеренный service-RTO 84.7 с при лимите
     14400 с (RPO ≤ 900 с — по приёмочным receipt'ам).
3. Customer-owned пункты раздела 2 на стенде заменены синтетическими аналогами и явно
   перечислены в `notCoveredHere` обеих сводок; они остаются входными данными заказчика
   и не считаются выполненными clean-room прогоном.

Финальный пакет HAND-02: `outputs/20260911_task_operations_runbook_1.0.0` (manifest, SHA-256,
validation report по правилам AGENTS.md).
