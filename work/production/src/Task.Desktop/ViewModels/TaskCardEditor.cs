using System.Globalization;
using System.Text.Json.Nodes;
using Task.Desktop.TaskApi;
using Task.Domain;

namespace Task.Desktop.ViewModels;

public sealed class TaskPersonSelection : ViewModelBase
{
    private bool _selected;
    public TaskPersonSelection(Guid id, string name, bool selected) { Id = id; Name = name; _selected = selected; }
    public Guid Id { get; }
    public string Name { get; }
    public bool Selected { get => _selected; set => SetProperty(ref _selected, value); }
}

public sealed class TaskCardEditor : ViewModelBase
{
    private TaskCardContent _source;
    private string _description, _date, _duration, _search = "";
    private TaskChoice? _project, _parent, _requester, _counterparty;
    private IReadOnlyList<TaskChoice> _projects = [], _tasks = [], _people = [], _counterparties = [];
    private IReadOnlyList<TaskPersonSelection> _assignees = [], _watchers = [];
    private string? _message;
    private readonly DateTimeOffset? _originalStart;
    private DateTimeOffset? _currentStart;
    private bool _canAssign, _canWatch;
    public event Action? Changed;
    public TaskCardEditor(TaskCardContent? source, DateTimeOffset? originalStart = null)
    {
        _originalStart = originalStart; _currentStart = originalStart;
        _source = source ?? new(); _description = _source.Description ?? "";
        _date = _source.ScheduledDate?.ToString("dd.MM.yyyy") ?? "";
        _duration = _source.PlannedDurationMinutes?.ToString(CultureInfo.InvariantCulture) ?? "";
        SetOptions(new JsonObject());
    }
    public string Description { get => _description; set { if (SetProperty(ref _description, value)) Changed?.Invoke(); } }
    public string Date { get => _date; set { if (SetProperty(ref _date, value)) Changed?.Invoke(); } }
    public string Duration { get => _duration; set { if (SetProperty(ref _duration, value)) Changed?.Invoke(); } }
    public string Search { get => _search; set => SetProperty(ref _search, value); }
    public string? Message { get => _message; set => SetProperty(ref _message, value); }
    public TaskChoice? Project { get => _project; set { if (SetProperty(ref _project, value)) Changed?.Invoke(); } }
    public TaskChoice? Parent { get => _parent; set { if (SetProperty(ref _parent, value)) Changed?.Invoke(); } }
    public TaskChoice? Requester { get => _requester; set { if (SetProperty(ref _requester, value)) Changed?.Invoke(); } }
    public TaskChoice? Counterparty { get => _counterparty; set { if (SetProperty(ref _counterparty, value)) Changed?.Invoke(); } }
    public IReadOnlyList<TaskChoice> Projects => _projects;
    public IReadOnlyList<TaskChoice> Tasks => _tasks;
    public IReadOnlyList<TaskChoice> People => _people;
    public IReadOnlyList<TaskChoice> Counterparties => _counterparties;
    public IReadOnlyList<TaskPersonSelection> Assignees => _assignees;
    public IReadOnlyList<TaskPersonSelection> Watchers => _watchers;
    public bool CanAssign { get => _canAssign; set => SetProperty(ref _canAssign, value); }
    public bool CanWatch { get => _canWatch; set => SetProperty(ref _canWatch, value); }
    public bool IsDateOnly => _currentStart is null;
    public void UpdateStart(DateTimeOffset? start) { _currentStart = start; OnPropertyChanged(nameof(IsDateOnly)); }

