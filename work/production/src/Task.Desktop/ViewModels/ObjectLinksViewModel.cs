using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed record ObjectLinkTypeChoice(string Value, string Label);

/// <summary>One use case for the existing Corporate routes and local Personal relations.</summary>
public sealed class ObjectLinksViewModel : ViewModelBase, IDisposable
{
    public event Action<long>? Changed;
    public bool ShowTargetId { get; init; } = true;
    private readonly IDesktopObjectLinksClient _client;
    private readonly Func<bool> _canWrite;
    private Guid _source;
    private long _version;
    private string _target = "", _type = "task_file", _message = "";
    private IReadOnlyList<DesktopObjectLink> _items = [];
    private DesktopObjectLink? _selected;
    public ObjectLinksViewModel(IDesktopObjectLinksClient client, Func<bool>? canWrite = null)
    {
        _client = client; _canWrite = canWrite ?? (() => true);
        RefreshCommand = new(async (_, ct) => { var source = _source; Apply(await _client.GetLinksAsync(source, ct), source); }, _ => _source != Guid.Empty && !IsBusy);
        AddCommand = new(async (_, ct) => { var source = _source; Apply(await _client.AddLinkAsync(source, _version, Guid.Parse(TargetId), LinkType, ct), source); }, _ => _canWrite() && !IsBusy && _source != Guid.Empty && Guid.TryParse(TargetId, out var target) && target != Guid.Empty);
        RemoveCommand = new(async (_, ct) => { var source = _source; Apply(await _client.RemoveLinkAsync(source, _version, Selected!.Id, ct), source); }, _ => _canWrite() && !IsBusy && Selected?.SourceObjectId == _source && _source != Guid.Empty);
        foreach (var c in new[] { RefreshCommand, AddCommand, RemoveCommand }) c.ExecutionFailed += _ => Message = "Не удалось изменить связи. Обновите объект.";
    }
    public IReadOnlyList<DesktopObjectLink> Items { get => _items; private set => SetProperty(ref _items, value); }
    public DesktopObjectLink? Selected { get => _selected; set { SetProperty(ref _selected, value); RemoveCommand.RaiseCanExecuteChanged(); } }
    public string TargetId { get => _target; set { SetProperty(ref _target, value); AddCommand.RaiseCanExecuteChanged(); } }
    public string LinkType { get => _type; set => SetProperty(ref _type, value); }
    public IReadOnlyList<ObjectLinkTypeChoice> LinkTypes { get; } = [new("task_file", "Файл задачи"), new("project_file", "Файл проекта"), new("contact_file", "Файл контакта"), new("task_contact", "Контакт задачи")];
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public AsyncCommand RefreshCommand { get; }
    public AsyncCommand AddCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    public bool IsBusy => RefreshCommand?.IsExecuting == true || AddCommand?.IsExecuting == true || RemoveCommand?.IsExecuting == true;
    public void SetSource(Guid id, long version)
    {
        _source = id; _version = version; Items = []; Selected = null;
        RefreshCommand.RaiseCanExecuteChanged(); AddCommand.RaiseCanExecuteChanged(); RemoveCommand.RaiseCanExecuteChanged();
    }
    private void Apply(DesktopWorkResult<DesktopObjectLinks> result, Guid source)
    {
        if (source != _source) return;
        if (result is DesktopWorkResult<DesktopObjectLinks>.Succeeded ok) { Items = ok.Value.Items; _version = ok.Value.SourceVersion; Selected = Items.FirstOrDefault(); Message = "Связи обновлены."; }
        else Message = result switch { DesktopWorkResult<DesktopObjectLinks>.ValidationFailure v => v.Message, DesktopWorkResult<DesktopObjectLinks>.Conflict => "Версия изменилась. Обновите связи.", DesktopWorkResult<DesktopObjectLinks>.Forbidden => "Нет прав на связи.", _ => "Связи недоступны. Обновите объект." };
        if (result is DesktopWorkResult<DesktopObjectLinks>.Succeeded) Changed?.Invoke(_version);
    }
    public void Dispose() { RefreshCommand.Dispose(); AddCommand.Dispose(); RemoveCommand.Dispose(); }
}
