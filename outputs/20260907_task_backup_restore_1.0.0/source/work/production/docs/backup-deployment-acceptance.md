# OPS-03 — проверка развёртывания, 0.6.1

Локальный тест подтверждает код. Защита данных компании подтверждается отдельными проверками ниже на её оборудовании. Приёмочный инструмент не выдаёт productionAccepted=true: read-only mount и введённый идентификатор snapshot не доказывают независимость дисков, неизменяемость NAS или физическое хранение ключей.

## Подготовка инфраструктуры

Ответственный фиксирует основной сервер и устройство PGDATA, отдельное устройство local repository, другой сервер и NFS/SMB export secondary repository, ёмкость для ежедневных full copies и WAL за 30/366 дней, владельца и политику immutable/offline snapshots. Backup-оператор не должен иметь права изменять эту политику или удалять защищённые snapshots. Проверить штатными средствами хранилища и сохранить отчёт ACL/retention. Запрет записи в recovery-контейнере сам по себе immutable storage не обеспечивает.

Зафиксировать два разных места хранения escrow, ответственных и разрешённый доступ. Каждая копия должна независимо дать repo1-key, repo2-key и assets-key выбранной исторической генерации. Использовать корпоративный secret manager/носитель; значения ключей и их хеши не включать в отчёт. Не использовать существующий каталог ключей основного сервера вместо retrieval из escrow.

Согласовать представительный набор данных и нагрузку: объём базы, задачи, календарь, пользователи, история/audit, изменения WAL во время резервирования. Зафиксировать минимальный размер backup в байтах; minimum-database-bytes не заменяет подтверждение представительности. Физические файлы сотрудников копирует отдельная корпоративная система, с отдельным RPO и сверкой ссылок после восстановления.

Использовать образ, собранный и проверенный Test-BackupRestore.ps1; TASK_BACKUP_IMAGE закрепить на его sha256/digest. Инструменты рассчитаны на PostgreSQL 16 и текущие миграции Task. Ничего не устанавливать и не мигрировать поверх боевой базы во время проверки.

## Эталон и моделируемый инцидент

До инцидента на основном экземпляре выполнить:

```sh
docker compose -f work/production/deployment/backup/compose.yaml exec -T postgres python3 /opt/task-backup/acceptance.py baseline
```

Сохранить JSON и result.schemaSha256 вместе с версией приложения и каталогом миграций. Команда только читает схему и проверяет чтение iam.user_accounts, work.tasks, calendar.events и governance.audit_entries. Fingerprint включает столбцы, типы, nullability, defaults, constraints и индексы шести схем приложения. Это структурная проверка, не полная эквивалентность всех функций/данных БД и не HTTP/login-тест.

Во время утверждённой нагрузки записать известную транзакцию через приложение, её подтверждённое время и ожидаемый результат. Зафиксировать UTC target и incidentAt; target должен быть после завершения выбранного full backup, не позднее incidentAt и не более чем за 900 секунд до него. Убедиться, что WAL после target заархивирован. Начать отсчёт инцидента до retrieval escrow и подготовки резервного сервера. Не менять incidentAt при повторном запуске, чтобы скрыть время подготовки. Строгий acceptance CLI рассчитан на текущий drill в четырёхчасовом окне; для более старого инцидента использовать runner.py plan/restore и отдельный протокол измерений.

## Автономный recovery operator

Отдельный compose.recovery.yaml не требует TASK_PGDATA, TASK_LOCAL_BACKUP, TASK_OFFHOST_BACKUP и TASK_RECOVERY_INPUT. Он монтирует только выбранный secondary repository/snapshot и независимо полученные ключи, оба read-only. Отсутствующий bind path не создаётся автоматически. Production socket, сеть и Docker socket контейнеру не передаются.

На резервном Docker Linux host задать TASK_BACKUP_IMAGE, TASK_RECOVERY_REPOSITORY и TASK_RECOVERY_KEYS. Пути относятся к этому host. UID/GID оператора 1001:1001, каталог ключей 0700, файлы ключей 0400. Для каждого retrieval использовать новый compose project name и новые служебные volumes:

