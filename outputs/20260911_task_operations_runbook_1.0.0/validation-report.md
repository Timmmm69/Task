# Validation report — HAND-02 operations runbook 1.0.0

Дата: 2026-09-11. Ревизии: base `9dcb53c3769e8b270350dd6cff1e48da98d640c9`,
validated `03270ae` (см. manifest.json). Все SHA-256 в manifest/SHA256SUMS сверены
с файлами рабочей копии; расхождений нет.

## 1. Что проверено

1. **Gap-анализ и runbook.** Gap-анализ сводит девять операционных документов с
   требованиями HAND-02 и фиксирует шесть пробелов (G1–G6). Runbook ссылается только на
   проверенные пакеты `outputs/` и фиксирует customer inputs с владельцами, календарь
   обслуживания, порядок обновления/отката серверных образов и сквозные stop conditions.
2. **Установка на чистом стенде** (`Invoke-OpsCleanRoomRepro.ps1`,
   `evidence/ops-runbook/install/install.json`, результат PASS, 2026-09-11T12:59Z):
   - проверка digest'ов девяти образов (release 0.6.0 image-map + инфраструктурные пины);
   - gate `ops02-foundation` PASS — контракт параметров, resolved compose, firewall plan;
   - gate `ops02-runtime-foundation` PASS — DNS, сети, загрузка образов;
   - gate `ops02-clean-room` PASS — два независимых DinD-прогона: PostgreSQL TLS-бутстрап,
     миграции до v14, runtime grants, HTTPS readiness, HSTS, отклонение untrusted CA /
     wrong-SAN / TLS 1.1, ротация edge-сертификата, port inventory (только 443);
   - gate `sec03-contract` PASS — контракт производственного secret-слоя.
3. **Обслуживание и recovery** (`Invoke-OpsOperateRecoverRepro.ps1`,
   `evidence/ops-runbook/operate/checks.json`, результат PASS, 2026-09-11T13:38Z):
   - gate `ops04-monitoring-alert-delivery` PASS — 19 алертов / 10 scrape jobs, синтетическая
     доставка firing-алерта в production mode: token auth, redaction секретов, receipts;
   - gate `backup-pitr-restore-and-recovery-drills` PASS — provisioning, полная копия, PITR
     target, три независимых восстановления (readonly-secondary, protected-snapshot,
     escrow-b) с проверкой schema SHA-256 и fixture; восстановленный Task.Api проходит
     business smoke (login/task/audit/catalog/capabilities); измеренный service-RTO 84.7 с
     при лимите 14400 с, RPO-лимит 900 с по receipt'ам;
   - gate `ops02-maintenance-rerun-certificate-rotation` PASS — повторный OPS-02 прогон
     после изменения конфигурации: ротация edge-сертификата меняет thumbprint, wrong-SAN
     листовка отклоняется и происходит возврат к доверенной;
   - gate `ops05-client-update-rollback` PASS — подписанная сборка 1.0.0/1.1.0, установка,
     mandatory update, fail-closed локального канала, отклонение tamper и uncontrolled
     downgrade, проверенный rollback, валидность publisher-пинов установленных релизов.

## 2. Ограничения synthetic-прогона

Корпоративный secrets manager/CA/DNS, реальные устройства хранения, alert webhook,
code-signing цепочка и корпоративные Windows-образы не используются; это входные данные
заказчика, перечисленные в `notCoveredHere` обеих сводок и в разделе 2 runbook. Хост-firewall
live apply проверен ранее (`work/production/evidence/ops02-linux-firewall`).

## 3. Dashboard и пакет

HAND-02 в `.project-dashboard/roadmap.json`: status `done`, progress 100, evidence — сводки
двух прогонов. `validate.mjs` — valid (items 40, overall 91.49); `recalculate-order.mjs`
выполнен (npm run dashboard:order). Пакет соответствует правилам AGENTS.md: VERSION,
manifest.json, SHA256SUMS, validation report.
