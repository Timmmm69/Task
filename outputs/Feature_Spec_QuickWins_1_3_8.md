# ТЗ: Быстрые UX-победы — фичи 1, 3, 8
**Версия:** 1.1  
**Дата:** 2026-08-12  
**Область:** прототип (визуальный бейзлайн Stage 5.6), WPF-клиент (продакшен)  
**Статус:** черновик ТЗ (исправлен после аудита)

---

## 0. Общие положения

### 0.1. Область действия

Документ описывает реализацию трёх фич для десктопного органайзера Task:

| # | Фича | Приоритет | Трудозатраты (оценка) |
|---|------|-----------|------------------------|
| 1 | Кнопка «Перенести всё просроченное на сегодня» | P1 | 2–4 ч |
| 2 | Закрепление задач (Pin) | P1 | 4–8 ч |
| 3 | Итог дня (End-of-Day Summary) | P1 | 3–5 ч |

### 0.2. Нормативная база

- Визуальный бейзлайн: `work/stage_5_6_final_visual_baseline_and_handoff/`
- Design System 1.0, 45 component families
- Direction 2 (Timeline Planner), токены: `Foundations_Tokens_Direction_2_0.1.md`
- Прототип: React 19.2, Vite 6.4.2, Fluent UI Icons 2.0.334, CSS custom properties
- API: Stage 2.3 normative contract (OpenAPI 3.1.0)

### 0.3. Принципы реализации

1. Не нарушать существующую архитектуру и токены.
2. Все новые компоненты наследуют паттерны из замороженного Design System 1.0.
3. Цвет никогда не несёт смысл один — всегда в паре с иконкой, текстом, позицией.
4. Каждое состояние должно быть проработано: default, hover, focus, disabled, loading, empty, error, offline, conflict.
5. Новые иконки — только из `@fluentui/react-icons` (v2.0.334+). Никаких эмодзи, CSS-рисунков, самодельных SVG.
6. Минимальная цель касания: 38 px (компакт), 44 px (первичная навигация).
7. Фокус: 2 px `var(--focus)`, outline-offset 2 px.

---

## 1. Фича 1: Кнопка «Перенести всё просроченное на сегодня»

### 1.1. Мотивация

Каждое утро пользователь видит 5–20 просроченных задач. Вручную открывать каждую, менять дедлайн, сохранять — рутина на 2–5 минут. Одна кнопка устраняет это трение полностью.

### 1.2. Пользовательский сценарий

1. Пользователь открывает раздел «Сегодня».
2. В блоке «Несрочные и просроченные» видит просроченные задачи с красной меткой.
3. Справа от заголовка секции — кнопка: **«Перенести просроченные (3)»**.
4. Пользователь нажимает кнопку.
5. Кнопка переходит в состояние загрузки (спиннер + «Переносим…»).
6. Сервер выполняет перенос: `deadlineAt` всех просроченных задач не-терминального статуса выставляется на `сегодня 23:59:59 UTC`.
7. Кнопка на 2 секунды показывает результат: «✓ Перенесено: 3. Ошибок: 0».
8. Список задач обновляется — просроченные перемещаются в разнесённые или без времени на сегодня.
9. Если просроченных нет — кнопка скрыта.

### 1.3. Размещение в UI

**Расположение:** в заголовке секции «Несрочные и просроченные», справа, на одной линии с названием секции и счётчиком. Заголовок секции — `<button class="section-heading">`, внутрь добавляется `<span class="section-heading__actions">` с кнопкой.

```
┌─────────────────────────────────────────────────────────┐
│ Несрочные и просроченные (5)    [Перенести просроченные (3)] │
├─────────────────────────────────────────────────────────┤
│ 🔴 Проверить инциденты поддержки      Высокая  Просрочено │
│    Проект: Техподдержка                                   │
│ ...                                                       │
└─────────────────────────────────────────────────────────┘
```

**Альтернативное размещение** (для WPF): кнопка может дублироваться в command bar раздела «Сегодня» как иконка-кнопка с тултипом.

### 1.4. Визуальная спецификация

**Вариант кнопки:** `.button--secondary` (обводка `#c8c6c4`, белый фон).

**Состояния:**