```sh
docker compose -p task-drill-escrow-a -f work/production/deployment/backup/compose.recovery.yaml up -d
docker compose -p task-drill-escrow-a -f work/production/deployment/backup/compose.recovery.yaml exec -T recovery-operator python3 /opt/task-backup/acceptance.py drill --label "$BACKUP_LABEL" --target "$TARGET_UTC" --incident-at "$INCIDENT_UTC" --dataset-id "$DATASET_ID" --storage-copy-id "$STORAGE_COPY_ID" --escrow-copy-id escrow-a --minimum-database-bytes "$MINIMUM_DATABASE_BYTES" --expected-schema-sha256 "$SCHEMA_SHA256" --scope company
docker compose -p task-drill-escrow-a -f work/production/deployment/backup/compose.recovery.yaml cp recovery-operator:/var/lib/task-backup/acceptance ./acceptance-escrow-a
```

Проверка выполняет настоящее PITR, AES-GCM-проверку recovery assets, pg_amcheck, сверку schema fingerprint и SQL smoke. Успех возвращает exit 0; отказ, превышение бюджета, недостаточный размер и mismatch дают ненулевой код. JSON содержит метаданные и измерения, но не строки бизнес-данных. Журнал receipt сохраняется в state/acceptance с уникальным именем. Операции блокируются общим operation.lock. Успешный drill останавливает временную PostgreSQL и удаляет её данные и расшифрованный архив. При ошибке остановки каталог сохраняется для диагностики, успех не выдаётся.

Выполнить три независимых проверки:

1. Read-only secondary repository с недоступным основным сервером и escrow A.
2. Защищённый snapshot/offline copy с недоступным live repository и escrow A.
3. Тот же защищённый snapshot в новом recovery project и ключами, независимо полученными из escrow B.

Для второго и третьего запуска изменить TASK_RECOVERY_REPOSITORY/TASK_RECOVERY_KEYS и project name. Идентификаторы в отчёте — ссылки на внешние доказательства, а не автоматическое подтверждение разных носителей. Для сохранения восстановленного экземпляра под последующие business/API проверки использовать runner.py plan/restore в этом же изолированном operator; acceptance drill очищает свою временную копию.

После экспорта receipt остановить свой project командой docker compose с теми же -p и -f и действием down. State/work volumes сохраняются; удаление — только конкретных volumes завершённого drill по политике инцидентов. Не выполнять глобальный prune и не удалять primary/repository volumes.

## Измерения и окончательная приёмка

requestedLossWindowSeconds — разница incidentAt и успешно достигнутого target, максимум 900 секунд. Дополнительно проверить наличие ожидаемой подтверждённой транзакции и отсутствие более позднего изменения в восстановленном приложении: этот бизнес-факт CLI самостоятельно не знает.

incidentToDatabaseReadySeconds включает подготовку до запуска drill, восстановление и SQL smoke. restoreAndDatabaseSmokeSeconds — длительность операции. Четырёхчасовой предел CLI не равен RTO готового сервиса: в общий RTO также входят развёртывание соответствующей версии API, restricted readiness, login, запрос задачи, audit, metadata каталога и утверждённое переключение сервиса. Зафиксировать serviceReadyAt и проверить serviceReadyAt − incidentAt ≤ 14400 секунд. SQL smoke не подменяет отсутствующие или ещё не подключённые HTTP/business сценарии.

Общий лимит TASK_RESTORE_TIMEOUT_SECONDS (1–14400, default 14400) разделяется между physical restore, replay, pg_amcheck и offline checks. Двухминутного ограничения на WAL replay больше нет. Остановка и cleanup имеют отдельное ограниченное время. Родительский BackupAgent сохраняет общий CommandTimeoutSeconds; расписание и capacity подбирать по измерениям обеих копий.

Для окончательного допуска приложить три receipt, проверенный image digest, эталон схемы, характеристики workload, отчёт независимости/retention/ACL, протокол retrieval двух escrow, бизнес-проверки и полный RTO. Проверить доставку тестового backup failure/overdue alert ответственному через OPS-04. Указать ответственных и решение владельца инцидента; боевой cutover следует существующему двухстороннему согласованию. До этих доказательств OPS-03 остаётся in_progress.

Повторять проверку protected snapshot и escrow ежеквартально, после изменения storage policy, ключей, версии PostgreSQL или существенного роста данных. Автоматическая weekly PITR verification работает отдельно.