    public void SetOptions(JsonObject options)
    {
        IReadOnlyList<TaskChoice> Choices(string key, Guid? selected)
        {
            var values = (options[key]?.AsArray() ?? []).Select(n => new TaskChoice(Guid.Parse(n!["id"]!.ToString()), n["name"]!.ToString())).ToList();
            if (selected.HasValue && values.All(v => v.Id != selected)) values.Insert(0, new(selected, "Текущее значение (вне результатов поиска)"));
            values.Insert(0, new(null, "Не указано")); return values;
        }
        var project = _project is null ? _source.ProjectId : _project.Id; var parent = _parent is null ? _source.ParentTaskId : _parent.Id;
        var requester = _requester is null ? _source.RequesterUserId : _requester.Id; var counterparty = _counterparty is null ? _source.PrimaryCounterpartyObjectId : _counterparty.Id;
        _projects = Choices("projects", project); _tasks = Choices("tasks", parent); _people = Choices("people", requester); _counterparties = Choices("counterparties", counterparty);
        _project = _projects.First(v => v.Id == project); _parent = _tasks.First(v => v.Id == parent); _requester = _people.First(v => v.Id == requester); _counterparty = _counterparties.First(v => v.Id == counterparty);
        IReadOnlyList<TaskPersonSelection> SelectPeople(IReadOnlyList<TaskPersonSelection> existing, IReadOnlyList<Guid> original)
        {
            var selected = existing.Count == 0 ? original.ToHashSet() : existing.Where(p => p.Selected).Select(p => p.Id).ToHashSet();
            var names = _people.Where(p => p.Id.HasValue).ToDictionary(p => p.Id!.Value, p => p.Name);
            foreach (var id in selected) names.TryAdd(id, existing.FirstOrDefault(p => p.Id == id)?.Name ?? "Участник вне результатов поиска");
            return names.Select(p => { var item = new TaskPersonSelection(p.Key, p.Value, selected.Contains(p.Key)); item.PropertyChanged += (_, _) => Changed?.Invoke(); return item; }).ToArray();
        }
        _assignees = SelectPeople(_assignees, _source.AssigneeIds); _watchers = SelectPeople(_watchers, _source.WatcherIds);
        foreach (var name in new[] { nameof(Projects), nameof(Tasks), nameof(People), nameof(Counterparties), nameof(Project), nameof(Parent), nameof(Requester), nameof(Counterparty), nameof(Assignees), nameof(Watchers) }) OnPropertyChanged(name);
        Message = options.Any(p => p.Key.EndsWith("HasMore") && p.Value?.GetValue<bool>() == true) ? "Показаны первые 200 результатов. Уточните поиск." : null;
    }

    public TaskCardContent Build(DateTimeOffset? start)
    {
        DateOnly? date = null; int? duration = null;
        if (!string.IsNullOrWhiteSpace(Date))
        {
            if (!DateOnly.TryParseExact(Date.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)) throw new ArgumentException("Дата: ДД.ММ.ГГГГ."); date = parsed;
        }
        if (!string.IsNullOrWhiteSpace(Duration))
        {
            if (!int.TryParse(Duration, out var parsed) || parsed is < 1 or > 10080) throw new ArgumentException("Длительность: от 1 до 10080 минут."); duration = parsed;
        }
        // Exact start owns the local schedule tuple; a date alone remains an all-day planning date.
        var local = start?.ToLocalTime();
        var preserveSchedule = start == _originalStart && start is not null;
        var zone = TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? iana : TimeZoneInfo.Local.Id;
        var content = new TaskCardContent
        {
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            ProjectId = Project?.Id,
            ParentTaskId = Parent?.Id,
            RequesterUserId = Requester?.Id,
            PrimaryCounterpartyObjectId = Counterparty?.Id,
            ScheduledDate = preserveSchedule ? _source.ScheduledDate : local is null ? date : DateOnly.FromDateTime(local.Value.DateTime),
            StartTimeLocal = preserveSchedule ? _source.StartTimeLocal : local is null ? null : TimeOnly.FromDateTime(local.Value.DateTime),
            ScheduleTimeZone = preserveSchedule ? _source.ScheduleTimeZone : local is null ? null : zone,
            PlannedDurationMinutes = duration,
            AssigneeIds = Assignees.Where(p => p.Selected).Select(p => p.Id).ToArray(),
            WatcherIds = Watchers.Where(p => p.Selected).Select(p => p.Id).ToArray()
        };
        content.Validate(start); return content;
    }
    public string? Patch(TaskCardContent value)
    {
        var before = JsonNode.Parse(_source.ToJson())!.AsObject(); var after = JsonNode.Parse(value.ToJson())!.AsObject();
        var patch = new JsonObject(); foreach (var (key, v) in after) if (!JsonNode.DeepEquals(before[key], v)) patch[key] = v?.DeepClone();
        return patch.Count == 0 ? null : patch.ToJsonString();
    }
}