| Состояние | Вид |
|-----------|-----|
| **Default** | Текст: «Перенести просроченные (N)». Иконка: `ArrowUndoRegular` (уже импортирована). Цвет текста: `var(--text)`. |
| **Hover** | Фон: `#f3f2f1`. Курсор: pointer. |
| **Focus** | outline: 2px `var(--focus)`, offset 2px. |
| **Disabled / 0 просроченных** | Кнопка скрыта. |
| **Offline** | Кнопка скрыта. (Данные сервера недоступны, перенос невозможен.) |
| **Loading** | Текст: «Переносим…». Иконка: `ArrowSyncRegular` с `className="icon-spin"`. Кнопка disabled. |
| **Success (2 сек)** | Текст: «✓ Перенесено: 3». Цвет: `var(--green)`. Иконка: `CheckmarkCircleRegular`. Затем кнопка исчезает (список обновлён, просроченных нет). |
| **Partial (2 сек)** | Текст: «⚠ Перенесено: 2. Ошибок: 1». Иконка: `WarningRegular`. Цвет: `var(--amber)`. Кнопка остаётся (оставшиеся просроченные видны). |
| **Error** | Возврат в default + inline alert под кнопкой: «Не удалось перенести задачи. [Повторить]». Иконка: `WarningRegular`, цвет `var(--red)`. |

**Размеры:**
- Высота: 38 px (минимальная цель касания по Design System §CMP-control-height-compact).
- Паддинг: 0 12 px.
- Шрифт: 13 px, weight 400.
- Иконка: 16–18 px, слева от текста.

### 1.5. Модель данных

**Существующие поля (без изменений):**
- `deadlineAt` (string | null, UTC date-time) — поле задачи, которое будет обновлено.
- `status` — проверяется, что задача не в терминальном состоянии (`completed`, `cancelled`).

**Логика фильтрации просроченных на клиенте:**
```
isOverdue = deadlineAt !== null
         && new Date(deadlineAt).getTime() < Date.now()
         && status !== 'completed'
         && status !== 'cancelled'
```

**Новое значение deadlineAt при переносе:**
```
newDeadline = todayEndOfDayUTC()
// = начало сегодняшнего дня в UTC + 23:59:59
// = floor(now UTC / day) + 23:59:59
```

### 1.6. API-контракт

**Новый endpoint:** `POST /api/v1/tasks/bulk-reschedule-overdue`

```json
// Запрос:
{
  "targetDeadline": "2026-08-12T20:59:59Z",
  "idempotencyKey": "uuid-v7"
}

// Ответ 200 (все успешно):
{
  "rescheduled": 3,
  "failed": 0,
  "items": [
    { "taskId": "uuid", "newVersion": 15 },
    { "taskId": "uuid", "newVersion": 8 }
  ]
}

// Ответ 207 (частичный успех):
{
  "rescheduled": 2,
  "failed": 1,
  "items": [
    { "taskId": "uuid", "newVersion": 15, "status": "succeeded" },
    { "taskId": "uuid", "newVersion": null, "status": "failed", "code": "VERSION_CONFLICT" }
  ]
}
```

**Поведение сервера:**
- Выбирает все задачи текущего пользователя, где `deadlineAt < now UTC` и `status NOT IN ('completed', 'cancelled')`.
- Пакетно обновляет `deadlineAt` на `targetDeadline`.
- Per-item optimistic concurrency: если отдельная задача изменилась — она попадает в failed, остальные обрабатываются.
- Общее: transaction per item, не единая атомарная транзакция.
- Лимит: до 500 задач за вызов.
- Право: `Task.Update`.
- Аудит: на каждую изменённую задачу пишется запись `TaskDeadlineChanged`.

**Для прототипа** (без реального сервера):
- Клиентская функция `rescheduleOverdueApi(targetDeadline)` имитирует ответ через `setTimeout` 600–800 мс.
- Возвращает мок-результат: все задачи из `unscheduled`, у которых `due.includes("Просрочено")`, получают `due = "Сегодня"`.
- Одна задача из пяти искусственно фейлится (для демонстрации partial state).

### 1.7. Логика в прототипе (React)

**State-переменные:**
```js
const [rescheduleState, setRescheduleState] = useState('idle');
// 'idle' | 'loading' | 'success' | 'partial' | 'error'
const [rescheduleResult, setRescheduleResult] = useState(null);
// { rescheduled: number, failed: number }
```

**Вспомогательная функция:**
```js
function todayEndOfDayUTC() {
  const now = new Date();
  return new Date(Date.UTC(
    now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate(),
    23, 59, 59, 0
  )).toISOString();
}
```

