import { useEffect, useMemo, useRef, useState } from "react";
import { getGateAccount, hasCapability } from "./gateFixture.js";
import {
  AddRegular,
  AlertRegular,
  ArchiveRegular,
  ArrowDownRegular,
  ArrowUndoRegular,
  ArrowSyncRegular,
  ArrowUpRegular,
  BranchForkRegular,
  CalendarRegular,
  CheckmarkCircleRegular,
  CheckmarkRegular,
  ChevronDownRegular,
  ChevronLeftRegular,
  ChevronRightRegular,
  ChevronUpRegular,
  ClipboardTaskListLtrRegular,
  CommentRegular,
  DatabaseRegular,
  DeleteRegular,
  DismissRegular,
  DocumentRegular,
  DrinkCoffeeRegular,
  FlagRegular,
  FilterRegular,
  FolderRegular,
  EditRegular,
  HistoryRegular,
  KeyRegular,
  LockClosedRegular,
  MailInboxRegular,
  MoreHorizontalRegular,
  NavigationRegular,
  PersonRegular,
  PlugDisconnectedRegular,
  PlayCircleRegular,
  QuestionRegular,
  SaveRegular,
  SearchRegular,
  ServerRegular,
  ShieldErrorRegular,
  SignOutRegular,
  SettingsRegular,
  SquareRegular,
  SubtractRegular,
  TaskListSquareLtrFilled,
  WarningRegular,
} from "@fluentui/react-icons";
import {
  canEnterMaintenance,
  filterAuthorizedAudit,
  getVisibleOperationSections,
  isOperationsWritable,
  transitionOperation,
} from "./operationsModel.js";
import {
  applyCalendarResponse,
  canCommitCalendarEvent,
  createCalendarEventDraft,
  validateCalendarEventDraft,
} from "./calendarEventModel.js";

const baseTasks = [
  {
    id: "analysis",
    time: "10:00 – 11:00",
    title: "Подготовить анализ продаж за июнь",
    project: "Отчётность",
    priority: "Высокая",
    priorityTone: "high",
    status: "В работе",
    people: "",
  },
  {
    id: "planning",
    time: "09:00 – 09:45",
    title: "Ежедневное планирование команды",
    project: "Внутренние процессы",
    priority: "Низкая",
    priorityTone: "low",
    status: "Готово",
    people: "5",
  },
  {
    id: "presentation",
    time: "11:15 – 12:00",
    title: "Согласование макета презентации",
    project: "Маркетинговая кампания",
    priority: "Средняя",
    priorityTone: "medium",
    status: "Запланировано",
  },
  {
    id: "meeting",
    time: "13:00 – 14:00",
    title: "Встреча с отделом продаж",
    project: "Отчётность",
    priority: "Средняя",
    priorityTone: "medium",
    status: "Запланировано",
    people: "8",
  },
  {
    id: "contract",
    time: "14:15 – 15:00",
    title: "Проверить договор с ООО «Вектор»",
    project: "Юридическая поддержка",
    priority: "Высокая",
    priorityTone: "high",
    status: "Запланировано",
  },
  {
    id: "mail",
    time: "15:15 – 16:00",
    title: "Ответить на письма",
    project: "Коммуникации",
    priority: "Низкая",
    priorityTone: "low",
    status: "Запланировано",
  },
  {
    id: "tomorrow",
    time: "16:15 – 17:00",
    title: "Подготовить задачи на завтра",
    project: "Внутренние процессы",
    priority: "Низкая",
    priorityTone: "low",
    status: "Запланировано",
  },
];

const initialUnscheduled = [
  { id: "incident", title: "Проверить инциденты поддержки", project: "Техподдержка", priority: "Высокая", priorityTone: "high", due: "Просрочено 24.07" },
  { id: "rules", title: "Обновить регламент работы с клиентами", project: "Внутренние процессы", priority: "Средняя", priorityTone: "medium", due: "Нет срока" },
  { id: "training", title: "Подготовить материалы для обучения", project: "Обучение", priority: "Низкая", priorityTone: "low", due: "Нет срока" },
  { id: "office", title: "Заказать канцелярию в офис", project: "Административные задачи", priority: "Средняя", priorityTone: "medium", due: "Нет срока" },
  { id: "archive", title: "Архивировать старые отчёты", project: "Отчётность", priority: "Низкая", priorityTone: "low", due: "Нет срока" },
];

const dateLabels = [
  "27 июля 2026, понедельник",
  "28 июля 2026, вторник",
  "29 июля 2026, среда",
];

const initialInboxItems = [
  { id: "inbox-1", title: "Уточнить цифры у региональных менеджеров", source: "Быстрый ввод", created: "Сегодня, 09:12", status: "Новая" },
  { id: "inbox-2", title: "Идеи для квартального отчёта", source: "Заметка", created: "Вчера, 17:46", status: "Новая" },
  { id: "inbox-3", title: "Проверить обновлённый договор Вектора", source: "Контекстное меню", created: "27 июля, 14:20", status: "Требует классификации" },
  { id: "inbox-4", title: "Черновик письма партнёрам", source: "Быстрый ввод", created: "26 июля, 11:03", status: "Новая" },
];

const searchResults = [
  { id: "search-task", group: "Задачи", type: "Задача", title: "Подготовить анализ продаж за июнь", meta: "Отчётность · срок сегодня", authorized: true },
  { id: "search-task-overdue", group: "Задачи", type: "Задача", title: "Проверить инциденты поддержки", meta: "Техподдержка · просрочено 24.07", authorized: true },
  { id: "search-project", group: "Проекты", type: "Проект", title: "Альфа", meta: "8 активных задач", authorized: true },
  { id: "search-project-marketing", group: "Проекты", type: "Проект", title: "Маркетинговая кампания", meta: "12 задач · 3 участника", authorized: true },
  { id: "search-file", group: "Файлы", type: "Файл", title: "Отчёт_июль.xlsx", meta: "Каталог · доступное расположение", authorized: true },
  { id: "search-contact", group: "CRM", type: "Контакт", title: "Мария Соколова", meta: "ООО «Вектор» · разрешённые поля", authorized: true },
  { id: "search-person", group: "Сотрудники", type: "Сотрудник", title: "Иван С.", meta: "Отдел продаж", authorized: true },
  { id: "search-redacted", group: "Сотрудники", type: "Ограниченный результат", title: "Сотрудник недоступен", meta: "Вне вашей разрешённой области", authorized: false },
];

const lifecycleSeed = [
  {
    id: "archive-project-north",
    section: "archive",
    type: "Проект",
    title: "Проект «Север»",
    meta: "Архивирован 24 июля · Анна К.",
    reason: "Работы завершены, история и связи сохранены.",
    canRestore: true,
    history: ["24 июля · Архивирован Анной К.", "22 июля · Закрыты открытые задачи"],
  },
  {
    id: "archive-task-kpi",
    section: "archive",
    type: "Задача",
    title: "Сверить KPI отдела продаж",
    meta: "Архивирована 18 июля · Иван С.",
    reason: "Восстановление требует разрешения Archive.Restore.",
    canRestore: false,
    history: ["18 июля · Архивирована Иваном С.", "17 июля · Статус изменён на «Готово»"],
  },
  {
    id: "archive-redacted",
    section: "archive",
    type: "Недоступный объект",
    title: "Архивный объект недоступен",
    meta: "Вне вашей разрешённой области",
    reason: "Название, владелец и связи не раскрываются.",
    authorized: false,
    canRestore: false,
    history: [],
  },
  {
    id: "trash-task-draft",
    section: "trash",
    type: "Задача",
    title: "Черновик отчёта по региону",
    meta: "В корзине до 28 августа · Мария С.",
    reason: "Можно восстановить в исходный разрешённый раздел.",
    canRestore: true,
    canPurge: false,
    history: ["29 июля · Перемещена в корзину Марией С.", "28 июля · Создан черновик"],
  },
  {
    id: "trash-project-campaign",
    section: "trash",
    type: "Проект",
    title: "Маркетинговая кампания 2025",
    meta: "В корзине до 15 августа · Анна К.",
    reason: "Имя уже занято активным проектом.",
    canRestore: true,
    canPurge: true,
    restoreIssue: "name",
    history: ["16 июля · Перемещён в корзину Анной К.", "15 июля · Архивирован"],
  },
  {
    id: "trash-file-parent",
    section: "trash",
    type: "Файл",
    title: "Протокол_встречи.docx",
    meta: "Метаданные в корзине до 12 августа",
    reason: "Исходный родитель недоступен; скрытый путь не раскрывается.",
    canRestore: true,
    canPurge: true,
    restoreIssue: "parent",
    history: ["13 июля · Метаданные перемещены в корзину", "12 июля · Изменено расположение"],
  },
  {
    id: "trash-project-hold",
    section: "trash",
    type: "Проект",
    title: "Аудит договоров 2024",
    meta: "Legal hold · срок удержания не завершён",
    reason: "RetentionBlocked: политика удержания запрещает purge.",
    canRestore: true,
    canPurge: false,
    retentionBlocked: true,
    history: ["9 июля · Установлено юридическое удержание", "8 июля · Перемещён в корзину"],
  },
  {
    id: "trash-file-purge",
    section: "trash",
    type: "Файл",
    title: "Дубликат_сметы.xlsx",
    meta: "Метаданные в корзине · purge разрешён",
    reason: "Удаление затронет только метаданные Task, не физический файл.",
    canRestore: true,
    canPurge: true,
    history: ["30 июля · Метаданные перемещены в корзину", "30 июля · Обнаружен дубликат записи"],
  },
];

const taskTableRows = [
  { id: "table-1", title: "Подготовить анализ продаж за июнь", project: "Отчётность", assignee: "Иван С.", status: "В работе", priority: "Высокая", priorityTone: "high", due: "Сегодня, 17:00" },
  { id: "table-2", title: "Согласовать макет презентации", project: "Маркетинговая кампания", assignee: "Мария С.", status: "Запланировано", priority: "Средняя", priorityTone: "medium", due: "Сегодня, 12:00" },
  { id: "table-3", title: "Проверить договор с ООО «Вектор»", project: "Юридическая поддержка", assignee: "Иван С.", status: "Запланировано", priority: "Высокая", priorityTone: "high", due: "29 июля" },
  { id: "table-4", title: "Обновить регламент работы с клиентами", project: "Внутренние процессы", assignee: "Ольга Н.", status: "На проверке", priority: "Средняя", priorityTone: "medium", due: "31 июля" },
  { id: "table-5", title: "Подготовить материалы для обучения", project: "Обучение", assignee: "Иван С.", status: "Запланировано", priority: "Низкая", priorityTone: "low", due: "Нет срока" },
  { id: "table-6", title: "Архивировать старые отчёты", project: "Отчётность", assignee: "Анна К.", status: "Готово", priority: "Низкая", priorityTone: "low", due: "Вчера" },
  { id: "table-7", title: "Ответить на письма партнёров", project: "Коммуникации", assignee: "Иван С.", status: "В работе", priority: "Средняя", priorityTone: "medium", due: "1 августа" },
  { id: "table-8", title: "Проверить инциденты поддержки", project: "Техподдержка", assignee: "Сергей В.", status: "Просрочено", priority: "Высокая", priorityTone: "high", due: "24 июля" },
];

const projectTree = [
  { id: "alpha", title: "Альфа", group: "Активные проекты", status: "Активен", progress: 68, tasks: 24, members: ["АК", "ИС", "МС"], owner: "Анна К.", deadline: "30 сентября 2026" },
  { id: "marketing", title: "Маркетинговая кампания", group: "Активные проекты", status: "Активен", progress: 42, tasks: 18, members: ["МС", "ИС"], owner: "Мария С.", deadline: "15 августа 2026" },
  { id: "processes", title: "Внутренние процессы", group: "Планирование", status: "Планирование", progress: 21, tasks: 11, members: ["ОН", "ИС", "АК"], owner: "Ольга Н.", deadline: "1 октября 2026" },
  { id: "archive-project", title: "Переезд архива", group: "Приостановленные", status: "Пауза", progress: 76, tasks: 7, members: ["СВ", "ИС"], owner: "Сергей В.", deadline: "Без срока" },
];

const initialNotifications = [
  { id: "notice-1", title: "Срок задачи наступает через 37 минут", meta: "Подготовить анализ продаж за июнь", time: "10:23", unread: true, action: "Завершить", targetState: "changed" },
  { id: "notice-2", title: "Анна К. назначила вас исполнителем", meta: "Согласовать макет презентации", time: "09:48", unread: true, action: "Открыть", targetState: "available" },
  { id: "notice-3", title: "Проект «Альфа» обновлён", meta: "Изменён срок: 30 сентября 2026", time: "Вчера", unread: false, action: "Открыть", targetState: "available" },
];

function useDialogFocusTrap(containerRef, onClose) {
  useEffect(() => {
    const container = containerRef.current;
    if (!container) return undefined;
    const selector = "button:not(:disabled), input:not(:disabled), select:not(:disabled), textarea:not(:disabled), [href], [tabindex]:not([tabindex='-1'])";
    const previous = document.activeElement;

    function onKeyDown(event) {
      if (event.key === "Escape") {
        event.preventDefault();
        onClose();
        return;
      }
      if (event.key !== "Tab") return;
      const focusable = [...container.querySelectorAll(selector)].filter((element) => element.offsetParent !== null);
      if (!focusable.length) return;
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }

    container.addEventListener("keydown", onKeyDown);
    return () => {
      container.removeEventListener("keydown", onKeyDown);
      if (previous instanceof HTMLElement) previous.focus();
    };
  }, [containerRef, onClose]);
}

function PriorityIcon({ tone }) {
  if (tone === "high") return <ArrowUpRegular aria-hidden="true" />;
  if (tone === "low") return <ArrowDownRegular aria-hidden="true" />;
  return <SubtractRegular aria-hidden="true" />;
}

function Priority({ tone, label }) {
  return (
    <span className={`priority priority--${tone}`}>
      <PriorityIcon tone={tone} />
      <span>{label}</span>
    </span>
  );
}

function NavItem({ icon: Icon, label, active, onClick }) {
  return (
    <button className={`nav-item ${active ? "is-active" : ""}`} onClick={onClick} type="button" aria-label={label}>
      <Icon aria-hidden="true" />
      <span>{label}</span>
    </button>
  );
}

function TimelineCard({ task, selected, onSelect }) {
  const statusIcon = task.status === "Готово" ? CheckmarkCircleRegular : PlayCircleRegular;
  const StatusIcon = statusIcon;
  return (
    <button
      className={`timeline-card ${selected ? "is-selected" : ""} ${task.id === "planning" ? "is-complete" : ""}`}
      type="button"
      onClick={() => onSelect(task)}
      aria-pressed={selected}
    >
      <StatusIcon className="timeline-card__status" aria-hidden="true" />
      <div className="timeline-card__body">
        <div className="timeline-card__time">{task.time}</div>
        <div className="timeline-card__title">{task.title}</div>
        <div className="timeline-card__meta">
          <span>Проект: {task.project}</span>
          <span className="timeline-card__right">
            <Priority tone={task.priorityTone} label={task.priority} />
            {task.people && <span className="people-count"><PersonRegular aria-hidden="true" />{task.people}</span>}
          </span>
        </div>
      </div>
    </button>
  );
}

function NewTaskDialog({ onClose, onCreate }) {
  const [title, setTitle] = useState("");
  const [project, setProject] = useState("");
  const [priority, setPriority] = useState("Средняя");
  const [dueDate, setDueDate] = useState("");
  const [dueTime, setDueTime] = useState("");

  function submit(event) {
    event.preventDefault();
    if (!title.trim()) return;
    onCreate({
      title: title.trim(),
      project,
      priority,
      dueDate: dueDate || null,
      dueTime: dueTime || null,
      due: dueDate ? (dueTime ? `${dueDate} ${dueTime}` : dueDate) : "Нет срока",
    });
  }

  return (
    <div className="dialog-backdrop" role="presentation" onMouseDown={onClose}>
      <section className="dialog dialog--new-task" role="dialog" aria-modal="true" aria-labelledby="new-task-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="dialog__header">
          <h2 id="new-task-title">Новая задача</h2>
          <button className="icon-button" type="button" onClick={onClose} aria-label="Закрыть">
            <DismissRegular aria-hidden="true" />
          </button>
        </div>
        <form onSubmit={submit}>
          <label className="field">
            <span>Название</span>
            <input autoFocus value={title} onChange={(event) => setTitle(event.target.value)} placeholder="Что нужно сделать?" />
          </label>
          <div className="dialog__grid dialog__grid--new-task">
            <label className="field">
              <span>Проект <small className="field-hint">необязательно</small></span>
              <select value={project} onChange={(event) => setProject(event.target.value)}>
                <option value="">Без проекта</option>
                <option>Отчётность</option>
                <option>Внутренние процессы</option>
                <option>Коммуникации</option>
              </select>
            </label>
            <label className="field">
              <span>Приоритет</span>
              <select value={priority} onChange={(event) => setPriority(event.target.value)}>
                <option>Низкая</option>
                <option>Средняя</option>
                <option>Высокая</option>
              </select>
            </label>
          </div>
          <div className="dialog__grid">
            <label className="field">
              <span>Срок</span>
              <input type="date" value={dueDate} onChange={(event) => setDueDate(event.target.value)} />
            </label>
            <label className="field">
              <span>Время</span>
              <input type="time" value={dueTime} onChange={(event) => setDueTime(event.target.value)} />
            </label>
          </div>
          <div className="dialog__actions">
            <button className="button button--secondary" type="button" onClick={onClose}>Отмена</button>
            <button className="button button--primary" type="submit" disabled={!title.trim()}>Создать задачу</button>
          </div>
        </form>
      </section>
    </div>
  );
}

function AuthSurface({ onAuthenticated, account }) {
  const [step, setStep] = useState("endpoint");
  const [endpoint, setEndpoint] = useState("https://task.company.local");
  const [endpointStatus, setEndpointStatus] = useState("idle");
  const [username, setUsername] = useState(account.login);
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");
  const [errorCode, setErrorCode] = useState("");
  const [bootstrapMode, setBootstrapMode] = useState("progress");
  const [bootstrapAttempts, setBootstrapAttempts] = useState(0);

  function verifyEndpoint(event) {
    event.preventDefault();
    const normalized = endpoint.trim().toLowerCase();
    if (!normalized.startsWith("https://")) {
      setEndpointStatus("error");
      return;
    }
    if (normalized.includes("tls.")) {
      setEndpointStatus("tls");
      return;
    }
    if (normalized.includes("legacy.")) {
      setEndpointStatus("incompatible");
      return;
    }
    if (normalized.includes("offline.")) {
      setEndpointStatus("unavailable");
      return;
    }
    setEndpointStatus("verified");
  }

  async function submitLogin(event) {
    event.preventDefault();
    const normalizedUser = username.trim().toLowerCase();
    if (normalizedUser === "locked.s") {
      setErrorCode("ACCOUNT_LOCKED_TEMPORARILY");
      setError("Слишком много неудачных попыток. Повторите вход через 14 минут или обратитесь в службу поддержки.");
      return;
    }
    if (normalizedUser === "blocked.s") {
      setErrorCode("ACCOUNT_BLOCKED");
      setError("Учётная запись заблокирована администратором. Самостоятельный повторный вход недоступен.");
      return;
    }
    const validScenarioUsers = ["ivan.s", "cursor.s", "scope.s", "storage.s", "maintenance.s", "signature.s", "download.s"];
    const fixtureAuthenticated = globalThis.taskDesktop?.authenticate
      ? await globalThis.taskDesktop.authenticate(normalizedUser, password)
      : false;
    const credentialsValid = globalThis.taskDesktop
      ? fixtureAuthenticated
      : validScenarioUsers.includes(normalizedUser) && password === "task2026";
    if (!credentialsValid) {
      setErrorCode("INVALID_CREDENTIALS");
      setError("Неверный логин или пароль. Проверьте данные и повторите вход.");
      return;
    }
    setError("");
    setErrorCode("");
    const nextMode = normalizedUser === "cursor.s"
      ? "cursor"
      : normalizedUser === "scope.s"
        ? "scope"
        : normalizedUser === "storage.s"
          ? "storage"
          : normalizedUser === "maintenance.s"
            ? "maintenance"
            : normalizedUser === "signature.s"
              ? "signature"
              : normalizedUser === "download.s"
                ? "download"
                : "progress";
    setBootstrapMode(nextMode);
    setStep("bootstrap");
  }

  const progressStep = step === "sync" ? "bootstrap" : step;
  const progressSteps = ["endpoint", "login", "bootstrap"];

  return (
    <main className="auth-surface" data-testid="auth-surface">
      <section className="auth-intro" aria-label="Task для компании">
        <span className="auth-intro__mark"><TaskListSquareLtrFilled aria-hidden="true" /></span>
        <p className="eyebrow">Локальная сеть компании</p>
        <h1>Рабочие задачи — в одном спокойном пространстве.</h1>
        <p>Task подключается только к одобренному серверу компании и загружает данные, разрешённые вашей ролью.</p>
        <ul>
          <li><CheckmarkRegular aria-hidden="true" /> Сервер — источник актуальных данных</li>
          <li><CheckmarkRegular aria-hidden="true" /> Локальный кэш учитывает права доступа</li>
          <li><CheckmarkRegular aria-hidden="true" /> Состояние подключения видно всегда</li>
        </ul>
      </section>

      <section className="auth-card" aria-live="polite">
        <div className="auth-progress" aria-label="Этап входа">
          {progressSteps.map((item, index) => (
            <span key={item} className={progressStep === item || progressSteps.indexOf(progressStep) > index ? "is-current" : ""}>
              {index + 1}
            </span>
          ))}
        </div>

        {step === "endpoint" && (
          <>
            <ServerRegular className="auth-card__icon" aria-hidden="true" />
            <h2>Первое подключение</h2>
            <p>Укажите адрес сервера, выданный вашим IT‑администратором.</p>
            <form onSubmit={verifyEndpoint}>
              <label className="field">
                <span>Адрес сервера компании</span>
                <input value={endpoint} onChange={(event) => { setEndpoint(event.target.value); setEndpointStatus("idle"); }} aria-describedby="endpoint-hint" />
              </label>
              <small id="endpoint-hint" className="field-hint">Используется защищённое HTTPS‑подключение в локальной сети.</small>
              {endpointStatus === "error" && <div className="inline-message inline-message--error" role="alert"><ShieldErrorRegular aria-hidden="true" />Введите HTTPS‑адрес сервера.</div>}
              {endpointStatus === "tls" && <div className="inline-message inline-message--error" role="alert"><ShieldErrorRegular aria-hidden="true" /><span><strong>Сертификат сервера не прошёл проверку.</strong> Подключение остановлено: Task не позволит обойти проверку TLS. Проверьте адрес и обратитесь к IT‑администратору.</span></div>}
              {endpointStatus === "incompatible" && <div className="inline-message inline-message--error" role="alert"><WarningRegular aria-hidden="true" /><span><strong>Версия сервера несовместима с этим клиентом.</strong> Установите поддерживаемую версию Task из корпоративного каталога.</span></div>}
              {endpointStatus === "unavailable" && <div className="inline-message inline-message--warning" role="status"><PlugDisconnectedRegular aria-hidden="true" /><span><strong>Сервер сейчас недоступен.</strong> Проверьте подключение к локальной сети или повторите попытку позднее. Данные ещё не загружались.</span></div>}
              {endpointStatus === "verified" && <div className="inline-message inline-message--success" role="status"><CheckmarkCircleRegular aria-hidden="true" />Сервер доступен, сертификат проверен.</div>}
              <div className="auth-actions">
                <button className="button button--secondary" type="submit">Проверить подключение</button>
                <button className="button button--primary" type="button" disabled={endpointStatus !== "verified"} onClick={() => setStep("login")}>Продолжить</button>
              </div>
            </form>
          </>
        )}

        {step === "login" && (
          <>
            <KeyRegular className="auth-card__icon" aria-hidden="true" />
            <h2>Вход в Task</h2>
            <p>Используйте корпоративную учётную запись.</p>
            <form onSubmit={submitLogin}>
              <label className="field">
                <span>Логин</span>
                <input value={username} onChange={(event) => { setUsername(event.target.value); setError(""); setErrorCode(""); }} autoComplete="username" />
              </label>
              <label className="field">
                <span>Пароль</span>
                <input type="password" value={password} onChange={(event) => { setPassword(event.target.value); setError(""); setErrorCode(""); }} autoComplete="current-password" />
              </label>
              <small className="field-hint">{globalThis.taskDesktop ? `Локальная Gate fixture · ${account.roleLabel}` : <>Для прототипа: пароль <strong>task2026</strong>.</>}</small>
              {error && <div className="inline-message inline-message--error" role="alert"><ShieldErrorRegular aria-hidden="true" /><span>{errorCode && <strong>{errorCode}</strong>}{error}</span></div>}
              <div className="auth-actions">
                <button className="button button--secondary" type="button" onClick={() => setStep("endpoint")}>Назад</button>
                <button className="button button--primary" type="submit">Войти</button>
              </div>
            </form>
          </>
        )}

        {step === "bootstrap" && (
          <>
            <DatabaseRegular className="auth-card__icon" aria-hidden="true" />
            <h2>Подготовка разрешённых данных</h2>
            <p>Task проверяет сессию, область доступа и курсор синхронизации до открытия рабочего пространства.</p>

            {bootstrapMode === "progress" && (
              <>
                <div className="bootstrap-progress" role="progressbar" aria-label="Синхронизация разрешённых данных" aria-valuemin="0" aria-valuemax="100" aria-valuenow="68">
                  <span><strong>Задачи и проекты</strong><em>68%</em></span>
                  <i><b /></i>
                  <small>Загружено 846 из 1 248 объектов. Неразрешённые данные не сохраняются.</small>
                </div>
                <div className="auth-actions">
                  <button className="button button--secondary" type="button" onClick={() => setStep("login")}>Отменить</button>
                  <button className="button button--primary" type="button" onClick={() => setStep("sync")}>Завершить синхронизацию</button>
                </div>
              </>
            )}

            {bootstrapMode === "cursor" && (
              <>
                <div className="inline-message inline-message--warning" role="status"><ArrowSyncRegular aria-hidden="true" /><span><strong>SYNC_CURSOR_EXPIRED</strong> Курсор синхронизации устарел. Task сохранит локальный кэш только для чтения, получит актуальный снимок и не откроет рабочую область до повторной проверки прав.</span></div>
                <button className="button button--primary auth-open" type="button" onClick={() => setBootstrapMode("progress")}>Получить актуальный снимок</button>
              </>
            )}

            {bootstrapMode === "scope" && (
              <>
                <div className="inline-message inline-message--warning" role="status"><LockClosedRegular aria-hidden="true" /><span><strong>SYNC_SCOPE_CHANGED</strong> Область доступа изменилась. Недоступные объекты будут удалены из локального кэша до открытия Task; новые разрешённые данные будут загружены заново.</span></div>
                <button className="button button--primary auth-open" type="button" onClick={() => setBootstrapMode("progress")}>Обновить разрешённые данные</button>
              </>
            )}

            {bootstrapMode === "storage" && (
              <>
                <div className="inline-message inline-message--error" role="alert"><DatabaseRegular aria-hidden="true" /><span><strong>STORAGE_FULL</strong> Недостаточно места для безопасного обновления локального кэша. Освободите не менее 620 МБ; текущие разрешённые данные останутся без изменений.</span></div>
                <button className="button button--secondary auth-open" type="button" onClick={() => setBootstrapMode("progress")}>Повторить после освобождения места</button>
              </>
            )}

            {bootstrapMode === "maintenance" && (
              <>
                <div className="inline-message inline-message--warning" role="status"><ServerRegular aria-hidden="true" /><span><strong>MAINTENANCE_MODE</strong> Сервер временно на обслуживании. Повторная проверка доступна через 15 минут; неподтверждённые данные не будут показаны.</span></div>
                <button className="button button--secondary auth-open" type="button" onClick={() => setBootstrapMode("progress")}>Проверить подключение снова</button>
              </>
            )}

            {bootstrapMode === "signature" && (
              <>
                <div className="inline-message inline-message--error" role="alert"><ShieldErrorRegular aria-hidden="true" /><span><strong>DEPENDENCY_UNAVAILABLE</strong> Подпись пакета обновления не подтверждена. Task не установит файл и не откроет рабочую область с неподдерживаемой версией.</span></div>
                <button className="button button--secondary auth-open" type="button" onClick={() => { const next = bootstrapAttempts + 1; setBootstrapAttempts(next); setBootstrapMode(next >= 2 ? "failed" : "download"); }}>Загрузить пакет заново</button>
              </>
            )}

            {bootstrapMode === "download" && (
              <>
                <div className="inline-message inline-message--warning" role="status"><PlugDisconnectedRegular aria-hidden="true" /><span><strong>Загрузка обновления прервана.</strong> Неполный пакет удалён. Текущая версия не будет запущена, пока совместимый клиент не загружен и не проверен.</span></div>
                <button className="button button--secondary auth-open" type="button" onClick={() => { const next = bootstrapAttempts + 1; setBootstrapAttempts(next); setBootstrapMode(next >= 2 ? "failed" : "signature"); }}>Повторить загрузку</button>
              </>
            )}

            {bootstrapMode === "failed" && (
              <>
                <div className="inline-message inline-message--error" role="alert"><WarningRegular aria-hidden="true" /><span><strong>Повторное восстановление не удалось.</strong> После двух безопасных попыток Task остановил цикл. Диагностика сохранена локально; неподтверждённые данные не показаны.</span></div>
                <div className="auth-actions"><button className="button button--secondary" type="button" onClick={() => setStep("login")}>Сменить учётную запись</button><button className="button button--primary" type="button" onClick={() => { setBootstrapAttempts(0); setBootstrapMode("progress"); }}>Начать чистую проверку</button></div>
              </>
            )}
          </>
        )}

        {step === "sync" && (
          <>
            <DatabaseRegular className="auth-card__icon" aria-hidden="true" />
            <h2>Данные готовы</h2>
            <p>Разрешённый кэш подготовлен. Неразрешённые объекты не загружались.</p>
            <div className="sync-list">
              <span><CheckmarkCircleRegular aria-hidden="true" /><strong>Задачи и проекты</strong><small>1 248 объектов</small></span>
              <span><CheckmarkCircleRegular aria-hidden="true" /><strong>Справочники</strong><small>актуальны</small></span>
              <span><CheckmarkCircleRegular aria-hidden="true" /><strong>Курсор синхронизации</strong><small>проверен</small></span>
            </div>
            <button className="button button--primary auth-open" type="button" onClick={onAuthenticated}>Открыть Task</button>
          </>
        )}
      </section>
    </main>
  );
}

function SearchOverlay({ offline, onClose, onOpenResult, onShowAll, onToast }) {
  const [query, setQuery] = useState("");
  const [filter, setFilter] = useState("Все");
  const [selectedIndex, setSelectedIndex] = useState(0);
  const visibleResults = searchResults.filter((result) => {
    const matchesFilter = filter === "Все"
      || (filter === "Задачи" && result.type === "Задача")
      || (filter === "Проекты" && result.type === "Проект")
      || (filter === "Файлы" && result.type === "Файл")
      || (filter === "CRM" && result.group === "CRM")
      || (filter === "Сотрудники" && result.group === "Сотрудники");
    if (!matchesFilter) return false;
    if (!query.trim()) return true;
    const haystack = `${result.group} ${result.type} ${result.title} ${result.meta}`.toLowerCase();
    return haystack.includes(query.trim().toLowerCase());
  });

  function activate(result) {
    if (!result.authorized) {
      onToast("Результат недоступен. Защищённые данные и количество скрытых объектов не раскрываются.");
      return;
    }
    onOpenResult(result);
  }

  function handleKeyDown(event) {
    if (event.key === "ArrowDown") {
      event.preventDefault();
      setSelectedIndex((index) => Math.min(index + 1, Math.max(visibleResults.length - 1, 0)));
    }
    if (event.key === "ArrowUp") {
      event.preventDefault();
      setSelectedIndex((index) => Math.max(index - 1, 0));
    }
    if (event.key === "Enter" && visibleResults[selectedIndex]) {
      event.preventDefault();
      activate(visibleResults[selectedIndex]);
    }
    if (event.key === "Escape") onClose();
  }

  return (
    <div className="search-backdrop" role="presentation" onMouseDown={onClose}>
      <section className="search-overlay" role="dialog" aria-modal="true" aria-labelledby="global-search-title" onMouseDown={(event) => event.stopPropagation()}>
        <h2 id="global-search-title" className="sr-only">Глобальный поиск</h2>
        <div className="search-box">
          <SearchRegular aria-hidden="true" />
          <input
            autoFocus
            value={query}
            onChange={(event) => { setQuery(event.target.value); setSelectedIndex(0); }}
            onKeyDown={handleKeyDown}
            placeholder="Поиск задач, проектов, файлов и людей"
            aria-label="Глобальный поиск"
          />
          <kbd>Ctrl+K</kbd>
          <button className="icon-button" type="button" onClick={onClose} aria-label="Закрыть поиск"><DismissRegular aria-hidden="true" /></button>
        </div>
        <div className="search-filters" aria-label="Фильтры поиска">
          {["Все", "Задачи", "Проекты", "Файлы", "CRM", "Сотрудники"].map((item) => (
            <button
              key={item}
              className={filter === item ? "is-active" : ""}
              type="button"
              aria-pressed={filter === item}
              onClick={() => { setFilter(item); setSelectedIndex(0); }}
            >
              {item}
            </button>
          ))}
        </div>
        {offline && <div className="inline-message inline-message--warning" role="status"><PlugDisconnectedRegular aria-hidden="true" />Показаны только разрешённые данные из кэша. Результаты могут быть неполными.</div>}
        <div className="search-results" role="listbox" aria-label="Результаты поиска">
          {visibleResults.length === 0 && <div className="empty-state"><SearchRegular aria-hidden="true" /><strong>Ничего не найдено</strong><span>Измените запрос или очистите фильтры.</span></div>}
          {visibleResults.map((result, index) => (
            <button
              key={result.id}
              type="button"
              role="option"
              aria-selected={selectedIndex === index}
              aria-disabled={!result.authorized}
              className={`search-result ${selectedIndex === index ? "is-selected" : ""} ${!result.authorized ? "is-redacted" : ""}`}
              onMouseEnter={() => setSelectedIndex(index)}
              onClick={() => activate(result)}
            >
              <span className="search-result__icon">{result.authorized ? <DocumentRegular aria-hidden="true" /> : <LockClosedRegular aria-hidden="true" />}</span>
              <span><small>{result.group} · {result.type}</small><strong>{result.title}</strong><span>{result.meta}</span></span>
              {!result.authorized && <em>Недоступно</em>}
            </button>
          ))}
        </div>
        <footer className="search-footer">
          <span>↑↓ выбрать · Enter открыть · Esc закрыть</span>
          <button className="button button--quiet search-show-all" type="button" onClick={() => onShowAll({ query, filter })}>Все результаты</button>
        </footer>
      </section>
    </div>
  );
}

