# Stage 5 — план визуального дизайна

**Продукт:** Task — Windows desktop-органайзер для локальной сети компании  
**Версия плана:** 1.0  
**Статус:** Proposed for approval  
**Основание:** Stage 4 Final Baseline 4.5 + Stage 4.6 Lite PASS  
**Входной пакет:** `Organizer_Stage5_Design_Input.zip`  
**SHA-256 входного пакета:** `F4EB84501B9047F8275B5789D57FF556ACAEED066BDC97DFAB8AB30102CC647B`

## 1. Цель этапа

Создать утверждённый визуальный baseline продукта, который:

- покрывает все 21 модуля MVP и нормативный каталог Stage 3.5;
- превращает SCR/FLOW/STATE и PRD 4.5 в однозначные макеты, компоненты и интерактивные прототипы;
- учитывает роли, capabilities, read-only/offline, ошибки, конфликты и восстановление;
- проверен на Windows accessibility и масштабировании 100–200%;
- передаётся разработке без необходимости придумывать бизнес-логику.

Stage 5 не изменяет бизнес-требования, OpenAPI, DTO, permissions, errors или MVP scope. Обнаруженное противоречие возвращается владельцу соответствующего baseline как design finding.

## 2. Объём

Нормативная поверхность:

- 21 модуль PRD;
- 132 записи Screen Catalog;
- 37 User Flows;
- роли Admin, Manager, Employee и Observer;
- нормативные STATE и их утверждённые алиасы;
- light/read-only/offline/error/conflict/empty/loading/success состояния, когда они применимы;
- keyboard, focus, accessible names/states, screen-reader semantics и non-color indicators;
- Windows scaling 100%, 125%, 150%, 175% и 200%.

Не входит в Stage 5:

- реализация desktop-клиента, backend и PostgreSQL;
- изменение технического контракта;
- создание новых функций вне MVP;
- production deployment и утверждение SLA/RPO/RTO;
- маркетинговый сайт и материалы, не относящиеся к desktop-продукту

## 4. План работ

### Stage 5.0 — запуск и инвентаризация



Работы:

1. Зафиксировать входной baseline и SHA-256.
2. Разобрать 132 SCR на уникальные страницы, панели, диалоги, overlays и shared states.
3. Сгруппировать 37 FLOW в сквозные пользовательские journeys.
4. Создать трассировочную матрицу и реестр design findings.
5. Зафиксировать формат Figma-файла, naming, версии, owners и review cadence.
6. Подтвердить решения, влияющие на визуальный scope:
   - OQ-004: аватар исключён из MVP или нужен contract change;
   - OQ-005: Notification Center + diagnostics как fallback для Windows toast;
   - OQ-006: раздельная диагностика metadata permission и Windows/SMB access;
   - OQ-009: первая поставка только RU или подготовка нескольких locales.

Артефакты:

- Stage 5 Design Brief;
- Screen/Flow Inventory;
- Design Traceability Matrix;
- Design Finding Register;
- Stage 5 schedule и RACI.

**Gate 5.0:** каждый SCR и FLOW имеет design owner, приоритет и планируемый артефакт; нет скрытого расширения MVP.

### Stage 5.1 — визуальное направление и foundation



Работы:

1. Подготовить три визуальных направления для одной и той же репрезентативной поверхности.
2. Выбрать одно направление с product owner.
3. Утвердить:
   - типографику и иерархию;
   - цветовые роли и non-color status semantics;
   - сетку, spacing, размеры и desktop density;
   - elevation, borders, radii и iconography;
   - focus, hover, pressed, selected, disabled и validation states;
   - правила масштабирования 100–200%;
   - политику light/dark theme в пределах MVP.
4. Создать foundations и design tokens.

Артефакты:

- Direction Board с тремя вариантами;
- Visual Direction Decision;
- Foundations & Tokens 1.0;
- Accessibility baseline.

**Gate 5.1:** утверждено одно направление; текст, focus и статусы проходят базовые contrast/non-color проверки; технический представитель подтверждает реализуемость.

### Stage 5.2 — design system и сквозной vertical slice



Компоненты первой очереди:

- app shell, navigation, command bar, page header;
- buttons, inputs, selectors, people picker и search;
- table/list/tree, cards, badges и pagination/virtualization patterns;
- dialog, side panel, toast, inline message и Notification Center;
- loading, empty, error, read-only, offline, conflict и recovery;
- destructive confirmation, optimistic update и permission explanation.