**Функция-обработчик:**
```js
async function handleRescheduleOverdue() {
  if (!isWritable) return; // offline guard

  setRescheduleState('loading');

  try {
    const result = await rescheduleOverdueApi(todayEndOfDayUTC());
    setRescheduleResult(result);

    if (result.failed === 0) {
      setRescheduleState('success');
      // Обновить due всех просроченных на "Сегодня"
      setUnscheduled(prev => prev.map(task => {
        if (task.due?.includes('Просрочено')) {
          return { ...task, due: 'Сегодня' };
        }
        return task;
      }));
    } else {
      setRescheduleState('partial');
      // Обновить только успешные
      const failedIds = new Set(result.items
        .filter(i => i.status === 'failed')
        .map(i => i.taskId));
      setUnscheduled(prev => prev.map(task => {
        if (task.due?.includes('Просрочено') && !failedIds.has(task.id)) {
          return { ...task, due: 'Сегодня' };
        }
        return task;
      }));
    }

    // Автосброс success/partial через 3 секунды
    setTimeout(() => {
      setRescheduleState('idle');
      setRescheduleResult(null);
    }, 3000);
  } catch {
    setRescheduleState('error');
  }
}
```

**Рендер иконки в loading-состоянии:**
```jsx
{rescheduleState === 'loading'
  ? <ArrowSyncRegular className="icon-spin" aria-hidden="true" />
  : <ArrowUndoRegular aria-hidden="true" />
}
```

**Вычисление счётчика просроченных:**
```js
const overdueCount = unscheduled.filter(t => t.due?.includes('Просрочено')).length;
```

**Условие видимости кнопки:**
```js
const showRescheduleButton = isWritable && overdueCount > 0;
```

### 1.8. Accessibility

- Кнопка имеет `aria-label="Перенести все просроченные задачи на сегодня. Количество: 3"`.
- В состоянии loading: `aria-busy="true"`, кнопка disabled.
- Результат (success/partial/error) дублируется в `aria-live="polite"` регионе рядом с кнопкой.
- Иконки декоративные: `aria-hidden="true"`.
- После успешного переноса фокус остаётся на кнопке (или переходит к первой оставшейся просроченной задаче).

### 1.9. План реализации (прототип)

| Шаг | Что сделать | Файлы |
|-----|------------|-------|
| 1.1 | Импортировать иконки `ArrowSyncRegular`, `WarningRegular` | `App.jsx` |
| 1.2 | Добавить state: `rescheduleState`, `rescheduleResult` | `App.jsx` |
| 1.3 | Вычислить `overdueCount` из `unscheduled` | `App.jsx` |
| 1.4 | Добавить кнопку в заголовок секции (через `.section-heading__actions`) | `App.jsx` |
| 1.5 | Реализовать `todayEndOfDayUTC()`, `handleRescheduleOverdue()`, `rescheduleOverdueApi()` | `App.jsx` |
| 1.6 | Добавить CSS-анимацию спиннера (`@keyframes spin` + `.icon-spin`) | `styles.css` |
| 1.7 | Стилизовать состояния кнопки: `loading`, `success`, `partial`, `error` | `styles.css` |
| 1.8 | Добавить `aria-live` регион для результатов | `App.jsx` |

---

## 2. Фича 2: Закрепление задач (Pin)

### 2.1. Мотивация

Пользователь ежедневно работает с 3–7 ключевыми задачами среди 50+ в системе. Без закрепления они теряются в общем списке. Pin — это персональный флаг «это важно именно мне прямо сейчас», не зависящий от даты, приоритета или проекта.

### 2.2. Пользовательский сценарий

1. Пользователь открывает задачу в инспекторе (правая панель «Сегодня» или список «Мои задачи»).
2. Видит иконку булавки `PinRegular` в строке заголовка задачи.
3. Нажимает на иконку — булавка становится закрашенной `PinFilled` (цвет `var(--blue)`).
4. В **не-временны́х списках** (Unscheduled, Мои задачи, задачи проекта, результаты поиска) задача поднимается вверх.
5. В **временно́й сетке** (timed agenda Today, Календарь) pin даёт только визуальное выделение (фон `--blue-soft`), без пересортировки — временная позиция сохраняется.
6. Повторное нажатие — открепляет. Иконка возвращается к `PinRegular` (цвет `var(--subtle)`).
7. Закреплённые задачи в списках отображаются сгруппированными вверху, отделённые тонким разделителем от остальных.

**Где работает:**
- Не-временны́е списки: «Несрочные и просроченные», таблица «Мои задачи», список задач внутри проекта, результаты поиска. *(Pin → пересортировка вверх.)*
- Временны́е секции: разнесённые по времени задачи в повестке «Сегодня». *(Pin → только фон `--blue-soft`, позиция в сетке не меняется.)*

**Где НЕ работает:**
- Календарь (CalendarSurface) — требует отдельного дизайна для pin в сетке.
- Архив / Корзина.

### 2.3. Размещение в UI

**В карточке инспектора (правая панель «Сегодня»):**

```
┌────────────────────────────────────────┐
│ 📌 Подготовить сводный анализ      [✏️] │  ← pin слева от заголовка
│                                        │
│ ▶ В работе    Приоритет: Средняя       │
│ Срок: Сегодня                          │
│ ...                                    │
└────────────────────────────────────────┘
```