function SearchSurface({ offline, initialQuery, initialFilter, onOpenResult, onToast }) {
  const [query, setQuery] = useState(initialQuery || "");
  const [filter, setFilter] = useState(initialFilter || "Все");
  const [loading, setLoading] = useState(false);
  const filterOptions = ["Все", "Задачи", "Проекты", "Файлы", "CRM", "Сотрудники"];

  useEffect(() => {
    setQuery(initialQuery || "");
    setFilter(initialFilter || "Все");
  }, [initialFilter, initialQuery]);

  const visibleResults = useMemo(() => searchResults.filter((result) => {
    const matchesFilter = filter === "Все"
      || (filter === "Задачи" && result.type === "Задача")
      || (filter === "Проекты" && result.type === "Проект")
      || (filter === "Файлы" && result.type === "Файл")
      || (filter === "CRM" && result.group === "CRM")
      || (filter === "Сотрудники" && result.group === "Сотрудники");
    if (!matchesFilter) return false;
    if (!query.trim()) return true;
    return `${result.group} ${result.type} ${result.title} ${result.meta}`
      .toLowerCase()
      .includes(query.trim().toLowerCase());
  }), [filter, query]);
  const allowedResults = visibleResults.filter((result) => result.authorized);
  const unavailableResults = visibleResults.filter((result) => !result.authorized);

  function runSearch(event) {
    event?.preventDefault();
    setLoading(true);
    window.setTimeout(() => setLoading(false), 360);
  }

  function activate(result) {
    if (!result.authorized) {
      onToast("Результат недоступен. Защищённые данные и количество скрытых объектов не раскрываются.");
      return;
    }
    onOpenResult(result);
  }

  return (
    <section className="search-page" aria-labelledby="search-page-title">
      <div className="search-page__heading">
        <div>
          <p className="eyebrow">Wave C · FLOW-019</p>
          <h2 id="search-page-title">Поиск по Task</h2>
          <p>Полные результаты с фильтрами, permission-safe partial state и безопасным offline cache-only режимом.</p>
        </div>
        <button className="button button--secondary" type="button" disabled={offline || loading} onClick={runSearch}>
          <ArrowSyncRegular aria-hidden="true" />Обновить
        </button>
      </div>

      <form className="search-page__form" onSubmit={runSearch}>
        <SearchRegular aria-hidden="true" />
        <input
          autoFocus
          value={query}
          onChange={(event) => setQuery(event.target.value)}
          placeholder="Введите задачу, проект, файл, контакт или сотрудника"
          aria-label="Строка полного поиска"
        />
        {query && <button className="icon-button" type="button" aria-label="Очистить запрос" onClick={() => setQuery("")}><DismissRegular aria-hidden="true" /></button>}
        <button className="button button--primary" type="submit">Найти</button>
      </form>

      <div className="search-page__filters" aria-label="Фильтры полного поиска">
        <span><FilterRegular aria-hidden="true" />Тип результата</span>
        <div>
          {filterOptions.map((item) => (
            <button
              key={item}
              type="button"
              className={filter === item ? "is-active" : ""}
              aria-pressed={filter === item}
              onClick={() => setFilter(item)}
            >
              {item}
            </button>
          ))}
        </div>
      </div>

      {offline ? (
        <div className="search-page__notice search-page__notice--offline" role="status">
          <PlugDisconnectedRegular aria-hidden="true" />
          <span><strong>Offline · только разрешённый кэш</strong><small>Результаты могут быть неполными. Обновление и запись отключены до восстановления соединения.</small></span>
        </div>
      ) : (
        <div className="search-page__notice" role="status">
          <ShieldErrorRegular aria-hidden="true" />
          <span><strong>Permission-safe partial</strong><small>Показаны только доступные результаты. Скрытые объекты не включаются в количество и не раскрываются в подсказках.</small></span>
        </div>
      )}

      <div className="search-page__summary" aria-live="polite">
        <span><strong>{allowedResults.length}</strong> доступных результатов</span>
        <span>{offline ? "Кэш обновлён сегодня в 10:23" : "Область доступа проверена сервером"}</span>
      </div>

      <div className="search-page__results" aria-busy={loading}>
        {loading ? (
          <div className="search-loading" role="status" aria-label="Поиск выполняется">
            {[0, 1, 2].map((item) => <div key={item}><span /><p><span /><span /></p></div>)}
          </div>
        ) : allowedResults.length === 0 && unavailableResults.length === 0 ? (
          <div className="empty-state search-page__empty">
            <SearchRegular aria-hidden="true" />
            <strong>Ничего не найдено</strong>
            <span>Измените запрос или очистите выбранный тип результата.</span>
            <button className="button button--secondary" type="button" onClick={() => { setQuery(""); setFilter("Все"); }}>Сбросить фильтры</button>
          </div>
        ) : (
          <>
            <div className="search-page__list" role="list" aria-label="Доступные результаты">
              {allowedResults.map((result) => (
                <button key={result.id} type="button" role="listitem" className="search-page__result" onClick={() => activate(result)}>
                  <span className="search-result__icon">
                    {result.type === "Проект" ? <FolderRegular aria-hidden="true" /> : result.type === "Сотрудник" || result.type === "Контакт" ? <PersonRegular aria-hidden="true" /> : result.type === "Задача" ? <ClipboardTaskListLtrRegular aria-hidden="true" /> : <DocumentRegular aria-hidden="true" />}
                  </span>
                  <span><small>{result.group} · {result.type}</small><strong>{result.title}</strong><span>{result.meta}</span></span>
                  <ChevronRightRegular aria-hidden="true" />
                </button>
              ))}
            </div>
            {unavailableResults.length > 0 && (
              <section className="search-page__unavailable" aria-label="Недоступный результат">
                <div><LockClosedRegular aria-hidden="true" /><span><strong>Один из результатов недоступен</strong><small>Название, подразделение, совпавшие поля и общее количество скрытых объектов не раскрываются.</small></span></div>
                <button className="button button--quiet" type="button" onClick={() => onToast("Доступ проверяется сервером; оптимистичное открытие недоступно.")}>Почему недоступно</button>
              </section>
            )}
          </>
        )}
      </div>

      <footer className="search-page__footer">
        <span>Страница 1 · только доступные результаты</span>
        <div><button className="button button--secondary" type="button" disabled>Назад</button><button className="button button--secondary" type="button" disabled>Далее</button></div>
      </footer>
    </section>
  );
}