Vertical slice:

1. Auth/first connection.
2. Shell и Today.
3. Inbox capture.
4. Create/edit Task.
5. Global Search, включая employee results/redaction.
6. Server loss → read-only cache → recovery.
7. Optimistic conflict.

Артефакты:

- Component Library 1.0;
- responsive/high-DPI component specs;
- интерактивный prototype ключевого vertical slice;
- component-to-SCR usage map.

**Gate 5.2:** ключевой путь выполняется keyboard-only; компоненты имеют доступные имена/состояния; отсутствуют неподтверждённые API, поля и действия.

### Stage 5.3 — покрытие модулей



Рекомендуемые волны:

**Wave A — ежедневная работа**

- Today, Inbox, Tasks, Subtasks/Checklists;
- recurrence, reminders, notifications;
- Calendar.

**Wave B — совместная работа**

- Projects, members и project lifecycle;
- Files/FileLocations и SMB diagnostics;
- CRM: contacts, companies, interactions;
- comments, watchers и history.

**Wave C — lifecycle и управление**

- Search;
- Archive и Trash;
- Settings;
- Admin: users, departments, roles/permissions, sessions/devices;
- health, jobs, backups и audit surfaces.

Для каждой поверхности проектируются:

- default/happy path;
- loading/empty/partial/error;
- forbidden/hidden/disabled с нормативной причиной;
- offline/read-only/recovery, если применимо;
- destructive и irreversible actions;
- роль/capability variants без раскрытия скрытых данных;
- long content, realistic data и large-list behavior.

Артефакты:

- полный набор annotated screens;
- module prototypes для критических journeys;
- обновлённая traceability matrix;
- open design findings с владельцами.

**Gate 5.3:** 100% SCR имеют утверждённый frame, переиспользуемый component/state reference либо документированное N/A; все 37 FLOW имеют prototype или storyboard.

### Stage 5.4 — системные состояния, роли и accessibility



Проверки:

- Admin/Manager/Employee/Observer;
- hidden vs disabled vs forbidden;
- empty/loading/error/partial/unavailable;
- offline/read-only/reconnect;
- version conflict и stale notification action;
- archived/trashed/retention/legal hold;
- visible focus, deterministic focus order и focus return;
- keyboard alternatives для drag/resize и других pointer actions;
- screen-reader group/status/redaction semantics;
- status/urgency/error не передаются только цветом;
- 100–200% Windows scaling, multi-monitor и длинные русские строки.

Артефакты:

- State Coverage Matrix;
- Role/Capability Visual Matrix;
- Accessibility Review Report;
- High-DPI Review Report;
- перечень исправлений и evidence повторной проверки.

**Gate 5.4:** Critical/High accessibility и state coverage findings = 0; Medium findings либо исправлены, либо явно приняты владельцем с обоснованием.

### Stage 5.5 — usability validation и финальный prototype



Проверяемые задачи:

1. Первый вход и подключение к серверу.
2. Быстрое и полное создание задачи.
3. Назначение, смена статуса и bulk action.
4. Recurrence scope.
5. Calendar create и keyboard alternative для drag/resize.
6. Проект и участники.
7. Открытие/перепривязка файла и SMB failure.
8. Глобальный поиск.
9. Notification action.
10. Потеря сервера, read-only и восстановление.
11. Optimistic conflict.
12. Archive/Trash restore.
13. Критические admin actions.

Участники: представители минимум трёх рабочих ролей плюс отдельный admin reviewer. Для каждой задачи фиксируются completion, critical errors, assists, severity и решение.

Артефакты:

- test script и realistic fixture set;
- usability findings;
- исправленный финальный prototype;
- validation summary.

**Gate 5.5:** нет Critical/High usability blockers; критические journeys завершаются без объяснения бизнес-логики модератором.

### Stage 5.6 — финальный аудит и handoff



Работы:

1. Заморозить Figma baseline и версию component library.
2. Проверить полноту SCR/FLOW/STATE/role traceability.
3. Добавить размеры, spacing, tokens, interaction, validation и accessibility annotations.
4. Подготовить asset/icon/font inventory с лицензиями.
5. Зафиксировать open production-policy items отдельно от design defects.
6. Провести совместный review design + product + development + QA.
7. Сформировать финальный пакет, manifest, SHA-256 и validation report.

