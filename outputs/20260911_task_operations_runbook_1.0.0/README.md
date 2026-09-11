# HAND-02 — единый operations runbook: пакет 1.0.0

Пакет закрывает HAND-02 «Другой инженер может развернуть и обслуживать систему».

Состав поставки:

- `work/production/docs/OPS-operations-gap-analysis.md` — gap-анализ покрытия HAND-02 по
  OPS-02/03/04/05, SEC-03, migrator и container foundation; пробелы и версионные замечания.
- `work/production/docs/OPS-operations-runbook.md` — сквозной runbook «инженер с нуля»:
  prepare (customer inputs), deploy (фазы A–D), operate (фазы E–G, календарь, ротации,
  обновление/откат), recover (раздел 5), stop conditions.
- `work/production/deployment/ops-runbook/Invoke-OpsCleanRoomRepro.ps1` — воспроизводимая
  установка на чистом стенде (синтетические CA/DNS/secrets; только проверенные digest-образы).
- `work/production/deployment/ops-runbook/Invoke-OpsOperateRecoverRepro.ps1` — воспроизводимые
  обслуживание и recovery: alert delivery, backup/PITR/restore drills, ротация сертификата,
  обновление/rollback клиента, инцидентный drill.
- `work/production/evidence/ops-runbook/install/install.json` — PASS: фазы A–D + контракт SEC-03.
- `work/production/evidence/ops-runbook/operate/checks.json` — PASS: четыре gate
  (мониторинг, backup/recovery, ротация, клиент) с детальными receipt'ами.

Customer-owned входные данные (корпоративный CA/DNS/secrets manager, реальные хранилища,
alert webhook, code-signing) на стенде заменены синтетическими аналогами и явно перечислены
в `notCoveredHere` обеих сводок — они не считаются выполненными чисто-стендовым прогоном.
