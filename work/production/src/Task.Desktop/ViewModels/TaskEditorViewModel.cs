using System.Globalization;
using Task.Desktop.TaskApi;

namespace Task.Desktop.ViewModels;

public enum TaskEditorMode
{
    Create,
    Edit,
}

public sealed record TaskPriorityOption(DesktopTaskPriority Value, string Text);

/// <summary>Local draft for the Task side panel. Network operations remain in TasksViewModel.</summary>
public sealed class TaskEditorViewModel : ViewModelBase
{
    private static readonly CultureInfo RussianCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly IReadOnlyList<TaskPriorityOption> PriorityValues =
    [
        new(DesktopTaskPriority.Low, "Низкий"),
        new(DesktopTaskPriority.Normal, "Обычный"),
        new(DesktopTaskPriority.High, "Высокий"),
        new(DesktopTaskPriority.Critical, "Критический"),
    ];

    private DesktopTaskDto? _source;
    private string _title = string.Empty;
    private TaskPriorityOption _priority = PriorityValues[1];
    private string _startText = string.Empty;
    private string _deadlineText = string.Empty;
    private string? _titleError;
    private string? _priorityError;
    private string? _startError;
    private string? _deadlineError;
    private string? _statusMessage;
    private bool _isBusy;
    private bool _isDirty;
    private bool _hasConflict;
    private bool _isDiscardConfirmationVisible;
    private bool _retryAvailable = true;
    private long _revision;
    private bool _suppressChanges;

    public TaskEditorViewModel(TaskEditorMode mode, DesktopTaskDto? source = null)
    {
        Mode = mode;
        Priorities = PriorityValues;
        Load(source);
    }

    public TaskEditorMode Mode { get; }
    public Guid? SourceId => _source?.Id;
    public IReadOnlyList<TaskPriorityOption> Priorities { get; }
    public string Heading => Mode == TaskEditorMode.Create ? "Новая задача" : "Изменить задачу";
    public string SubmitText => _retryAvailable ? "Сохранить" : "Повтор станет доступен…";
    public long Revision => _revision;
    public bool HasUnsavedChanges => _isDirty;
    public bool HasConflict => _hasConflict;
    public bool IsDiscardConfirmationVisible => _isDiscardConfirmationVisible;
    public bool RetryAvailable => _retryAvailable;
    public bool HasErrors => TitleError is not null || PriorityError is not null
        || StartError is not null || DeadlineError is not null;
    public bool CanSubmit => !_isBusy && !_hasConflict && _retryAvailable && !HasErrors;

    public string Title
    {
        get => _title;
        set
        {
            if (SetProperty(ref _title, value ?? string.Empty))
            {
                Changed();
            }
        }
    }

    public TaskPriorityOption Priority
    {
        get => _priority;
        set
        {
            if (value is not null && SetProperty(ref _priority, value))
            {
                Changed();
            }
        }
    }

    public string StartText
    {
        get => _startText;
        set
        {
            if (SetProperty(ref _startText, value ?? string.Empty))
            {
                Changed();
            }
        }
    }

    public string DeadlineText
    {
        get => _deadlineText;
        set
        {
            if (SetProperty(ref _deadlineText, value ?? string.Empty))
            {
                Changed();
            }
        }
    }