**В строке задачи (не-временно́й список «Несрочные и просроченные»):**

```
┌─────────────────────────────────────────────────────────┐
│ 📌 Проверить инциденты поддержки      Высокая  Просрочено │  ← pin слева
│    Проект: Техподдержка                                   │
│ 📌 Обновить регламент                  Средняя  Нет срока │
│    Проект: Внутренние процессы                            │
│ ─── закреплённые / остальные ─────────────────────────── │  ← разделитель
│    Заказать канцелярию в офис          Средняя  Нет срока │
│    Архивировать старые отчёты          Низкая   Нет срока │
└─────────────────────────────────────────────────────────┘
```

**В таблице «Мои задачи»:**
- Колонка Pin (первая, 36 px, без заголовка) — иконка во всю строку, кликабельная.
- Закреплённые строки визуально выделены фоном `var(--blue-soft)` (#EAF3FF).

**В timed-секции (повестка «Сегодня»):**
- Pin-иконка в строке задачи (слева от статуса/времени).
- Строка имеет фон `var(--blue-soft)`, но позиция в сетке определяется временем.
- Разделителя между pinned/unpinned нет.

### 2.4. Визуальная спецификация

**Иконка Pin (16 px):**

| Состояние | Иконка | Цвет | Поведение |
|-----------|--------|------|-----------|
| Не закреплена | `PinRegular` (Fluent UI) | `var(--subtle)` (#8A8886) | При наведении: `var(--muted)` (#605E5C) |
| Закреплена | `PinFilled` (Fluent UI) | `var(--blue)` (#0F6CBD) | При наведении: `var(--blue-strong)` (#005A9E) |
| Offline / read-only | `PinRegular` или `PinFilled` | Как выше, но `cursor: default` | Клик игнорируется |

**Кнопка pin:**
- Класс: `.pin-button` — 28×28 px область касания, прозрачный фон, border-radius 4px.
- При фокусе: outline 2px `var(--focus)`, offset 2px.
- `aria-label`: «Закрепить задачу» / «Открепить задачу».
- `aria-pressed`: `true` / `false`.

**Строка закреплённой задачи:**
- Фон: `var(--blue-soft)` (#EAF3FF) — во всех списках и timed-секциях.
- В инспекторе: без изменения фона.

**Разделитель между закреплёнными и обычными (только в не-временны́х списках):**
- Элемент: `<hr>` или `<div>` высотой 1 px.
- Цвет: `var(--border)` (#E1DFDD).
- Отступы: 8 px сверху и снизу.
- Видим только когда есть и закреплённые, и обычные задачи в списке.

### 2.5. Модель данных

**Единственный источник истины:** `pinnedTaskIds` — `Set<string>` (ID задач).

Поле `pinned` в объектах моделей задач **не используется** — оно избыточно и ведёт к рассинхронизации. Пин определяется исключительно фактом присутствия `taskId` в Set.

**Инициализация (прототип):**
```js
const [pinnedTaskIds, setPinnedTaskIds] = useState(new Set(["incident"]));
```

**В продакшене (серверная модель):**

Поле в `UserPreferences` (рекомендация):
```sql
-- user_preferences.pinned_task_ids UUID[]
```

```json
// PATCH /api/v1/me/preferences
{
  "pinnedTaskIds": ["uuid1", "uuid2", "uuid3"]
}
```

Массив в preferences, потому что pin — персональная настройка, не требующая отдельного аудита.

**Для прототипа:**
```js
function togglePin(taskId) {
  setPinnedTaskIds(prev => {
    const next = new Set(prev);
    if (next.has(taskId)) {
      next.delete(taskId);
    } else {
      next.add(taskId);
    }
    return next;
  });
}

function isPinned(taskId) {
  return pinnedTaskIds.has(taskId);
}
```

**Сортировка не-временно́го списка:**
```js
const sortedUnscheduled = useMemo(() => {
  return [...unscheduled].sort((a, b) => {
    const aPinned = isPinned(a.id);
    const bPinned = isPinned(b.id);
    if (aPinned && !bPinned) return -1;
    if (!aPinned && bPinned) return 1;
    return 0; // внутри группы сохраняется исходный порядок
  });
}, [unscheduled, pinnedTaskIds]);
```

**Для timed-секции — без сортировки:**
```js
// timed-задачи НЕ сортируются по pin. Только визуальное выделение.
{scheduledTodayTasks.map(task => (
  <button className={`agenda-row ${isPinned(task.id) ? 'pinned-row' : ''}`} ...>
    <PinIcon taskId={task.id} />
    ...
  </button>
))}
```

### 2.6. Состояния и крайние случаи

| Ситуация | Поведение |
|----------|-----------|
| Нет закреплённых задач | Разделитель не показывается. Все задачи отображаются как обычно. |
| Все задачи закреплены | Разделитель не показывается. Все с фоном `--blue-soft`. |
| Offline | Pin-кнопка отображается, но disabled. Попытка клика — без эффекта (read-only режим). |
| Задача удалена / в архиве | ID удаляется из `pinnedTaskIds` автоматически при следующей синхронизации. |
| Задача в корзине | ID удаляется из `pinnedTaskIds`. |
| Конфликт версий при сохранении preferences | Стандартный ConflictDialog. Пользователь выбирает: перезаписать или перезагрузить. |
| Быстрый двойной клик по pin | Идемпотентность на клиенте: проверка текущего состояния `pinnedTaskIds` перед toggle. |

### 2.7. Accessibility

- Иконка pin — `<button>` с `aria-pressed="true|false"` и `aria-label="Закрепить задачу «Название»"`.
- Закреплённые строки имеют визуальный индикатор (фон) + иконку — не только цвет.
- Разделитель между группами — декоративный, `aria-hidden="true"`.
- Фокус при тоггле pin остаётся на кнопке.
- В таблице «Мои задачи» колонка pin не имеет заголовка (визуально), но для screen reader: `aria-label="Закреплена"` у каждой ячейки.

### 2.8. План реализации (прототип)

| Шаг | Что сделать | Файлы |
|-----|------------|-------|
| 2.1 | Импортировать `PinRegular`, `PinFilled` из `@fluentui/react-icons` | `App.jsx` |
| 2.2 | Добавить state `pinnedTaskIds` (Set). Поле `pinned` в объектах моделей НЕ добавлять. | `App.jsx` |
| 2.3 | Реализовать `togglePin(taskId)`, `isPinned(taskId)` | `App.jsx` |
| 2.4 | Добавить сортировку в не-временно́й `unscheduled.map()` | `App.jsx` |
| 2.5 | В timed-секции: добавить pin-иконку + фон, БЕЗ пересортировки | `App.jsx` |
| 2.6 | Добавить pin-иконку в заголовок инспектора (`.details-title-row`) | `App.jsx` |
| 2.7 | Добавить pin-колонку в таблицу «Мои задачи» | `App.jsx` |
| 2.8 | Стилизовать `.pin-button`, `.pinned-row`, `.pin-divider` | `styles.css` |
| 2.9 | Добавить сортировку по pin в таблицу «Мои задачи» | `App.jsx` |

---

## 3. Фича 3: Итог дня (End-of-Day Summary)

### 3.1. Мотивация

Психологическое закрытие рабочего дня. Пользователь открывает «Сегодня» вечером и видит, что сделано, а что осталось. Одно нажатие — и просроченное уходит на завтра. Чистое утро без груза вчерашних задач.

### 3.2. Пользовательский сценарий

1. Пользователь открывает раздел «Сегодня» после 17:00 (окончание рабочего дня).
2. Под заголовком раздела появляется итоговый баннер.
3. Баннер показывает:
   - «Сегодня завершено задач: 4»
   - «Осталось просроченных: 3»
4. Доступные действия:
   - **[Перенести на завтра]** — переносит deadlineAt всех просроченных на завтра 23:59:59 UTC.
   - **[Показать просроченные]** — скроллит к секции «Несрочные и просроченные» и раскрывает её.
   - **[×]** — закрывает баннер. Сегодня он больше не появится.
5. Если пользователь закрыл баннер — он не показывается до следующего дня.
6. Если просроченных нет, но есть завершённые — баннер показывает только итог: «Сегодня завершено: 5 задач. Отличная работа!». Кнопки «Перенести» нет.
7. Если нет ни завершённых, ни просроченных — баннер не показывается.

**Время активации:** `currentHour >= workDayEndHour`, где `workDayEndHour` берётся из настроек пользователя (по умолчанию 17).

### 3.3. Размещение в UI

Баннер размещается непосредственно под `workspace-header`, над `content-grid`, на всю ширину контентной области.

```
┌──────────────────────────────────────────────────────────┐
│ Сегодня                                         28 июля   │  ← workspace-header
│ Вт, 28 июля 2026                                        │
├──────────────────────────────────────────────────────────┤
│ ✓ Сегодня завершено: 4 задачи. Осталось просроченных: 3.  │  ← EOD баннер
│ [Перенести на завтра]  [Показать просроченные]        [×] │
├──────────────────────────────────────────────────────────┤
│                                                          │
│ 09:00 ┌──────────────────────┐                           │  ← timeline
│       │ Подготовить сводный   │                           │
│       │ анализ               │                           │
│ ...                                                      │
└──────────────────────────────────────────────────────────┘
```

### 3.4. Визуальная спецификация

**Тип компонента:** `.eod-banner` — новый класс, наследующий паттерн от `.connectivity-banner` и `.inline-alert`.

**Токены баннера:**

| Свойство | Значение |
|----------|----------|
| Фон | `var(--blue-soft)` (#EAF3FF) |
| Граница | 1px solid `var(--blue)` (#0F6CBD) |
| Скругление | 5 px |
| Паддинг | 12 px 16 px |
| Шрифт | 14 px, weight 400 |
| Цвет текста | `var(--text)` (#1B1A19) |
| Иконка | `CheckmarkCircleRegular`, 20 px, цвет `var(--green)` (#107C10), слева |
| Отступы | margin-bottom: 16px |

**Структура баннера:**
```
[иконка] [текст: «Сегодня завершено: 4. Осталось просроченных: 3.»] [действия] [×]
```

**Кнопки действий:**

| Кнопка | Вариант | Иконка |
|--------|---------|--------|
| «Перенести на завтра» | `.button--primary`, высота 38 px | `ArrowUndoRegular` |
| «Показать просроченные» | `.button--secondary`, высота 38 px | `ChevronDownRegular` |
| «×» (закрыть) | `.icon-button`, 28×28 px | `DismissRegular` |

**Состояния баннера:**

| Состояние | Описание |
|-----------|----------|
| **Default** | Баннер видим. Кнопки активны. |
| **Loading** (после нажатия «Перенести») | Кнопка «Перенести на завтра» показывает `ArrowSyncRegular className="icon-spin"` + «Переносим…». Остальные кнопки disabled. |
| **Success** (после переноса) | Текст обновляется: «✓ Перенесено на завтра: 3 задачи. Сегодня завершено: 4.» Цифра просроченных обнуляется. Кнопка «Перенести» исчезает. Кнопка «Показать» исчезает. Остаётся только [×]. |
| **Error** (ошибка переноса) | Под баннером появляется inline-alert--error: «Не удалось перенести задачи. [Повторить]». Кнопка «Перенести» возвращается. |
| **Dismissed** | Баннер скрыт. Не показывается до следующего дня. |
| **Offline** | Баннер видим, но кнопка «Перенести на завтра» disabled с тултипом «Нет подключения к серверу». |

**Анимация появления:**
- Баннер появляется с анимацией `eodSlideDown` (max-height: 0 → 80px, opacity: 0 → 1, duration: 250ms, easing: ease-out).

### 3.5. Модель данных

**Вычисляемые значения (клиент):**

```js
const workDayEndHour = userPreferences.workDayEndHour ?? 17;
const currentHour = new Date().getHours();
const isEndOfDay = currentHour >= workDayEndHour;

// В прототипе завершённые задачи лежат в массиве completedTodayTasks
const completedTodayCount = completedTodayTasks.length;

// Просроченные — те, у кого due содержит "Просрочено"
const overdueCount = unscheduled.filter(t => t.due?.includes('Просрочено')).length;
```

**Примечание для продакшена:** серверный запрос `GET /api/v1/tasks?filter=completed&date=today` вернёт реальный `completedTodayCount`. А `overdueCount` — через `GET /api/v1/tasks?filter=overdue`.

**Флаг показа баннера (localStorage в прототипе):**

```js
const today = new Date().toISOString().slice(0, 10); // "2026-08-12"
const dismissKey = `eod-dismissed-${today}`;
const [dismissed, setDismissed] = useState(
  () => localStorage.getItem(dismissKey) === 'true'
);

function dismissEod() {
  localStorage.setItem(dismissKey, 'true');
  setDismissed(true);
}
```

**Примечание для продакшена:** в WPF-клиенте `localStorage` недоступен. Dismissal-флаг хранится в `UserPreferences.eodLastDismissedDate` (строка ISO date). При загрузке клиент сравнивает `eodLastDismissedDate === today`.

**Условие видимости баннера:**
```js
const showEodBanner = isEndOfDay
  && !dismissed
  && (completedTodayCount > 0 || overdueCount > 0);
```

### 3.6. Логика «Показать просроченные»

```js
// Refs:
const unscheduledSectionRef = useRef(null);

// Обработчик:
function handleScrollToOverdue() {
  // 1. Раскрыть секцию, если свёрнута
  setUntimedOpen(true);
  // 2. Скролл к секции (в следующем тике, после раскрытия)
  setTimeout(() => {
    unscheduledSectionRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }, 100);
}

// Привязка ref в JSX:
<section className="unscheduled" ref={unscheduledSectionRef}>
  ...
</section>
```

### 3.7. Логика «Перенести на завтра»

Использует ту же функцию `handleRescheduleOverdue` (см. §1.7), но с `targetDeadline` = завтра 23:59:59 UTC:

```js
function tomorrowEndOfDayUTC() {
  const now = new Date();
  const tomorrow = new Date(Date.UTC(
    now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() + 1,
    23, 59, 59, 0
  ));
  return tomorrow.toISOString();
}

async function handleRescheduleToTomorrow() {
  setEodLoading(true);
  try {
    const result = await rescheduleOverdueApi(tomorrowEndOfDayUTC());
    setEodResult(result);
    setEodLoading(false);

    if (result.failed === 0) {
      setUnscheduled(prev => prev.map(task => {
        if (task.due?.includes('Просрочено')) {
          return { ...task, due: 'Завтра' };
        }
        return task;
      }));
    } else {
      const failedIds = new Set(result.items
        .filter(i => i.status === 'failed')
        .map(i => i.taskId));
      setUnscheduled(prev => prev.map(task => {
        if (task.due?.includes('Просрочено') && !failedIds.has(task.id)) {
          return { ...task, due: 'Завтра' };
        }
        return task;
      }));
    }
  } catch {
    setEodLoading(false);
    setEodResult('error');
  }
}
```

API тот же: `POST /api/v1/tasks/bulk-reschedule-overdue` с параметром `targetDeadline`.

### 3.8. Accessibility

- Баннер — `<section>` с `aria-label="Итоги рабочего дня"` и `role="status"`.
- Иконка `CheckmarkCircleRegular` — декоративная, `aria-hidden="true"`.
- Кнопка закрытия: `aria-label="Закрыть итоги дня"`.
- После переноса: `aria-live="polite"` регион обновляется с результатом.
- При появлении баннера: фокус **не** перехватывается (не блокируем работу).
- Если пользователь использует screen reader, баннер зачитывается как «Итоги рабочего дня. Сегодня завершено 4 задачи. Осталось просроченных 3.»

### 3.9. План реализации (прототип)

| Шаг | Что сделать | Файлы |
|-----|------------|-------|
| 3.1 | Создать компонент `EodBanner` (отдельный блок в JSX раздела Today) | `App.jsx` |
| 3.2 | Добавить state: `eodDismissed`, `eodLoading`, `eodResult` | `App.jsx` |
| 3.3 | Вычислить `completedTodayCount` из `completedTodayTasks.length`, `overdueCount`, `isEndOfDay`, `showEodBanner` | `App.jsx` |
| 3.4 | Добавить `unscheduledSectionRef` для скролла | `App.jsx` |
| 3.5 | Реализовать `handleRescheduleToTomorrow()`, `handleScrollToOverdue()`, `dismissEod()` | `App.jsx` |
| 3.6 | Стилизовать `.eod-banner`, `.eod-banner__icon`, `.eod-banner__text`, `.eod-banner__actions` | `styles.css` |
| 3.7 | Добавить анимацию `@keyframes eodSlideDown` | `styles.css` |
| 3.8 | Добавить все состояния: loading, success, error, dismissed | `App.jsx` |

---

## 4. Общие изменения в токенах и иконках

### 4.1. Новые иконки Fluent UI (добавить в импорт)

```js
import {
  // Уже есть:
  // AddRegular, ArrowSyncRegular, ArrowUndoRegular, CalendarRegular,
  // CheckmarkCircleRegular, ChevronDownRegular, ChevronUpRegular,
  // DismissRegular, EditRegular, WarningRegular, ...

  // Добавить:
  PinRegular,
  PinFilled,
} from '@fluentui/react-icons';
```

### 4.2. Новые CSS-классы и токены

```css
/* ===== Спиннер-анимация ===== */
@keyframes spin {
  from { transform: rotate(0deg); }
  to   { transform: rotate(360deg); }
}
.icon-spin {
  animation: spin 1s linear infinite;
}

/* ===== Кнопка pin ===== */
.pin-button {
  width: 28px;
  height: 28px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  border: none;
  background: transparent;
  border-radius: 4px;
  cursor: pointer;
  flex-shrink: 0;
}
.pin-button:hover {
  background: #f3f2f1;
}
.pin-button[aria-pressed="true"] {
  color: var(--blue);
}
.pin-button[aria-pressed="false"] {
  color: var(--subtle);
}
.pin-button:focus-visible {
  outline: 2px solid var(--focus);
  outline-offset: 2px;
}
.pin-button:disabled {
  opacity: 0.5;
  cursor: default;
}

/* ===== Строка закреплённой задачи (фон) ===== */
.pinned-row {
  background: var(--blue-soft);
}

/* ===== Разделитель групп (только не-временны́е списки) ===== */
.pin-divider {
  height: 1px;
  background: var(--border);
  margin: 8px 0;
  border: none;
}

/* ===== Баннер итогов дня ===== */
.eod-banner {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 12px 16px;
  background: var(--blue-soft);
  border: 1px solid var(--blue);
  border-radius: 5px;
  margin-bottom: 16px;
  font-size: 14px;
  color: var(--text);
  overflow: hidden;
  animation: eodSlideDown 250ms ease-out;
}
@keyframes eodSlideDown {
  from { max-height: 0; opacity: 0; margin-bottom: 0; padding-top: 0; padding-bottom: 0; border-width: 0; }
  to   { max-height: 80px; opacity: 1; }
}
.eod-banner__icon {
  color: var(--green);
  flex-shrink: 0;
  font-size: 20px;
}
.eod-banner__text {
  flex: 1;
  min-width: 0;
}
.eod-banner__actions {
  display: flex;
  gap: 8px;
  flex-shrink: 0;
  align-items: center;
}

/* ===== Кнопка reschedule в заголовке секции ===== */
.section-heading__actions {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: 8px;
}
.reschedule-button {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  min-height: 38px;       /* соответствие CMP-control-height-compact */
  padding: 0 12px;
  border: 1px solid #c8c6c4;
  border-radius: 5px;
  background: #fff;
  color: var(--text);
  font-size: 13px;
  font-weight: 400;
  cursor: pointer;
  white-space: nowrap;
}
.reschedule-button:hover {
  background: #f3f2f1;
}
.reschedule-button:focus-visible {
  outline: 2px solid var(--focus);
  outline-offset: 2px;
}
.reschedule-button:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
.reschedule-button--success {
  color: var(--green);
  border-color: var(--green);
}
.reschedule-button--partial {
  color: var(--amber);
  border-color: var(--amber);
}
.reschedule-button--error {
  color: var(--red);
  border-color: var(--red);
}
```

---

## 5. Резюме трудозатрат и зависимостей

### 5.1. Очерёдность реализации

| Порядок | Фича | Часы | Зависит от |
|---------|------|------|------------|
| **1** | Фича 1: перенос просроченных | 2–4 ч | — |
| **2** | Фича 3: итог дня | 3–5 ч | Фича 1 (переиспользует `rescheduleOverdueApi`) |
| **3** | Фича 2: закрепление задач | 4–8 ч | — (независима) |

### 5.2. Критерии приёмки

**Фича 1:**
- [ ] Кнопка видна только когда есть просроченные задачи и соединение с сервером.
- [ ] Счётчик просроченных корректен.
- [ ] Все состояния отработаны: loading (`className="icon-spin"`), success, partial, error.
- [ ] После успеха список задач обновлён, просроченные перемещены.
- [ ] При частичном успехе оставшиеся просроченные остаются видимы.
- [ ] Клавиатурная доступность: Tab до кнопки, Enter/Space для активации.
- [ ] Screen reader зачитывает результат операции.

**Фича 2:**
- [ ] Pin работает в не-временны́х списках (сортировка вверх + разделитель).
- [ ] Pin в timed-секциях — только фон, без пересортировки.
- [ ] Тоггл pin мгновенный (без задержки на сервер в прототипе).
- [ ] Иконка меняется PinRegular ↔ PinFilled, aria-pressed корректен.
- [ ] Закреплённые строки имеют фон `--blue-soft`.
- [ ] В offline режиме pin-кнопка disabled.
- [ ] Поле `pinned` в объектах моделей отсутствует. Единственный источник — Set.

**Фича 3:**
- [ ] Баннер появляется только после окончания рабочего дня.
- [ ] Баннер показывает корректные счётчики (`completedTodayTasks.length` и overdue из `unscheduled`).
- [ ] «Перенести на завтра» переиспользует API фичи 1.
- [ ] «Показать просроченные» раскрывает секцию (`setUntimedOpen`) и скроллит (`scrollIntoView`).
- [ ] Закрытие баннера персистентно в рамках дня.
- [ ] Баннер не появляется, если нет ни завершённых, ни просроченных.
- [ ] При закрытии баннера фокус не теряется.
- [ ] Анимация появления плавная (250ms).

### 5.3. Не затронуто данным ТЗ

- Календарь (CalendarSurface) — pin и reschedule в календаре требуют отдельного дизайна.
- Режим read-only при scope change — кнопки скрываются стандартным механизмом `isWritable`.
- Админские сценарии (массовый перенос чужих задач).
- Мобильная версия (отсутствует в бейзлайне).
- WPF-продакшен: замена `localStorage` на `UserPreferences` (описано в примечаниях).