function LifecycleSurface({ offline, onToast }) {
  const [section, setSection] = useState("archive");
  const [query, setQuery] = useState("");
  const [typeFilter, setTypeFilter] = useState("Все типы");
  const [items, setItems] = useState(lifecycleSeed);
  const [selectedId, setSelectedId] = useState("archive-project-north");
  const [loading, setLoading] = useState(false);
  const [dialog, setDialog] = useState("");
  const [restoreName, setRestoreName] = useState("");
  const [restoreParent, setRestoreParent] = useState("Разрешённый корневой раздел");
  const [purgeText, setPurgeText] = useState("");
  const [operationStatus, setOperationStatus] = useState("");

  const visibleItems = useMemo(() => items.filter((item) => {
    if (item.section !== section) return false;
    if (typeFilter !== "Все типы" && item.type !== typeFilter) return false;
    if (!query.trim()) return true;
    if (item.authorized === false) return false;
    return `${item.type} ${item.title} ${item.meta}`.toLowerCase().includes(query.trim().toLowerCase());
  }), [items, query, section, typeFilter]);
  const selected = items.find((item) => item.id === selectedId && item.section === section) || visibleItems[0] || null;
  const allowedCount = visibleItems.filter((item) => item.authorized !== false).length;

  useEffect(() => {
    if (!visibleItems.some((item) => item.id === selectedId)) setSelectedId(visibleItems[0]?.id || "");
  }, [selectedId, visibleItems]);

  function selectSection(nextSection) {
    setSection(nextSection);
    setQuery("");
    setTypeFilter("Все типы");
    setOperationStatus("");
    setSelectedId(items.find((item) => item.section === nextSection)?.id || "");
  }

  function refresh() {
    if (offline) return;
    setLoading(true);
    setOperationStatus("");
    window.setTimeout(() => setLoading(false), 420);
  }

  function completeRestore() {
    if (!selected) return;
    setItems((current) => current.filter((item) => item.id !== selected.id));
    setDialog("");
    setOperationStatus("");
    setRestoreName("");
    onToast(`«${selected.title}» восстановлен после server recheck`);
  }

  function requestRestore() {
    if (!selected || offline) return;
    if (!selected.canRestore) {
      setOperationStatus("forbidden");
      return;
    }
    if (selected.restoreIssue) {
      setRestoreName(selected.title);
      setRestoreParent("Разрешённый корневой раздел");
      setDialog("restore");
      return;
    }
    completeRestore();
  }

  function confirmRestore() {
    if (!selected) return;
    if (selected.restoreIssue === "name" && restoreName.trim() === selected.title.trim()) {
      setOperationStatus("duplicate");
      return;
    }
    if (selected.restoreIssue === "parent" && !restoreParent) {
      setOperationStatus("parent");
      return;
    }
    completeRestore();
  }

  function requestPurge() {
    if (!selected || offline) return;
    if (selected.retentionBlocked) {
      setOperationStatus("retention");
      return;
    }
    if (!selected.canPurge) {
      setOperationStatus("forbidden-purge");
      return;
    }
    setPurgeText("");
    setDialog("purge");
  }

  function confirmPurge() {
    if (!selected) return;
    if (purgeText !== selected.title) {
      setOperationStatus("purge-name");
      return;
    }
    setItems((current) => current.filter((item) => item.id !== selected.id));
    setDialog("");
    setOperationStatus("");
    setPurgeText("");
    onToast(selected.type === "Файл"
      ? "Метаданные удалены необратимо; физический файл не затронут"
      : "Метаданные объекта удалены необратимо");
  }

  const typeIcon = (item) => item.authorized === false
    ? <LockClosedRegular aria-hidden="true" />
    : item.type === "Проект"
      ? <FolderRegular aria-hidden="true" />
      : item.type === "Файл"
        ? <DocumentRegular aria-hidden="true" />
        : <ClipboardTaskListLtrRegular aria-hidden="true" />;

  return (
    <section className="lifecycle-page" aria-labelledby="lifecycle-page-title">
      <header className="lifecycle-page__heading">
        <div>
          <p className="eyebrow">Wave C · FLOW-026–028 · FLOW-035</p>
          <h2 id="lifecycle-page-title">Архив и корзина</h2>
          <p>Единый lifecycle для задач, проектов и файлов с безопасным восстановлением и отдельным разрешением на purge.</p>
        </div>
        <button className="button button--secondary" type="button" disabled={offline || loading} onClick={refresh}>
          <ArrowSyncRegular aria-hidden="true" />{loading ? "Обновление…" : "Обновить"}
        </button>
      </header>

      <div className="lifecycle-tabs" role="tablist" aria-label="Раздел жизненного цикла">
        <button type="button" role="tab" aria-selected={section === "archive"} className={section === "archive" ? "is-active" : ""} onClick={() => selectSection("archive")}><ArchiveRegular aria-hidden="true" />Архив</button>
        <button type="button" role="tab" aria-selected={section === "trash"} className={section === "trash" ? "is-active" : ""} onClick={() => selectSection("trash")}><DeleteRegular aria-hidden="true" />Корзина</button>
      </div>

      {offline ? (
        <div className="lifecycle-banner lifecycle-banner--offline" role="status"><PlugDisconnectedRegular aria-hidden="true" /><span><strong>Offline · только разрешённый кэш</strong><small>Состав может быть неполным. Восстановление, purge и обновление отключены.</small></span></div>
      ) : (
        <div className="lifecycle-banner" role="status"><ShieldErrorRegular aria-hidden="true" /><span><strong>Область доступа проверена сервером</strong><small>Недоступные названия, родители и количество скрытых объектов не раскрываются.</small></span></div>
      )}

      <div className="lifecycle-toolbar">
        <label className="lifecycle-search"><SearchRegular aria-hidden="true" /><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder={`Поиск в разделе «${section === "archive" ? "Архив" : "Корзина"}»`} aria-label="Поиск по архиву и корзине" /></label>
        <label><span>Тип</span><select value={typeFilter} onChange={(event) => setTypeFilter(event.target.value)}><option>Все типы</option><option>Задача</option><option>Проект</option><option>Файл</option></select></label>
        <span className="lifecycle-count"><strong>{allowedCount}</strong> доступных объектов</span>
      </div>

      <div className="lifecycle-layout" aria-busy={loading}>
        <section className="lifecycle-list" aria-label={section === "archive" ? "Архивные объекты" : "Объекты в корзине"}>
          {loading ? (
            <div className="lifecycle-skeleton" role="status" aria-label="Обновление списка">{[0, 1, 2].map((item) => <span key={item} />)}</div>
          ) : visibleItems.length === 0 ? (
            <div className="empty-state lifecycle-empty"><ArchiveRegular aria-hidden="true" /><strong>Объектов не найдено</strong><span>Измените запрос или сбросьте фильтр типа.</span><button className="button button--secondary" type="button" onClick={() => { setQuery(""); setTypeFilter("Все типы"); }}>Сбросить фильтры</button></div>
          ) : visibleItems.map((item) => (
            <button key={item.id} type="button" className={`lifecycle-row ${selected?.id === item.id ? "is-selected" : ""} ${item.authorized === false ? "is-redacted" : ""}`} onClick={() => { setSelectedId(item.id); setOperationStatus(""); }} aria-pressed={selected?.id === item.id} title={item.title}>
              <span className="lifecycle-row__icon">{typeIcon(item)}</span>
              <span><small>{item.type}</small><strong title={item.title}>{item.title}</strong><span>{item.meta}</span></span>
              <ChevronRightRegular aria-hidden="true" />
            </button>
          ))}
        </section>

        <aside className="lifecycle-detail" aria-label="Карточка объекта">
          {!selected ? (
            <div className="empty-state"><ArchiveRegular aria-hidden="true" /><strong>Выберите объект</strong><span>Здесь появятся lifecycle-состояние и разрешённые действия.</span></div>
          ) : selected.authorized === false ? (
            <div className="lifecycle-redacted"><LockClosedRegular aria-hidden="true" /><h3>Архивный объект недоступен</h3><p>Название, владелец, связи, история и исходный раздел не раскрываются.</p><small>Forbidden · доступ проверяется сервером</small></div>
          ) : (
            <>
              <div className="lifecycle-detail__title">
                <span>{typeIcon(selected)}</span>
                <div><small>{selected.type} · {section === "archive" ? "Archived" : "Trashed"}</small><h3>{selected.title}</h3><p>{selected.meta}</p></div>
              </div>
              <div className="lifecycle-readonly"><LockClosedRegular aria-hidden="true" /><span><strong>Только чтение</strong><small>{selected.reason}</small></span></div>

              {operationStatus === "forbidden" && <div className="inline-alert inline-alert--warning" role="alert"><ShieldErrorRegular aria-hidden="true" /><span><strong>Forbidden</strong><small>Archive.Restore недоступно. Объект и история остаются только для чтения.</small></span></div>}
              {operationStatus === "forbidden-purge" && <div className="inline-alert inline-alert--warning" role="alert"><ShieldErrorRegular aria-hidden="true" /><span><strong>Trash.Purge недоступно</strong><small>Восстановление и purge проверяются раздельно.</small></span></div>}
              {operationStatus === "retention" && <div className="inline-alert inline-alert--warning lifecycle-retention" role="alert"><WarningRegular aria-hidden="true" /><span><strong>RetentionBlocked · purge запрещён</strong><small>Legal hold нельзя обойти. Task не удаляет объект и не показывает оптимистичный успех.</small></span><button className="button button--secondary" type="button" onClick={() => { setOperationStatus("retention"); onToast("Политика удержания проверена повторно: purge по-прежнему запрещён"); }}>Повторить проверку</button></div>}

              <section className="lifecycle-history" aria-labelledby="lifecycle-history-title"><h4 id="lifecycle-history-title"><HistoryRegular aria-hidden="true" />История</h4>{selected.history.map((entry) => <p key={entry}><span />{entry}</p>)}</section>

              <div className="lifecycle-actions">
                <button className="button button--primary" type="button" disabled={offline} onClick={requestRestore}><ArrowUndoRegular aria-hidden="true" />{section === "archive" ? "Разархивировать" : "Восстановить"}</button>
                {section === "trash" && <button className="button button--danger" type="button" disabled={offline} onClick={requestPurge}><DeleteRegular aria-hidden="true" />Удалить метаданные</button>}
              </div>
              <p className="helper-copy">Перед записью Task повторно проверит {section === "archive" ? "Archive.Restore" : "Trash.Restore"}{section === "trash" ? " и отдельное Trash.Purge" : ""}.</p>
            </>
          )}
        </aside>
      </div>

      {dialog === "restore" && selected && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog lifecycle-dialog" role="dialog" aria-modal="true" aria-labelledby="lifecycle-restore-title"><div className="dialog__header"><div><p className="eyebrow">Trash.Restore · server recheck</p><h2 id="lifecycle-restore-title">Разрешить конфликт восстановления</h2></div><button className="icon-button" type="button" aria-label="Закрыть" onClick={() => { setDialog(""); setOperationStatus(""); }}><DismissRegular aria-hidden="true" /></button></div>{selected.restoreIssue === "name" ? <><div className="inline-alert inline-alert--warning" role="alert"><WarningRegular aria-hidden="true" /><span><strong>DUPLICATE_RESOURCE · имя уже занято</strong><small>Укажите новое имя. Активный конфликтующий объект не раскрывается.</small></span></div><label className="field"><span>Новое имя</span><input autoFocus value={restoreName} onChange={(event) => { setRestoreName(event.target.value); setOperationStatus(""); }} /></label>{operationStatus === "duplicate" && <div className="error-message" role="alert">Имя должно отличаться от исходного.</div>}</> : <><div className="inline-alert inline-alert--warning" role="alert"><WarningRegular aria-hidden="true" /><span><strong>ParentUnavailable · исходный раздел недоступен</strong><small>Скрытый родитель и его путь не раскрываются. Выберите только разрешённое назначение.</small></span></div><label className="field"><span>Разрешённое назначение</span><select autoFocus value={restoreParent} onChange={(event) => setRestoreParent(event.target.value)}><option>Разрешённый корневой раздел</option><option>Личная папка «Восстановленные»</option></select></label></>}<div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => { setDialog(""); setOperationStatus(""); }}>Отмена</button><button className="button button--primary" type="button" onClick={confirmRestore}>Восстановить</button></div></section></div>}

      {dialog === "purge" && selected && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog lifecycle-dialog" role="dialog" aria-modal="true" aria-labelledby="lifecycle-purge-title"><div className="dialog__header"><div><p className="eyebrow">Trash.Purge · необратимо</p><h2 id="lifecycle-purge-title">Удалить метаданные без возможности восстановления</h2></div><button className="icon-button" type="button" aria-label="Закрыть" onClick={() => { setDialog(""); setOperationStatus(""); }}><DismissRegular aria-hidden="true" /></button></div><div className="inline-alert inline-alert--warning" role="alert"><WarningRegular aria-hidden="true" /><span><strong>Действие нельзя отменить</strong><small>{selected.type === "Файл" ? "Task удалит запись и историю метаданных, но не физический файл." : "Task удалит объект и его метаданные после повторной проверки разрешения и retention."}</small></span></div><label className="field"><span>Введите точное название: <strong>{selected.title}</strong></span><input autoFocus value={purgeText} onChange={(event) => { setPurgeText(event.target.value); setOperationStatus(""); }} /></label>{operationStatus === "purge-name" && <div className="error-message" role="alert">Подтверждение не совпадает с названием объекта.</div>}<div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => { setDialog(""); setOperationStatus(""); }}>Отмена</button><button className="button button--danger" type="button" disabled={purgeText !== selected.title} onClick={confirmPurge}>Удалить метаданные</button></div></section></div>}
    </section>
  );
}


function SettingsSurface({ offline, onToast, onForceSignIn, account }) {
  const sections = [
    { id: "profile", label: "Профиль", scope: "Личные", icon: PersonRegular },
    { id: "security", label: "Безопасность", scope: "Личные", icon: KeyRegular },
    { id: "notifications", label: "Уведомления и DND", scope: "Личные", icon: AlertRegular },
    { id: "calendar", label: "Календарь", scope: "Личные", icon: CalendarRegular },
    { id: "device", label: "Устройство и запуск", scope: "Это устройство", icon: SettingsRegular },
    { id: "cache", label: "Кэш и синхронизация", scope: "Это устройство", icon: DatabaseRegular },
    { id: "connection", label: "Подключение", scope: "Организация", icon: ServerRegular },
    { id: "accessibility", label: "Специальные возможности", scope: "Это устройство", icon: QuestionRegular },
    { id: "sessions", label: "Сессии и устройства", scope: "Личные", icon: LockClosedRegular },
  ];
  const [activeSection, setActiveSection] = useState("profile");
  const [profile, setProfile] = useState({ name: account.displayName, role: account.roleLabel, department: account.department, locale: "Русский", timezone: "Europe/Minsk" });
  const [passwords, setPasswords] = useState({ current: "", next: "", confirm: "" });
  const [passwordError, setPasswordError] = useState("");
  const [notifications, setNotifications] = useState({ desktop: true, sound: false, digest: true, dnd: true, quietFrom: "19:00", quietTo: "08:30" });
  const [quietError, setQuietError] = useState("");
  const [osDenied, setOsDenied] = useState(false);
  const [calendarPrefs, setCalendarPrefs] = useState({ firstDay: "Понедельник", workFrom: "09:00", workTo: "18:00", defaultView: "Неделя" });
  const [devicePrefs, setDevicePrefs] = useState({ autostart: true, tray: true, closeToTray: false });
  const [windowsDenied, setWindowsDenied] = useState(false);
  const [syncState, setSyncState] = useState("ready");
  const [connectionIssue, setConnectionIssue] = useState("");
  const [accessibilityPrefs, setAccessibilityPrefs] = useState({ scale: "100%", reducedMotion: false, strongFocus: true });
  const [saveState, setSaveState] = useState("");
  const [dialog, setDialog] = useState("");
  const [revokedSessions, setRevokedSessions] = useState([]);
  const [deviceRevoked, setDeviceRevoked] = useState(false);
  const [loading, setLoading] = useState(false);
  const currentSection = sections.find((item) => item.id === activeSection);
  const isWritable = !offline && !["conflict", "forbidden"].includes(saveState) && !deviceRevoked;

  function saveProfile() {
    if (!profile.name.trim()) {
      setSaveState("validation");
      return;
    }
    setSaveState("saved");
    onToast("Личные настройки сохранены после server recheck");
  }

  function changePassword() {
    setPasswordError("");
    if (passwords.current !== "Task-2026") {
      setPasswordError("INVALID_CREDENTIALS · Текущий пароль неверен.");
      return;
    }
    if (passwords.next.length < 10 || passwords.next !== passwords.confirm) {
      setPasswordError("Пароль должен содержать не менее 10 символов; подтверждение должно совпадать.");
      return;
    }
    setPasswords({ current: "", next: "", confirm: "" });
    onToast("Пароль изменён; другие сессии сохранены");
  }

  function saveNotifications() {
    setQuietError("");
    if (notifications.dnd && notifications.quietFrom === notifications.quietTo) {
      setQuietError("ValidationError · начало и конец тихих часов не могут совпадать.");
      return;
    }
    onToast("Настройки уведомлений сохранены");
  }

  function refreshSync() {
    if (offline) return;
    setSyncState("syncing");
    window.setTimeout(() => {
      setSyncState("ready");
      onToast("Разрешённый кэш синхронизирован");
    }, 480);
  }

  function refreshSettings() {
    if (offline) return;
    setLoading(true);
    window.setTimeout(() => setLoading(false), 380);
  }

  function renderProfile() {
    return <>
      <div className="settings-form-grid">
        <label className="field"><span>Имя для отображения</span><input value={profile.name} disabled={!isWritable} onChange={(event) => { setProfile((value) => ({ ...value, name: event.target.value })); setSaveState(""); }} /></label>
        <label className="field"><span>Язык интерфейса</span><select value={profile.locale} disabled={!isWritable} onChange={(event) => setProfile((value) => ({ ...value, locale: event.target.value }))}><option>Русский</option><option>English</option></select></label>
        <label className="field"><span>Часовой пояс</span><select value={profile.timezone} disabled={!isWritable} onChange={(event) => setProfile((value) => ({ ...value, timezone: event.target.value }))}><option>Europe/Minsk</option><option>Europe/Moscow</option></select></label>
        <label className="field settings-managed"><span>Роль</span><input value={profile.role} disabled /><small>Управляется организацией</small></label>
        <label className="field settings-managed settings-span-two"><span>Подразделение</span><input value={profile.department} disabled /><small>Server-managed field · изменение доступно администратору с User.Update.</small></label>
      </div>
      {saveState === "validation" && <div className="error-message" role="alert">ValidationError · укажите имя для отображения.</div>}
      <div className="settings-actions"><button className="button button--primary" type="button" disabled={!isWritable} onClick={saveProfile}><SaveRegular aria-hidden="true" />Сохранить профиль</button><button className="button button--secondary" type="button" disabled={offline} onClick={() => setSaveState("conflict")}>Проверить конфликт</button><button className="button button--secondary" type="button" disabled={offline} onClick={() => setSaveState("forbidden")}>Проверить запрет</button></div>
    </>;
  }

  function renderSecurity() {
    return <>
      <section className="settings-card"><h4>Смена пароля</h4><p>Пароль проверяется сервером; Task не показывает сохранённые credential fields.</p><div className="settings-form-grid"><label className="field settings-span-two"><span>Текущий пароль</span><input type="password" autoComplete="current-password" value={passwords.current} disabled={!isWritable} onChange={(event) => setPasswords((value) => ({ ...value, current: event.target.value }))} /></label><label className="field"><span>Новый пароль</span><input type="password" autoComplete="new-password" value={passwords.next} disabled={!isWritable} onChange={(event) => setPasswords((value) => ({ ...value, next: event.target.value }))} /></label><label className="field"><span>Подтверждение</span><input type="password" autoComplete="new-password" value={passwords.confirm} disabled={!isWritable} onChange={(event) => setPasswords((value) => ({ ...value, confirm: event.target.value }))} /></label></div>{passwordError && <div className="error-message" role="alert">{passwordError}</div>}<div className="settings-actions"><button className="button button--primary" type="button" disabled={!isWritable} onClick={changePassword}>Изменить пароль</button></div></section>
      <section className="settings-card settings-card--danger"><h4>Активные входы</h4><p>Можно завершить другие сессии или выйти на всех устройствах. Текущая сессия помечена отдельно, чтобы исключить случайную потерю работы.</p><button className="button button--danger" type="button" disabled={!isWritable} onClick={() => setDialog("logout-all")}>Управлять завершением сессий</button></section>
    </>;
  }

  function renderNotifications() {
    return <>
      {osDenied && <div className="inline-alert inline-alert--warning" role="alert"><AlertRegular aria-hidden="true" /><span><strong>Windows запретила системные уведомления</strong><small>Task не может изменить разрешение самостоятельно.</small></span><button className="button button--secondary" type="button" onClick={() => onToast("Переход в параметры Windows доступен только в desktop-клиенте")}>Открыть параметры Windows</button></div>}
      <div className="settings-toggle-list">
        <label><span><strong>Системные уведомления</strong><small>Баннеры Windows для разрешённых событий</small></span><input type="checkbox" checked={notifications.desktop} disabled={!isWritable} onChange={(event) => setNotifications((value) => ({ ...value, desktop: event.target.checked }))} /></label>
        <label><span><strong>Звук</strong><small>Звуковой сигнал без раскрытия содержимого</small></span><input type="checkbox" checked={notifications.sound} disabled={!isWritable} onChange={(event) => setNotifications((value) => ({ ...value, sound: event.target.checked }))} /></label>
        <label><span><strong>Ежедневная сводка</strong><small>Личная сводка в Task</small></span><input type="checkbox" checked={notifications.digest} disabled={!isWritable} onChange={(event) => setNotifications((value) => ({ ...value, digest: event.target.checked }))} /></label>
      </div>
      <section className="settings-card"><h4>Не беспокоить</h4><label className="settings-check"><input type="checkbox" checked={notifications.dnd} disabled={!isWritable} onChange={(event) => setNotifications((value) => ({ ...value, dnd: event.target.checked }))} />Использовать тихие часы</label><div className="settings-time-row"><label className="field"><span>С</span><input type="time" value={notifications.quietFrom} disabled={!isWritable || !notifications.dnd} onChange={(event) => setNotifications((value) => ({ ...value, quietFrom: event.target.value }))} /></label><label className="field"><span>До</span><input type="time" value={notifications.quietTo} disabled={!isWritable || !notifications.dnd} onChange={(event) => setNotifications((value) => ({ ...value, quietTo: event.target.value }))} /></label></div>{quietError && <div className="error-message" role="alert">{quietError}</div>}</section>
      <div className="settings-actions"><button className="button button--primary" type="button" disabled={!isWritable} onClick={saveNotifications}>Сохранить</button><button className="button button--secondary" type="button" disabled={offline} onClick={() => setOsDenied(true)}>Проверить доступ Windows</button><button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => { setNotifications((value) => ({ ...value, dnd: true, quietTo: value.quietFrom })); setQuietError("ValidationError · начало и конец тихих часов не могут совпадать."); }}>Проверить invalid hours</button></div>
    </>;
  }

  function renderCalendar() {
    return <><div className="settings-form-grid"><label className="field"><span>Первый день недели</span><select value={calendarPrefs.firstDay} disabled={!isWritable} onChange={(event) => setCalendarPrefs((value) => ({ ...value, firstDay: event.target.value }))}><option>Понедельник</option><option>Воскресенье</option></select></label><label className="field"><span>Вид по умолчанию</span><select value={calendarPrefs.defaultView} disabled={!isWritable} onChange={(event) => setCalendarPrefs((value) => ({ ...value, defaultView: event.target.value }))}><option>День</option><option>Неделя</option><option>Месяц</option></select></label><label className="field"><span>Рабочий день с</span><input type="time" value={calendarPrefs.workFrom} disabled={!isWritable} onChange={(event) => setCalendarPrefs((value) => ({ ...value, workFrom: event.target.value }))} /></label><label className="field"><span>Рабочий день до</span><input type="time" value={calendarPrefs.workTo} disabled={!isWritable} onChange={(event) => setCalendarPrefs((value) => ({ ...value, workTo: event.target.value }))} /></label></div><div className="settings-actions"><button className="button button--primary" type="button" disabled={!isWritable} onClick={() => onToast("Настройки календаря сохранены")}>Сохранить</button><button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => { setCalendarPrefs({ firstDay: "Понедельник", workFrom: "09:00", workTo: "18:00", defaultView: "Неделя" }); onToast("Восстановлены значения организации по умолчанию"); }}>Сбросить к настройкам организации</button></div></>;
  }

  function renderDevice() {
    return <>{windowsDenied && <div className="inline-alert inline-alert--warning" role="alert"><WarningRegular aria-hidden="true" /><span><strong>Windows отклонила автозапуск</strong><small>Политика устройства запрещает изменение. Остальные настройки не потеряны.</small></span></div>}<div className="settings-toggle-list"><label><span><strong>Запускать Task при входе в Windows</strong><small>Локальная настройка этого устройства</small></span><input type="checkbox" checked={devicePrefs.autostart} disabled={!isWritable || windowsDenied} onChange={(event) => setDevicePrefs((value) => ({ ...value, autostart: event.target.checked }))} /></label><label><span><strong>Показывать значок в области уведомлений</strong><small>Не влияет на серверные уведомления</small></span><input type="checkbox" checked={devicePrefs.tray} disabled={!isWritable} onChange={(event) => setDevicePrefs((value) => ({ ...value, tray: event.target.checked }))} /></label><label><span><strong>Сворачивать в область уведомлений при закрытии</strong><small>Физическое завершение приложения остаётся отдельным действием</small></span><input type="checkbox" checked={devicePrefs.closeToTray} disabled={!isWritable || !devicePrefs.tray} onChange={(event) => setDevicePrefs((value) => ({ ...value, closeToTray: event.target.checked }))} /></label></div><div className="settings-actions"><button className="button button--primary" type="button" disabled={!isWritable} onClick={() => onToast("Настройки устройства сохранены локально")}>Сохранить</button><button className="button button--secondary" type="button" disabled={offline} onClick={() => setWindowsDenied(true)}>Проверить ограничение Windows</button></div></>;
  }

  function renderCache() {
    return <><div className="settings-stat-grid"><article><span>Разрешённый кэш</span><strong>684 МБ</strong><small>Последнее обновление сегодня в 10:23</small></article><article><span>Синхронизация</span><strong>{syncState === "syncing" ? "Выполняется…" : syncState === "expired" ? "Нужен bootstrap" : "Актуально"}</strong><small>{syncState === "expired" ? "SYNC_CURSOR_EXPIRED" : "Область доступа проверена"}</small></article></div>{syncState === "expired" && <div className="inline-alert inline-alert--warning" role="alert"><ArrowSyncRegular aria-hidden="true" /><span><strong>SYNC_CURSOR_EXPIRED</strong><small>Локальный cursor устарел. Task повторно загрузит только разрешённую область.</small></span><button className="button button--secondary" type="button" onClick={refreshSync}>Начать bootstrap</button></div>}<div className="settings-actions"><button className="button button--primary" type="button" disabled={offline || syncState === "syncing"} onClick={refreshSync}>{syncState === "syncing" ? "Синхронизация…" : "Синхронизировать"}</button><button className="button button--secondary" type="button" disabled={offline} onClick={() => setSyncState("expired")}>Проверить устаревший cursor</button><button className="button button--danger" type="button" disabled={!isWritable} onClick={() => setDialog("clear-cache")}>Очистить кэш</button></div><p className="helper-copy">Очистка удалит только разрешённую локальную копию. Серверные задачи, проекты и file metadata не удаляются.</p></>;
  }

  function renderConnection() {
    const issueCopy = connectionIssue === "tls" ? ["TLS_ERROR", "Сертификат сервера не прошёл проверку. Подмена endpoint запрещена."] : connectionIssue === "version" ? ["CLIENT_VERSION_UNSUPPORTED", "Версия клиента несовместима; запись заблокирована до обновления."] : ["", ""];
    return <><div className="settings-form-grid"><label className="field settings-managed settings-span-two"><span>Сервер организации</span><input value="https://task.company.local" disabled /><small>Управляется организацией; endpoint нельзя заменить локально.</small></label></div><div className={`settings-connection ${connectionIssue ? "is-error" : ""}`}><ServerRegular aria-hidden="true" /><span><strong>{connectionIssue ? issueCopy[0] : "Подключение защищено"}</strong><small>{connectionIssue ? issueCopy[1] : "TLS проверен · сервер доступен · client 1.4.2 поддерживается"}</small></span></div>{connectionIssue && <div className="safe-report"><strong>Ограниченная диагностика</strong><span>endpoint: organization-managed; account: redacted; result: {issueCopy[0]}; request id: unavailable.</span></div>}<div className="settings-actions"><button className="button button--primary" type="button" disabled={offline} onClick={() => { setConnectionIssue(""); onToast("Подключение проверено повторно"); }}>Проверить подключение</button><button className="button button--secondary" type="button" onClick={() => setConnectionIssue("tls")}>Проверить TLS error</button><button className="button button--secondary" type="button" onClick={() => setConnectionIssue("version")}>Проверить версию</button>{connectionIssue && <button className="button button--quiet" type="button" onClick={() => onToast("Безопасный диагностический отчёт скопирован без endpoint и account data")}>Копировать безопасный отчёт</button>}</div></>;
  }

  function renderAccessibility() {
    const restartRequired = accessibilityPrefs.scale !== "100%";
    return <><div className="settings-form-grid"><label className="field"><span>Масштаб текста Task</span><select value={accessibilityPrefs.scale} disabled={!isWritable} onChange={(event) => setAccessibilityPrefs((value) => ({ ...value, scale: event.target.value }))}><option>100%</option><option>125%</option><option>150%</option><option>200%</option></select></label></div><div className="settings-toggle-list"><label><span><strong>Усиленный индикатор фокуса</strong><small>Дополнительная рамка для keyboard navigation</small></span><input type="checkbox" checked={accessibilityPrefs.strongFocus} disabled={!isWritable} onChange={(event) => setAccessibilityPrefs((value) => ({ ...value, strongFocus: event.target.checked }))} /></label><label><span><strong>Уменьшить анимацию</strong><small>Состояния сохраняют текстовые и non-color признаки</small></span><input type="checkbox" checked={accessibilityPrefs.reducedMotion} disabled={!isWritable} onChange={(event) => setAccessibilityPrefs((value) => ({ ...value, reducedMotion: event.target.checked }))} /></label></div>{restartRequired && <div className="inline-alert" role="status"><ArrowSyncRegular aria-hidden="true" /><span><strong>Требуется перезапуск</strong><small>Новый масштаб будет применён после перезапуска desktop-клиента.</small></span></div>}<div className="settings-actions"><button className="button button--primary" type="button" disabled={!isWritable} onClick={() => onToast(restartRequired ? "Настройки сохранены; перезапустите Task" : "Настройки доступности сохранены")}>Сохранить</button></div></>;
  }

  function renderSessions() {
    return <>{deviceRevoked && <div className="inline-alert inline-alert--warning" role="alert"><LockClosedRegular aria-hidden="true" /><span><strong>DEVICE_REVOKED · это устройство отозвано</strong><small>Локальный кэш заблокирован. Требуется повторный вход.</small></span><button className="button button--primary" type="button" onClick={onForceSignIn}>Войти снова</button></div>}<div className="settings-session-list"><article><span className="settings-session-icon"><SettingsRegular aria-hidden="true" /></span><span><strong>Windows 11 · этот компьютер</strong><small>Текущая сессия · Минск · сейчас</small></span><em>Текущая</em><button className="button button--secondary" type="button" disabled title="Текущую сессию можно завершить только через выход">Защищена</button></article><article className={revokedSessions.includes("laptop") ? "is-revoked" : ""}><span className="settings-session-icon"><SettingsRegular aria-hidden="true" /></span><span><strong>Ноутбук отдела продаж</strong><small>{revokedSessions.includes("laptop") ? "SESSION_REVOKED" : "Windows 11 · последняя активность вчера, 18:42"}</small></span><em>{revokedSessions.includes("laptop") ? "Завершена" : "Активна"}</em><button className="button button--danger" type="button" disabled={!isWritable || revokedSessions.includes("laptop")} onClick={() => setDialog("revoke-session")}>Завершить</button></article></div><div className="settings-actions"><button className="button button--secondary" type="button" disabled={offline} onClick={() => setDeviceRevoked(true)}>Проверить отзыв устройства</button></div></>;
  }

  function renderActivePanel() {
    if (activeSection === "profile") return renderProfile();
    if (activeSection === "security") return renderSecurity();
    if (activeSection === "notifications") return renderNotifications();
    if (activeSection === "calendar") return renderCalendar();
    if (activeSection === "device") return renderDevice();
    if (activeSection === "cache") return renderCache();
    if (activeSection === "connection") return renderConnection();
    if (activeSection === "accessibility") return renderAccessibility();
    return renderSessions();
  }

  return (
    <section className="settings-page" aria-labelledby="settings-page-title">
      <header className="settings-page__heading"><div><p className="eyebrow">Wave C · SCR-150–159 · SCR-161</p><h2 id="settings-page-title">Настройки</h2><p>Личные, локальные и server-managed параметры с явными scope, recovery и destructive boundaries.</p></div><button className="button button--secondary" type="button" disabled={offline || loading} onClick={refreshSettings}><ArrowSyncRegular aria-hidden="true" />{loading ? "Обновление…" : "Обновить"}</button></header>
      {offline && <div className="settings-banner settings-banner--offline" role="status"><PlugDisconnectedRegular aria-hidden="true" /><span><strong>Offline · настройки только для чтения</strong><small>Показана разрешённая локальная копия. Сохранение, revoke, очистка кэша и серверная проверка отключены.</small></span></div>}
      {deviceRevoked && <div className="settings-banner settings-banner--offline" role="alert"><LockClosedRegular aria-hidden="true" /><span><strong>Доступ этого устройства отозван</strong><small>Изменения заблокированы до повторного входа.</small></span></div>}
      <div className="settings-layout" aria-busy={loading}>
        <nav className="settings-nav" aria-label="Разделы настроек">{sections.map((item) => { const Icon = item.icon; return <button key={item.id} type="button" className={activeSection === item.id ? "is-active" : ""} aria-current={activeSection === item.id ? "page" : undefined} onClick={() => { setActiveSection(item.id); setSaveState(""); }} title={item.label}><Icon aria-hidden="true" /><span><strong>{item.label}</strong><small>{item.scope}</small></span><ChevronRightRegular aria-hidden="true" /></button>; })}</nav>
        <main className="settings-panel"><header><div><small>{currentSection.scope}</small><h3>{currentSection.label}</h3></div><span className={`settings-scope ${currentSection.scope === "Организация" ? "is-managed" : ""}`}>{currentSection.scope === "Организация" ? "Server-managed" : "Settings.UpdateOwn"}</span></header>{saveState === "conflict" && <div className="inline-alert inline-alert--warning settings-conflict" role="alert"><WarningRegular aria-hidden="true" /><span><strong>VERSION_CONFLICT · настройки изменились на сервере</strong><small>Локальное сохранение отменено. Выберите актуальную версию или повторите свои изменения после reload.</small></span><button className="button button--secondary" type="button" onClick={() => { setSaveState(""); onToast("Загружена актуальная версия настроек"); }}>Загрузить серверную</button><button className="button button--primary" type="button" onClick={() => { setSaveState(""); onToast("Локальные изменения повторно применены после server recheck"); }}>Повторить свои</button></div>}{saveState === "forbidden" && <div className="inline-alert inline-alert--warning" role="alert"><ShieldErrorRegular aria-hidden="true" /><span><strong>Forbidden · Settings.UpdateOwn недоступно</strong><small>Раздел остаётся доступным только для чтения. Task не применяет оптимистичное сохранение.</small></span><button className="button button--secondary" type="button" onClick={() => setSaveState("")}>Закрыть</button></div>}{loading ? <div className="settings-loading" role="status" aria-label="Настройки обновляются"><span /><span /><span /></div> : renderActivePanel()}</main>
      </div>

      {dialog === "logout-all" && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog settings-dialog" role="dialog" aria-modal="true" aria-labelledby="logout-all-title"><div className="dialog__header"><div><p className="eyebrow">Session.ReadOwn · destructive</p><h2 id="logout-all-title">Завершить активные сессии</h2></div><button className="icon-button" type="button" aria-label="Закрыть" onClick={() => setDialog("")}><DismissRegular aria-hidden="true" /></button></div><div className="inline-alert inline-alert--warning"><WarningRegular aria-hidden="true" /><span><strong>Выход на всех устройствах включает текущую сессию</strong><small>Несохранённые локальные черновики могут стать недоступны. Завершение других сессий оставляет текущую защищённой.</small></span></div><div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => setDialog("")}>Отмена</button><button className="button button--secondary" type="button" onClick={() => { setDialog(""); setRevokedSessions(["laptop"]); onToast("Другие сессии завершены; текущая сохранена"); }}>Завершить другие</button><button className="button button--danger" type="button" onClick={() => { setDialog(""); onForceSignIn(); }}>Выйти на всех устройствах</button></div></section></div>}
      {dialog === "clear-cache" && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog settings-dialog" role="dialog" aria-modal="true" aria-labelledby="clear-cache-title"><div className="dialog__header"><div><p className="eyebrow">Local cache</p><h2 id="clear-cache-title">Очистить разрешённый кэш</h2></div><button className="icon-button" type="button" aria-label="Закрыть" onClick={() => setDialog("")}><DismissRegular aria-hidden="true" /></button></div><p>Будет удалена только локальная копия на этом устройстве. Серверные данные и физические файлы останутся без изменений.</p><div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => setDialog("")}>Отмена</button><button className="button button--danger" type="button" onClick={() => { setDialog(""); setSyncState("expired"); onToast("Кэш очищен; для продолжения нужен безопасный bootstrap"); }}>Очистить кэш</button></div></section></div>}
      {dialog === "revoke-session" && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog settings-dialog" role="dialog" aria-modal="true" aria-labelledby="revoke-session-title"><div className="dialog__header"><div><p className="eyebrow">Session.ReadOwn</p><h2 id="revoke-session-title">Завершить сессию ноутбука</h2></div><button className="icon-button" type="button" aria-label="Закрыть" onClick={() => setDialog("")}><DismissRegular aria-hidden="true" /></button></div><p>Сессия потеряет доступ при следующей серверной проверке. Текущая сессия на этом компьютере не будет затронута.</p><div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => setDialog("")}>Отмена</button><button className="button button--danger" type="button" onClick={() => { setDialog(""); setRevokedSessions(["laptop"]); onToast("Сессия ноутбука завершена"); }}>Завершить сессию</button></div></section></div>}
    </section>
  );
}


function AdminSurface({ offline, onToast }) {
  const sectionDefinitions = [
    { id: "users", label: "Пользователи", capability: "User.Read", icon: PersonRegular },
    { id: "departments", label: "Подразделения", capability: "Department.Read", icon: BranchForkRegular },
    { id: "roles", label: "Роли и права", capability: "Role.Read", icon: KeyRegular },
    { id: "sessions", label: "Сессии и устройства", capability: "Session.ReadOwnOrAll", icon: LockClosedRegular },
    { id: "resources", label: "Сетевые ресурсы", capability: "NetworkResource.Manage", icon: ServerRegular },
  ];
  const [activeSection, setActiveSection] = useState("users");
  const [limitedMode, setLimitedMode] = useState(false);
  const [loading, setLoading] = useState(false);
  const [query, setQuery] = useState("");
  const [dialog, setDialog] = useState("");
  const [alertState, setAlertState] = useState("");
  const [validation, setValidation] = useState("");
  const [users, setUsers] = useState([
    { id: "ivan", name: "Иван Сергеев", login: "ivan.s", department: "Отдел продаж", role: "Системный администратор", status: "Активен", current: true, lastAdmin: false, authorized: true },
    { id: "anna", name: "Анна Крылова", login: "anna.k", department: "Маркетинг", role: "Руководитель", status: "Активен", current: false, lastAdmin: false, authorized: true },
    { id: "olga", name: "Ольга Дмитриева", login: "olga.d", department: "ИТ", role: "Системный администратор", status: "Активен", current: false, lastAdmin: true, authorized: true },
    { id: "blocked", name: "Павел Морозов", login: "p.morozov", department: "Поддержка", role: "Сотрудник", status: "Заблокирован", current: false, lastAdmin: false, authorized: true },
    { id: "restricted", name: "Пользователь недоступен", login: "redacted", department: "Недоступно", role: "Недоступно", status: "Вне области", current: false, lastAdmin: false, authorized: false },
  ]);
  const [selectedUserId, setSelectedUserId] = useState("anna");
  const [userDraft, setUserDraft] = useState({ name: "", login: "", department: "Отдел продаж", role: "Сотрудник" });
  const [departments, setDepartments] = useState([
    { id: "sales", name: "Отдел продаж", parent: "Коммерческий блок", manager: "Анна Крылова", people: 18, children: 2, status: "Активно", authorized: true },
    { id: "marketing", name: "Маркетинг", parent: "Коммерческий блок", manager: "Мария С.", people: 9, children: 0, status: "Активно", authorized: true },
    { id: "support", name: "Поддержка", parent: "Операционный блок", manager: "Игорь В.", people: 12, children: 0, status: "Активно", authorized: true },
    { id: "hidden-dept", name: "Подразделение недоступно", parent: "Скрытый родитель", manager: "Недоступно", people: null, children: null, status: "Вне области", authorized: false },
  ]);
  const [selectedDepartmentId, setSelectedDepartmentId] = useState("sales");
  const [roles, setRoles] = useState([
    { id: "system-admin", name: "Системный администратор", members: 2, system: true, description: "Неизменяемая системная роль", permissions: ["User.Read", "Role.Read", "System.HealthRead", "Backup.Read"] },
    { id: "manager", name: "Руководитель отдела", members: 8, system: false, description: "Управление задачами и участниками своего отдела", permissions: ["User.Read", "Project.ManageMembers", "Task.Update"] },
    { id: "auditor", name: "Аудитор", members: 3, system: false, description: "Чтение истории и security audit", permissions: ["History.Read", "SecurityAudit.Read"] },
  ]);
  const [selectedRoleId, setSelectedRoleId] = useState("manager");
  const [dangerousPermission, setDangerousPermission] = useState(false);
  const [effectiveMode, setEffectiveMode] = useState("");
  const [sessions, setSessions] = useState([
    { id: "session-current", user: "Иван Сергеев", device: "WORKSTATION-17", state: "Текущая", heartbeat: "сейчас", tone: "online", current: true },
    { id: "session-anna", user: "Анна Крылова", device: "LAPTOP-SALES-04", state: "Активна", heartbeat: "2 мин назад", tone: "online", current: false },
    { id: "session-stale", user: "Павел Морозов", device: "WORKSTATION-31", state: "Сердцебиение устарело", heartbeat: "3 часа назад", tone: "warning", current: false },
    { id: "session-risk", user: "Ольга Дмитриева", device: "UNKNOWN-DEVICE", state: "Подозрительная", heartbeat: "18 мин назад", tone: "danger", current: false },
  ]);
  const [sessionFilter, setSessionFilter] = useState("Все состояния");
  const [selectedSessionId, setSelectedSessionId] = useState("session-anna");
  const [resources, setResources] = useState([
    { id: "shared", name: "Общие документы", path: "\\\\fileserver\\shared", state: "Доступен", enabled: true },
    { id: "sales-files", name: "Продажи", path: "\\\\fileserver\\departments\\sales", state: "Доступен", enabled: true },
    { id: "archive-files", name: "Архив отчётов", path: "\\\\old-storage\\reports", state: "NETWORK_RESOURCE_UNAVAILABLE", enabled: true },
  ]);
  const [selectedResourceId, setSelectedResourceId] = useState("shared");
  const [resourceDraft, setResourceDraft] = useState({ name: "", path: "" });

  const visibleSections = limitedMode ? sectionDefinitions.filter((item) => ["users", "sessions"].includes(item.id)) : sectionDefinitions;
  const selectedUser = users.find((item) => item.id === selectedUserId) || users[0];
  const visibleUsers = users.filter((item) => {
    if (!query.trim()) return true;
    if (!item.authorized) return false;
    return `${item.name} ${item.login} ${item.department} ${item.role}`.toLowerCase().includes(query.trim().toLowerCase());
  });
  const selectedDepartment = departments.find((item) => item.id === selectedDepartmentId) || departments[0];
  const selectedRole = roles.find((item) => item.id === selectedRoleId) || roles[0];
  const filteredSessions = sessions.filter((item) => sessionFilter === "Все состояния" || item.state === sessionFilter);
  const selectedSession = sessions.find((item) => item.id === selectedSessionId) || sessions[0];
  const selectedResource = resources.find((item) => item.id === selectedResourceId) || resources[0];
  const isWritable = !offline && !limitedMode;

  useEffect(() => {
    if (!visibleSections.some((item) => item.id === activeSection)) setActiveSection("users");
  }, [activeSection, visibleSections]);

  function refreshAdmin() {
    if (offline) return;
    setLoading(true);
    window.setTimeout(() => setLoading(false), 420);
  }

  function requestUserAction(action) {
    setAlertState("");
    if (!selectedUser.authorized) {
      setAlertState("object-unavailable");
      return;
    }
    if (selectedUser.current) {
      setAlertState("self-lockout");
      return;
    }
    if (selectedUser.lastAdmin) {
      setAlertState("last-admin");
      return;
    }
    setDialog(action);
  }

  function confirmUserAction(action) {
    setUsers((items) => items.map((item) => item.id === selectedUser.id ? { ...item, status: action === "block-user" ? "Заблокирован" : "Деактивирован" } : item));
    setDialog("");
    onToast(action === "block-user" ? "Пользователь заблокирован; активные сессии отозваны" : "Пользователь деактивирован после server recheck");
  }

  function createUser() {
    setValidation("");
    if (!userDraft.name.trim() || !userDraft.login.trim()) {
      setValidation("ValidationError · укажите имя и логин.");
      return;
    }
    if (users.some((item) => item.login.toLowerCase() === userDraft.login.trim().toLowerCase())) {
      setValidation("DUPLICATE_RESOURCE · такой логин уже существует.");
      return;
    }
    const created = { id: `user-${Date.now()}`, ...userDraft, status: "Активен", current: false, lastAdmin: false, authorized: true };
    setUsers((items) => [created, ...items]);
    setSelectedUserId(created.id);
    setDialog("");
    setUserDraft({ name: "", login: "", department: "Отдел продаж", role: "Сотрудник" });
    onToast("Пользователь создан после server recheck");
  }

  function renderUsers() {
    return <div className="admin-split"><section className="admin-list-panel"><div className="admin-list-toolbar"><label><SearchRegular aria-hidden="true" /><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Имя, логин, подразделение" aria-label="Поиск пользователей" /></label><button className="button button--primary" type="button" disabled={!isWritable} onClick={() => { setValidation(""); setDialog("create-user"); }}><AddRegular aria-hidden="true" />Создать</button></div>{visibleUsers.length === 0 ? <div className="empty-state"><PersonRegular aria-hidden="true" /><strong>Пользователи не найдены</strong><span>Измените запрос или очистите фильтр.</span><button className="button button--secondary" type="button" onClick={() => setQuery("")}>Сбросить</button></div> : <div className="admin-list" role="list" aria-label="Пользователи">{visibleUsers.map((user) => <button key={user.id} type="button" role="listitem" className={`${selectedUser.id === user.id ? "is-selected" : ""} ${!user.authorized ? "is-redacted" : ""}`} onClick={() => { setSelectedUserId(user.id); setAlertState(""); }}><span className="admin-avatar">{user.authorized ? user.name.split(" ").map((part) => part[0]).slice(0, 2).join("") : <LockClosedRegular aria-hidden="true" />}</span><span><strong>{user.name}</strong><small>{user.authorized ? `${user.login} · ${user.department}` : "Protected fields are redacted"}</small></span><em>{user.status}</em></button>)}</div>}</section><aside className="admin-inspector">{!selectedUser.authorized ? <div className="admin-redacted"><LockClosedRegular aria-hidden="true" /><h3>Пользователь недоступен</h3><p>Имя, логин, подразделение, роль, события входа и количество связанных объектов не раскрываются.</p><small>ObjectUnavailable · capability recheck required</small></div> : <><header><div><small>User.Read · User.Update · User.Block</small><h3>{selectedUser.name}</h3><p>{selectedUser.login} · {selectedUser.status}</p></div><span className={`semantic-badge ${selectedUser.status === "Активен" ? "is-success" : "is-warning"}`}>{selectedUser.status}</span></header><dl className="admin-facts"><div><dt>Подразделение</dt><dd>{selectedUser.department}</dd></div><div><dt>Роль</dt><dd>{selectedUser.role}</dd></div><div><dt>Последний вход</dt><dd>Сегодня, 10:18 · разрешённая область</dd></div><div><dt>Версия</dt><dd>v12 · сервер authoritative</dd></div></dl>{alertState === "self-lockout" && <div className="inline-alert inline-alert--warning" role="alert"><WarningRegular aria-hidden="true" /><span><strong>Self-lockout запрещён</strong><small>Нельзя заблокировать или деактивировать собственную учётную запись из текущей сессии.</small></span></div>}{alertState === "last-admin" && <div className="inline-alert inline-alert--warning" role="alert"><WarningRegular aria-hidden="true" /><span><strong>INVALID_STATE_TRANSITION · последний администратор</strong><small>Сначала назначьте другого системного администратора.</small></span></div>}{alertState === "version" && <div className="inline-alert inline-alert--warning" role="alert"><ArrowSyncRegular aria-hidden="true" /><span><strong>VERSION_CONFLICT</strong><small>Карточка изменилась на сервере. Локальное действие отменено.</small></span><button className="button button--secondary" type="button" onClick={() => { setAlertState(""); onToast("Актуальная карточка пользователя загружена"); }}>Обновить</button></div>}<section className="admin-history"><h4><HistoryRegular aria-hidden="true" />Последние изменения</h4><p><span />Сегодня 10:18 · успешный вход</p><p><span />29 июля · роль подтверждена сервером</p></section><div className="admin-actions"><button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => setAlertState("version")}>Проверить конфликт</button><button className="button button--secondary" type="button" disabled={!isWritable || selectedUser.status === "Заблокирован"} onClick={() => requestUserAction("block-user")}>Заблокировать</button><button className="button button--danger" type="button" disabled={!isWritable || selectedUser.status === "Деактивирован"} onClick={() => requestUserAction("deactivate-user")}>Деактивировать</button></div></>}</aside></div>;
  }

  function renderDepartments() {
    return <div className="admin-split"><section className="admin-list-panel"><div className="admin-section-intro"><div><strong>Иерархия подразделений</strong><small>Department.Read · Department.Manage</small></div><button className="button button--primary" type="button" disabled={!isWritable} onClick={() => setDialog("department")}>Создать</button></div><div className="admin-tree" role="tree">{departments.map((department) => <button key={department.id} type="button" role="treeitem" className={`${selectedDepartment.id === department.id ? "is-selected" : ""} ${!department.authorized ? "is-redacted" : ""}`} onClick={() => { setSelectedDepartmentId(department.id); setAlertState(""); }}><BranchForkRegular aria-hidden="true" /><span><strong>{department.name}</strong><small>{department.authorized ? `${department.parent} · ${department.people} сотрудников` : "Родитель и структура недоступны"}</small></span><ChevronRightRegular aria-hidden="true" /></button>)}</div></section><aside className="admin-inspector">{!selectedDepartment.authorized ? <div className="admin-redacted"><LockClosedRegular aria-hidden="true" /><h3>Подразделение недоступно</h3><p>Название родителя, руководитель, участники и число дочерних узлов не раскрываются.</p></div> : <><header><div><small>Department.Manage</small><h3>{selectedDepartment.name}</h3><p>{selectedDepartment.parent}</p></div><span className="semantic-badge is-success">{selectedDepartment.status}</span></header><div className="settings-form-grid"><label className="field"><span>Руководитель</span><select value={selectedDepartment.manager} disabled={!isWritable} onChange={(event) => setDepartments((items) => items.map((item) => item.id === selectedDepartment.id ? { ...item, manager: event.target.value } : item))}><option>Анна Крылова</option><option>Мария С.</option><option>Игорь В.</option></select></label><label className="field"><span>Родитель</span><select value={selectedDepartment.parent} disabled={!isWritable} onChange={(event) => setDepartments((items) => items.map((item) => item.id === selectedDepartment.id ? { ...item, parent: event.target.value } : item))}><option>Коммерческий блок</option><option>Операционный блок</option><option>Корень организации</option></select></label></div>{alertState === "cycle" && <div className="error-message" role="alert">DEPENDENCY_CYCLE · подразделение нельзя сделать собственным потомком.</div>}{alertState === "children" && <div className="inline-alert inline-alert--warning" role="alert"><WarningRegular aria-hidden="true" /><span><strong>INVALID_STATE_TRANSITION · есть активные дочерние подразделения</strong><small>Переместите или архивируйте дочерние узлы до изменения lifecycle.</small></span></div>}<dl className="admin-facts"><div><dt>Сотрудники</dt><dd>{selectedDepartment.people}</dd></div><div><dt>Дочерние узлы</dt><dd>{selectedDepartment.children}</dd></div></dl><div className="admin-actions"><button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => setAlertState("cycle")}>Проверить цикл</button><button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => { if (selectedDepartment.children) setAlertState("children"); else onToast("Подразделение архивировано"); }}>Архивировать</button><button className="button button--primary" type="button" disabled={!isWritable} onClick={() => onToast("Подразделение сохранено после VERSION recheck")}>Сохранить</button></div></>}</aside></div>;
  }

  function renderRoles() {
    return <div className="admin-split"><section className="admin-list-panel"><div className="admin-section-intro"><div><strong>Роли</strong><small>Role.Read · Role.Manage</small></div><button className="button button--primary" type="button" disabled={!isWritable} onClick={() => { setValidation(""); setDialog("role"); }}>Создать</button></div><div className="admin-list admin-list--roles">{roles.map((role) => <button key={role.id} type="button" className={selectedRole.id === role.id ? "is-selected" : ""} onClick={() => { setSelectedRoleId(role.id); setDangerousPermission(false); setEffectiveMode(""); }}><span className="admin-avatar"><KeyRegular aria-hidden="true" /></span><span><strong>{role.name}</strong><small>{role.description}</small></span><em>{role.members}</em></button>)}</div></section><aside className="admin-inspector"><header><div><small>{selectedRole.system ? "System role · immutable" : "Role.Manage"}</small><h3>{selectedRole.name}</h3><p>{selectedRole.members} участников</p></div>{selectedRole.system && <span className="semantic-badge">Системная</span>}</header>{selectedRole.system && <div className="lifecycle-readonly"><LockClosedRegular aria-hidden="true" /><span><strong>Роль нельзя изменить или удалить</strong><small>Состав системной роли опубликован сервером.</small></span></div>}<section className="admin-permissions"><h4>Разрешения</h4>{selectedRole.permissions.map((permission) => <label key={permission}><span><strong>{permission}</strong><small>Effective scope проверяется для каждого объекта</small></span><input type="checkbox" checked readOnly disabled={selectedRole.system || !isWritable} /></label>)}{!selectedRole.system && <label className={dangerousPermission ? "is-dangerous" : ""}><span><strong>Backup.Restore</strong><small>Опасное разрешение: может перевести систему в maintenance mode</small></span><input type="checkbox" checked={dangerousPermission} disabled={!isWritable} onChange={(event) => setDangerousPermission(event.target.checked)} /></label>}</section>{dangerousPermission && <div className="inline-alert inline-alert--warning" role="alert"><WarningRegular aria-hidden="true" /><span><strong>Опасное разрешение</strong><small>Назначение требует отдельного подтверждения и не должно расширять скрытые object scopes.</small></span></div>}{effectiveMode && <div className={`admin-effective ${effectiveMode === "deny" ? "is-deny" : "is-allow"}`}><ShieldErrorRegular aria-hidden="true" /><span><strong>{effectiveMode === "deny" ? "Deny: объект вне области отдела" : "Allow: Project.ManageMembers"}</strong><small>{effectiveMode === "deny" ? "Название и владелец скрытого объекта не раскрываются." : "Разрешено ролью руководителя в пределах своего отдела."}</small></span></div>}<div className="admin-actions"><button className="button button--secondary" type="button" onClick={() => setEffectiveMode("allow")}>Проверить Allow</button><button className="button button--secondary" type="button" onClick={() => setEffectiveMode("deny")}>Проверить Deny</button><button className="button button--secondary" type="button" disabled={selectedRole.system || !isWritable} onClick={() => { setDangerousPermission(false); onToast("Роль сброшена к опубликованной версии"); }}>Сбросить</button><button className="button button--primary" type="button" disabled={selectedRole.system || !isWritable} onClick={() => onToast("Роль сохранена после VERSION recheck")}>Сохранить</button></div></aside></div>;
  }

  function renderSessions() {
    return <div className="admin-split"><section className="admin-list-panel"><div className="admin-section-intro"><label className="field"><span>Состояние</span><select value={sessionFilter} onChange={(event) => setSessionFilter(event.target.value)}><option>Все состояния</option><option>Активна</option><option>Сердцебиение устарело</option><option>Подозрительная</option><option>Текущая</option></select></label></div><div className="admin-list">{filteredSessions.map((session) => <button key={session.id} type="button" className={selectedSession.id === session.id ? "is-selected" : ""} onClick={() => setSelectedSessionId(session.id)}><span className="admin-avatar"><SettingsRegular aria-hidden="true" /></span><span><strong>{session.user}</strong><small>{session.device} · {session.heartbeat}</small></span><em className={`is-${session.tone}`}>{session.state}</em></button>)}</div></section><aside className="admin-inspector"><header><div><small>Session.ReadOwnOrAll · Device.ReadOwnOrAll</small><h3>{selectedSession.device}</h3><p>{selectedSession.user}</p></div><span className={`semantic-badge ${selectedSession.tone === "online" ? "is-success" : "is-warning"}`}>{selectedSession.state}</span></header><dl className="admin-facts"><div><dt>Последний heartbeat</dt><dd>{selectedSession.heartbeat}</dd></div><div><dt>Client</dt><dd>Task 1.4.2 · Windows 11</dd></div><div><dt>Источник</dt><dd>Разрешённые security metadata</dd></div><div><dt>Login attempts</dt><dd>2 успешных · protected IP</dd></div></dl>{selectedSession.state === "Подозрительная" && <div className="inline-alert inline-alert--warning"><WarningRegular aria-hidden="true" /><span><strong>Требуется проверка</strong><small>IP, точный location и credential fields не раскрываются в этой области.</small></span></div>}<div className="admin-actions"><button className="button button--danger" type="button" disabled={!isWritable || selectedSession.current || selectedSession.state === "SESSION_REVOKED"} onClick={() => setDialog("revoke-admin-session")}>Отозвать сессию</button></div></aside></div>;
  }

  function renderResources() {
    return <div className="admin-split"><section className="admin-list-panel"><div className="admin-section-intro"><div><strong>Сетевые ресурсы</strong><small>NetworkResource.Manage</small></div><button className="button button--primary" type="button" disabled={!isWritable} onClick={() => { setValidation(""); setResourceDraft({ name: "", path: "" }); setDialog("resource"); }}>Добавить</button></div><div className="admin-list">{resources.map((resource) => <button key={resource.id} type="button" className={selectedResource.id === resource.id ? "is-selected" : ""} onClick={() => { setSelectedResourceId(resource.id); setAlertState(""); }}><span className="admin-avatar"><ServerRegular aria-hidden="true" /></span><span><strong>{resource.name}</strong><small>{resource.path}</small></span><em className={resource.state === "Доступен" ? "is-online" : "is-danger"}>{resource.state === "Доступен" ? "Доступен" : "Недоступен"}</em></button>)}</div></section><aside className="admin-inspector"><header><div><small>NetworkResource.Manage</small><h3>{selectedResource.name}</h3><p>{selectedResource.path}</p></div><span className={`semantic-badge ${selectedResource.enabled ? "is-success" : "is-warning"}`}>{selectedResource.enabled ? "Включён" : "Отключён"}</span></header><div className={`settings-connection ${selectedResource.state !== "Доступен" ? "is-error" : ""}`}><ServerRegular aria-hidden="true" /><span><strong>{selectedResource.state}</strong><small>{selectedResource.state === "Доступен" ? "Последняя probe: сегодня 10:21" : "Task не подменяет недоступный UNC локальной копией."}</small></span></div>{alertState === "probe" && <div className="inline-alert" role="status"><ArrowSyncRegular aria-hidden="true" /><span><strong>Probe завершена</strong><small>{selectedResource.state === "Доступен" ? "NETWORK_RESOURCE_AVAILABLE" : "NETWORK_RESOURCE_UNAVAILABLE"}</small></span></div>}{alertState === "resource-conflict" && <div className="inline-alert inline-alert--warning" role="alert"><WarningRegular aria-hidden="true" /><span><strong>VERSION_CONFLICT</strong><small>Ресурс изменён другим администратором; локальное действие отменено.</small></span></div>}<div className="admin-actions"><button className="button button--primary" type="button" disabled={offline} onClick={() => setAlertState("probe")}>Проверить доступность</button><button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => setResources((items) => items.map((item) => item.id === selectedResource.id ? { ...item, enabled: !item.enabled } : item))}>{selectedResource.enabled ? "Отключить" : "Включить"}</button><button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => setAlertState("resource-conflict")}>Проверить конфликт</button></div></aside></div>;
  }

  function renderActiveSection() {
    if (activeSection === "users") return renderUsers();
    if (activeSection === "departments") return renderDepartments();
    if (activeSection === "roles") return renderRoles();
    if (activeSection === "sessions") return renderSessions();
    return renderResources();
  }

  return <section className="admin-page" aria-labelledby="admin-page-title"><header className="admin-page__heading"><div><p className="eyebrow">Wave C · SCR-170–182 · FLOW-029/030</p><h2 id="admin-page-title">Администрирование</h2><p>Capability-filtered управление пользователями, структурой, ролями, сессиями и сетевыми ресурсами.</p></div><div><button className="button button--secondary" type="button" disabled={offline || loading} onClick={refreshAdmin}><ArrowSyncRegular aria-hidden="true" />{loading ? "Обновление…" : "Обновить"}</button><button className="button button--secondary" type="button" onClick={() => { setLimitedMode((value) => !value); setAlertState(""); }}>{limitedMode ? "Полная роль" : "Ограниченная роль"}</button></div></header>{offline && <div className="admin-banner admin-banner--offline" role="status"><PlugDisconnectedRegular aria-hidden="true" /><span><strong>Offline · Admin только для чтения</strong><small>Создание, lifecycle, revoke, role changes и network probe отключены.</small></span></div>}{limitedMode && <div className="admin-banner" role="status"><ShieldErrorRegular aria-hidden="true" /><span><strong>PartialAccess · navigation отфильтрована capability</strong><small>Показаны только User.Read и Session.ReadOwnOrAll. Скрытые разделы и их количество не раскрываются в подсказках.</small></span></div>}<div className="admin-tabs" role="tablist" aria-label="Разделы администрирования">{visibleSections.map((item) => { const Icon = item.icon; return <button key={item.id} type="button" role="tab" aria-selected={activeSection === item.id} className={activeSection === item.id ? "is-active" : ""} onClick={() => { setActiveSection(item.id); setAlertState(""); setQuery(""); }}><Icon aria-hidden="true" /><span><strong>{item.label}</strong><small>{item.capability}</small></span></button>; })}</div><div className="admin-workspace" aria-busy={loading}>{loading ? <div className="settings-loading admin-loading" role="status" aria-label="Admin данные обновляются"><span /><span /><span /></div> : renderActiveSection()}</div>

    {dialog === "create-user" && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog admin-dialog" role="dialog" aria-modal="true" aria-labelledby="admin-create-user-title"><div className="dialog__header"><div><p className="eyebrow">User.Create</p><h2 id="admin-create-user-title">Создать пользователя</h2></div><button className="icon-button" type="button" aria-label="Закрыть" onClick={() => setDialog("")}><DismissRegular aria-hidden="true" /></button></div><div className="settings-form-grid"><label className="field"><span>Имя</span><input autoFocus value={userDraft.name} onChange={(event) => setUserDraft((value) => ({ ...value, name: event.target.value }))} /></label><label className="field"><span>Логин</span><input value={userDraft.login} onChange={(event) => setUserDraft((value) => ({ ...value, login: event.target.value }))} /></label><label className="field"><span>Подразделение</span><select value={userDraft.department} onChange={(event) => setUserDraft((value) => ({ ...value, department: event.target.value }))}><option>Отдел продаж</option><option>Маркетинг</option><option>Поддержка</option></select></label><label className="field"><span>Роль</span><select value={userDraft.role} onChange={(event) => setUserDraft((value) => ({ ...value, role: event.target.value }))}><option>Сотрудник</option><option>Руководитель</option><option>Аудитор</option></select></label></div>{validation && <div className="error-message" role="alert">{validation}</div>}<div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => setDialog("")}>Отмена</button><button className="button button--primary" type="button" onClick={createUser}>Создать</button></div></section></div>}
    {["block-user", "deactivate-user"].includes(dialog) && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog admin-dialog" role="dialog" aria-modal="true" aria-labelledby="admin-user-lifecycle-title"><div className="dialog__header"><div><p className="eyebrow">User.Block · server recheck</p><h2 id="admin-user-lifecycle-title">{dialog === "block-user" ? "Заблокировать пользователя" : "Деактивировать пользователя"}</h2></div><button className="icon-button" type="button" aria-label="Закрыть" onClick={() => setDialog("")}><DismissRegular aria-hidden="true" /></button></div><div className="inline-alert inline-alert--warning"><WarningRegular aria-hidden="true" /><span><strong>{selectedUser.name}</strong><small>Активные сессии будут отозваны. История и audit events сохраняются.</small></span></div><div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => setDialog("")}>Отмена</button><button className="button button--danger" type="button" onClick={() => confirmUserAction(dialog)}>Подтвердить</button></div></section></div>}
    {dialog === "revoke-admin-session" && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog admin-dialog" role="dialog" aria-modal="true" aria-labelledby="admin-revoke-title"><div className="dialog__header"><div><p className="eyebrow">Session.ReadOwnOrAll</p><h2 id="admin-revoke-title">Отозвать сессию</h2></div><button className="icon-button" type="button" aria-label="Закрыть" onClick={() => setDialog("")}><DismissRegular aria-hidden="true" /></button></div><p>{selectedSession.user} · {selectedSession.device}. Сессия получит `SESSION_REVOKED` при следующей проверке.</p><div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => setDialog("")}>Отмена</button><button className="button button--danger" type="button" onClick={() => { setSessions((items) => items.map((item) => item.id === selectedSession.id ? { ...item, state: "SESSION_REVOKED", tone: "warning" } : item)); setDialog(""); onToast("Сессия отозвана"); }}>Отозвать</button></div></section></div>}
    {dialog === "resource" && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog admin-dialog" role="dialog" aria-modal="true" aria-labelledby="admin-resource-title"><div className="dialog__header"><div><p className="eyebrow">NetworkResource.Manage</p><h2 id="admin-resource-title">Добавить сетевой ресурс</h2></div><button className="icon-button" type="button" aria-label="Закрыть" onClick={() => setDialog("")}><DismissRegular aria-hidden="true" /></button></div><label className="field"><span>Название</span><input value={resourceDraft.name} onChange={(event) => setResourceDraft((value) => ({ ...value, name: event.target.value }))} /></label><label className="field"><span>UNC-путь</span><input value={resourceDraft.path} placeholder="\\\\server\\share" onChange={(event) => { setResourceDraft((value) => ({ ...value, path: event.target.value })); setValidation(""); }} /></label>{validation && <div className="error-message" role="alert">{validation}</div>}<div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => setDialog("")}>Отмена</button><button className="button button--primary" type="button" onClick={() => { if (!resourceDraft.name.trim() || !resourceDraft.path.trim()) { setValidation("ValidationError · укажите название и UNC-путь."); return; } if (/^[a-z]:/i.test(resourceDraft.path) || !resourceDraft.path.startsWith("\\\\")) { setValidation("UNSAFE_PATH · разрешены только UNC-пути организации."); return; } const created = { id: `resource-${Date.now()}`, ...resourceDraft, state: "Доступен", enabled: true }; setResources((items) => [created, ...items]); setSelectedResourceId(created.id); setDialog(""); onToast("Сетевой ресурс добавлен после probe"); }}>Добавить</button></div></section></div>}
    {dialog === "department" && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog admin-dialog" role="dialog" aria-modal="true" aria-labelledby="admin-department-title"><div className="dialog__header"><div><p className="eyebrow">Department.Manage</p><h2 id="admin-department-title">Создать подразделение</h2></div><button className="icon-button" type="button" aria-label="Закрыть" onClick={() => setDialog("")}><DismissRegular aria-hidden="true" /></button></div><label className="field"><span>Название</span><input autoFocus placeholder="Новое подразделение" /></label><label className="field"><span>Разрешённый родитель</span><select><option>Корень организации</option><option>Коммерческий блок</option><option>Операционный блок</option></select></label><div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => setDialog("")}>Отмена</button><button className="button button--primary" type="button" onClick={() => { setDialog(""); onToast("Подразделение создано после cycle recheck"); }}>Создать</button></div></section></div>}
    {dialog === "role" && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog admin-dialog" role="dialog" aria-modal="true" aria-labelledby="admin-role-title"><div className="dialog__header"><div><p className="eyebrow">Role.Manage</p><h2 id="admin-role-title">Создать роль</h2></div><button className="icon-button" type="button" aria-label="Закрыть" onClick={() => setDialog("")}><DismissRegular aria-hidden="true" /></button></div><label className="field"><span>Название роли</span><input autoFocus placeholder="Название" /></label><p className="helper-copy">Новая роль не получает опасные разрешения автоматически.</p><div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => setDialog("")}>Отмена</button><button className="button button--primary" type="button" onClick={() => { setDialog(""); onToast("Черновик роли создан без разрешений"); }}>Создать</button></div></section></div>}
  </section>;
}

function OperationsSurface({ offline, onToast }) {
  const sections = [
    { id: "health", label: "Состояние системы", capability: "System.HealthRead", icon: ServerRegular },
    { id: "jobs", label: "Фоновые задания", capability: "System.Configure", icon: PlayCircleRegular },
    { id: "backups", label: "Резервные копии", capability: "Backup.Read/Restore", icon: DatabaseRegular },
    { id: "audit", label: "Аудит", capability: "Audit.ReadAll", icon: HistoryRegular },
    { id: "organization", label: "Организация", capability: "Organization.Update", icon: SettingsRegular },
  ];
  const [activeSection, setActiveSection] = useState("health");
  const [limitedMode, setLimitedMode] = useState(false);
  const [loading, setLoading] = useState(false);
  const [healthMode, setHealthMode] = useState("degraded");
  const [writeBlocked, setWriteBlocked] = useState(false);
  const [alertState, setAlertState] = useState("");
  const [dialog, setDialog] = useState("");
  const [jobs, setJobs] = useState([
    { id: "reindex", name: "Переиндексация поиска", state: "Выполняется", progress: 64, started: "10:04", trace: "job-7f2a" },
    { id: "cleanup", name: "Очистка временных данных", state: "В очереди", progress: 0, started: "—", trace: "job-92bd" },
    { id: "audit-export", name: "Экспорт журнала аудита", state: "Ошибка", progress: 38, started: "09:41", trace: "job-4c18" },
  ]);
  const [selectedJobId, setSelectedJobId] = useState("reindex");
  const [backups, setBackups] = useState([
    { id: "nightly", name: "Ночная копия · 30 июля", state: "Успешно", size: "18,4 ГБ", detail: "Проверка завершена в 03:18" },
    { id: "hourly", name: "Инкрементальная · 10:00", state: "Ошибка", size: "—", detail: "DEPENDENCY_UNAVAILABLE · backup storage" },
    { id: "verified", name: "Проверенная копия · 29 июля", state: "Успешно", size: "18,1 ГБ", detail: "Restore test пройден" },
  ]);
  const [selectedBackupId, setSelectedBackupId] = useState("nightly");
  const [restoreApproved, setRestoreApproved] = useState(false);
  const [restorePhrase, setRestorePhrase] = useState("");
  const [auditQuery, setAuditQuery] = useState("");
  const [auditRange, setAuditRange] = useState("7 дней");
  const [auditType, setAuditType] = useState("Все события");
  const [auditExport, setAuditExport] = useState("");
  const [orgWritable, setOrgWritable] = useState(true);
  const [featureFlags, setFeatureFlags] = useState([
    { id: "calendar-v2", name: "Новый календарь", enabled: false, impact: "Требует Task 1.5+ на всех устройствах" },
    { id: "strict-unc", name: "Строгая проверка UNC", enabled: true, impact: "Новые пути проверяются до публикации" },
    { id: "audit-retention", name: "Расширенный audit retention", enabled: false, impact: "Увеличивает объём append-only журнала" },
  ]);
  const [organizationAlert, setOrganizationAlert] = useState("");

  const visibleSections = getVisibleOperationSections(sections, limitedMode);
  const selectedJob = jobs.find((item) => item.id === selectedJobId) || jobs[0];
  const selectedBackup = backups.find((item) => item.id === selectedBackupId) || backups[0];
  const activeJobExists = jobs.some((item) => item.state === "Выполняется") || backups.some((item) => item.state === "Выполняется");
  const isMaintenance = healthMode === "maintenance";
  const isWritable = isOperationsWritable({ offline, loading, writeBlocked, maintenance: isMaintenance });
  const auditRows = [
    { id: "audit-1", time: "Сегодня, 10:21", actor: "Иван Сергеев", action: "User.Block проверен", target: "Разрешённая учётная запись", type: "Безопасность", authorized: true },
    { id: "audit-2", time: "Сегодня, 10:08", actor: "Система", action: "Backup.Execute", target: "Инкрементальная копия", type: "Резервные копии", authorized: true },
    { id: "audit-3", time: "Сегодня, 09:42", actor: "Пользователь недоступен", action: "Настройка изменена", target: "Объект вне разрешённой области", type: "Организация", authorized: false },
    { id: "audit-4", time: "Вчера, 18:17", actor: "Анна Крылова", action: "Session.Revoke", target: "Разрешённая сессия", type: "Безопасность", authorized: true },
  ];
  const filteredAudit = filterAuthorizedAudit(auditRows, { query: auditQuery, type: auditType });

  useEffect(() => {
    if (limitedMode && !["health", "audit"].includes(activeSection)) setActiveSection("health");
  }, [limitedMode, activeSection]);

  function refreshOperations() {
    if (offline) return;
    setLoading(true);
    setAlertState("");
    window.setTimeout(() => {
      setLoading(false);
      onToast("Операционные данные обновлены");
    }, 620);
  }

  function updateJob(state, progress) {
    setJobs((items) => transitionOperation(items, selectedJob.id, { state, progress }));
  }

  function renderHealth() {
    const checks = healthMode === "online"
      ? [
        { name: "Database", status: "Готово", detail: "readiness probe · 41 мс", tone: "ok" },
        { name: "Хранилище", status: "Доступно", detail: "62% занято · порог 90%", tone: "ok" },
        { name: "Каталог сотрудников", status: "Готово", detail: "Последняя синхронизация 1 мин назад", tone: "ok" },
      ]
      : [
        { name: "Database", status: "DATABASE_UNAVAILABLE", detail: "Readiness не подтверждена; повторная проверка безопасна", tone: "danger" },
        { name: "Хранилище", status: "STORAGE_FULL", detail: "92% занято · новые backup jobs приостановлены", tone: "warning" },
        { name: "Каталог сотрудников", status: "DEPENDENCY_UNAVAILABLE", detail: "Cached directory доступен только для чтения", tone: "warning" },
      ];
    return <div className="operations-health">
      <div className="operations-summary">
        <div><small>Readiness</small><strong>{isMaintenance ? "MAINTENANCE_MODE" : healthMode === "online" ? "Готово" : "Деградация"}</strong><span>{isMaintenance ? "Глобальные записи остановлены контролируемым планом." : healthMode === "online" ? "Все обязательные зависимости отвечают." : "Диагностика честно показывает недоступные зависимости."}</span></div>
        <div><small>Версия сервера</small><strong>Task Server 1.4.2</strong><span>Совместима с клиентом 1.4.2</span></div>
        <div><small>Активные jobs</small><strong>{jobs.filter((item) => item.state === "Выполняется").length}</strong><span>Allowlisted background operations</span></div>
      </div>
      <div className="operations-health-grid">{checks.map((item) => <article key={item.name} className={`operations-health-card is-${item.tone}`}><span><ServerRegular aria-hidden="true" /></span><div><strong>{item.name}</strong><em>{item.status}</em><small>{item.detail}</small></div></article>)}</div>
      <div className="operations-actions">
        <button className="button button--secondary" type="button" disabled={offline || loading || isMaintenance} onClick={() => setHealthMode((value) => value === "online" ? "degraded" : "online")}>{healthMode === "online" ? "Смоделировать деградацию" : "Повторить readiness"}</button>
        <button className="button button--secondary" type="button" disabled={offline || isMaintenance} onClick={() => { setWriteBlocked((value) => !value); setAlertState(""); }}>{writeBlocked ? "Снять global write block" : "Включить global write block"}</button>
        {isMaintenance && <button className="button button--primary" type="button" disabled={offline} onClick={() => { setHealthMode("online"); setWriteBlocked(false); setAlertState("reconnected"); }}>Завершить maintenance и переподключить</button>}
      </div>
      {alertState === "reconnected" && <div className="operations-notice is-success" role="status"><CheckmarkCircleRegular aria-hidden="true" /><span><strong>Reconnect завершён</strong><small>Readiness подтверждена; записи снова разрешены после серверной проверки.</small></span></div>}
    </div>;
  }

  function renderJobs() {
    return <div className="operations-split"><section className="operations-list-panel"><div className="operations-section-intro"><div><strong>Allowlisted задания</strong><small>System.HealthRead · System.Configure</small></div><button className="button button--primary" type="button" disabled={!isWritable} onClick={() => { setSelectedJobId("cleanup"); setJobs((items) => items.map((item) => item.id === "cleanup" ? { ...item, state: "В очереди", progress: 0 } : item)); onToast("Задание поставлено в очередь"); }}>Поставить в очередь</button></div><div className="operations-list">{jobs.map((job) => <button key={job.id} type="button" className={selectedJob.id === job.id ? "is-selected" : ""} onClick={() => { setSelectedJobId(job.id); setAlertState(""); }}><span className="operations-job-icon"><PlayCircleRegular aria-hidden="true" /></span><span><strong>{job.name}</strong><small>{job.started === "—" ? "Ещё не запущено" : `Старт ${job.started}`} · {job.trace}</small></span><em className={job.state === "Ошибка" ? "is-danger" : job.state === "Выполняется" ? "is-running" : ""}>{job.state}</em></button>)}</div></section><aside key={selectedJob.id} className="operations-inspector"><header><div><small>BackgroundOperation</small><h3>{selectedJob.name}</h3><p>{selectedJob.trace}</p></div><span className="semantic-badge">{selectedJob.state}</span></header><div className="operations-progress"><div><span>Прогресс</span><strong>{selectedJob.progress}%</strong></div><progress max="100" value={selectedJob.progress}>{selectedJob.progress}%</progress></div>{selectedJob.state === "Ошибка" && <div className="operations-notice is-danger" role="alert"><WarningRegular aria-hidden="true" /><span><strong>DEPENDENCY_UNAVAILABLE</strong><small>Результат не опубликован. Retry создаёт новый аудируемый run.</small></span></div>}{alertState === "job-conflict" && <div className="operations-notice is-warning" role="alert"><WarningRegular aria-hidden="true" /><span><strong>VERSION_CONFLICT · lease изменился</strong><small>Локальный запуск отменён; список обновлён без двойного выполнения.</small></span></div>}<dl className="operations-facts"><div><dt>Запуск</dt><dd>{selectedJob.started}</dd></div><div><dt>Повторяемость</dt><dd>Safe retry · server-authoritative</dd></div><div><dt>История</dt><dd>Все переходы записываются в audit</dd></div></dl><div className="operations-actions"><button className="button button--primary" type="button" disabled={!isWritable || selectedJob.state === "Выполняется"} onClick={() => updateJob("Выполняется", Math.max(selectedJob.progress, 12))}>{selectedJob.state === "Ошибка" ? "Повторить" : "Запустить"}</button>{selectedJob.state === "Выполняется" && <button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => updateJob("Успешно", 100)}>Завершить демонстрацию</button>}<button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => setAlertState("job-conflict")}>Проверить lease conflict</button></div></aside></div>;
  }

  function renderBackups() {
    return <div className="operations-split"><section className="operations-list-panel"><div className="operations-section-intro"><div><strong>Резервные копии</strong><small>Backup.Read · Execute · RestoreTest · Restore</small></div><button className="button button--primary" type="button" disabled={!isWritable} onClick={() => { setBackups((items) => transitionOperation(items, selectedBackup.id, { state: "Выполняется", detail: "BackgroundOperation · проверка хранилища" })); onToast("Backup job запущен"); }}>Запустить backup</button></div><div className="operations-list">{backups.map((backup) => <button key={backup.id} type="button" className={selectedBackup.id === backup.id ? "is-selected" : ""} onClick={() => { setSelectedBackupId(backup.id); setAlertState(""); }}><span className="operations-job-icon"><DatabaseRegular aria-hidden="true" /></span><span><strong>{backup.name}</strong><small>{backup.detail}</small></span><em className={backup.state === "Ошибка" ? "is-danger" : backup.state === "Выполняется" ? "is-running" : ""}>{backup.state}</em></button>)}</div></section><aside key={selectedBackup.id} className="operations-inspector"><header><div><small>Backup.RestoreTest · Backup.Restore</small><h3>{selectedBackup.name}</h3><p>{selectedBackup.size}</p></div><span className="semantic-badge">{selectedBackup.state}</span></header>{selectedBackup.state === "Ошибка" && <div className="operations-notice is-danger" role="alert"><WarningRegular aria-hidden="true" /><span><strong>BackupFailed · DEPENDENCY_UNAVAILABLE</strong><small>Последняя успешная копия остаётся неизменной; ложный success не показывается.</small></span></div>}{restoreApproved ? <div className="operations-notice is-success" role="status"><CheckmarkCircleRegular aria-hidden="true" /><span><strong>Согласование получено</strong><small>План всё ещё проверит активные задания и версию состояния перед maintenance.</small></span></div> : <div className="operations-notice is-warning" role="status"><LockClosedRegular aria-hidden="true" /><span><strong>Approval required</strong><small>Restore недоступен без отдельного согласования и контролируемого плана.</small></span></div>}<dl className="operations-facts"><div><dt>Последний success</dt><dd>30 июля, 03:18</dd></div><div><dt>Restore test</dt><dd>{selectedBackup.id === "verified" ? "Пройден" : "Требуется"}</dd></div><div><dt>Активные jobs</dt><dd>{activeJobExists ? "Есть · guard активен" : "Нет"}</dd></div></dl><div className="operations-actions"><button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => setRestoreApproved(true)}>Запросить согласование</button><button className="button button--secondary" type="button" disabled={!isWritable || selectedBackup.state === "Выполняется"} onClick={() => onToast("Restore test запущен как отдельный background job")}>Проверить restore</button><button className="button button--danger" type="button" disabled={!isWritable} onClick={() => { setRestorePhrase(""); setDialog("restore"); }}>Подготовить restore plan</button></div></aside></div>;
  }

  function renderAudit() {
    const largeRange = auditRange === "90 дней";
    return <div className="operations-audit"><div className="operations-filterbar"><label className="field"><span>Поиск в разрешённых событиях</span><input value={auditQuery} onChange={(event) => setAuditQuery(event.target.value)} placeholder="Actor, действие, разрешённый объект" /></label><label className="field"><span>Тип</span><select value={auditType} onChange={(event) => setAuditType(event.target.value)}><option>Все события</option><option>Безопасность</option><option>Резервные копии</option><option>Организация</option></select></label><label className="field"><span>Период</span><select value={auditRange} onChange={(event) => { setAuditRange(event.target.value); setAuditExport(""); }}><option>7 дней</option><option>30 дней</option><option>90 дней</option></select></label><button className="button button--secondary" type="button" disabled={offline || loading} onClick={() => largeRange ? setAuditExport("large") : setAuditExport("ready")}>Экспорт</button></div>{limitedMode && <div className="operations-notice" role="status"><ShieldErrorRegular aria-hidden="true" /><span><strong>PartialAccess · SecurityAudit.Read</strong><small>Показаны только разрешённые security events; скрытые события и их число не раскрываются.</small></span></div>}{auditExport === "large" && <div className="operations-notice is-warning" role="alert"><WarningRegular aria-hidden="true" /><span><strong>REQUEST_TOO_LARGE</strong><small>90-дневный диапазон не формируется синхронно. Запустите фоновый экспорт без расширения текущей области доступа.</small><button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => setAuditExport("running")}>Запустить фоновый экспорт</button></span></div>}{auditExport === "running" && <div className="operations-notice" role="status"><ArrowSyncRegular aria-hidden="true" /><span><strong>Экспорт выполняется · 42%</strong><small>BackgroundOperation продолжится на сервере; результат сохранит redaction.</small></span></div>}{auditExport === "ready" && <div className="operations-notice is-success" role="status"><CheckmarkCircleRegular aria-hidden="true" /><span><strong>Экспорт подготовлен</strong><small>Только видимые события и redacted поля.</small></span></div>}<div className="operations-audit-list" role="list" aria-label="Журнал аудита">{filteredAudit.map((entry) => <article key={entry.id} role="listitem" className={!entry.authorized ? "is-redacted" : ""}><time>{entry.time}</time><span><strong>{entry.action}</strong><small>{entry.actor} · {entry.target}</small></span><em>{entry.authorized ? entry.type : "Redacted"}</em></article>)}</div>{filteredAudit.length === 0 && <div className="operations-empty" role="status"><SearchRegular aria-hidden="true" /><strong>Разрешённые события не найдены</strong><small>Измените фильтры; скрытые события не участвуют в результате.</small><button className="button button--secondary" type="button" onClick={() => { setAuditQuery(""); setAuditType("Все события"); }}>Сбросить фильтры</button></div>}</div>;
  }

  function renderOrganization() {
    return <div className="operations-organization"><div className="operations-section-intro"><div><strong>Настройки организации и feature flags</strong><small>Organization.Update · System.Configure</small></div><button className="button button--secondary" type="button" onClick={() => { setOrgWritable((value) => !value); setOrganizationAlert(""); }}>{orgWritable ? "Нет Organization.Update" : "Вернуть capability"}</button></div>{!orgWritable && <div className="operations-notice is-danger" role="alert"><LockClosedRegular aria-hidden="true" /><span><strong>Forbidden · только чтение</strong><small>Настройки видимы по разрешению чтения, но save и flag rollout скрыты от server mutation.</small></span></div>}<div className="operations-org-grid"><section><h3>Поддерживаемые параметры</h3><label className="field"><span>Домен организации</span><input value="company.local" disabled /></label><label className="field"><span>Минимальная версия клиента</span><select defaultValue="1.4" disabled={!orgWritable || !isWritable}><option>1.4</option><option>1.5</option></select></label><label className="field"><span>Retention audit, дней</span><input defaultValue="365" disabled={!orgWritable || !isWritable} /></label></section><section><h3>Feature flags</h3>{featureFlags.map((flag) => <label key={flag.id} className="operations-flag"><span><strong>{flag.name}</strong><small>{flag.impact}</small></span><input type="checkbox" checked={flag.enabled} disabled={!orgWritable || !isWritable} onChange={(event) => { setFeatureFlags((items) => items.map((item) => item.id === flag.id ? { ...item, enabled: event.target.checked } : item)); if (flag.id === "calendar-v2" && event.target.checked) setOrganizationAlert("client"); }} /></label>)}</section></div>{organizationAlert === "client" && <div className="operations-notice is-warning" role="alert"><WarningRegular aria-hidden="true" /><span><strong>CLIENT_VERSION_UNSUPPORTED</strong><small>Два устройства на Task 1.4 не поддерживают новый календарь. Rollout не опубликован.</small></span></div>}{organizationAlert === "conflict" && <div className="operations-notice is-danger" role="alert"><WarningRegular aria-hidden="true" /><span><strong>VERSION_CONFLICT</strong><small>Настройки изменены другим администратором. Локальные изменения не применены.</small><button className="button button--secondary" type="button" onClick={() => setOrganizationAlert("")}>Загрузить серверную версию</button></span></div>}<div className="operations-actions"><button className="button button--primary" type="button" disabled={!orgWritable || !isWritable || organizationAlert === "client"} onClick={() => onToast("Настройки организации сохранены после server recheck")}>Сохранить</button><button className="button button--secondary" type="button" disabled={!orgWritable || !isWritable} onClick={() => setOrganizationAlert("conflict")}>Проверить конфликт</button></div></div>;
  }

  function renderActiveSection() {
    if (activeSection === "health") return renderHealth();
    if (activeSection === "jobs") return renderJobs();
    if (activeSection === "backups") return renderBackups();
    if (activeSection === "audit") return renderAudit();
    return renderOrganization();
  }

  return <section className="operations-page" aria-labelledby="operations-page-title">
    <header className="operations-page__heading"><div><p className="eyebrow">Wave C · SCR-183–188 · SB-12/13</p><h2 id="operations-page-title">Операции</h2><p>Health, background jobs, backups, audit и настройки организации — только через разрешённые server-authoritative действия.</p></div><div><button className="button button--secondary" type="button" disabled={offline || loading} onClick={refreshOperations}><ArrowSyncRegular aria-hidden="true" />{loading ? "Обновление…" : "Обновить"}</button><button className="button button--secondary" type="button" onClick={() => { setLimitedMode((value) => !value); setAlertState(""); }}>{limitedMode ? "Полная роль" : "Ограниченная роль"}</button></div></header>
    {offline && <div className="operations-banner is-offline" role="status"><PlugDisconnectedRegular aria-hidden="true" /><span><strong>Offline · Operations только для чтения</strong><small>Readiness, jobs, backup, export и organization mutations отключены.</small></span></div>}
    {(writeBlocked || isMaintenance) && <div className="operations-banner is-blocked" role="alert"><LockClosedRegular aria-hidden="true" /><span><strong>{isMaintenance ? "MAINTENANCE_MODE · глобальный read-only" : "Global write block · только чтение"}</strong><small>{isMaintenance ? "Restore plan активировал контролируемый maintenance; reconnect выполняется отдельно." : "Сервер отклоняет новые business и operational writes до снятия блокировки."}</small></span></div>}
    {limitedMode && <div className="operations-banner" role="status"><ShieldErrorRegular aria-hidden="true" /><span><strong>PartialAccess · navigation отфильтрована capability</strong><small>Показаны только System.HealthRead и SecurityAudit.Read; скрытые разделы и их количество не раскрываются.</small></span></div>}
    <div className="operations-tabs" role="tablist" aria-label="Разделы Operations">{visibleSections.map((item) => { const Icon = item.icon; return <button key={item.id} type="button" role="tab" aria-selected={activeSection === item.id} className={activeSection === item.id ? "is-active" : ""} onClick={() => { setActiveSection(item.id); setAlertState(""); }}><Icon aria-hidden="true" /><span><strong>{item.label}</strong><small>{item.capability}</small></span></button>; })}</div>
    <div key={activeSection} className="operations-workspace" aria-busy={loading}>{loading ? <div className="settings-loading operations-loading" role="status" aria-label="Operations данные обновляются"><span /><span /><span /></div> : renderActiveSection()}</div>
    {dialog === "restore" && <div className="dialog-backdrop"><div className="dialog operations-dialog" role="dialog" aria-modal="true" aria-labelledby="restore-plan-title"><header className="dialog__header"><div><p className="eyebrow">Backup.Restore · System.Configure</p><h2 id="restore-plan-title">Контролируемый restore plan</h2></div><button className="icon-button" type="button" aria-label="Закрыть" onClick={() => setDialog("")}><DismissRegular aria-hidden="true" /></button></header><div className="dialog__body"><div className="operations-restore-summary"><strong>{selectedBackup.name}</strong><small>Maintenance остановит все business writes. Task не выполняет произвольные shell-команды.</small></div>{!restoreApproved && <div className="operations-notice is-danger" role="alert"><LockClosedRegular aria-hidden="true" /><span><strong>Approval required</strong><small>Получите отдельное согласование до подтверждения.</small></span></div>}{activeJobExists && <div className="operations-notice is-warning" role="alert"><WarningRegular aria-hidden="true" /><span><strong>INVALID_STATE_TRANSITION · active job guard</strong><small>Сначала завершите или дождитесь активных jobs/backups.</small></span></div>}<label className="field"><span>Введите RESTORE для подтверждения</span><input value={restorePhrase} onChange={(event) => setRestorePhrase(event.target.value)} aria-invalid={restorePhrase !== "" && restorePhrase !== "RESTORE"} /></label><p className="operations-dialog-note">Перед исполнением сервер повторно проверит approval, active jobs, backup version и readiness.</p></div><footer className="dialog__footer"><button className="button button--secondary" type="button" onClick={() => setDialog("")}>Отмена</button><button className="button button--danger" type="button" disabled={!canEnterMaintenance({ writable: isWritable, approved: restoreApproved, activeJobExists, confirmation: restorePhrase })} onClick={() => { setDialog(""); setHealthMode("maintenance"); setWriteBlocked(true); onToast("MAINTENANCE_MODE активирован контролируемым restore plan"); }}>Войти в maintenance</button></footer></div></div>}
  </section>;
}

function InboxSurface({ items, setItems, isWritable, onConvert, onToast }) {
  const [selectedId, setSelectedId] = useState(items[0]?.id);
  const [capture, setCapture] = useState("");
  const selected = items.find((item) => item.id === selectedId) || items[0];

  function addCapture(event) {
    event.preventDefault();
    if (!isWritable) {
      onToast("Создание отключено: сервер недоступен");
      return;
    }
    if (!capture.trim()) return;
    const item = { id: `inbox-${Date.now()}`, title: capture.trim(), source: "Быстрый ввод", created: "Только что", status: "Новая" };
    setItems((current) => [item, ...current]);
    setSelectedId(item.id);
    setCapture("");
    onToast("Запись добавлена во входящие");
  }

  return (
    <section className="inbox-surface" aria-label="Входящие">
      <div className="inbox-list-panel">
        <form className="quick-capture" onSubmit={addCapture}>
          <MailInboxRegular aria-hidden="true" />
          <input value={capture} onChange={(event) => setCapture(event.target.value)} placeholder="Быстро добавить во входящие" aria-label="Быстро добавить во входящие" />
          <button className="button button--primary" type="submit" disabled={!capture.trim() || !isWritable}>Добавить</button>
        </form>
        {!isWritable && <div className="inline-message inline-message--warning"><PlugDisconnectedRegular aria-hidden="true" />Входящие доступны только для чтения до восстановления сервера.</div>}
        <div className="inbox-list" role="listbox" aria-label="Записи входящих">
          {items.map((item) => (
            <button key={item.id} type="button" role="option" aria-selected={selected?.id === item.id} className={`${selected?.id === item.id ? "is-selected" : ""} ${item.status === "Преобразовано" ? "is-done" : ""}`} onClick={() => setSelectedId(item.id)}>
              <MailInboxRegular aria-hidden="true" />
              <span><strong>{item.title}</strong><small>{item.source} · {item.created}</small></span>
              <em>{item.status}</em>
            </button>
          ))}
        </div>
      </div>
      <aside className="inbox-inspector">
        {selected ? (
          <>
            <p className="eyebrow">Запись входящих</p>
            <h2>{selected.title}</h2>
            <dl>
              <dt>Источник</dt><dd>{selected.source}</dd>
              <dt>Создано</dt><dd>{selected.created}</dd>
              <dt>Состояние</dt><dd>{selected.status}</dd>
            </dl>
            <p className="inbox-inspector__copy">Классифицируйте запись: преобразуйте её в задачу или оставьте во входящих до следующего разбора.</p>
            <button className="button button--primary" type="button" disabled={!isWritable} onClick={() => onConvert(selected)}>Преобразовать в задачу</button>
            {!isWritable && <small className="disabled-reason">Недоступно офлайн: требуется подтверждение сервера.</small>}
          </>
        ) : <div className="empty-state"><MailInboxRegular aria-hidden="true" /><strong>Входящие пусты</strong></div>}
      </aside>
    </section>
  );
}

function ConversionDrawer({ item, isWritable, onClose, onConvert }) {
  const [title, setTitle] = useState(item.title);
  const [project, setProject] = useState("Отчётность");
  const [priority, setPriority] = useState("Средняя");
  const [due, setDue] = useState("2026-07-29");

  function submit(event) {
    event.preventDefault();
    if (!title.trim() || !isWritable) return;
    onConvert({ ...item, title: title.trim(), project, priority, due });
  }

  return (
    <div className="drawer-backdrop" role="presentation" onMouseDown={onClose}>
      <section className="conversion-drawer" role="dialog" aria-modal="true" aria-labelledby="conversion-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="dialog__header"><div><p className="eyebrow">Inbox → Task</p><h2 id="conversion-title">Преобразовать в задачу</h2></div><button className="icon-button" type="button" onClick={onClose} aria-label="Закрыть преобразование"><DismissRegular aria-hidden="true" /></button></div>
        <form onSubmit={submit}>
          <label className="field"><span>Название задачи</span><input value={title} onChange={(event) => setTitle(event.target.value)} autoFocus /></label>
          <label className="field"><span>Проект</span><select value={project} onChange={(event) => setProject(event.target.value)}><option>Отчётность</option><option>Внутренние процессы</option><option>Коммуникации</option></select></label>
          <div className="dialog__grid">
            <label className="field"><span>Приоритет</span><select value={priority} onChange={(event) => setPriority(event.target.value)}><option>Низкая</option><option>Средняя</option><option>Высокая</option></select></label>
            <label className="field"><span>Срок</span><input type="date" value={due} onChange={(event) => setDue(event.target.value)} /></label>
          </div>
          <div className="conversion-source"><MailInboxRegular aria-hidden="true" /><span><strong>Исходная запись будет закрыта</strong><small>Связь с созданной задачей сохранится в истории.</small></span></div>
          <div className="dialog__actions"><button className="button button--secondary" type="button" onClick={onClose}>Отмена</button><button className="button button--primary" type="submit" disabled={!title.trim() || !isWritable}>Создать задачу</button></div>
        </form>
      </section>
    </div>
  );
}

function TaskEditorDialog({ task, isWritable, onClose, onSave }) {
  const dialogRef = useRef(null);
  const [title, setTitle] = useState(task.title);
  const [description, setDescription] = useState("Подготовить сводный анализ продаж по всем регионам за июнь 2026. Сравнить с маем и планом.");
  const [project, setProject] = useState(task.project || "Отчётность");
  const [assignee, setAssignee] = useState(task.assignee || "Иван С.");
  const [dueDate, setDueDate] = useState("2026-07-28");
  const [dueTime, setDueTime] = useState("17:00");
  const [timezone, setTimezone] = useState("Europe/Minsk");
  const [reminder, setReminder] = useState("За 30 минут");
  const [recurrence, setRecurrence] = useState("Не повторять");
  const [recurrenceEnd, setRecurrenceEnd] = useState("2026-09-30");

  useDialogFocusTrap(dialogRef, onClose);

  function submit(event) {
    event.preventDefault();
    if (!title.trim() || !isWritable) return;
    onSave({ ...task, title: title.trim(), description, project, assignee, dueDate, dueTime, timezone, reminder, recurrence, recurrenceEnd });
  }

  return (
    <div className="dialog-backdrop" role="presentation" onMouseDown={onClose}>
      <section ref={dialogRef} className="dialog task-editor" role="dialog" aria-modal="true" aria-labelledby="task-editor-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="dialog__header"><h2 id="task-editor-title">Изменить задачу</h2><button className="icon-button" type="button" onClick={onClose} aria-label="Закрыть редактор"><DismissRegular aria-hidden="true" /></button></div>
        <form onSubmit={submit}>
          <label className="field"><span>Название</span><input value={title} onChange={(event) => setTitle(event.target.value)} autoFocus /></label>
          <div className="dialog__grid">
            <PickerField label="Проект" icon={FolderRegular} value={project} onChange={setProject} options={["Отчётность", "Альфа", "Внутренние процессы", "Коммуникации"]} helper="Показываются только доступные вам проекты." />
            <PickerField label="Исполнитель" icon={PersonRegular} value={assignee} onChange={setAssignee} options={["Иван С.", "Анна К.", "Мария С.", "Ольга Н."]} helper="Недоступные сотрудники не раскрываются в поиске." />
          </div>
          <label className="field"><span>Описание</span><textarea value={description} onChange={(event) => setDescription(event.target.value)} rows="4" /></label>
          <section className="editor-section" aria-labelledby="schedule-title">
            <div className="editor-section__heading"><CalendarRegular aria-hidden="true" /><div><h3 id="schedule-title">Срок и время</h3><span>Дата и время сохраняются с часовым поясом.</span></div></div>
            <div className="dialog__grid dialog__grid--three">
              <label className="field"><span>Дата</span><input type="date" value={dueDate} onChange={(event) => setDueDate(event.target.value)} /></label>
              <label className="field"><span>Время</span><input type="time" value={dueTime} onChange={(event) => setDueTime(event.target.value)} /></label>
              <label className="field"><span>Часовой пояс</span><select value={timezone} onChange={(event) => setTimezone(event.target.value)}><option>Europe/Minsk</option><option>Europe/Moscow</option><option>Europe/Warsaw</option></select></label>
            </div>
          </section>
          <section className="editor-section" aria-labelledby="reminder-title">
            <div className="editor-section__heading"><AlertRegular aria-hidden="true" /><div><h3 id="reminder-title">Напоминание</h3><span>Уведомление перепроверит задачу перед действием.</span></div></div>
            <label className="field"><span>Когда напомнить</span><select value={reminder} onChange={(event) => setReminder(event.target.value)}><option>Не напоминать</option><option>За 15 минут</option><option>За 30 минут</option><option>За 1 час</option><option>За 1 день</option></select></label>
            {reminder !== "Не напоминать" && <div className="inline-message inline-message--success" role="status"><CheckmarkCircleRegular aria-hidden="true" />Напоминание: {dueDate} в {dueTime}, {reminder.toLowerCase()} ({timezone}).</div>}
          </section>
          <section className="editor-section" aria-labelledby="recurrence-title">
            <div className="editor-section__heading"><ArrowSyncRegular aria-hidden="true" /><div><h3 id="recurrence-title">Повторение</h3><span>Правило применяется только после явного сохранения.</span></div></div>
            <div className="dialog__grid">
              <label className="field"><span>Правило</span><select value={recurrence} onChange={(event) => setRecurrence(event.target.value)}><option>Не повторять</option><option>Каждый рабочий день</option><option>Каждую неделю</option><option>Каждый месяц</option></select></label>
              <label className="field"><span>Завершить серию</span><input type="date" disabled={recurrence === "Не повторять"} value={recurrenceEnd} onChange={(event) => setRecurrenceEnd(event.target.value)} /></label>
            </div>
            {recurrence !== "Не повторять" && <div className="recurrence-preview"><strong>Предпросмотр серии</strong><span>{recurrence}; первая задача — {dueDate}, последняя — не позднее {recurrenceEnd}.</span></div>}
          </section>
          <div className="inline-message inline-message--warning"><WarningRegular aria-hidden="true" />Для проверки конфликта серверная версия будет изменена перед сохранением.</div>
          <div className="dialog__actions"><button className="button button--secondary" type="button" onClick={onClose}>Отмена</button><button className="button button--primary" type="submit" disabled={!isWritable}><SaveRegular aria-hidden="true" />Сохранить</button></div>
        </form>
      </section>
    </div>
  );
}

function ConflictDialog({ draft, onClose, onResolve }) {
  return (
    <div className="dialog-backdrop" role="presentation">
      <section className="dialog conflict-dialog" role="dialog" aria-modal="true" aria-labelledby="conflict-title">
        <div className="conflict-heading"><WarningRegular aria-hidden="true" /><div><p className="eyebrow">VERSION_CONFLICT</p><h2 id="conflict-title">Задача изменилась на сервере</h2></div></div>
        <p>Ваш черновик сохранён. Сравните значения — Task ничего не перезапишет без вашего решения.</p>
        <div className="conflict-grid" role="table" aria-label="Сравнение изменений">
          <div role="row"><strong role="columnheader">Поле</strong><strong role="columnheader">Ваш черновик</strong><strong role="columnheader">Версия сервера</strong></div>
          <div role="row"><span>Название</span><span>{draft.title}</span><span>Подготовить анализ продаж за июнь — финал</span></div>
          <div role="row"><span>Описание</span><span>{draft.description}</span><span>Добавлен комментарий финансового отдела.</span></div>
        </div>
        <div className="conflict-actions">
          <button className="button button--secondary" type="button" onClick={() => onResolve("reload")}>Загрузить серверную версию</button>
          <button className="button button--primary" type="button" onClick={() => onResolve("reapply")}>Повторить мои изменения</button>
          <button className="button button--ghost" type="button" onClick={() => onResolve("discard")}>Отменить мой черновик</button>
          <button className="button button--secondary" type="button" onClick={onClose}>Вернуться к черновику</button>
        </div>
      </section>
    </div>
  );
}

function DiagnosticsDialog({ mode, onClose, onRetry }) {
  const facts = mode === "storage"
    ? [
        [ServerRegular, "Сервер компании", "Доступен", "ok"],
        [DatabaseRegular, "Локальное хранилище", "Свободно 84 МБ из требуемых 620 МБ", ""],
        [LockClosedRegular, "Разрешённый кэш", "Доступен без обновления", "ok"],
        [PlugDisconnectedRegular, "Режим", "Только чтение", ""],
      ]
    : mode === "maintenance"
      ? [
          [ServerRegular, "Сервер компании", "Обслуживание до 10:45", ""],
          [LockClosedRegular, "Разрешённый кэш", "Доступен", "ok"],
          [DatabaseRegular, "Последняя синхронизация", "Сегодня, 10:23", "ok"],
          [PlugDisconnectedRegular, "Режим", "Только чтение", ""],
        ]
      : mode === "reconnecting" || mode === "scope"
        ? [
            [ServerRegular, "Сервер компании", "Доступен, проверка продолжается", "ok"],
            [KeyRegular, "Сессия", "Подтверждена", "ok"],
            [LockClosedRegular, "Область доступа", mode === "scope" ? "Изменилась — требуется обновление" : "Проверяется", ""],
            [PlugDisconnectedRegular, "Режим", "Запись отключена до готовности", ""],
          ]
        : [
            [ServerRegular, "Сервер компании", "Недоступен", ""],
            [LockClosedRegular, "Разрешённый кэш", "Доступен", "ok"],
            [DatabaseRegular, "Последняя синхронизация", "Сегодня, 10:23", "ok"],
            [PlugDisconnectedRegular, "Режим", "Только чтение", ""],
          ];

  return (
    <div className="dialog-backdrop" role="presentation" onMouseDown={onClose}>
      <section className="dialog diagnostics-dialog" role="dialog" aria-modal="true" aria-labelledby="diagnostics-title" onMouseDown={(event) => event.stopPropagation()}>
        <div className="dialog__header"><h2 id="diagnostics-title">Диагностика подключения</h2><button className="icon-button" type="button" onClick={onClose} aria-label="Закрыть диагностику"><DismissRegular aria-hidden="true" /></button></div>
        <div className="diagnostics-list">
          {facts.map(([Icon, label, value, tone]) => (
            <span key={label}><Icon aria-hidden="true" /><strong>{label}</strong><em className={tone}>{value}</em></span>
          ))}
        </div>
        <p className="dialog-note">Проверка не отправляет диагностические данные за пределы локальной сети.</p>
        <div className="dialog__actions"><button className="button button--secondary" type="button" onClick={onClose}>Закрыть</button><button className="button button--primary" type="button" onClick={onRetry}>{mode === "storage" ? "Проверить свободное место" : "Повторить подключение"}</button></div>
      </section>
    </div>
  );
}

function PickerField({ label, icon: Icon, value, onChange, options, helper }) {
  return (
    <label className="field picker-field">
      <span>{label}</span>
      <span className="picker-field__control">
        <Icon aria-hidden="true" />
        <select value={value} onChange={(event) => onChange(event.target.value)} aria-label={label}>
          {options.map((option) => <option key={option}>{option}</option>)}
        </select>
        <span className="picker-chip" aria-hidden="true">{value}</span>
      </span>
      {helper && <small className="field-hint">{helper}</small>}
    </label>
  );
}

function TasksSurface({ isWritable, onOpenTask, onToast, onPushUndo }) {
  const [taskRows, setTaskRows] = useState(taskTableRows);
  const [statusFilter, setStatusFilter] = useState("Все статусы");
  const [projectFilter, setProjectFilter] = useState("Все проекты");
  const [page, setPage] = useState(1);
  const [selectedIds, setSelectedIds] = useState(new Set());
  const [contextTask, setContextTask] = useState(null);
  const [bulkResult, setBulkResult] = useState(null);
  const filtered = taskRows.filter((task) => (
    (statusFilter === "Все статусы" || task.status === statusFilter)
    && (projectFilter === "Все проекты" || task.project === projectFilter)
  ));
  const pageSize = 5;
  const pageCount = Math.max(1, Math.ceil(filtered.length / pageSize));
  const visible = filtered.slice((page - 1) * pageSize, page * pageSize);

  useEffect(() => {
    setPage(1);
  }, [statusFilter, projectFilter]);

  useEffect(() => {
    function closeMenu(event) {
      if (event.key === "Escape") setContextTask(null);
    }
    window.addEventListener("keydown", closeMenu);
    return () => window.removeEventListener("keydown", closeMenu);
  }, []);

  function toggleSelected(id) {
    setBulkResult(null);
    setSelectedIds((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleVisible() {
    const visibleIds = visible.map((task) => task.id);
    const allVisibleSelected = visibleIds.every((id) => selectedIds.has(id));
    setSelectedIds((current) => {
      const next = new Set(current);
      visibleIds.forEach((id) => allVisibleSelected ? next.delete(id) : next.add(id));
      return next;
    });
  }

  function applyBulk(nextStatus) {
    const selected = taskRows.filter((task) => selectedIds.has(task.id));
    const failed = nextStatus === "Готово" ? selected.filter((task) => task.id === "table-2") : [];
    const failedIds = new Set(failed.map((task) => task.id));
    const successful = selected.filter((task) => !failedIds.has(task.id));
    setTaskRows((rows) => rows.map((task) => successful.some((item) => item.id === task.id) ? { ...task, status: nextStatus } : task));
    setBulkResult({
      action: nextStatus,
      success: successful.length,
      failed: failed.length,
      failedTitle: failed[0]?.title || "",
    });
    setSelectedIds(new Set());
  }

  function runContextAction(action, task) {
    setContextTask(null);
    if (action === "open") {
      onOpenTask(task);
      return;
    }
    if (action === "ready") {
      const previousStatus = task.status;
      setTaskRows((rows) => rows.map((item) => item.id === task.id ? { ...item, status: "Готово" } : item));
      onPushUndo("Изменён статус", () => setTaskRows((rows) => rows.map((item) => item.id === task.id ? { ...item, status: previousStatus } : item)));
      onToast(`Задача «${task.title}» завершена`);
      return;
    }
    onToast(`Действие «${action}» для «${task.title}»`);
  }

  return (
    <section className="tasks-surface" aria-label="Мои задачи">
      {!isWritable && <div className="surface-readonly"><LockClosedRegular aria-hidden="true" />Фильтрация доступна, изменения отключены до восстановления актуальных данных.</div>}
      {selectedIds.size > 0 && (
        <section className="selection-bar" aria-label={`Выбрано задач: ${selectedIds.size}`}>
          <span><CheckmarkCircleRegular aria-hidden="true" /><strong>Выбрано: {selectedIds.size}</strong></span>
          <button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => applyBulk("В работе")}>Перевести в работу</button>
          <button className="button button--primary" type="button" disabled={!isWritable} onClick={() => applyBulk("Готово")}>Завершить</button>
          <button className="button button--ghost" type="button" onClick={() => setSelectedIds(new Set())}>Снять выбор</button>
        </section>
      )}
      {bulkResult && (
        <section className={`bulk-result ${bulkResult.failed ? "bulk-result--partial" : "bulk-result--success"}`} role="status" aria-label="Результат массового действия">
          {bulkResult.failed ? <WarningRegular aria-hidden="true" /> : <CheckmarkCircleRegular aria-hidden="true" />}
          <span><strong>{bulkResult.failed ? "Действие выполнено частично" : "Действие выполнено"}</strong>{bulkResult.success} задач обновлено до статуса «{bulkResult.action}».{bulkResult.failed > 0 && <> Не обновлено: {bulkResult.failed} — «{bulkResult.failedTitle}» изменилась на сервере; выбор сохранён в отчёте, перезаписи не было.</>}</span>
          <button className="icon-button" type="button" onClick={() => setBulkResult(null)} aria-label="Закрыть результат"><DismissRegular aria-hidden="true" /></button>
        </section>
      )}
      <div className="surface-toolbar">
        <div className="filter-group" aria-label="Фильтры задач">
          <FilterRegular aria-hidden="true" />
          <label><span>Статус</span><select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}><option>Все статусы</option><option>Запланировано</option><option>В работе</option><option>На проверке</option><option>Готово</option><option>Просрочено</option></select></label>
          <label><span>Проект</span><select value={projectFilter} onChange={(event) => setProjectFilter(event.target.value)}><option>Все проекты</option>{[...new Set(taskRows.map((task) => task.project))].map((project) => <option key={project}>{project}</option>)}</select></label>
        </div>
        <span className="result-count">{filtered.length} задач</span>
      </div>

      <div className="task-table-wrap">
        <table className="task-table">
          <thead><tr><th className="task-select-cell"><input type="checkbox" aria-label="Выбрать задачи на странице" checked={visible.length > 0 && visible.every((task) => selectedIds.has(task.id))} onChange={toggleVisible} /></th><th>Задача</th><th>Проект</th><th>Исполнитель</th><th>Статус</th><th>Приоритет</th><th>Срок</th><th><span className="sr-only">Действия</span></th></tr></thead>
          <tbody>
            {visible.map((task) => (
              <tr key={task.id} className={selectedIds.has(task.id) ? "is-selected" : ""}>
                <td className="task-select-cell"><input type="checkbox" aria-label={`Выбрать: ${task.title}`} checked={selectedIds.has(task.id)} onChange={() => toggleSelected(task.id)} /></td>
                <td><button type="button" className="table-title" onClick={() => onOpenTask(task)} title={task.title}>{task.title}</button></td>
                <td>{task.project}</td>
                <td><span className="table-person"><span className="mini-avatar">{task.assignee.split(" ").map((word) => word[0]).join("")}</span>{task.assignee}</span></td>
                <td><span className={`status-pill status-pill--${task.status === "Просрочено" ? "danger" : task.status === "Готово" ? "done" : "neutral"}`}>{task.status}</span></td>
                <td><Priority tone={task.priorityTone} label={task.priority} /></td>
                <td className={task.status === "Просрочено" ? "overdue" : ""}>{task.due}</td>
                <td className="context-cell"><button className="icon-button" type="button" onClick={() => setContextTask((current) => current?.id === task.id ? null : task)} aria-expanded={contextTask?.id === task.id} aria-haspopup="menu" aria-label={`Действия: ${task.title}`}><MoreHorizontalRegular aria-hidden="true" /></button>
                  {contextTask?.id === task.id && (
                    <div className="context-menu" role="menu" aria-label={`Действия задачи: ${task.title}`}>
                      <button role="menuitem" type="button" onClick={() => runContextAction("open", task)}>Открыть детали</button>
                      <button role="menuitem" type="button" disabled={!isWritable} onClick={() => runContextAction("ready", task)}>Отметить готовой</button>
                      <button role="menuitem" type="button" disabled={!isWritable} onClick={() => runContextAction("edit", task)}>Изменить</button>
                      <button role="menuitem" type="button" disabled={!isWritable || task.project === "Юридическая поддержка"} title={task.project === "Юридическая поддержка" ? "Недоступно: нет права Task.Archive в этом проекте" : ""} onClick={() => runContextAction("archive", task)}>Архивировать{task.project === "Юридическая поддержка" ? " — нет права" : ""}</button>
                    </div>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
        {visible.length === 0 && <div className="surface-empty"><SearchRegular aria-hidden="true" /><strong>По этим фильтрам задач нет</strong><span>Сбросьте один из фильтров, чтобы вернуть результаты.</span><button className="button button--secondary" type="button" onClick={() => { setStatusFilter("Все статусы"); setProjectFilter("Все проекты"); }}>Сбросить фильтры</button></div>}
      </div>

      <nav className="pagination" aria-label="Страницы списка задач">
        <span>Показано {visible.length} из {filtered.length}</span>
        <button className="icon-button icon-button--bordered" type="button" disabled={page === 1} onClick={() => setPage((value) => value - 1)} aria-label="Предыдущая страница"><ChevronLeftRegular aria-hidden="true" /></button>
        {Array.from({ length: pageCount }, (_, index) => index + 1).map((number) => <button key={number} className={number === page ? "is-current" : ""} type="button" onClick={() => setPage(number)} aria-current={number === page ? "page" : undefined}>{number}</button>)}
        <button className="icon-button icon-button--bordered" type="button" disabled={page === pageCount} onClick={() => setPage((value) => value + 1)} aria-label="Следующая страница"><ChevronRightRegular aria-hidden="true" /></button>
      </nav>
    </section>
  );
}

function ProjectsSurface({ isWritable, onToast }) {
  const [projects, setProjects] = useState(projectTree);
  const [selectedId, setSelectedId] = useState("alpha");
  const [collapsed, setCollapsed] = useState({});
  const [activeTab, setActiveTab] = useState("overview");
  const [query, setQuery] = useState("");
  const [lifecycleFilter, setLifecycleFilter] = useState("Все");
  const [dialog, setDialog] = useState("");
  const [validation, setValidation] = useState("");
  const [projectDraft, setProjectDraft] = useState({ title: "", owner: "Анна К.", deadline: "2026-10-30" });
  const [memberDialog, setMemberDialog] = useState(null);
  const [memberNames, setMemberNames] = useState({});
  const [comments, setComments] = useState([
    { id: 1, author: "Анна К.", text: "Добавила результаты последнего согласования.", deleted: false },
    { id: 2, author: "Иван С.", text: "Проверяю связанные задачи перед завершением этапа.", deleted: false },
  ]);
  const [commentDraft, setCommentDraft] = useState("");
  const [purgeText, setPurgeText] = useState("");
  const selected = projects.find((project) => project.id === selectedId) || projects[0];
  const groups = [...new Set(projects.map((project) => project.group))];
  const lifecycleReadonly = selected ? ["Архив", "В корзине", "Удалён"].includes(selected.status) : false;
  const canWriteProject = isWritable && !lifecycleReadonly;
  const memberLabels = selected?.members.map((member, index) => memberNames[`${selected.id}-${member}`] || ["Анна К.", "Иван С.", "Мария С."][index] || `Участник ${index + 1}`) || [];
  const tabs = [
    ["overview", "Обзор"], ["tasks", "Задачи"], ["calendar", "Календарь"], ["members", "Участники"],
    ["files", "Файлы"], ["contacts", "Контакты"], ["comments", "Комментарии"], ["history", "История"], ["settings", "Настройки"],
  ];

  const filteredProjects = projects.filter((project) => {
    const matchesQuery = project.title.toLowerCase().includes(query.trim().toLowerCase());
    const matchesLifecycle = lifecycleFilter === "Все"
      || (lifecycleFilter === "Активные" && ["Активен", "Пауза", "Завершён"].includes(project.status))
      || (lifecycleFilter === "Архив" && project.status === "Архив")
      || (lifecycleFilter === "Корзина" && ["В корзине", "Удалён"].includes(project.status));
    return matchesQuery && matchesLifecycle;
  });

  useEffect(() => {
    setActiveTab("overview");
    setValidation("");
    setDialog("");
  }, [selectedId]);

  function updateSelected(patch) {
    setProjects((items) => items.map((project) => project.id === selected.id ? { ...project, ...patch } : project));
  }

  function createProject() {
    const title = projectDraft.title.trim();
    if (!title) {
      setValidation("VALIDATION_FAILED · Укажите название проекта.");
      return;
    }
    if (projects.some((project) => project.title.toLowerCase() === title.toLowerCase())) {
      setValidation("DUPLICATE_RESOURCE · Проект с таким названием уже существует.");
      return;
    }
    const created = {
      id: `project-${Date.now()}`,
      group: "Мои проекты",
      title,
      owner: projectDraft.owner,
      progress: 0,
      tasks: 0,
      deadline: projectDraft.deadline,
      status: "Активен",
      members: ["АК"],
    };
    setProjects((items) => [...items, created]);
    setSelectedId(created.id);
    setDialog("");
    setValidation("");
    onToast(`Проект «${title}» создан`);
  }

  function performLifecycle(action) {
    const nextByAction = {
      pause: selected.status === "Пауза" ? "Активен" : "Пауза",
      complete: "Завершён",
      archive: "Архив",
      trash: "В корзине",
      restore: "Активен",
      unarchive: "Активен",
      purge: "Удалён",
    };
    const next = nextByAction[action];
    if (action === "purge" && purgeText !== selected.title) {
      setValidation(`Для необратимого удаления метаданных введите «${selected.title}».`);
      return;
    }
    updateSelected({ status: next });
    setDialog("");
    setValidation("");
    setPurgeText("");
    onToast(action === "purge" ? "Метаданные проекта помечены как удалённые" : `Состояние проекта: ${next}`);
  }

  function addMember() {
    const initials = `Н${selected.members.length + 1}`;
    updateSelected({ members: [...selected.members, initials] });
    setMemberNames((current) => ({ ...current, [`${selected.id}-${initials}`]: "Новый участник" }));
    onToast("Участник добавлен после проверки Project.ManageMembers");
  }

  function addComment() {
    const text = commentDraft.trim();
    if (!text) return;
    setComments((items) => [...items, { id: Date.now(), author: "Вы", text, deleted: false }]);
    setCommentDraft("");
    onToast("Комментарий добавлен");
  }

  if (!selected) return null;

  return (
    <section className="projects-surface" aria-label="Проекты">
      <aside className="project-tree" aria-label="Дерево проектов">
        <div className="project-tree__header"><div><p className="eyebrow">Контекст работы</p><h2>Проекты</h2></div><button className="icon-button icon-button--bordered" type="button" disabled={!isWritable} onClick={() => { setProjectDraft({ title: "", owner: "Анна К.", deadline: "2026-10-30" }); setValidation(""); setDialog("project"); }} aria-label="Создать проект"><AddRegular aria-hidden="true" /></button></div>
        <label className="field project-filter"><span>Поиск проекта</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Название" /></label>
        <label className="field project-filter"><span>Состояние</span><select value={lifecycleFilter} onChange={(event) => setLifecycleFilter(event.target.value)}><option>Все</option><option>Активные</option><option>Архив</option><option>Корзина</option></select></label>
        {groups.map((group) => {
          const groupProjects = filteredProjects.filter((project) => project.group === group);
          if (!groupProjects.length) return null;
          return (
            <div className="tree-group" key={group}>
              <button type="button" className="tree-group__toggle" onClick={() => setCollapsed((state) => ({ ...state, [group]: !state[group] }))} aria-expanded={!collapsed[group]}>
                {collapsed[group] ? <ChevronRightRegular aria-hidden="true" /> : <ChevronDownRegular aria-hidden="true" />}<span>{group}</span><small>{groupProjects.length}</small>
              </button>
              {!collapsed[group] && <div role="tree">
                {groupProjects.map((project) => (
                  <button key={project.id} role="treeitem" aria-selected={selectedId === project.id} className={`project-node ${selectedId === project.id ? "is-selected" : ""}`} type="button" onClick={() => setSelectedId(project.id)}>
                    <FolderRegular aria-hidden="true" /><span><strong>{project.title}</strong><small>{project.status} · {project.tasks} задач · {project.progress}%</small></span>
                  </button>
                ))}
              </div>}
            </div>
          );
        })}
        {!filteredProjects.length && <div className="surface-empty"><FolderRegular aria-hidden="true" /><strong>Проекты не найдены</strong><span>Измените фильтр или создайте проект, если у вас есть Project.Create.</span></div>}
      </aside>

      <article className="project-inspector" aria-labelledby="project-title">
        {!isWritable && <div className="surface-readonly"><LockClosedRegular aria-hidden="true" />Проект показан из разрешённого кэша. Изменения временно отключены.</div>}
        {lifecycleReadonly && <div className="surface-readonly"><LockClosedRegular aria-hidden="true" />{selected.status === "Архив" ? "Архивный проект доступен только для чтения." : "Проект находится в корзине. Доступны восстановление и история."}</div>}
        <section className={`lifecycle-banner ${selected.status === "Активен" ? "lifecycle-banner--active" : "lifecycle-banner--paused"}`} aria-label={`Состояние проекта: ${selected.status}`}>
          {selected.status === "Активен" ? <CheckmarkCircleRegular aria-hidden="true" /> : <WarningRegular aria-hidden="true" />}
          <span><strong>{selected.status}</strong>{selected.status === "Пауза" ? "Новые задачи и изменения состава отключены; просмотр и история доступны." : selected.status === "Архив" ? "Архив не равен корзине: метаданные сохранены, запись отключена." : selected.status === "В корзине" ? "Томбстоун скрывает рабочие вкладки до восстановления." : "Команда видит только разрешённые действия текущего состояния."}</span>
          {!lifecycleReadonly && <button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => performLifecycle("pause")}>{selected.status === "Пауза" ? "Возобновить" : "Пауза"}</button>}
          {selected.status === "Архив" && <button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => performLifecycle("unarchive")}>Разархивировать</button>}
          {selected.status === "В корзине" && <button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => performLifecycle("restore")}>Восстановить</button>}
        </section>
        <div className="project-inspector__heading">
          <div><p className="eyebrow">{selected.status}</p><h2 id="project-title">{selected.title}</h2><span>Ответственный: {selected.owner}</span></div>
          <button className="button button--secondary" type="button" disabled={!canWriteProject} onClick={() => { setProjectDraft({ title: selected.title, owner: selected.owner, deadline: selected.deadline }); setValidation(""); setDialog("edit"); }}><EditRegular aria-hidden="true" />Изменить</button>
        </div>
        <div className="project-progress"><span><strong>Выполнение проекта</strong><em>{selected.progress}%</em></span><i><b style={{ width: `${selected.progress}%` }} /></i></div>
        <dl className="project-facts">
          <div><dt>Задачи</dt><dd>{selected.tasks}</dd></div><div><dt>Срок</dt><dd>{selected.deadline}</dd></div>
          <div><dt>Участники</dt><dd><span className="avatar-stack">{selected.members.map((member) => <span key={member}>{member}</span>)}</span>{selected.members.length} человека</dd></div>
          <div><dt>Состояние</dt><dd><span className="status-pill status-pill--neutral">{selected.status}</span></dd></div>
        </dl>
        <div className="project-tabs" role="tablist" aria-label="Разделы проекта">
          {tabs.map(([id, label]) => <button key={id} type="button" role="tab" aria-selected={activeTab === id} onClick={() => setActiveTab(id)}>{label}</button>)}
        </div>
        {activeTab === "overview" && <section className="project-summary"><h3>Ближайшие контрольные точки</h3><div><CheckmarkCircleRegular aria-hidden="true" /><span><strong>Исходные данные собраны</strong><small>Завершено 24 июля</small></span></div><div><CalendarRegular aria-hidden="true" /><span><strong>Промежуточное согласование</strong><small>5 августа · 4 задачи открыты</small></span></div><div><BranchForkRegular aria-hidden="true" /><span><strong>Финальная проверка</strong><small>22 сентября · зависит от 2 задач</small></span></div></section>}
        {activeTab === "tasks" && <section className="project-tab-panel" role="tabpanel" aria-label="Задачи проекта"><div className="panel-heading"><h3>Задачи проекта</h3><button className="button button--secondary" type="button" disabled={!canWriteProject}>Создать задачу</button></div>{taskTableRows.filter((task) => task.project === selected.title || selected.id === "alpha").slice(0, 4).map((task) => <div className="project-task-row" key={task.id}><TaskListSquareLtrFilled aria-hidden="true" /><span><strong>{task.title}</strong><small>{task.assignee} · {task.due}</small></span><span className="status-pill status-pill--neutral">{task.status}</span></div>)}</section>}
        {activeTab === "calendar" && <section className="project-tab-panel" role="tabpanel" aria-label="Календарь проекта"><h3>Календарь проекта</h3><p className="helper-copy">Проектный диапазон использует общий Calendar pattern. Перемещение отключается при потере Calendar.Read или записи.</p><div className="linked-card"><CalendarRegular aria-hidden="true" /><span><strong>Планирование команды</strong><small>5 августа · 10:00–11:00</small></span><button className="button button--ghost" type="button" disabled={!canWriteProject}>Открыть</button></div></section>}
        {activeTab === "members" && <section className="project-tab-panel" role="tabpanel" aria-label="Участники проекта"><div className="panel-heading"><div><h3>Участники</h3><p className="helper-copy">Владелец всегда остаётся в составе проекта.</p></div><button className="button button--secondary" type="button" disabled={!canWriteProject} onClick={addMember}><AddRegular aria-hidden="true" />Добавить</button></div>{selected.members.map((member, index) => <div className="member-row" key={member}><span className="mini-avatar">{member}</span><span><strong>{memberLabels[index]}</strong><small>{index === 0 ? "Владелец проекта" : "Участник"}</small></span><button className="button button--ghost" type="button" disabled={!canWriteProject} onClick={() => { setMemberDialog({ index, name: memberLabels[index] }); setValidation(""); }}>Изменить роль</button></div>)}</section>}
        {activeTab === "files" && <section className="project-tab-panel" role="tabpanel" aria-label="Файлы проекта"><div className="panel-heading"><h3>Связанные файлы</h3><button className="button button--secondary" type="button" disabled={!canWriteProject}>Связать</button></div><div className="linked-card"><DocumentRegular aria-hidden="true" /><span><strong>Отчёт_июль.xlsx</strong><small>Сетевое расположение · проверено 6 минут назад</small></span><button className="button button--ghost" type="button">Открыть</button></div><div className="linked-card is-unavailable"><LockClosedRegular aria-hidden="true" /><span><strong>Связанный объект недоступен</strong><small>Название и расположение скрыты текущей областью доступа.</small></span></div></section>}
        {activeTab === "contacts" && <section className="project-tab-panel" role="tabpanel" aria-label="Контакты проекта"><div className="panel-heading"><h3>Контакты</h3><button className="button button--secondary" type="button" disabled={!canWriteProject}>Связать</button></div><div className="linked-card"><PersonRegular aria-hidden="true" /><span><strong>Елена Морозова · ООО «Вектор»</strong><small>Заказчик · разрешённые контактные данные</small></span><button className="button button--ghost" type="button">Открыть</button></div></section>}
        {activeTab === "comments" && <section className="project-tab-panel" role="tabpanel" aria-label="Комментарии проекта"><h3>Комментарии</h3>{comments.map((comment) => <div className={`comment-row ${comment.deleted ? "is-deleted" : ""}`} key={comment.id}><CommentRegular aria-hidden="true" /><span><strong>{comment.deleted ? "Комментарий удалён" : comment.author}</strong><small>{comment.deleted ? "Текст недоступен; запись сохранена в истории." : comment.text}</small></span>{!comment.deleted && <button className="button button--ghost" type="button" disabled={!canWriteProject} onClick={() => setComments((items) => items.map((item) => item.id === comment.id ? { ...item, deleted: true } : item))}>Удалить</button>}</div>)}<div className="comment-composer"><label className="field"><span>Новый комментарий</span><textarea value={commentDraft} onChange={(event) => setCommentDraft(event.target.value)} disabled={!canWriteProject} /></label><button className="button button--primary" type="button" disabled={!canWriteProject || !commentDraft.trim()} onClick={addComment}>Добавить</button></div></section>}
        {activeTab === "history" && <section className="project-tab-panel timeline-history" role="tabpanel" aria-label="История проекта"><h3>История проекта</h3><div><span className="history-icon"><EditRegular aria-hidden="true" /></span><span><strong>Анна К. изменила срок проекта</strong><small>Сегодня, 09:42 · 15 сентября → 30 сентября</small></span></div><div><span className="history-icon"><PersonRegular aria-hidden="true" /></span><span><strong>Мария С. добавлена в проект</strong><small>Вчера, 16:18 · роль «Участник»</small></span></div><div className="history-redacted"><span className="history-icon"><LockClosedRegular aria-hidden="true" /></span><span><strong>Изменение недоступного объекта</strong><small>Содержимое скрыто вашей текущей областью доступа.</small></span></div></section>}
        {activeTab === "settings" && <section className="project-tab-panel" role="tabpanel" aria-label="Настройки проекта"><h3>Состояние проекта</h3><p className="helper-copy">Complete, Archive и Trash — разные переходы. Физические файлы не удаляются.</p><div className="lifecycle-actions"><button className="button button--secondary" type="button" disabled={!canWriteProject} onClick={() => { setValidation("4 активные задачи требуют решения перед завершением."); setDialog("complete"); }}>Завершить</button><button className="button button--secondary" type="button" disabled={!canWriteProject} onClick={() => { setValidation(""); setDialog("archive"); }}>Архивировать</button><button className="button button--danger" type="button" disabled={!canWriteProject} onClick={() => { setValidation(""); setDialog("trash"); }}>Переместить в корзину</button>{selected.status === "В корзине" && <button className="button button--danger" type="button" disabled={!isWritable} onClick={() => { setValidation(""); setDialog("purge"); }}>Удалить метаданные</button>}</div></section>}
      </article>

      {dialog && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog" role="dialog" aria-modal="true" aria-labelledby="wave-project-dialog-title"><div className="dialog__header"><div><p className="eyebrow">Projects · Wave B</p><h2 id="wave-project-dialog-title">{dialog === "project" ? "Создать проект" : dialog === "edit" ? "Изменить проект" : dialog === "complete" ? "Завершить проект" : dialog === "archive" ? "Архивировать проект" : dialog === "trash" ? "Переместить проект в корзину" : "Необратимо удалить метаданные"}</h2></div><button className="icon-button" type="button" onClick={() => { setDialog(""); setValidation(""); }} aria-label="Закрыть"><DismissRegular aria-hidden="true" /></button></div>{["project", "edit"].includes(dialog) ? <div className="dialog__grid"><label className="field"><span>Название</span><input value={projectDraft.title} onChange={(event) => setProjectDraft((draft) => ({ ...draft, title: event.target.value }))} /></label><label className="field"><span>Ответственный</span><select value={projectDraft.owner} onChange={(event) => setProjectDraft((draft) => ({ ...draft, owner: event.target.value }))}><option>Анна К.</option><option>Иван С.</option><option>Мария С.</option></select></label><label className="field"><span>Срок</span><input type="date" value={projectDraft.deadline} onChange={(event) => setProjectDraft((draft) => ({ ...draft, deadline: event.target.value }))} /></label></div> : dialog === "purge" ? <label className="field"><span>Введите название проекта для подтверждения</span><input value={purgeText} onChange={(event) => setPurgeText(event.target.value)} /></label> : <p>{dialog === "complete" ? "Завершение сохраняет проект активным для просмотра и истории. Сначала подтвердите обработку открытых задач." : dialog === "archive" ? "Архив станет read-only. Проект можно будет разархивировать при наличии разрешения." : "Корзина создаёт tombstone. Восстановление не раскрывает скрытый родительский проект."}</p>}{validation && <div className="inline-alert inline-alert--warning" role="alert"><WarningRegular aria-hidden="true" /><span>{validation}</span></div>}<div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => { setDialog(""); setValidation(""); }}>Отмена</button>{dialog === "project" && <button className="button button--primary" type="button" onClick={createProject}>Создать</button>}{dialog === "edit" && <button className="button button--primary" type="button" onClick={() => { if (!projectDraft.title.trim()) { setValidation("VALIDATION_FAILED · Укажите название проекта."); return; } updateSelected(projectDraft); setDialog(""); onToast("Проект обновлён"); }}>Сохранить</button>}{dialog === "complete" && <button className="button button--primary" type="button" onClick={() => performLifecycle("complete")}>Завершить несмотря на предупреждение</button>}{dialog === "archive" && <button className="button button--primary" type="button" onClick={() => performLifecycle("archive")}>Архивировать</button>}{dialog === "trash" && <button className="button button--danger" type="button" onClick={() => performLifecycle("trash")}>В корзину</button>}{dialog === "purge" && <button className="button button--danger" type="button" onClick={() => performLifecycle("purge")}>Удалить метаданные</button>}</div></section></div>}

      {memberDialog && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog" role="dialog" aria-modal="true" aria-labelledby="member-dialog-title"><div className="dialog__header"><div><p className="eyebrow">Project.ManageMembers</p><h2 id="member-dialog-title">Роль: {memberDialog.name}</h2></div><button className="icon-button" type="button" onClick={() => setMemberDialog(null)} aria-label="Закрыть"><DismissRegular aria-hidden="true" /></button></div><label className="field"><span>Роль проекта</span><select defaultValue={memberDialog.index === 0 ? "Владелец" : "Участник"}><option>Участник</option><option>Редактор</option><option>Владелец</option></select></label>{memberDialog.index === 0 && <div className="inline-alert inline-alert--warning"><WarningRegular aria-hidden="true" /><span>Нельзя удалить текущего владельца. Передайте владение другому участнику.</span></div>}<div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => setMemberDialog(null)}>Отмена</button>{memberDialog.index !== 0 && <button className="button button--secondary" type="button" disabled={!canWriteProject} onClick={() => { updateSelected({ owner: memberDialog.name }); setMemberDialog(null); onToast("Владение передано после подтверждения"); }}>Передать владение</button>}<button className="button button--primary" type="button" disabled={!canWriteProject} onClick={() => { setMemberDialog(null); onToast("Роль обновлена после server recheck"); }}>Применить</button></div></section></div>}
    </section>
  );
}

function FilesSurface({ isWritable, onToast }) {
  const [items, setItems] = useState([
    { id: "report", title: "Отчёт_июль.xlsx", kind: "Таблица", status: "Доступен", owner: "Анна К.", locations: [{ id: 1, scope: "Сеть", path: "\\\\fileserver\\reports\\Отчёт_июль.xlsx", priority: 1, availability: "Доступно", checked: "6 минут назад" }, { id: 2, scope: "Локально", path: "D:\\Task\\Cache\\Отчёт_июль.xlsx", priority: 2, availability: "Доступно на этом устройстве", checked: "12 минут назад" }] },
    { id: "brief", title: "Бриф_кампании.docx", kind: "Документ", status: "Сеть недоступна", owner: "Мария С.", locations: [{ id: 3, scope: "Сеть", path: "\\\\marketing\\campaigns\\Бриф_кампании.docx", priority: 1, availability: "NETWORK_RESOURCE_UNAVAILABLE", checked: "только что" }] },
    { id: "contract", title: "Договор_Вектор.pdf", kind: "PDF", status: "Ограничен", owner: "Иван С.", locations: [{ id: 4, scope: "Другое устройство", path: "Путь скрыт текущей областью доступа", priority: 1, availability: "Доступно на устройстве владельца", checked: "1 час назад" }] },
  ]);
  const [selectedId, setSelectedId] = useState("report");
  const [query, setQuery] = useState("");
  const [activeTab, setActiveTab] = useState("overview");
  const [dialog, setDialog] = useState("");
  const [diagnostic, setDiagnostic] = useState("");
  const [pathDraft, setPathDraft] = useState("");
  const [validation, setValidation] = useState("");
  const selected = items.find((item) => item.id === selectedId) || items[0];
  const filtered = items.filter((item) => item.title.toLowerCase().includes(query.trim().toLowerCase()));
  const activeLocation = selected.locations.find((location) => location.priority === 1) || selected.locations[0];
  const hasLocalLocation = selected.locations.some((location) => location.scope === "Локально" && location.availability.includes("Доступно"));
  const diagnosisCopy = {
    FILE_NO_LOCATION: "Для объекта нет разрешённого расположения.",
    FILE_NOT_FOUND: "Метаданные доступны, но Windows не нашла файл по разрешённому пути.",
    FILE_ACCESS_DENIED: "Метаданные разрешены; доступ отклонён Windows/SMB после handoff.",
    NETWORK_RESOURCE_UNAVAILABLE: "Сетевой ресурс не отвечает. Права каталога не изменились.",
    UNSAFE_FILE_TYPE: "Открытие остановлено политикой безопасных типов.",
    OTHER_DEVICE: "Файл доступен на другом устройстве; локальный путь скрыт.",
  };

  useEffect(() => {
    setActiveTab("overview");
    setDiagnostic("");
    setDialog("");
    setValidation("");
  }, [selectedId]);

  function openResolution() {
    if (!activeLocation) {
      setDiagnostic("FILE_NO_LOCATION");
      setDialog("diagnostic");
      return;
    }
    if (selected.status === "Сеть недоступна") {
      setDiagnostic("NETWORK_RESOURCE_UNAVAILABLE");
      setDialog("diagnostic");
      return;
    }
    if (selected.status === "Ограничен") {
      setDiagnostic("OTHER_DEVICE");
      setDialog("diagnostic");
      return;
    }
    setDialog("open");
  }

  function saveLocation(mode) {
    const path = pathDraft.trim();
    if (!path) {
      setValidation("UNSAFE_PATH · Укажите разрешённый путь.");
      return;
    }
    if (!path.startsWith("\\\\") && !/^[A-Za-z]:\\/.test(path)) {
      setValidation("UNSAFE_PATH · Допустим UNC или локальный абсолютный путь.");
      return;
    }
    const nextLocation = { id: Date.now(), scope: path.startsWith("\\\\") ? "Сеть" : "Локально", path, priority: mode === "replace" ? 1 : selected.locations.length + 1, availability: "Ожидает проверки", checked: "ещё не проверено" };
    setItems((catalog) => catalog.map((item) => item.id === selected.id ? { ...item, locations: mode === "replace" ? [nextLocation, ...item.locations.map((location) => ({ ...location, priority: location.priority + 1 }))] : [...item.locations, nextLocation] } : item));
    setDialog("");
    setPathDraft("");
    setValidation("");
    onToast(mode === "replace" ? "Основное мета-расположение заменено; физический файл не перемещён" : "Добавлено альтернативное расположение");
  }

  return (
    <section className="wave-surface" aria-label="Файлы">
      <aside className="wave-list" aria-label="Каталог файлов">
        <div className="wave-list__header"><div><p className="eyebrow">Виртуальный каталог</p><h2>Файлы</h2></div><button className="icon-button icon-button--bordered" type="button" disabled={!isWritable} onClick={() => { setDialog("add"); setPathDraft(""); setValidation(""); }} aria-label="Добавить объект каталога"><AddRegular aria-hidden="true" /></button></div>
        <label className="field"><span>Поиск в каталоге</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Название файла" /></label>
        <div className="catalog-group"><button className="tree-group__toggle" type="button" aria-expanded="true"><ChevronDownRegular aria-hidden="true" /><span>Документы команды</span><small>{filtered.length}</small></button>{filtered.map((item) => <button className={`catalog-item ${selected.id === item.id ? "is-selected" : ""}`} type="button" key={item.id} onClick={() => setSelectedId(item.id)} aria-current={selected.id === item.id}><DocumentRegular aria-hidden="true" /><span><strong>{item.title}</strong><small>{item.kind} · {item.status}</small></span></button>)}</div>
        {!filtered.length && <div className="surface-empty"><SearchRegular aria-hidden="true" /><strong>Ничего не найдено</strong><span>Фильтр не меняет доступ и не раскрывает скрытые объекты.</span></div>}
      </aside>
      <article className="wave-inspector" aria-labelledby="file-title">
        {!isWritable && <div className="surface-readonly"><LockClosedRegular aria-hidden="true" />Каталог показан из кэша. Метаданные read-only; локальное открытие оценивается отдельно Windows.</div>}
        <div className="wave-heading"><div><p className="eyebrow">CatalogItem · {selected.kind}</p><h2 id="file-title">{selected.title}</h2><span>Владелец метаданных: {selected.owner}</span></div><div className="wave-heading__actions"><button className="button button--primary" type="button" onClick={openResolution}>Открыть</button><button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => { setPathDraft(""); setValidation(""); setDialog("location"); }}>Расположения</button></div></div>
        <div className="file-trust-note"><ShieldErrorRegular aria-hidden="true" /><span><strong>Каталог ≠ доступ Windows</strong><small>Видимый CatalogItem не гарантирует, что OS/SMB разрешит открыть файл.</small></span></div>
        <dl className="project-facts"><div><dt>Состояние</dt><dd>{selected.status}</dd></div><div><dt>Расположения</dt><dd>{selected.locations.length}</dd></div><div><dt>Основная область</dt><dd>{activeLocation?.scope || "Нет"}</dd></div><div><dt>Последняя проверка</dt><dd>{activeLocation?.checked || "Нет данных"}</dd></div></dl>
        <div className="project-tabs" role="tablist" aria-label="Разделы файла">{[["overview", "Обзор"], ["locations", "Расположения"], ["links", "Связи"], ["history", "История"]].map(([id, label]) => <button key={id} type="button" role="tab" aria-selected={activeTab === id} onClick={() => setActiveTab(id)}>{label}</button>)}</div>
        {activeTab === "overview" && <section className="project-tab-panel"><h3>Разрешённые метаданные</h3><div className="linked-card"><DocumentRegular aria-hidden="true" /><span><strong>{selected.title}</strong><small>{selected.kind} · физический файл не копируется и не перемещается</small></span></div><div className="diagnostic-simulator" aria-label="Проверка состояний открытия"><span>Проверить recovery-состояние:</span>{Object.keys(diagnosisCopy).map((code) => <button className="button button--ghost" type="button" key={code} onClick={() => { setDiagnostic(code); setDialog("diagnostic"); }}>{code}</button>)}</div></section>}
        {activeTab === "locations" && <section className="project-tab-panel"><div className="panel-heading"><div><h3>Расположения</h3><p className="helper-copy">Путь другого пользователя не выводится, если он недоступен текущей области.</p></div><button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => { setPathDraft(""); setValidation(""); setDialog("location"); }}>Добавить</button></div>{selected.locations.map((location) => <div className="location-row" key={location.id}><FolderRegular aria-hidden="true" /><span><strong>{location.scope} · приоритет {location.priority}</strong><small>{location.path}</small><small>{location.availability} · {location.checked}</small></span><button className="button button--ghost" type="button" onClick={() => onToast("Проверка расположения запущена без изменения метаданных")}>Проверить</button></div>)}</section>}
        {activeTab === "links" && <section className="project-tab-panel"><h3>Типизированные связи</h3><div className="linked-card"><FolderRegular aria-hidden="true" /><span><strong>Проект «Отчётность»</strong><small>Project link · доступен</small></span></div><div className="linked-card is-unavailable"><LockClosedRegular aria-hidden="true" /><span><strong>Связанный объект недоступен</strong><small>Тип связи сохранён; идентифицирующие данные удалены до render.</small></span></div></section>}
        {activeTab === "history" && <section className="project-tab-panel timeline-history"><h3>История метаданных</h3><div><span className="history-icon"><EditRegular aria-hidden="true" /></span><span><strong>Добавлено альтернативное расположение</strong><small>Сегодня, 10:08 · Мария С.</small></span></div><div><span className="history-icon"><ArrowSyncRegular aria-hidden="true" /></span><span><strong>Доступность перепроверена</strong><small>Сегодня, 09:54 · без изменения файла</small></span></div></section>}
      </article>

      {dialog && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog" role="dialog" aria-modal="true" aria-labelledby="file-dialog-title"><div className="dialog__header"><div><p className="eyebrow">Files · Wave B</p><h2 id="file-dialog-title">{dialog === "open" ? "Разрешённое расположение" : dialog === "diagnostic" ? "Файл не открыт" : dialog === "add" ? "Добавить объект каталога" : "Управление расположениями"}</h2></div><button className="icon-button" type="button" aria-label="Закрыть" onClick={() => { setDialog(""); setValidation(""); }}><DismissRegular aria-hidden="true" /></button></div>{dialog === "open" && <><div className="location-row"><FolderRegular aria-hidden="true" /><span><strong>{activeLocation.scope}</strong><small>{activeLocation.path}</small><small>{activeLocation.availability}</small></span></div><p className="helper-copy">После подтверждения Task передаст разрешённый путь Windows. Дальнейший доступ решает OS/SMB.</p></>}{dialog === "diagnostic" && <><div className="inline-alert inline-alert--warning" role="alert"><WarningRegular aria-hidden="true" /><span><strong>{diagnostic}</strong><small>{diagnosisCopy[diagnostic]}</small></span></div>{!isWritable && hasLocalLocation && <div className="inline-alert"><CheckmarkCircleRegular aria-hidden="true" /><span>Сервер offline, но локальное расположение доступно для Windows open.</span></div>}<div className="safe-report"><strong>Безопасный отчёт</strong><span>Объект: {selected.kind}; результат: {diagnostic}; path: {diagnostic === "OTHER_DEVICE" ? "redacted" : activeLocation?.scope || "none"}.</span></div></>}{["location", "add"].includes(dialog) && <><label className="field"><span>{dialog === "add" ? "Выберите файл или UNC-путь" : "Новое расположение"}</span><input value={pathDraft} onChange={(event) => setPathDraft(event.target.value)} placeholder="\\server\share\file.ext" /></label><p className="helper-copy">Отмена native picker возвращает фокус сюда. Операция меняет только метаданные.</p></>}{validation && <div className="inline-alert inline-alert--warning" role="alert"><WarningRegular aria-hidden="true" /><span>{validation}</span></div>}<div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => setDialog("")}>Закрыть</button>{dialog === "open" && <button className="button button--primary" type="button" onClick={() => { setDialog(""); onToast("Путь передан Windows; результат OS/SMB ожидается"); }}>Передать Windows</button>}{dialog === "diagnostic" && <><button className="button button--secondary" type="button" onClick={() => onToast("Безопасный отчёт скопирован без скрытого пути")}>Копировать отчёт</button><button className="button button--primary" type="button" onClick={() => { setDialog(""); onToast("Повторная проверка запущена"); }}>Проверить снова</button></>}{dialog === "location" && <><button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => saveLocation("alternative")}>Добавить альтернативу</button><button className="button button--primary" type="button" disabled={!isWritable} onClick={() => saveLocation("replace")}>Заменить основное</button></>}{dialog === "add" && <button className="button button--primary" type="button" disabled={!isWritable} onClick={() => saveLocation("alternative")}>Добавить в каталог</button>}</div></section></div>}
    </section>
  );
}

function CrmSurface({ isWritable, onToast }) {
  const [records, setRecords] = useState([
    { id: "elena", type: "Контакт", name: "Елена Морозова", company: "ООО «Вектор»", role: "Коммерческий директор", channel: "+375 29 ••• •• 18", scope: "Разрешён", note: "Ключевой контакт по договору" },
    { id: "vector", type: "Компания", name: "ООО «Вектор»", company: "Производство", role: "Заказчик", channel: "info@vector.example", scope: "Разрешён", note: "Связана с проектом «Юридическая поддержка»" },
    { id: "hidden", type: "Контакт", name: "Недоступный контакт", company: "Скрыто", role: "Скрыто", channel: "Скрыто", scope: "Недоступен", note: "Идентифицирующие данные удалены до render" },
  ]);
  const [selectedId, setSelectedId] = useState("elena");
  const [filter, setFilter] = useState("Все");
  const [query, setQuery] = useState("");
  const [activeTab, setActiveTab] = useState("card");
  const [dialog, setDialog] = useState("");
  const [validation, setValidation] = useState("");
  const [draft, setDraft] = useState({ name: "", company: "", role: "", channel: "" });
  const [interactionDraft, setInteractionDraft] = useState({ occurred_at: "2026-07-28T10:30", type: "Звонок", summary: "", participants: "", next_step: "" });
  const [interactions, setInteractions] = useState([
    { id: 1, occurred_at: "28 июля · 09:30", type: "Звонок", summary: "Согласовали структуру отчёта", participants: "Елена Морозова, Анна К.", next_step: "Отправить итоговую версию 30 июля" },
    { id: 2, occurred_at: "24 июля · 16:10", type: "Встреча", summary: "Проверили условия договора", participants: "Участник недоступен", next_step: "Связать задачу юридического отдела" },
  ]);
  const selected = records.find((record) => record.id === selectedId) || records[0];
  const filtered = records.filter((record) => (filter === "Все" || record.type === filter) && record.name.toLowerCase().includes(query.trim().toLowerCase()));

  useEffect(() => {
    setActiveTab("card");
    setDialog("");
    setValidation("");
  }, [selectedId]);

  function saveRecord() {
    const name = draft.name.trim();
    if (!name) {
      setValidation("VALIDATION_FAILED · Укажите имя контакта или название компании.");
      return;
    }
    if (records.some((record) => record.name.toLowerCase() === name.toLowerCase() && record.id !== selected.id)) {
      setValidation("DUPLICATE_RESOURCE · Возможен дубликат. Черновик сохранён.");
      return;
    }
    if (dialog === "edit") {
      setRecords((items) => items.map((record) => record.id === selected.id ? { ...record, ...draft } : record));
    } else {
      const created = { id: `crm-${Date.now()}`, type: "Контакт", ...draft, scope: "Разрешён", note: "Создано в текущем allowed scope" };
      setRecords((items) => [...items, created]);
      setSelectedId(created.id);
    }
    setDialog("");
    setValidation("");
    onToast("CRM-черновик сохранён после server recheck");
  }

  function addInteraction() {
    if (!interactionDraft.summary.trim()) {
      setValidation("VALIDATION_FAILED · Добавьте краткое содержание взаимодействия.");
      return;
    }
    setInteractions((items) => [{ id: Date.now(), ...interactionDraft, occurred_at: interactionDraft.occurred_at.replace("T", " · ") }, ...items]);
    setDialog("");
    setValidation("");
    onToast("Взаимодействие добавлено без внешней отправки");
  }

  return (
    <section className="wave-surface" aria-label="CRM">
      <aside className="wave-list" aria-label="Контакты и компании"><div className="wave-list__header"><div><p className="eyebrow">Разрешённая область</p><h2>Контакты</h2></div><button className="icon-button icon-button--bordered" type="button" disabled={!isWritable} onClick={() => { setDraft({ name: "", company: "", role: "", channel: "" }); setValidation(""); setDialog("create"); }} aria-label="Создать контакт"><AddRegular aria-hidden="true" /></button></div><label className="field"><span>Поиск</span><input value={query} onChange={(event) => setQuery(event.target.value)} placeholder="Имя или компания" /></label><div className="segmented-filter" role="group" aria-label="Тип записи">{["Все", "Контакт", "Компания"].map((value) => <button type="button" key={value} aria-pressed={filter === value} onClick={() => setFilter(value)}>{value}</button>)}</div>{filtered.map((record) => <button className={`crm-list-item ${selected.id === record.id ? "is-selected" : ""}`} type="button" key={record.id} onClick={() => setSelectedId(record.id)}><PersonRegular aria-hidden="true" /><span><strong>{record.name}</strong><small>{record.type} · {record.company}</small></span>{record.scope === "Недоступен" && <LockClosedRegular aria-label="Недоступно" />}</button>)}</aside>
      <article className="wave-inspector" aria-labelledby="crm-title">{!isWritable && <div className="surface-readonly"><LockClosedRegular aria-hidden="true" />Offline-результаты могут быть неполными. Изменения и внешние действия отключены.</div>}<div className="wave-heading"><div><p className="eyebrow">{selected.type}</p><h2 id="crm-title">{selected.name}</h2><span>{selected.scope === "Недоступен" ? "Нейтральная недоступность" : selected.company}</span></div><button className="button button--secondary" type="button" disabled={!isWritable || selected.scope === "Недоступен"} onClick={() => { setDraft({ name: selected.name, company: selected.company, role: selected.role, channel: selected.channel }); setValidation(""); setDialog("edit"); }}><EditRegular aria-hidden="true" />Изменить</button></div>{selected.scope === "Недоступен" && <div className="inline-alert inline-alert--warning"><LockClosedRegular aria-hidden="true" /><span>Объект недоступен. Счётчики, каналы и связанные данные не раскрываются.</span></div>}<div className="project-tabs" role="tablist" aria-label="Разделы CRM">{[["card", "Карточка"], ["timeline", "Взаимодействия"], ["links", "Связи"], ["history", "История"]].map(([id, label]) => <button type="button" role="tab" key={id} aria-selected={activeTab === id} onClick={() => setActiveTab(id)}>{label}</button>)}</div>{activeTab === "card" && <section className="project-tab-panel"><h3>Разрешённые поля</h3><dl className="crm-facts"><div><dt>Компания</dt><dd>{selected.company}</dd></div><div><dt>Роль</dt><dd>{selected.role}</dd></div><div><dt>Канал</dt><dd>{selected.channel}</dd></div><div><dt>Контекст</dt><dd>{selected.note}</dd></div></dl>{selected.scope !== "Недоступен" && <button className="button button--secondary" type="button" disabled={!isWritable} onClick={() => onToast("Внешний обработчик открывается только после явного действия пользователя")}>Открыть канал</button>}</section>}{activeTab === "timeline" && <section className="project-tab-panel"><div className="panel-heading"><div><h3>Хронология взаимодействий</h3><p className="helper-copy">Ручные записи не отправляют сообщения и письма.</p></div><button className="button button--primary" type="button" disabled={!isWritable || selected.scope === "Недоступен"} onClick={() => { setInteractionDraft({ occurred_at: "2026-07-28T10:30", type: "Звонок", summary: "", participants: selected.name, next_step: "" }); setValidation(""); setDialog("interaction"); }}><AddRegular aria-hidden="true" />Добавить</button></div>{interactions.map((interaction) => <div className="interaction-row" key={interaction.id}><span className="history-icon"><CommentRegular aria-hidden="true" /></span><span><strong>{interaction.type} · {interaction.summary}</strong><small>{interaction.occurred_at} · {interaction.participants}</small><small>Следующий шаг: {interaction.next_step || "не указан"}</small></span><button className="button button--ghost" type="button" disabled={!isWritable}>Изменить</button></div>)}</section>}{activeTab === "links" && <section className="project-tab-panel"><h3>Типизированные связи</h3><div className="linked-card"><TaskListSquareLtrFilled aria-hidden="true" /><span><strong>Задача «Проверить договор»</strong><small>Task link · разрешён</small></span></div><div className="linked-card"><FolderRegular aria-hidden="true" /><span><strong>Проект «Юридическая поддержка»</strong><small>Project link · разрешён</small></span></div><div className="linked-card is-unavailable"><LockClosedRegular aria-hidden="true" /><span><strong>Связанный объект недоступен</strong><small>Тип связи известен, идентифицирующие метаданные скрыты.</small></span></div></section>}{activeTab === "history" && <section className="project-tab-panel timeline-history"><h3>История CRM</h3><div><span className="history-icon"><EditRegular aria-hidden="true" /></span><span><strong>Изменена роль контакта</strong><small>Сегодня, 08:45 · Анна К.</small></span></div><div className="history-redacted"><span className="history-icon"><LockClosedRegular aria-hidden="true" /></span><span><strong>Изменение недоступной связи</strong><small>Содержимое удалено по текущему History.Read.</small></span></div></section>}</article>
      {dialog && <div className="dialog-backdrop" role="presentation"><section className="dialog wave-dialog" role="dialog" aria-modal="true" aria-labelledby="crm-dialog-title"><div className="dialog__header"><div><p className="eyebrow">CRM · Wave B</p><h2 id="crm-dialog-title">{dialog === "interaction" ? "Добавить взаимодействие" : dialog === "edit" ? "Изменить запись" : "Создать контакт"}</h2></div><button className="icon-button" type="button" aria-label="Закрыть" onClick={() => { setDialog(""); setValidation(""); }}><DismissRegular aria-hidden="true" /></button></div>{dialog === "interaction" ? <div className="dialog__grid"><label className="field"><span>Дата и время</span><input type="datetime-local" value={interactionDraft.occurred_at} onChange={(event) => setInteractionDraft((draftValue) => ({ ...draftValue, occurred_at: event.target.value }))} /></label><label className="field"><span>Тип</span><select value={interactionDraft.type} onChange={(event) => setInteractionDraft((draftValue) => ({ ...draftValue, type: event.target.value }))}><option>Звонок</option><option>Встреча</option><option>Сообщение</option></select></label><label className="field"><span>Краткое содержание</span><textarea value={interactionDraft.summary} onChange={(event) => setInteractionDraft((draftValue) => ({ ...draftValue, summary: event.target.value }))} /></label><label className="field"><span>Участники</span><input value={interactionDraft.participants} onChange={(event) => setInteractionDraft((draftValue) => ({ ...draftValue, participants: event.target.value }))} /></label><label className="field"><span>Следующий шаг</span><input value={interactionDraft.next_step} onChange={(event) => setInteractionDraft((draftValue) => ({ ...draftValue, next_step: event.target.value }))} /></label></div> : <div className="dialog__grid"><label className="field"><span>Имя / название</span><input value={draft.name} onChange={(event) => setDraft((draftValue) => ({ ...draftValue, name: event.target.value }))} /></label><label className="field"><span>Компания</span><input value={draft.company} onChange={(event) => setDraft((draftValue) => ({ ...draftValue, company: event.target.value }))} /></label><label className="field"><span>Роль</span><input value={draft.role} onChange={(event) => setDraft((draftValue) => ({ ...draftValue, role: event.target.value }))} /></label><label className="field"><span>Канал</span><input value={draft.channel} onChange={(event) => setDraft((draftValue) => ({ ...draftValue, channel: event.target.value }))} /></label></div>}{validation && <div className="inline-alert inline-alert--warning" role="alert"><WarningRegular aria-hidden="true" /><span>{validation}</span></div>}<div className="dialog__actions"><button className="button button--secondary" type="button" onClick={() => setDialog("")}>Отмена</button><button className="button button--primary" type="button" onClick={dialog === "interaction" ? addInteraction : saveRecord}>{dialog === "interaction" ? "Добавить" : "Сохранить"}</button></div></section></div>}
    </section>
  );
}

function NotificationCenter({ notifications, setNotifications, onClose, onOpen, onToast }) {
  const [filter, setFilter] = useState("Непрочитанные");
  const visible = notifications.filter((item) => filter === "Все" || item.unread || item.result);

  function applyAction(notification) {
    setNotifications((items) => items.map((item) => item.id === notification.id ? { ...item, unread: false } : item));
    if (notification.targetState === "changed") {
      setNotifications((items) => items.map((item) => item.id === notification.id ? { ...item, result: "Состояние изменилось: задача уже завершена Анной К. в 10:21. Повторное действие не выполнено." } : item));
      onToast("Действие не применено: состояние задачи изменилось");
      return;
    }
    onOpen(notification);
  }

  return (
    <section className="notification-center" role="dialog" aria-modal="false" aria-labelledby="notifications-title">
      <div className="notification-center__header"><div><p className="eyebrow">Центр уведомлений</p><h2 id="notifications-title">Уведомления</h2></div><button className="icon-button" type="button" onClick={onClose} aria-label="Закрыть уведомления"><DismissRegular aria-hidden="true" /></button></div>
      <div className="notification-filters" role="group" aria-label="Фильтр уведомлений"><button type="button" className={filter === "Непрочитанные" ? "is-active" : ""} onClick={() => setFilter("Непрочитанные")}>Непрочитанные</button><button type="button" className={filter === "Все" ? "is-active" : ""} onClick={() => setFilter("Все")}>Все</button><button type="button" onClick={() => setNotifications((items) => items.map((item) => ({ ...item, unread: false })))}>Прочитать все</button></div>
      <div className="notification-list">
        {visible.map((notification) => (
          <article key={notification.id} className={`notification-item ${notification.unread ? "is-unread" : ""}`}>
            <span className="notification-item__marker" />
            <div><span className="notification-item__time">{notification.time}</span><strong>{notification.title}</strong><p>{notification.meta}</p>
              {notification.result && <div className="notification-result" role="status"><WarningRegular aria-hidden="true" /><span><strong>Состояние цели изменилось</strong>{notification.result}</span></div>}
              <div className="notification-actions"><button className="button button--secondary" type="button" onClick={() => applyAction(notification)}>{notification.action}</button>{notification.unread && <button className="button button--ghost" type="button" onClick={() => setNotifications((items) => items.map((item) => item.id === notification.id ? { ...item, unread: false } : item))}>Отметить прочитанным</button>}</div>
            </div>
          </article>
        ))}
        {visible.length === 0 && <div className="surface-empty"><CheckmarkCircleRegular aria-hidden="true" /><strong>Новых уведомлений нет</strong><span>Все события уже просмотрены.</span></div>}
      </div>
    </section>
  );
}

function SessionRevokedDialog({ onSignIn }) {
  return (
    <div className="dialog-backdrop dialog-backdrop--blocking" role="presentation">
      <section className="dialog session-dialog" role="alertdialog" aria-modal="true" aria-labelledby="session-title">
        <LockClosedRegular aria-hidden="true" />
        <p className="eyebrow">SESSION_REVOKED</p>
        <h2 id="session-title">Сессия завершена администратором</h2>
        <p>Доступ к локальному кэшу закрыт. Task не показывает ранее загруженные данные, пока вы не подтвердите учётную запись.</p>
        <div className="dialog__actions"><button className="button button--primary" type="button" onClick={onSignIn}>Войти снова</button></div>
      </section>
    </div>
  );
}

const calendarSeed = [
  { id: "calendar-planning", eventDate: "2026-07-28", title: "Ежедневное планирование команды", project: "Внутренние процессы", assignee: "Иван С.", status: "Готово", start: 540, duration: 45, tone: "done", description: "Короткая синхронизация приоритетов.", userAttendees: ["Иван С."], contactAttendees: [], response: "accepted", version: 3 },
  { id: "calendar-analysis", eventDate: "2026-07-28", title: "Подготовить анализ продаж за июнь", project: "Отчётность", assignee: "Иван С.", status: "В работе", start: 600, duration: 60, tone: "high", description: "Сверить показатели с утверждённой витриной.", userAttendees: ["Иван С.", "Мария С."], contactAttendees: ["Алексей В. · клиент"], response: "tentative", version: 7 },
  { id: "calendar-client", eventDate: "2026-07-28", title: "Звонок с клиентом", project: "Коммуникации", assignee: "Мария С.", status: "Запланировано", start: 660, duration: 45, tone: "medium", description: "Статус интеграции и следующие шаги.", userAttendees: ["Мария С."], contactAttendees: ["ООО «Вектор»"], response: "pending", version: 2 },
  { id: "calendar-presentation", eventDate: "2026-07-28", title: "Согласование макета презентации", project: "Маркетинговая кампания", assignee: "Мария С.", status: "Запланировано", start: 690, duration: 45, tone: "medium", description: "Проверить макет перед передачей в производство.", userAttendees: ["Мария С."], contactAttendees: [], response: "accepted", version: 4 },
  { id: "calendar-contract", eventDate: "2026-07-28", title: "Проверить договор с ООО «Вектор»", project: "Юридическая поддержка", assignee: "Иван С.", status: "Запланировано", start: 855, duration: 45, tone: "high", description: "Согласовать замечания юридического отдела.", userAttendees: ["Иван С."], contactAttendees: ["ООО «Вектор»"], response: "pending", version: 5 },
];

function minutesLabel(value) {
  const hours = String(Math.floor(value / 60)).padStart(2, "0");
  const minutes = String(value % 60).padStart(2, "0");
  return `${hours}:${minutes}`;
}

function CalendarSurface({ isWritable, onToast, onSelect, onPushUndo }) {
  const [mode, setMode] = useState("week");
  const [cursorDate, setCursorDate] = useState(() => new Date(2026, 6, 28));
  const [items, setItems] = useState(calendarSeed);
  const [selectedId, setSelectedId] = useState("calendar-analysis");
  const [projectFilter, setProjectFilter] = useState("Все проекты");
  const [statusFilter, setStatusFilter] = useState("Все статусы");
  const [assigneeFilter, setAssigneeFilter] = useState("Все исполнители");
  const [slot, setSlot] = useState(null);
  const [newTitle, setNewTitle] = useState("");
  const [editor, setEditor] = useState(null);
  const [overlapDraft, setOverlapDraft] = useState(null);
  const [staleRollback, setStaleRollback] = useState(null);
  const [draggedId, setDraggedId] = useState(null);
  const surfaceRef = useRef(null);
  const selected = items.find((item) => item.id === selectedId) || items[0];
  const visibleItems = items.filter((item) => (
    (projectFilter === "Все проекты" || item.project === projectFilter)
    && (statusFilter === "Все статусы" || item.status === statusFilter)
    && (assigneeFilter === "Все исполнители" || item.assignee === assigneeFilter)
  ));
  const projects = [...new Set(items.map((item) => item.project))];
  const assignees = [...new Set(items.map((item) => item.assignee))];
  const monthNames = ["января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря"];
  const monthNamesNominative = ["Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"];
  const shortWeekdays = ["Вс", "Пн", "Вт", "Ср", "Чт", "Пт", "Сб"];
  const todayKey = "2026-07-28";
  const toDateKey = (date) => `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
  const copyDate = (date) => new Date(date.getFullYear(), date.getMonth(), date.getDate());
  const getWeekStart = (date) => {
    const start = copyDate(date);
    start.setDate(start.getDate() - ((start.getDay() + 6) % 7));
    return start;
  };
  const formatDate = (date) => `${date.getDate()} ${monthNames[date.getMonth()]} ${date.getFullYear()}`;
  const formatDay = (date) => `${shortWeekdays[date.getDay()]} ${date.getDate()}`;
  const weekStart = getWeekStart(cursorDate);
  const weekDates = Array.from({ length: 5 }, (_, index) => {
    const day = copyDate(weekStart);
    day.setDate(day.getDate() + index);
    return day;
  });
  const visibleDates = mode === "week" ? weekDates : [cursorDate];
  const periodTitle = mode === "day"
    ? formatDate(cursorDate)
    : mode === "week"
      ? `${weekStart.getDate()}–${weekDates[4].getDate()} ${monthNames[weekStart.getMonth()]} ${weekStart.getFullYear()}`
      : mode === "month"
        ? `${monthNames[cursorDate.getMonth()]} ${cursorDate.getFullYear()}`
        : String(cursorDate.getFullYear());
  const periodUnit = mode === "day" ? "день" : mode === "week" ? "неделя" : mode === "month" ? "месяц" : "год";
  const monthStartOffset = (new Date(cursorDate.getFullYear(), cursorDate.getMonth(), 1).getDay() + 6) % 7;
  const daysInMonth = new Date(cursorDate.getFullYear(), cursorDate.getMonth() + 1, 0).getDate();
  const monthCells = Array.from({ length: monthStartOffset + daysInMonth }, (_, index) => index < monthStartOffset ? null : index - monthStartOffset + 1);
  const datesWithEvents = new Set(visibleItems.map((item) => item.eventDate));
  const yearMonths = Array.from({ length: 12 }, (_, month) => ({
    month,
    label: monthNamesNominative[month],
    eventCount: visibleItems.filter((item) => item.eventDate?.startsWith(`${cursorDate.getFullYear()}-${String(month + 1).padStart(2, "0")}`)).length,
  }));

  function selectCalendarItem(item) {
    setSelectedId(item.id);
    onSelect({ ...item, priority: item.tone === "high" ? "Высокая" : item.tone === "medium" ? "Средняя" : "Низкая", priorityTone: item.tone === "done" ? "low" : item.tone });
  }

  function shiftPeriod(direction) {
    setCursorDate((current) => {
      const next = copyDate(current);
      if (mode === "day") next.setDate(next.getDate() + direction);
      if (mode === "week") next.setDate(next.getDate() + direction * 7);
      if (mode === "month") next.setMonth(next.getMonth() + direction);
      if (mode === "year") next.setFullYear(next.getFullYear() + direction);
      return next;
    });
    setSlot(null);
  }

  function returnToToday() {
    setCursorDate(new Date(2026, 6, 28));
    setSlot(null);
  }

  function openEditor(item = null, start = 600, title = "") {
    if (!isWritable) return onToast("Редактор событий недоступен в режиме только для чтения");
    setEditor(createCalendarEventDraft({ ...(item || {}), eventDate: item?.eventDate || toDateKey(cursorDate), title: item?.title || title, start: item?.start ?? start, isNew: !item }));
  }

  function updateEditor(field, value) {
    setEditor((current) => ({
      ...current,
      [field]: value,
      state: field === "state" ? value : current.state === "validation" ? null : current.state,
    }));
  }

  function saveEditor(event) {
    event.preventDefault();
    if (!editor || !isWritable) return;
    const fieldErrors = validateCalendarEventDraft(editor);
    if (Object.keys(fieldErrors).length) return setEditor((current) => ({ ...current, state: "validation", fieldErrors }));
    if (!canCommitCalendarEvent({ isWritable, capability: true, state: editor.state })) return;
    const item = { ...editor, id: editor.id || `calendar-${Date.now()}`, title: editor.title.trim(), version: (editor.version || 0) + 1, tone: editor.status === "Готово" ? "done" : editor.status === "В работе" ? "high" : "medium" };
    const collision = overlaps(item, item.id);
    if (collision) {
      setOverlapDraft({ candidate: item, collision, isNew: editor.isNew });
      setEditor(null);
      return;
    }
    setItems((current) => editor.isNew ? [...current, item] : current.map((entry) => entry.id === item.id ? item : entry));
    setSelectedId(item.id);
    setEditor(null);
    onToast(editor.isNew ? "CalendarEvent создан с idempotency key; повтор не создаст дубль" : "CalendarEvent обновлён с проверкой версии");
  }

  function respondToInvite(response) {
    if (!editor || !canCommitCalendarEvent({ isWritable, capability: true, state: editor.state })) {
      onToast("Ответ не отправлен: команда недоступна в текущем состоянии");
      return;
    }
    setEditor((current) => applyCalendarResponse(current, response));
    onToast("Ответ на приглашение добавлен в черновик события");
  }

  function overlaps(candidate, withoutId = candidate.id) {
    return items.find((item) => item.id !== withoutId && candidate.start < item.start + item.duration && candidate.start + candidate.duration > item.start);
  }

  function applySchedule(candidate, source) {
    if (!isWritable) {
      onToast("Календарь доступен только для чтения: сервер недоступен");
      return;
    }
    if (source === "drag" && candidate.id === "calendar-analysis" && candidate.start >= 660) {
      setStaleRollback({ candidate, previous: items.find((item) => item.id === candidate.id) });
      return;
    }
    const collision = overlaps(candidate);
    if (collision) {
      setOverlapDraft({ candidate, collision });
      return;
    }
    setItems((current) => current.map((item) => item.id === candidate.id ? candidate : item));
    onToast(`Расписание обновлено: ${minutesLabel(candidate.start)}–${minutesLabel(candidate.start + candidate.duration)}`);
  }

  function forceOverlap() {
    setItems((current) => overlapDraft.isNew
      ? [...current, overlapDraft.candidate]
      : current.map((item) => item.id === overlapDraft.candidate.id ? overlapDraft.candidate : item));
    setSelectedId(overlapDraft.candidate.id);
    setOverlapDraft(null);
    onToast("Пересечение сохранено с предупреждением для участников");
  }

  function createFromSlot(event) {
    event.preventDefault();
    if (!newTitle.trim() || !slot || !isWritable) return;
    openEditor(null, slot, newTitle.trim());
    setNewTitle("");
    setSlot(null);
  }

  function adjustSelected(type, amount) {
    if (!selected) return;
    const candidate = type === "move"
      ? { ...selected, start: Math.max(480, Math.min(1050, selected.start + amount)) }
      : { ...selected, duration: Math.max(15, Math.min(180, selected.duration + amount)) };
    applySchedule(candidate, "keyboard");
  }

  useEffect(() => {
    function handleKeyDown(event) {
      if (!surfaceRef.current?.contains(document.activeElement) || !selected || !isWritable) return;
      if (event.altKey && ["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) {
        event.preventDefault();
        if (event.key === "ArrowLeft") adjustSelected("move", -30);
        if (event.key === "ArrowRight") adjustSelected("move", 30);
        if (event.key === "ArrowUp") adjustSelected("resize", -15);
        if (event.key === "ArrowDown") adjustSelected("resize", 15);
      }
    }
    window.addEventListener("keydown", handleKeyDown);
    return () => window.removeEventListener("keydown", handleKeyDown);
  }, [selected, isWritable, items]);

  const renderItem = (item, compact = false) => (
    <button
      key={item.id}
      type="button"
      draggable={isWritable}
      className={`calendar-event calendar-event--${item.tone} ${selectedId === item.id ? "is-selected" : ""}`}
      onClick={() => { selectCalendarItem(item); openEditor(item); }}
      onDragStart={() => setDraggedId(item.id)}
      aria-label={`${item.title}, ${minutesLabel(item.start)}–${minutesLabel(item.start + item.duration)}. Перетащите или используйте Alt со стрелками.`}
    >
      <strong>{compact ? item.title.slice(0, 18) : item.title}</strong>
      <small>{minutesLabel(item.start)}–{minutesLabel(item.start + item.duration)} · {item.assignee}</small>
    </button>
  );

  return (
    <section className="calendar-surface" ref={surfaceRef} aria-label="Календарь">
      <div className="calendar-surface__header">
        <div>
          <p className="eyebrow">Планирование</p>
          <h2>Календарь</h2>
          <span>{periodTitle} · Europe/Minsk</span>
          <div className="calendar-navigation" role="group" aria-label="Навигация по календарю">
            <button className="icon-button icon-button--bordered" type="button" onClick={() => shiftPeriod(-1)} aria-label={`Предыдущ${mode === "week" ? "ая" : "ий"} ${periodUnit}`}>
              <ChevronLeftRegular aria-hidden="true" />
            </button>
            <button className="button button--secondary" type="button" onClick={returnToToday}>Сегодня</button>
            <button className="icon-button icon-button--bordered" type="button" onClick={() => shiftPeriod(1)} aria-label={`Следующ${mode === "week" ? "ая" : "ий"} ${periodUnit}`}>
              <ChevronRightRegular aria-hidden="true" />
            </button>
            <strong>{periodTitle}</strong>
          </div>
        </div>
        <div className="view-switcher" role="group" aria-label="Вид календаря">
          {[['day', 'День'], ['week', 'Неделя'], ['month', 'Месяц'], ['year', 'Год']].map(([value, label]) => <button key={value} type="button" className={mode === value ? "is-active" : ""} onClick={() => setMode(value)} aria-pressed={mode === value}>{label}</button>)}
        </div>
      </div>
      {!isWritable && <div className="calendar-readonly" role="status"><LockClosedRegular aria-hidden="true" /><span><strong>Только чтение.</strong> Данные календаря из разрешённого кэша; создание, перемещение и изменение длительности отключены.</span></div>}
      <div className="calendar-filters" aria-label="Фильтры календаря">
        <FilterRegular aria-hidden="true" />
        <label><span>Проект</span><select value={projectFilter} onChange={(event) => setProjectFilter(event.target.value)}><option>Все проекты</option>{projects.map((value) => <option key={value}>{value}</option>)}</select></label>
        <label><span>Статус</span><select value={statusFilter} onChange={(event) => setStatusFilter(event.target.value)}><option>Все статусы</option>{[...new Set(items.map((item) => item.status))].map((value) => <option key={value}>{value}</option>)}</select></label>
        <label><span>Исполнитель</span><select value={assigneeFilter} onChange={(event) => setAssigneeFilter(event.target.value)}><option>Все исполнители</option>{assignees.map((value) => <option key={value}>{value}</option>)}</select></label>
      </div>
      {mode === "month" ? (
        <div className="month-grid" role="grid" aria-label={periodTitle}>
          {["Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс"].map((day) => <strong key={day}>{day}</strong>)}
          {monthCells.map((day, index) => day === null
            ? <span className="month-grid__blank" key={`blank-${index}`} aria-hidden="true" />
            : (() => {
              const date = new Date(cursorDate.getFullYear(), cursorDate.getMonth(), day);
              const dateKey = toDateKey(date);
              return <button key={day} type="button" className={`${dateKey === todayKey ? "is-today" : ""} ${dateKey === toDateKey(cursorDate) ? "is-selected-day" : ""}`} onClick={() => { setCursorDate(date); setMode("day"); onToast(`Открыт день ${formatDate(date)}`); }}><span>{day}</span>{datesWithEvents.has(dateKey) && <i aria-label="Есть события" />}</button>;
            })())}
        </div>
      ) : mode === "year" ? (
        <div className="year-grid" role="grid" aria-label={`Календарный год ${periodTitle}`}>
          {yearMonths.map(({ month, label, eventCount }) => (
            <button
              key={month}
              type="button"
              className={cursorDate.getMonth() === month ? "is-current-month" : ""}
              onClick={() => {
                setCursorDate(new Date(cursorDate.getFullYear(), month, 1));
                setMode("month");
              }}
              aria-label={`Открыть ${label.toLowerCase()} ${cursorDate.getFullYear()}`}
            >
              <strong>{label}</strong>
              <span>{eventCount ? `${eventCount} ${eventCount === 1 ? "событие" : "события"}` : "Нет событий"}</span>
            </button>
          ))}
        </div>
      ) : (
        <div className={`calendar-grid calendar-grid--${mode}`}>
          <div className="calendar-grid__days"><span>Время</span>{visibleDates.map((day) => <strong key={toDateKey(day)} className={toDateKey(day) === todayKey ? "is-today" : ""}>{formatDay(day)}</strong>)}</div>
          <div className="calendar-grid__body">
            {Array.from({ length: 11 }, (_, index) => 8 + index).map((hour) => <div className="calendar-hour" key={hour}><time>{String(hour).padStart(2, "0")}:00</time>{visibleDates.map((date) => <div key={toDateKey(date)} className="calendar-slot" onDragOver={(event) => event.preventDefault()} onDrop={() => { const moved = items.find((item) => item.id === draggedId); if (moved) { const previous = { ...moved }; onPushUndo("Перемещено событие", () => setItems((current) => current.map((item) => item.id === previous.id ? previous : item))); applySchedule({ ...moved, eventDate: toDateKey(date), start: hour * 60 }, "drag"); } setDraggedId(null); }}>{visibleItems.filter((item) => item.eventDate === toDateKey(date) && item.start >= hour * 60 && item.start < (hour + 1) * 60).map((item) => renderItem(item, mode === "week"))}<button type="button" className="calendar-slot__create" disabled={!isWritable} onClick={() => setSlot(hour * 60)} aria-label={`Создать задачу в ${hour}:00, ${formatDate(date)}`}><AddRegular aria-hidden="true" /></button></div>)}</div>)}
          </div>
        </div>
      )}
      <section className="calendar-keyboard" aria-label="Управление выбранным событием">
        <div><strong>{selected?.title || "Событие не выбрано"}</strong><span>{selected && `${minutesLabel(selected.start)}–${minutesLabel(selected.start + selected.duration)}`}</span></div>
        <div><button type="button" disabled={!isWritable} onClick={() => adjustSelected("move", -30)} aria-label="Переместить на 30 минут раньше">← 30 мин</button><button type="button" disabled={!isWritable} onClick={() => adjustSelected("move", 30)} aria-label="Переместить на 30 минут позже">30 мин →</button><button type="button" disabled={!isWritable} onClick={() => adjustSelected("resize", -15)} aria-label="Уменьшить длительность на 15 минут">− 15 мин</button><button type="button" disabled={!isWritable} onClick={() => adjustSelected("resize", 15)} aria-label="Увеличить длительность на 15 минут">+ 15 мин</button><button type="button" disabled={!isWritable} onClick={() => selected && setStaleRollback({ candidate: { ...selected, start: selected.start + 60 }, previous: selected })}>Проверить конфликт</button></div>
        <small>Клавиатура: Alt + ←/→ перемещает на 30 минут; Alt + ↑/↓ меняет длительность на 15 минут.</small>
      </section>
      {slot !== null && <div className="calendar-composer" role="dialog" aria-modal="false" aria-labelledby="slot-composer-title"><form onSubmit={createFromSlot}><strong id="slot-composer-title">Новое событие · {minutesLabel(slot)}</strong><input autoFocus value={newTitle} onChange={(event) => setNewTitle(event.target.value)} placeholder="Название события" aria-label="Название нового события" /><button className="button button--primary" type="submit" disabled={!isWritable || !newTitle.trim()}>Продолжить в редакторе</button><button className="button button--ghost" type="button" onClick={() => setSlot(null)}>Отмена</button></form></div>}
      {editor && <div className="calendar-editor-backdrop"><section className="calendar-editor" role="dialog" aria-modal="true" aria-labelledby="calendar-editor-title"><form onSubmit={saveEditor}><header><div><p className="eyebrow">CalendarEvent · Europe/Minsk</p><h3 id="calendar-editor-title">{editor.isNew ? "Новое событие" : "Редактор события"}</h3><span>{editor.isNew ? "Создание с idempotency key" : `Версия ${editor.version} · PATCH с If-Match`}</span></div><button type="button" className="icon-button icon-button--bordered" onClick={() => setEditor(null)} aria-label="Закрыть редактор"><DismissRegular aria-hidden="true" /></button></header>{editor.state && <div className="calendar-editor__state" role="alert"><strong>{editor.state === "validation" ? "VALIDATION_FAILED" : editor.state === "conflict" ? "VERSION_CONFLICT" : editor.state === "forbidden" ? "FORBIDDEN" : editor.state === "deleted" ? "OBJECT_DELETED" : "SESSION_REVOKED"}</strong><span>{editor.state === "validation" ? "Проверьте обязательные поля: черновик сохранён, ошибки привязаны к canonical field path." : editor.state === "conflict" ? "Серверная версия изменилась; If-Match не позволяет перезаписать данные." : editor.state === "forbidden" ? "Нет capability для команды; скрытые данные не раскрываются." : editor.state === "deleted" ? "Событие удалено или в корзине; команда не создаёт новый объект." : "Сеанс или устройство отозваны; неподтверждённое изменение не сохранено."}</span></div>}<div className="calendar-editor__grid"><label className="calendar-editor__wide"><span>Название *</span><input autoFocus required value={editor.title} onChange={(event) => updateEditor("title", event.target.value)} aria-invalid={editor.state === "validation" && !editor.title.trim()} /></label><label><span>Дата *</span><input type="date" required value={editor.eventDate} onChange={(event) => updateEditor("eventDate", event.target.value)} /></label><label><span>Часовой пояс</span><input value={editor.timeZone} readOnly aria-readonly="true" /></label><label><span>Начало *</span><select value={editor.start} disabled={editor.isAllDay} onChange={(event) => updateEditor("start", Number(event.target.value))}>{Array.from({ length: 22 }, (_, index) => 480 + index * 30).map((value) => <option key={value} value={value}>{minutesLabel(value)}</option>)}</select></label><label><span>Длительность *</span><select value={editor.duration} disabled={editor.isAllDay} onChange={(event) => updateEditor("duration", Number(event.target.value))}>{[15, 30, 45, 60, 90, 120].map((value) => <option key={value} value={value}>{value} мин</option>)}</select></label><label><span>Проект *</span><select value={editor.project} onChange={(event) => updateEditor("project", event.target.value)}>{projects.map((value) => <option key={value}>{value}</option>)}</select></label><label><span>Статус</span><select value={editor.status} onChange={(event) => updateEditor("status", event.target.value)}>{["Запланировано", "В работе", "Готово"].map((value) => <option key={value}>{value}</option>)}</select></label><label className="calendar-editor__wide calendar-editor__all-day"><input type="checkbox" checked={editor.isAllDay} onChange={(event) => updateEditor("isAllDay", event.target.checked)} /><span>Событие на весь день</span></label><label className="calendar-editor__wide"><span>Описание</span><textarea value={editor.description} onChange={(event) => updateEditor("description", event.target.value)} /></label><fieldset className="calendar-editor__wide"><legend>Участники</legend>{["Иван С.", "Мария С.", "Алексей В. · клиент"].map((person) => <label key={person} className="calendar-editor__check"><input type="checkbox" checked={[...editor.userAttendees, ...editor.contactAttendees].includes(person)} onChange={(event) => { const field = person.includes("клиент") ? "contactAttendees" : "userAttendees"; updateEditor(field, event.target.checked ? [...editor[field], person] : editor[field].filter((value) => value !== person)); }} />{person}</label>)}</fieldset></div><section className="calendar-editor__responses"><span>Мой ответ</span>{[["accepted", "Принять"], ["tentative", "Под вопросом"], ["declined", "Отклонить"]].map(([value, label]) => <button key={value} type="button" className={editor.response === value ? "is-active" : ""} aria-pressed={editor.response === value} onClick={() => respondToInvite(value)}>{label}</button>)}</section><footer><div className="calendar-editor__scenarios"><span>Ответ сервера:</span><button type="button" onClick={() => updateEditor("state", "validation")}>Валидация</button><button type="button" onClick={() => updateEditor("state", "conflict")}>Конфликт версии</button><button type="button" onClick={() => updateEditor("state", "forbidden")}>Нет доступа</button><button type="button" onClick={() => updateEditor("state", "deleted")}>Объект удалён</button><button type="button" onClick={() => updateEditor("state", "session")}>Сеанс отозван</button><button type="button" onClick={() => updateEditor("state", null)}>Очистить состояние</button></div><div className="dialog__actions"><button className="button button--ghost" type="button" onClick={() => setEditor(null)}>Отмена</button><button className="button button--primary" type="submit" disabled={!editor.title.trim() || !editor.eventDate || ["forbidden", "deleted", "session", "conflict"].includes(editor.state)}>Сохранить событие</button></div></footer></form></section></div>}
      {overlapDraft && <div className="calendar-alert" role="alert"><WarningRegular aria-hidden="true" /><div><strong>Обнаружено пересечение: {overlapDraft.collision.title}</strong><span>{minutesLabel(overlapDraft.candidate.start)}–{minutesLabel(overlapDraft.candidate.start + overlapDraft.candidate.duration)} пересекается с занятым временем. Подтвердите сохранение или вернитесь к расписанию.</span></div><button className="button button--secondary" type="button" onClick={() => setOverlapDraft(null)}>Отменить</button><button className="button button--primary" type="button" onClick={forceOverlap}>Сохранить всё равно</button></div>}
      {staleRollback && <div className="calendar-alert calendar-alert--stale" role="alert"><ArrowSyncRegular aria-hidden="true" /><div><strong>Расписание изменилось на сервере</strong><span>Перетаскивание не сохранено: версия события устарела. Локальное положение возвращено к {minutesLabel(staleRollback.previous.start)}–{minutesLabel(staleRollback.previous.start + staleRollback.previous.duration)}.</span></div><button className="button button--secondary" type="button" onClick={() => { setStaleRollback(null); onToast("Загружена актуальная версия расписания"); }}>Обновить</button><button className="button button--primary" type="button" onClick={() => { setStaleRollback(null); onToast("Черновик перемещения отменён; события не потеряны"); }}>Понятно</button></div>}
    </section>
  );
}

export function App() {
  const gateAccount = useMemo(() => getGateAccount(), []);
  const [authenticated, setAuthenticated] = useState(true);
  const [undoStack, setUndoStack] = useState([]);
  const [nowMinutes, setNowMinutes] = useState(() => {
    const d = new Date();
    return d.getHours() * 60 + d.getMinutes();
  });
  const [onboardingStep, setOnboardingStep] = useState(null);
  const [activeView, setActiveView] = useState("today");
  const [selectedTask, setSelectedTask] = useState(baseTasks[0]);
  const [taskStatus, setTaskStatus] = useState("В работе");
  const [checklist, setChecklist] = useState([
    { id: "crm", label: "Собрать данные из CRM", done: true },
    { id: "summary", label: "Сформировать сводную таблицу и диаграммы", done: false },
  ]);
  const [checklistDraft, setChecklistDraft] = useState("");
  const [completedOpen, setCompletedOpen] = useState(false);
  const [commentsOpen, setCommentsOpen] = useState(false);
  const [watchersOpen, setWatchersOpen] = useState(false);
  const [watchingTask, setWatchingTask] = useState(true);
  const [detailsOpen, setDetailsOpen] = useState(true);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [dateIndex, setDateIndex] = useState(1);
  const [unscheduled, setUnscheduled] = useState(initialUnscheduled);
  const [connectionIndex, setConnectionIndex] = useState(0);
  const [recoveryState, setRecoveryState] = useState("");
  const [toast, setToast] = useState("");
  const [searchOpen, setSearchOpen] = useState(false);
  const [searchRequest, setSearchRequest] = useState({ query: "", filter: "Все" });
  const [userOpen, setUserOpen] = useState(false);
  const [inboxItems, setInboxItems] = useState(initialInboxItems);
  const [conversionItem, setConversionItem] = useState(null);
  const [editorOpen, setEditorOpen] = useState(false);
  const [editorDraft, setEditorDraft] = useState(null);
  const [conflictDraft, setConflictDraft] = useState(null);
  const [diagnosticsOpen, setDiagnosticsOpen] = useState(false);
  const [notificationOpen, setNotificationOpen] = useState(false);
  const [notifications, setNotifications] = useState(initialNotifications);
  const [sessionRevoked, setSessionRevoked] = useState(false);
  const [recoveryAttempts, setRecoveryAttempts] = useState(0);
  const [fileLocationState, setFileLocationState] = useState("available");
  const [plannerWidth, setPlannerWidth] = useState(null);

  const currentTimeTop = useMemo(() => {
    const ROW = 69;
    const START = 8;
    return ((nowMinutes - START * 60) / 60) * ROW;
  }, [nowMinutes]);

  const connections = useMemo(() => [
    { title: "Подключено к серверу компании", subtitle: "Онлайн", tone: "online" },
    { title: "Нет подключения к серверу", subtitle: "Работа офлайн", tone: "offline" },
    { title: "Подключение восстановлено", subtitle: "Изменения синхронизированы", tone: "online" },
    { title: "Сервер на обслуживании", subtitle: "Только чтение · повторить позднее", tone: "offline" },
    { title: "Локальное хранилище заполнено", subtitle: "Кэш не может быть обновлён", tone: "offline" },
  ], []);
  const isDegraded = [1, 3, 4].includes(connectionIndex) || Boolean(recoveryState);
  const isOffline = isDegraded;
  const isWritable = !isDegraded && hasCapability(gateAccount, "Task.Write");
  const canReadAdmin = hasCapability(gateAccount, "Admin.Read");
  const canReadOperations = hasCapability(gateAccount, "Operations.Read");

  function windowAction(action) {
    globalThis.taskDesktop?.windowAction?.(action);
  }

  function pushUndo(label, rollback) {
    setUndoStack((previous) => {
      const next = [...previous, { label, rollback }];
      return next.length > 20 ? next.slice(-20) : next;
    });
  }

  function undo() {
    if (!undoStack.length) return;
    const action = undoStack[undoStack.length - 1];
    action.rollback();
    setUndoStack((previous) => previous.slice(0, -1));
    setToast(`Отменено: ${action.label}`);
  }

  useEffect(() => {
    function onKeyDown(event) {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "z" && !event.shiftKey) {
        const editing = document.activeElement?.tagName === "INPUT"
          || document.activeElement?.tagName === "TEXTAREA"
          || document.activeElement?.tagName === "SELECT"
          || document.activeElement?.isContentEditable;
        if (!editing) {
          event.preventDefault();
          undo();
          return;
        }
      }
      if (event.altKey && event.key.toLowerCase() === "n") {
        event.preventDefault();
        if (authenticated && isWritable) setDialogOpen(true);
        else if (authenticated) setToast("Создание отключено: сервер недоступен");
      }
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k" && authenticated) {
        event.preventDefault();
        setSearchOpen(true);
      }
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "f" && authenticated) {
        event.preventDefault();
        setSearchRequest({ query: "", filter: "Все" });
        setSearchOpen(false);
        setActiveView("search");
      }
      if (event.key === "Escape") {
        setDialogOpen(false);
        setSearchOpen(false);
        setUserOpen(false);
        setConversionItem(null);
        setEditorOpen(false);
        setDiagnosticsOpen(false);
        setNotificationOpen(false);
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [authenticated, isWritable, undo]);

  useEffect(() => {
    if (!toast) return undefined;
    const timer = window.setTimeout(() => setToast(""), 2400);
    return () => window.clearTimeout(timer);
  }, [toast]);

  useEffect(() => {
    const timer = window.setInterval(() => {
      const d = new Date();
      setNowMinutes(d.getHours() * 60 + d.getMinutes());
    }, 60_000);
    return () => window.clearInterval(timer);
  }, []);

  function selectTask(task) {
    setSelectedTask(task);
    setTaskStatus(task.status || "Запланировано");
    setDetailsOpen(true);
  }

  function showSection(label) {
    if (label === "Сегодня") {
      setActiveView("today");
      return;
    }
    if (label === "Входящие") {
      setActiveView("inbox");
      return;
    }
    if (label === "Мои задачи") {
      setActiveView("tasks");
      return;
    }
    if (label === "Календарь") {
      setActiveView("calendar");
      return;
    }
    if (label === "Проекты") {
      setActiveView("projects");
      return;
    }
    if (label === "Файлы") {
      setActiveView("files");
      return;
    }
    if (label === "CRM") {
      setActiveView("crm");
      return;
    }
    if (label === "Поиск") {
      setSearchRequest({ query: "", filter: "Все" });
      setActiveView("search");
      return;
    }
    if (label === "Архив и корзина") {
      setActiveView("lifecycle");
      return;
    }
    if (label === "Настройки") {
      setActiveView("settings");
      return;
    }
    if (label === "Администрирование") {
      setActiveView("admin");
      return;
    }
    if (label === "Операции") {
      setActiveView("operations");
      return;
    }
    setToast(`Раздел «${label}» вне текущего vertical slice`);
  }

  function openSearchResult(result) {
    if (result.type === "Задача") {
      setActiveView("today");
      selectTask(baseTasks.find((task) => task.title === result.title) || baseTasks[0]);
    } else if (result.type === "Проект") {
      setActiveView("projects");
      setToast(`Проект «${result.title}» открыт`);
    } else if (result.type === "Файл") {
      setActiveView("files");
      setToast(`Файл «${result.title}» открыт в каталоге`);
    } else if (result.group === "CRM") {
      setActiveView("crm");
      setToast(`Контакт «${result.title}» открыт`);
    } else {
      setToast(`${result.type} «${result.title}» открыт`);
    }
    setSearchOpen(false);
  }

  function createTask(values) {
    if (!isWritable) {
      setToast("Создание отключено: сервер недоступен");
      return;
    }
    const tone = values.priority === "Высокая" ? "high" : values.priority === "Низкая" ? "low" : "medium";
    const newTask = {
      id: `task-${Date.now()}`,
      title: values.title,
      project: values.project || "Без проекта",
      priority: values.priority,
      priorityTone: tone,
      due: values.due,
      status: "Запланировано",
    };
    setUnscheduled((items) => [newTask, ...items]);
    pushUndo("Создана задача", () => setUnscheduled((items) => items.filter((item) => item.id !== newTask.id)));
    selectTask(newTask);
    setDialogOpen(false);
    setToast(values.due === "Нет срока" ? "Задача создана без срока" : `Задача создана: ${values.due}`);
  }

  function resizePlanner(event) {
    const grid = event.currentTarget.parentElement;
    if (!grid) return;
    const bounds = grid.getBoundingClientRect();
    const minPanelWidth = 360;
    const nextWidth = Math.round(Math.min(Math.max(event.clientX - bounds.left, minPanelWidth), bounds.width - minPanelWidth));
    setPlannerWidth(nextWidth);
  }

  function handlePlannerResizerKeyDown(event) {
    if (!event.key.startsWith("Arrow")) return;
    event.preventDefault();
    const amount = event.shiftKey ? 48 : 24;
    const direction = event.key === "ArrowLeft" ? -1 : event.key === "ArrowRight" ? 1 : 0;
    if (!direction) return;
    const grid = event.currentTarget.parentElement;
    const width = grid?.getBoundingClientRect().width || 0;
    const current = plannerWidth ?? Math.round(width * 0.476);
    setPlannerWidth(Math.min(Math.max(current + direction * amount, 360), width - 360));
  }

  function convertInboxItem(values) {
    const tone = values.priority === "Высокая" ? "high" : values.priority === "Низкая" ? "low" : "medium";
    const newTask = {
      id: `converted-${Date.now()}`,
      title: values.title,
      project: values.project,
      priority: values.priority,
      priorityTone: tone,
      due: values.due,
      status: "Запланировано",
    };
    setUnscheduled((items) => [newTask, ...items]);
    setInboxItems((items) => items.map((item) => item.id === values.id ? { ...item, status: "Преобразовано" } : item));
    setConversionItem(null);
    setSelectedTask(newTask);
    setActiveView("today");
    setToast("Задача создана, исходная запись закрыта");
  }

  function saveTaskDraft(draft) {
    const previousTask = selectedTask;
    const previousEditorDraft = editorDraft;
    const previousConflictDraft = conflictDraft;
    setSelectedTask(draft);
    setEditorOpen(false);
    setEditorDraft(draft);
    setConflictDraft(draft);
    pushUndo("Изменена задача", () => {
      setSelectedTask(previousTask);
      setEditorDraft(previousEditorDraft);
      setConflictDraft(previousConflictDraft);
    });
  }

  function returnToConflictDraft() {
    setConflictDraft(null);
    setEditorOpen(true);
    setToast("Локальный черновик открыт без потери изменений");
  }

  function resolveConflict(action) {
    if (action === "reapply") {
      setSelectedTask((task) => ({ ...task, ...conflictDraft }));
      setToast("Изменения повторно применены к актуальной версии");
    }
    if (action === "reload") setToast("Загружена актуальная версия сервера");
    if (action === "discard") setToast("Локальный черновик отменён");
    setEditorDraft(null);
    setConflictDraft(null);
  }

  const selectedPriority = selectedTask.priorityTone || "medium";
  const connection = recoveryState === "reconnecting"
    ? { title: "Восстановление подключения", subtitle: "Проверка сессии и сервера", tone: "offline" }
    : recoveryState === "scope"
      ? { title: "Проверка области доступа", subtitle: "Запись пока отключена", tone: "offline" }
      : recoveryState === "failed"
        ? { title: "Восстановление не удалось", subtitle: "Повторные попытки остановлены", tone: "offline" }
      : connections[connectionIndex];
  const viewMeta = activeView === "today"
    ? ["Сегодня", dateLabels[dateIndex]]
    : activeView === "inbox"
      ? ["Входящие", `${inboxItems.filter((item) => item.status !== "Преобразовано").length} необработанных записей`]
      : activeView === "tasks"
        ? ["Мои задачи", "Список, фильтры и безопасные действия"]
        : activeView === "calendar"
          ? ["Календарь", "День, неделя, месяц и планирование"]
          : activeView === "projects"
            ? ["Проекты", "Команда, жизненный цикл и связанные объекты"]
            : activeView === "files"
              ? ["Файлы", "Виртуальный каталог и диагностика расположений"]
              : activeView === "search"
                ? ["Поиск", "Полные permission-safe результаты и offline cache-only"]
                : activeView === "lifecycle"
                  ? ["Архив и корзина", "Read-only история, безопасное восстановление и отдельный purge"]
                  : activeView === "settings"
                    ? ["Настройки", "Личные, локальные и server-managed параметры"]
                    : activeView === "admin"
                      ? ["Администрирование", "Пользователи, структура, роли, сессии и ресурсы"]
                      : activeView === "operations"
                        ? ["Операции", "Состояние системы, jobs, backups, audit и настройки организации"]
                        : ["CRM", "Контакты, компании и ручная хронология"];
  const unreadCount = notifications.filter((item) => item.unread).length;

  if (!authenticated) {
    return (
      <div className="desktop-stage">
        <div className="window" data-testid="task-window">
          <header className="titlebar">
            <div className="titlebar__brand"><span className="app-mark"><TaskListSquareLtrFilled aria-hidden="true" /></span><span>Task</span></div>
            <div className="window-controls" aria-label="Управление окном">
              <button type="button" aria-label="Свернуть" onClick={() => windowAction("minimize")}><SubtractRegular aria-hidden="true" /></button>
              <button type="button" aria-label="Развернуть" onClick={() => windowAction("toggleMaximize")}><SquareRegular aria-hidden="true" /></button>
              <button type="button" aria-label="Закрыть" onClick={() => windowAction("close")}><DismissRegular aria-hidden="true" /></button>
            </div>
          </header>
          <AuthSurface account={gateAccount} onAuthenticated={() => { setAuthenticated(true); setOnboardingStep(0); setConnectionIndex(0); setRecoveryState(""); setToast("Вход выполнен, данные синхронизированы"); }} />
          {toast && <div className="toast" role="status">{toast}</div>}
        </div>
      </div>
    );
  }

  return (
    <div className="desktop-stage">
      <div className="window" data-testid="task-window">
        <header className="titlebar">
          <div className="titlebar__brand">
            <span className="app-mark"><TaskListSquareLtrFilled aria-hidden="true" /></span>
            <span>Task</span>
          </div>
          <div className="window-controls" aria-label="Управление окном">
            <button type="button" aria-label="Свернуть" onClick={() => windowAction("minimize")}><SubtractRegular aria-hidden="true" /></button>
            <button type="button" aria-label="Развернуть" onClick={() => windowAction("toggleMaximize")}><SquareRegular aria-hidden="true" /></button>
            <button type="button" aria-label="Закрыть" onClick={() => windowAction("close")}><DismissRegular aria-hidden="true" /></button>
          </div>
        </header>

        <div className="app-shell">
          <aside className="sidebar">
            <button className="sidebar__menu" type="button" aria-label="Свернуть меню">
              <NavigationRegular aria-hidden="true" />
            </button>
            <nav aria-label="Основная навигация">
              <NavItem icon={CalendarRegular} label="Сегодня" active={activeView === "today"} onClick={() => showSection("Сегодня")} />
              <NavItem icon={CalendarRegular} label="Календарь" active={activeView === "calendar"} onClick={() => showSection("Календарь")} />
              <NavItem icon={MailInboxRegular} label="Входящие" active={activeView === "inbox"} onClick={() => showSection("Входящие")} />
              <NavItem icon={ClipboardTaskListLtrRegular} label="Мои задачи" active={activeView === "tasks"} onClick={() => showSection("Мои задачи")} />
              <NavItem icon={FolderRegular} label="Проекты" active={activeView === "projects"} onClick={() => showSection("Проекты")} />
              <NavItem icon={DocumentRegular} label="Файлы" active={activeView === "files"} onClick={() => showSection("Файлы")} />
              <NavItem icon={PersonRegular} label="CRM" active={activeView === "crm"} onClick={() => showSection("CRM")} />
              <NavItem icon={SearchRegular} label="Поиск" active={activeView === "search"} onClick={() => showSection("Поиск")} />
              <NavItem icon={ArchiveRegular} label="Архив и корзина" active={activeView === "lifecycle"} onClick={() => showSection("Архив и корзина")} />
              {canReadAdmin && <NavItem icon={ShieldErrorRegular} label="Администрирование" active={activeView === "admin"} onClick={() => showSection("Администрирование")} />}
              {canReadOperations && <NavItem icon={DatabaseRegular} label="Операции" active={activeView === "operations"} onClick={() => showSection("Операции")} />}
              <NavItem icon={AddRegular} label="Создать задачу" onClick={() => isWritable ? setDialogOpen(true) : setToast("Создание отключено: сервер недоступен")} />
            </nav>
            <div className="sidebar__bottom">
              <NavItem icon={SettingsRegular} label="Настройки" active={activeView === "settings"} onClick={() => showSection("Настройки")} />
              <NavItem icon={QuestionRegular} label="Справка" onClick={() => showSection("Справка")} />
              <div className="sidebar__version">Версия 1.4.2<br />© Компания</div>
            </div>
          </aside>

          <main className="workspace">
            <header className="workspace-header">
              <div className="page-title">
                <h1>{viewMeta[0]}</h1>
                <span>{viewMeta[1]}</span>
              </div>
              <button className="search-trigger" type="button" onClick={() => setSearchOpen(true)} aria-label="Открыть глобальный поиск">
                <SearchRegular aria-hidden="true" />
                <span>Поиск по Task</span>
                <kbd>Ctrl+K</kbd>
              </button>
              <button className="button button--primary new-task" type="button" aria-label="Новая задача" disabled={!isWritable} onClick={() => isWritable ? setDialogOpen(true) : setToast("Создание отключено: сервер недоступен")}>
                <AddRegular aria-hidden="true" />
                <span>Новая задача</span>
                <kbd>Alt+N</kbd>
              </button>
              <button className="notification-trigger" type="button" onClick={() => { setNotificationOpen((open) => !open); setUserOpen(false); }} aria-expanded={notificationOpen} aria-label={`Уведомления: ${unreadCount} непрочитанных`}>
                <AlertRegular aria-hidden="true" />
                {unreadCount > 0 && <span>{unreadCount}</span>}
              </button>
              {notificationOpen && (
                <NotificationCenter
                  notifications={notifications}
                  setNotifications={setNotifications}
                  onClose={() => setNotificationOpen(false)}
                  onToast={setToast}
                  onOpen={(notification) => { setNotificationOpen(false); setActiveView(notification.id === "notice-3" ? "projects" : "today"); setToast(`Открыто: ${notification.meta}`); }}
                />
              )}
              <button
                className={`connection connection--${connection.tone}`}
                type="button"
                onClick={() => { setRecoveryState(""); setConnectionIndex((index) => (index + 1) % connections.length); }}
                aria-label="Переключить демонстрационное состояние подключения"
              >
                <span className="connection__dot" />
                <span>
                  <strong title={connection.title}>{connection.title}</strong>
                  <small>{connection.subtitle}</small>
                </span>
              </button>
              <button className="user-menu" type="button" onClick={() => setUserOpen((open) => !open)} aria-expanded={userOpen} aria-label={`Профиль ${gateAccount.displayName}, роль ${gateAccount.roleLabel}`}>
                <span className="avatar">{gateAccount.initials}</span>
                <span>{gateAccount.shortName}</span>
                <ChevronDownRegular aria-hidden="true" />
              </button>
              {userOpen && (
                <div className="user-popover">
                  <strong>{gateAccount.displayName}</strong>
                  <span>{gateAccount.login} · {gateAccount.roleLabel}</span>
                  <button type="button" onClick={() => { setSessionRevoked(true); setUserOpen(false); }}>
                    <LockClosedRegular aria-hidden="true" /> Прервать сессию (демо)
                  </button>
                  <button type="button" onClick={() => { setAuthenticated(false); setRecoveryState(""); setUserOpen(false); }}>
                    <SignOutRegular aria-hidden="true" /> Выйти
                  </button>
                </div>
              )}
            </header>

            {isDegraded && (
              <section className={`connection-banner ${recoveryState ? "connection-banner--progress" : ""}`} role="status">
                {recoveryState === "reconnecting"
                  ? <ArrowSyncRegular className="is-spinning" aria-hidden="true" />
                  : recoveryState === "scope"
                    ? <LockClosedRegular aria-hidden="true" />
                    : recoveryState === "failed"
                      ? <WarningRegular aria-hidden="true" />
                    : connectionIndex === 3
                      ? <ServerRegular aria-hidden="true" />
                      : connectionIndex === 4
                        ? <DatabaseRegular aria-hidden="true" />
                        : <PlugDisconnectedRegular aria-hidden="true" />}
                <span>
                  {recoveryState === "reconnecting" && <><strong>Восстанавливаем подключение.</strong> Проверяем сессию, доступность сервера и курсор синхронизации. Запись останется отключённой до подтверждения актуальных данных.</>}
                  {recoveryState === "scope" && <><strong>Область доступа изменилась.</strong> Task удалит недоступные объекты из локального кэша и загрузит только разрешённые данные до возврата режима записи.</>}
                  {recoveryState === "failed" && <><strong>Повторное восстановление не удалось.</strong> После двух попыток Task остановил цикл и сохранил безопасный режим только чтения. Можно открыть диагностику или начать новую проверку вручную.</>}
                  {!recoveryState && connectionIndex === 1 && <><strong>Сервер компании недоступен.</strong> Показан разрешённый кэш от 10:23; создание и изменения временно отключены.</>}
                  {!recoveryState && connectionIndex === 3 && <><strong>Сервер временно на обслуживании.</strong> Разрешённый кэш доступен только для чтения. Рекомендуемая повторная попытка — через 15 минут.</>}
                  {!recoveryState && connectionIndex === 4 && <><strong>Локальное хранилище заполнено.</strong> Текущий кэш доступен только для чтения, но обновить или безопасно записать изменения сейчас нельзя. Освободите не менее 620 МБ.</>}
                </span>
                <button type="button" onClick={() => setDiagnosticsOpen(true)}>Диагностика</button>
                {recoveryState === "reconnecting" && <button type="button" onClick={() => { setRecoveryState(""); setToast("Попытка восстановления прервана; режим только чтение сохранён"); }}>Прервать</button>}
                {recoveryState === "reconnecting" && <button type="button" onClick={() => setRecoveryState("scope")}>Проверить область</button>}
                {recoveryState === "scope" && <button type="button" onClick={() => { setRecoveryState(""); setConnectionIndex(2); setToast("Разрешённые данные обновлены, изменения снова доступны"); }}>Обновить данные</button>}
                {recoveryState === "failed" && <button type="button" onClick={() => { setRecoveryAttempts(0); setRecoveryState("reconnecting"); }}>Начать новую проверку</button>}
                {!recoveryState && <button type="button" onClick={() => { setRecoveryAttempts((value) => value + 1); setRecoveryState("reconnecting"); }}>{connectionIndex === 4 ? "Проверить место" : "Повторить"}</button>}
              </section>
            )}

            {activeView === "today" ? <div className="content-grid" style={plannerWidth ? { "--planner-width": `${plannerWidth}px` } : undefined}>
              <section className="planner" aria-label="План дня">
                <div className="date-toolbar">
                  <button className="icon-button icon-button--bordered" type="button" onClick={() => setDateIndex((index) => Math.max(0, index - 1))} aria-label="Предыдущий день">
                    <ChevronLeftRegular aria-hidden="true" />
                  </button>
                  <button className="icon-button icon-button--bordered" type="button" onClick={() => setDateIndex(1)} aria-label="Открыть календарь">
                    <CalendarRegular aria-hidden="true" />
                  </button>
                  <button className="icon-button icon-button--bordered" type="button" onClick={() => setDateIndex((index) => Math.min(2, index + 1))} aria-label="Следующий день">
                    <ChevronRightRegular aria-hidden="true" />
                  </button>
                  <button className="button button--secondary button--today" type="button" onClick={() => setDateIndex(1)}>Сегодня</button>
                </div>

                <div className="all-day">
                  <span className="all-day__label">Весь день</span>
                  <button type="button" className="all-day__task" onClick={() => setToast("Открыта задача «Сформировать отчёт по проекту «Альфа»»")}>
                    <FlagRegular aria-hidden="true" />
                    <span>Сформировать отчёт по проекту «Альфа»</span>
                    <small>Крайний срок</small>
                  </button>
                </div>

                <div className="timeline">
                  <div className="hour-row hour-row--8"><time>08:00</time></div>
                  <div className="hour-row hour-row--9"><time>09:00</time></div>
                  <div className="hour-row hour-row--10"><time>10:00</time></div>
                  <div className="hour-row hour-row--11"><time>11:00</time></div>
                  <div className="hour-row hour-row--12"><time>12:00</time></div>
                  <div className="hour-row hour-row--13"><time>13:00</time></div>
                  <div className="hour-row hour-row--14"><time>14:00</time></div>
                  <div className="hour-row hour-row--15"><time>15:00</time></div>
                  <div className="hour-row hour-row--16"><time>16:00</time></div>
                  <div className="hour-row hour-row--17"><time>17:00</time></div>
                  <div className="hour-row hour-row--18"><time>18:00</time></div>

                  <div className="timeline-event event--planning">
                    <TimelineCard task={baseTasks[1]} selected={selectedTask.id === "planning"} onSelect={selectTask} />
                  </div>
                  <div className="timeline-event event--analysis">
                    <TimelineCard task={baseTasks[0]} selected={selectedTask.id === "analysis"} onSelect={selectTask} />
                  </div>
                  <div className="timeline-event event--presentation">
                    <TimelineCard task={baseTasks[2]} selected={selectedTask.id === "presentation"} onSelect={selectTask} />
                  </div>
                  <div className="timeline-event event--lunch">
                    <button type="button" onClick={() => setToast("Обед: 12:00 – 12:45")}>
                      <DrinkCoffeeRegular aria-hidden="true" />
                      <span><small>12:00 – 12:45</small><strong>Обед</strong></span>
                    </button>
                  </div>
                  <div className="timeline-event event--meeting">
                    <TimelineCard task={baseTasks[3]} selected={selectedTask.id === "meeting"} onSelect={selectTask} />
                  </div>
                  <div className="timeline-event event--contract">
                    <TimelineCard task={baseTasks[4]} selected={selectedTask.id === "contract"} onSelect={selectTask} />
                  </div>
                  <div className="timeline-event event--mail">
                    <TimelineCard task={baseTasks[5]} selected={selectedTask.id === "mail"} onSelect={selectTask} />
                  </div>
                  <div className="timeline-event event--tomorrow">
                    <TimelineCard task={baseTasks[6]} selected={selectedTask.id === "tomorrow"} onSelect={selectTask} />
                  </div>
                  {nowMinutes >= 480 && nowMinutes <= 1080 && (
                    <div className="current-time" style={{ top: `${currentTimeTop}px` }} aria-label={`Текущее время ${minutesLabel(nowMinutes)}`}>
                      <span>{minutesLabel(nowMinutes)}</span>
                      <i />
                    </div>
                  )}
                </div>
              </section>

              <div
                className="planner-resizer"
                role="separator"
                aria-label="Изменить ширину плана дня"
                aria-orientation="vertical"
                aria-valuemin={360}
                aria-valuetext={plannerWidth ? `${plannerWidth} пикселей` : "Стандартная ширина"}
                tabIndex={0}
                onPointerDown={(event) => {
                  event.currentTarget.setPointerCapture(event.pointerId);
                  resizePlanner(event);
                }}
                onPointerMove={(event) => {
                  if (event.currentTarget.hasPointerCapture(event.pointerId)) resizePlanner(event);
                }}
                onPointerUp={(event) => event.currentTarget.releasePointerCapture(event.pointerId)}
                onKeyDown={handlePlannerResizerKeyDown}
                title="Перетащите, чтобы изменить ширину панелей"
              />

              <aside className="right-panel">
                <section className="unscheduled">
                  <button className="section-heading" type="button" onClick={() => setCompletedOpen((open) => open)} aria-expanded="true">
                    <span>Несрочные и просроченные ({unscheduled.length})</span>
                    <ChevronUpRegular aria-hidden="true" />
                  </button>
                  <div className="unscheduled-list">
                    {unscheduled.map((task) => (
                      <button className={`unscheduled-row ${selectedTask.id === task.id ? "is-selected" : ""}`} type="button" key={task.id} onClick={() => selectTask(task)}>
                        <PriorityIcon tone={task.priorityTone} />
                        <span className="unscheduled-row__body">
                          <strong>{task.title}</strong>
                          <small>Проект: {task.project}</small>
                        </span>
                        <span className="unscheduled-row__meta">
                          <Priority tone={task.priorityTone} label={task.priority} />
                          <small className={task.due?.includes("Просрочено") ? "overdue" : ""}>{task.due}</small>
                        </span>
                      </button>
                    ))}
                  </div>
                  <button className="completed-toggle" type="button" onClick={() => setCompletedOpen((open) => !open)} aria-expanded={completedOpen}>
                    <span>{completedOpen ? "Скрыть завершённые (3)" : "Показать завершённые (3)"}</span>
                    {completedOpen ? <ChevronUpRegular aria-hidden="true" /> : <ChevronDownRegular aria-hidden="true" />}
                  </button>
                  {completedOpen && (
                    <div className="completed-list">
                      <span><CheckmarkCircleRegular aria-hidden="true" /> Отправить еженедельный отчёт</span>
                      <span><CheckmarkCircleRegular aria-hidden="true" /> Проверить план команды</span>
                      <span><CheckmarkCircleRegular aria-hidden="true" /> Ответить бухгалтерии</span>
                    </div>
                  )}
                </section>

                <section className="details">
                  <button className="section-heading" type="button" onClick={() => setDetailsOpen((open) => !open)} aria-expanded={detailsOpen}>
                    <span>Детали задачи</span>
                    {detailsOpen ? <ChevronUpRegular aria-hidden="true" /> : <ChevronDownRegular aria-hidden="true" />}
                  </button>
                  {detailsOpen && (
                    <div className="details__content">
                      <div className="details-title-row">
                        <h2>{selectedTask.title}</h2>
                        <button className="icon-button icon-button--bordered" type="button" disabled={!isWritable} onClick={() => {
                          if (isWritable) {
                            setEditorDraft(null);
                            setEditorOpen(true);
                          } else {
                            setToast("Редактирование отключено: сервер недоступен");
                          }
                        }} aria-label="Редактировать задачу">
                          <EditRegular aria-hidden="true" />
                        </button>
                      </div>
                      <div className="details__topline">
                        <label className="status-control">
                          <PlayCircleRegular aria-hidden="true" />
                          <select value={taskStatus} disabled={!isWritable} onChange={(event) => { const previousStatus = taskStatus; setTaskStatus(event.target.value); pushUndo("Изменён статус", () => setTaskStatus(previousStatus)); }} aria-label="Статус задачи">
                            <option>Запланировано</option>
                            <option>В работе</option>
                            <option>Готово</option>
                          </select>
                        </label>
                        <span>Приоритет: <Priority tone={selectedPriority} label={selectedTask.priority || "Средняя"} /></span>
                        <span className="due"><CalendarRegular aria-hidden="true" /> Срок: {selectedTask.due || "Сегодня"}</span>
                      </div>
                      <dl className="task-meta">
                        <dt className="meta-project">Проект:</dt><dd className="link meta-project-value">{selectedTask.project || "Отчётность"}</dd>
                        <dt className="meta-executor">Исполнитель:</dt><dd className="meta-executor-value">Иван С.</dd>
                        <dt className="meta-created">Создана:</dt><dd className="meta-created-value">24.07.2026 11:32</dd>
                        <dt className="meta-author">Автор:</dt><dd className="meta-author-value">Анна К.</dd>
                        <dt>Описание:</dt><dd className="description">{selectedTask.description || "Подготовить сводный анализ продаж по всем регионам за июнь 2026. Сравнить с маем и планом."}</dd>
                        <dt>Чек-лист:</dt>
                        <dd className="checklist">
                          <div className="checklist__summary"><span>{checklist.filter((item) => item.done).length} из {checklist.length} выполнено</span><i><b style={{ width: `${checklist.length ? (checklist.filter((item) => item.done).length / checklist.length) * 100 : 0}%` }} /></i></div>
                          {checklist.map((item) => (
                            <div className="checklist__row" key={item.id}>
                              <label><input type="checkbox" disabled={!isWritable} checked={item.done} onChange={() => setChecklist((items) => items.map((current) => current.id === item.id ? { ...current, done: !current.done } : current))} /><span>{item.label}</span></label>
                              <button type="button" className="checklist__remove" disabled={!isWritable} onClick={() => setChecklist((items) => items.filter((current) => current.id !== item.id))} aria-label={`Удалить пункт «${item.label}»`}><DismissRegular aria-hidden="true" /></button>
                            </div>
                          ))}
                          <form className="checklist__add" onSubmit={(event) => { event.preventDefault(); if (!checklistDraft.trim() || !isWritable) return; setChecklist((items) => [...items, { id: `check-${Date.now()}`, label: checklistDraft.trim(), done: false }]); setChecklistDraft(""); }}>
                            <input disabled={!isWritable} value={checklistDraft} onChange={(event) => setChecklistDraft(event.target.value)} placeholder="Добавить пункт" aria-label="Новый пункт чек-листа" />
                            <button type="submit" className="button button--secondary" disabled={!isWritable || !checklistDraft.trim()}><AddRegular aria-hidden="true" />Добавить</button>
                          </form>
                          <small className="checklist__hint">Один уровень пунктов; вложенные чек-листы не поддерживаются.</small>
                        </dd>
                        <dt>Файлы:</dt>
                        <dd className={`file-location-view file-location-view--${fileLocationState}`} aria-live="polite">
                          <div className="file-location-view__summary">
                            {fileLocationState === "available"
                              ? <DocumentRegular aria-hidden="true" />
                              : <WarningRegular aria-hidden="true" />}
                            <span>
                              <strong>Шаблон_анализа.xlsx</strong>
                              <small>\\fileserver\departments\sales\reports · 118 КБ</small>
                            </span>
                            <span className="file-location-view__status">
                              {fileLocationState === "available" ? "Расположение подтверждено" : "Сетевой путь недоступен"}
                            </span>
                          </div>
                          <div className="file-location-view__actions">
                            <button
                              type="button"
                              disabled={fileLocationState !== "available"}
                              onClick={() => setToast("Открытие файла недоступно в прототипе")}
                            >
                              Открыть файл
                            </button>
                            <button
                              type="button"
                              onClick={() => setFileLocationState((state) => state === "available" ? "unavailable" : "available")}
                            >
                              {fileLocationState === "available" ? "Проверить ошибку пути" : "Проверить снова"}
                            </button>
                          </div>
                          {fileLocationState === "unavailable" && (
                            <p>Файл не удалён. Task сохранил исходный путь и не предлагает локальную копию без подтверждения сервера.</p>
                          )}
                        </dd>
                      </dl>
                      <button className="comments-toggle" type="button" onClick={() => setCommentsOpen((open) => !open)} aria-expanded={commentsOpen}>
                        <span><CommentRegular aria-hidden="true" />Комментарии (2)</span>
                        <ChevronRightRegular className={commentsOpen ? "is-rotated" : ""} aria-hidden="true" />
                      </button>
                      {commentsOpen && (
                        <div className="comments">
                          <p><strong>Анна К.</strong> Добавила исходные данные по северному региону.</p>
                          <p><strong>Иван С.</strong> Проверяю расхождения с планом.</p>
                        </div>
                      )}
                      <button className="comments-toggle watchers-toggle" type="button" onClick={() => setWatchersOpen((open) => !open)} aria-expanded={watchersOpen}>
                        <span><PersonRegular aria-hidden="true" />Наблюдатели ({watchingTask ? 3 : 2})</span>
                        <ChevronRightRegular className={watchersOpen ? "is-rotated" : ""} aria-hidden="true" />
                      </button>
                      {watchersOpen && (
                        <div className="watchers" aria-label="Наблюдатели задачи">
                          <div className="watchers__actions">
                            <span>
                              <strong>{watchingTask ? "Вы получаете обновления" : "Вы не наблюдаете за задачей"}</strong>
                              <small>Изменение подписки проверяется разрешением Task.Watch.</small>
                            </span>
                            <button
                              type="button"
                              className="button button--secondary"
                              disabled={!isWritable}
                              onClick={() => {
                                setWatchingTask((value) => !value);
                                setToast(watchingTask ? "Вы больше не наблюдаете за задачей" : "Вы наблюдаете за задачей");
                              }}
                            >
                              {watchingTask ? "Не наблюдать" : "Наблюдать"}
                            </button>
                            <button type="button" className="button button--quiet" onClick={() => setToast("Список наблюдателей обновлён с сервера")}>
                              Обновить
                            </button>
                          </div>
                          <div className="watcher-row"><PersonRegular aria-hidden="true" /><span><strong>Анна К.</strong><small>Владелец задачи</small></span><small>С начала работы</small></div>
                          <div className="watcher-row"><PersonRegular aria-hidden="true" /><span><strong>Иван С.</strong><small>Участник проекта</small></span><small>Сегодня, 09:42</small></div>
                          {watchingTask && <div className="watcher-row"><PersonRegular aria-hidden="true" /><span><strong>Мария Л.</strong><small>Вы</small></span><small>Сегодня, 10:15</small></div>}
                          {!isWritable && <p className="watchers__readonly">Список доступен для чтения. Изменить подписку можно после восстановления соединения и повторной проверки Task.Watch.</p>}
                        </div>
                      )}
                    </div>
                  )}
                </section>
              </aside>
            </div> : activeView === "inbox" ? (
              <InboxSurface
                items={inboxItems}
                setItems={setInboxItems}
                isWritable={isWritable}
                onConvert={setConversionItem}
                onToast={setToast}
              />
            ) : activeView === "tasks" ? (
              <TasksSurface
                isWritable={isWritable}
                onToast={setToast}
                onPushUndo={pushUndo}
                onOpenTask={(task) => {
                  selectTask(task);
                  setActiveView("today");
                  setToast(`Открыта задача «${task.title}»`);
                }}
              />
            ) : activeView === "calendar" ? (
              <CalendarSurface
                isWritable={isWritable}
                onToast={setToast}
                onSelect={(task) => selectTask(task)}
                onPushUndo={pushUndo}
              />
            ) : activeView === "projects" ? (
              <ProjectsSurface isWritable={isWritable} onToast={setToast} />
            ) : activeView === "files" ? (
              <FilesSurface isWritable={isWritable} onToast={setToast} />
            ) : activeView === "search" ? (
              <SearchSurface
                offline={isOffline}
                initialQuery={searchRequest.query}
                initialFilter={searchRequest.filter}
                onOpenResult={openSearchResult}
                onToast={setToast}
              />
            ) : activeView === "lifecycle" ? (
              <LifecycleSurface offline={isOffline} onToast={setToast} />
            ) : activeView === "settings" ? (
              <SettingsSurface
                offline={isOffline}
                onToast={setToast}
                account={gateAccount}
                onForceSignIn={() => {
                  setAuthenticated(false);
                  setRecoveryState("");
                  setConnectionIndex(0);
                  setToast("Сессия завершена; выполните вход снова");
                }}
              />
            ) : activeView === "admin" ? (
              <AdminSurface offline={isOffline} onToast={setToast} />
            ) : activeView === "operations" ? (
              <OperationsSurface offline={isOffline} onToast={setToast} />
            ) : (
              <CrmSurface isWritable={isWritable} onToast={setToast} />
            )}

            <footer className="statusbar">
              <span><LockClosedRegular aria-hidden="true" /> {
                recoveryState === "reconnecting"
                  ? "Восстановление · проверка сессии и сервера · запись отключена"
                  : recoveryState === "scope"
                    ? "Область доступа изменилась · обновление разрешённого кэша"
                    : recoveryState === "failed"
                      ? "Восстановление остановлено после двух попыток · только чтение"
                    : connectionIndex === 3
                      ? "Обслуживание сервера · разрешённый кэш · только чтение"
                      : connectionIndex === 4
                        ? "Хранилище заполнено · кэш не обновляется · только чтение"
                        : isOffline
                          ? "Разрешённый кэш · только чтение · данные от 10:23"
                          : "Данные предоставляются сервером компании · онлайн"
              }</span>
              <button type="button" onClick={() => isOffline ? setDiagnosticsOpen(true) : setConnectionIndex((index) => (index + 1) % connections.length)}>
                {isOffline ? "Открыть диагностику" : "Последнее обновление: сегодня 10:23"} <ArrowSyncRegular aria-hidden="true" />
              </button>
            </footer>
          </main>
        </div>
        {dialogOpen && <NewTaskDialog onClose={() => setDialogOpen(false)} onCreate={createTask} />}
        {searchOpen && (
          <SearchOverlay
            offline={isOffline}
            onClose={() => setSearchOpen(false)}
            onToast={setToast}
            onOpenResult={openSearchResult}
            onShowAll={(request) => {
              setSearchRequest(request);
              setActiveView("search");
              setSearchOpen(false);
            }}
          />
        )}
        {conversionItem && (
          <ConversionDrawer
            item={conversionItem}
            isWritable={isWritable}
            onClose={() => setConversionItem(null)}
            onConvert={convertInboxItem}
          />
        )}
        {editorOpen && (
          <TaskEditorDialog
            task={editorDraft || selectedTask}
            isWritable={isWritable}
            onClose={() => {
              setEditorOpen(false);
              setEditorDraft(null);
            }}
            onSave={saveTaskDraft}
          />
        )}
        {conflictDraft && <ConflictDialog draft={conflictDraft} onClose={returnToConflictDraft} onResolve={resolveConflict} />}
        {diagnosticsOpen && (
          <DiagnosticsDialog
            mode={recoveryState || (connectionIndex === 3 ? "maintenance" : connectionIndex === 4 ? "storage" : "offline")}
            onClose={() => setDiagnosticsOpen(false)}
            onRetry={() => {
              setDiagnosticsOpen(false);
              const nextAttempt = recoveryAttempts + 1;
              setRecoveryAttempts(nextAttempt);
              setRecoveryState(nextAttempt >= 2 ? "failed" : "reconnecting");
              setToast(nextAttempt >= 2 ? "Повторная проверка не удалась; цикл остановлен безопасно" : "Начата проверка подключения; запись остаётся отключённой");
            }}
          />
        )}
        {sessionRevoked && <SessionRevokedDialog onSignIn={() => { setSessionRevoked(false); setAuthenticated(false); setRecoveryState(""); setNotificationOpen(false); }} />}
        {onboardingStep !== null && onboardingStep < 3 && (
          <div className="onboarding-overlay" role="dialog" aria-modal="true" aria-label="Знакомство с Task">
            <h3>{["Сегодня", "Разделы", "Новая задача"][onboardingStep]}</h3>
            <p>{[
              "Здесь твой план на день. Задачи из календаря слева, несрочное — справа.",
              "Проекты, поиск, архив — слева в меню. Начни с Сегодня.",
              "Синяя кнопка вверху или Ctrl+N. Всё готово.",
            ][onboardingStep]}</p>
            <div>
              <button className="button button--secondary" type="button" onClick={() => setOnboardingStep(null)}>Пропустить</button>
              <button className="button button--primary" type="button" onClick={() => setOnboardingStep((step) => step === 2 ? null : step + 1)}>
                {onboardingStep === 2 ? "Начать работу" : "Далее"}
              </button>
            </div>
          </div>
        )}
        {toast && <div className="toast" role="status">{toast}</div>}
      </div>
    </div>
  );
}