    public string? TitleError { get => _titleError; private set => SetProperty(ref _titleError, value); }
    public string? PriorityError { get => _priorityError; private set => SetProperty(ref _priorityError, value); }
    public string? StartError { get => _startError; private set => SetProperty(ref _startError, value); }
    public string? DeadlineError { get => _deadlineError; private set => SetProperty(ref _deadlineError, value); }
    public string? StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetProperty(ref _isBusy, value))
            {
                NotifyState();
            }
        }
    }

    public DesktopCreateTaskCommand? BuildCreateCommand()
    {
        if (Mode != TaskEditorMode.Create || !Validate(out var start, out var deadline))
        {
            return null;
        }

        return new DesktopCreateTaskCommand(Title.Trim(), Priority.Value, start, deadline);
    }

    public DesktopPatchTaskCommand? BuildPatchCommand()
    {
        if (Mode != TaskEditorMode.Edit || _source is null || !Validate(out var start, out var deadline))
        {
            return null;
        }

        var title = Title.Trim();
        var titleField = title != _source.Title ? DesktopTaskField<string>.From(title) : default;
        var priorityField = Priority.Value != _source.Priority
            ? DesktopTaskField<DesktopTaskPriority>.From(Priority.Value)
            : default;
        var startField = !string.Equals(StartText.Trim(), FormatLocal(_source.StartAtUtc), StringComparison.Ordinal)
            ? DesktopTaskField<DateTimeOffset?>.From(start)
            : default;
        var deadlineField = !string.Equals(DeadlineText.Trim(), FormatLocal(_source.DeadlineAtUtc), StringComparison.Ordinal)
            ? DesktopTaskField<DateTimeOffset?>.From(deadline)
            : default;
        if (!titleField.IsSpecified && !priorityField.IsSpecified
            && !startField.IsSpecified && !deadlineField.IsSpecified)
        {
            StatusMessage = "Нет изменений для сохранения.";
            return null;
        }

        return new DesktopPatchTaskCommand(
            _source.Id,
            _source.Version,
            titleField,
            priorityField,
            startField,
            deadlineField);
    }

    public void LoadLatest(DesktopTaskDto task)
    {
        ArgumentNullException.ThrowIfNull(task);
        Load(task);
        StatusMessage = "Загружена актуальная версия. Проверьте изменения и сохраните снова.";
    }

    public void ApplyServerValidation(
        string message,
        IReadOnlyDictionary<string, IReadOnlyList<string>> fieldErrors)
    {
        StatusMessage = string.IsNullOrWhiteSpace(message)
            ? "Проверьте заполненные поля."
            : message;
        foreach (var (field, errors) in fieldErrors)
        {
            var safe = errors.FirstOrDefault();
            if (field.Equals("title", StringComparison.OrdinalIgnoreCase)) TitleError = safe;
            if (field.Equals("priority", StringComparison.OrdinalIgnoreCase)) PriorityError = safe;
            if (field.Equals("startAtUtc", StringComparison.OrdinalIgnoreCase)) StartError = safe;
            if (field.Equals("deadlineAtUtc", StringComparison.OrdinalIgnoreCase)) DeadlineError = safe;
        }
        NotifyState();
    }

    public void SetStatus(string message) => StatusMessage = message;

    public void SetConflict()
    {
        _hasConflict = true;
        StatusMessage = "Задача уже изменена другим пользователем.";
        OnPropertyChanged(nameof(HasConflict));
        NotifyState();
    }

    public void ShowDiscardConfirmation(bool visible)
    {
        _isDiscardConfirmationVisible = visible;
        OnPropertyChanged(nameof(IsDiscardConfirmationVisible));
    }

    public void SetRetryAvailable(bool available)
    {
        _retryAvailable = available;
        OnPropertyChanged(nameof(RetryAvailable));
        OnPropertyChanged(nameof(SubmitText));
        NotifyState();
    }

    private void Load(DesktopTaskDto? source)
    {
        _suppressChanges = true;
        _source = source;
        Title = source?.Title ?? string.Empty;
        Priority = PriorityValues.Single(option => option.Value == (source?.Priority ?? DesktopTaskPriority.Normal));
        StartText = FormatLocal(source?.StartAtUtc);
        DeadlineText = FormatLocal(source?.DeadlineAtUtc);
        _suppressChanges = false;
        _isDirty = false;
        _hasConflict = false;
        _isDiscardConfirmationVisible = false;
        _retryAvailable = true;
        _revision++;
        Validate(out _, out _);
        OnPropertyChanged(nameof(HasUnsavedChanges));
        OnPropertyChanged(nameof(HasConflict));
        OnPropertyChanged(nameof(IsDiscardConfirmationVisible));
        NotifyState();
    }

    private void Changed()
    {
        if (_suppressChanges)
        {
            return;
        }

        _isDirty = true;
        _revision++;
        StatusMessage = null;
        Validate(out _, out _);
        OnPropertyChanged(nameof(HasUnsavedChanges));
    }

    private bool Validate(out DateTimeOffset? startUtc, out DateTimeOffset? deadlineUtc)
    {
        TitleError = string.IsNullOrWhiteSpace(Title)
            ? "Введите название задачи."
            : Title.Trim().Length > 500 ? "Название не должно превышать 500 символов." : null;
        PriorityError = null;
        startUtc = ParseLocal(StartText, out var startError);
        deadlineUtc = ParseLocal(DeadlineText, out var deadlineError);
        StartError = startError;
        DeadlineError = deadlineError;
        if (StartError is null && DeadlineError is null
            && startUtc.HasValue && deadlineUtc.HasValue && deadlineUtc < startUtc)
        {
            DeadlineError = "Срок не может быть раньше начала.";
        }

        NotifyState();
        return !HasErrors;
    }

    private static DateTimeOffset? ParseLocal(string text, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!DateTime.TryParse(text.Trim(), RussianCulture, DateTimeStyles.AllowWhiteSpaces, out var local))
        {
            error = "Укажите дату и время, например 28.08.2026 14:30.";
            return null;
        }

        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (TimeZoneInfo.Local.IsInvalidTime(local))
        {
            error = "Это локальное время не существует из-за перехода часового пояса.";
            return null;
        }

        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
    }

    private static string FormatLocal(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("g", RussianCulture) ?? string.Empty;

    private void NotifyState()
    {
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(CanSubmit));
    }
}