Артефакты:

- Stage 5 Final Visual Baseline;
- Design System 1.0;
- Interactive Prototype 1.0;
- Design Traceability Matrix;
- Accessibility & High-DPI Validation;
- Development Handoff;
- Design Decision Log;
- Finding Register;
- manifest, version, SHA-256 и package validation.

**Gate 5.6 / допуск к разработке:**

- 100% нормативных SCR/FLOW/STATE имеют design evidence;
- Critical/High/Medium design findings = 0 либо Medium формально приняты и не требуют от разработчика придумывать логику;
- нет неизвестных permissions/errors/DTO fields/API operations;
- keyboard, focus, screen-reader, non-color и 100–200% scaling подтверждены;
- product owner, design owner, desktop tech lead и QA подписали handoff;
- финальный пакет фактически проверен и воспроизводимо открывается.

## 5. Контрольные точки

| Неделя | Контрольная точка | Решение |
|---|---|---|
| 1 | Gate 5.0 | Scope и трассировка утверждены |
| 2 | Gate 5.1 | Выбрано визуальное направление |
| 3–4 | Gate 5.2 | Design system и vertical slice готовы |
| 5–8 | Gate 5.3 | Модули и flows покрыты |
| 7–9 | Gate 5.4 | States, roles, accessibility и DPI проверены |
| 9–10 | Gate 5.5 | Usability blockers закрыты |
| 10–12 | Gate 5.6 | Visual baseline передан разработке |

Таблица отражает базовый сценарий для одного lead designer. При параллельной работе двух дизайнеров Wave A–C допускается сжать, но Gate 5.1 и единый component library остаются общими.

## 6. Приоритеты

**P0 — до начала массовой отрисовки:** visual direction, tokens, shell, accessibility rules, traceability.  
**P1 — до первого dev sprint:** auth, shell, Today, Inbox, Tasks, Search, offline/read-only/conflict и базовые компоненты.  
**P2 — до разработки соответствующих модулей:** Calendar, Projects, Files, CRM, Notifications.  
**P3 — до production-complete MVP:** Archive, Trash, Settings, Admin и operational surfaces.

Разработку foundation и P1 можно начинать после Gate 5.2 при условии, что утверждённые компоненты и контракт не меняются. Полный Stage 5 закрывается только после Gate 5.6.

## 7. Риски и управление ими

| Риск | Последствие | Мера |
|---|---|---|
| Попытка рисовать все SCR как уникальные экраны | Перерасход и расхождение паттернов | Инвентаризация и component/state reuse в Stage 5.0 |
| Массовая отрисовка до утверждения foundation | Дорогая переделка | Gate 5.1 до Wave A–C |
| Дизайнер придумывает отсутствующую логику | Нарушение baseline | Finding Register и возврат владельцу PRD/UX/API |
| Роли проверяются только на happy path | Утечки действий/данных | Role/Capability Visual Matrix |
| Accessibility откладывается на финал | Переделка компонентов | Проверки в 5.1–5.2 и полный аудит в 5.4 |
| Figma выглядит хорошо, но не реализуема в Windows/WPF | Разрыв с разработкой | Desktop tech review на каждом gate |
| Нереалистичные данные скрывают проблемы плотности | Срывы после интеграции | Production-like fixtures, long strings и large lists |
| OQ смешиваются с design defects | Неясный допуск | Отдельные реестры design findings и production-policy items |

## 8. Definition of Done Stage 5

Stage 5 считается завершённым только когда:

1. Выполнен Gate 5.6.
2. Есть утверждённый редактируемый визуальный baseline и интерактивный prototype.
3. Есть design system с tokens, компонентами, состояниями и accessibility annotations.
4. Полнота SCR/FLOW/STATE/role подтверждена трассировкой.
5. Критические сценарии проверены на realistic data.
6. Critical/High design findings отсутствуют.
7. Открытые production-policy вопросы не маскируются под завершённый design.
8. Development Handoff подписан.
9. Финальный пакет содержит manifest, версию, SHA-256 и фактический validation report.

## 9. Ближайшее действие

Запустить Stage 5.0: создать Screen/Flow Inventory и Design Traceability Matrix из утверждённого пакета `Organizer_Stage5_Design_Input.zip`, затем вынести четыре design-impact решения OQ-004/005/006/009 на короткий kickoff.
