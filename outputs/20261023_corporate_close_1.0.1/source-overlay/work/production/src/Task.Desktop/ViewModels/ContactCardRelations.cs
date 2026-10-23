using Task.Desktop.Work;

namespace Task.Desktop.ViewModels;

public sealed class ContactRelationDraft : ViewModelBase
{
    private string _type = "", _value = "", _label = "", _country = "", _region = "", _city = "", _street = "", _postal = "";
    private bool _primary;
    public Guid Id { get; init; }
    public string Type { get => _type; set => SetProperty(ref _type, value); }
    public string Value { get => _value; set => SetProperty(ref _value, value); }
    public string Label { get => _label; set => SetProperty(ref _label, value); }
    public string Country { get => _country; set => SetProperty(ref _country, value); }
    public string Region { get => _region; set => SetProperty(ref _region, value); }
    public string City { get => _city; set => SetProperty(ref _city, value); }
    public string Street { get => _street; set => SetProperty(ref _street, value); }
    public string Postal { get => _postal; set => SetProperty(ref _postal, value); }
    public bool IsPrimary { get => _primary; set => SetProperty(ref _primary, value); }
}

public sealed partial class ContactCardViewModel
{
    private bool _relationBusy;
    private ContactRelationDraft? _channelDraft, _addressDraft;
    private DesktopContactChannel? _deleteChannel;
    public ContactRelationDraft? ChannelDraft { get => _channelDraft; private set { SetProperty(ref _channelDraft, value); NotifyRelationDraft(); } }
    public ContactRelationDraft? AddressDraft { get => _addressDraft; private set { SetProperty(ref _addressDraft, value); NotifyRelationDraft(); } }
    public bool HasChannelDraft => ChannelDraft is not null;
    public bool HasAddressDraft => AddressDraft is not null;
    public bool HasRelationDraft => HasChannelDraft || HasAddressDraft || _deleteChannel is not null;
    public bool HasEditableRelationDraft => HasChannelDraft || HasAddressDraft;
    public string RelationSaveLabel => HasAddressDraft ? "Сохранить адрес" : "Сохранить канал";
    public bool IsConfirmingChannelDelete => _deleteChannel is not null;
    public bool CanChangeRelationDraft => CanEdit && !IsSaving;
    public string RelationAccessMessage => _client is not IDesktopContactRelationsClient ? "Управление каналами и адресами недоступно в текущем подключении."
        : !_canRequest() ? "Нет подключения или сервер недоступен. Изменения недоступны."
        : !CanEdit ? "Только чтение. Изменения требуют актуальной карточки и права Contact.Update; доступ к объекту проверяет сервер." : "";
    public IReadOnlyList<DesktopContactChannel> Channels => Details?.Channels ?? [];
    public IReadOnlyList<DesktopContactAddress> Addresses => Details?.Addresses ?? [];
    public bool ChannelsEmpty => Channels.Count == 0;
    public bool AddressesEmpty => Addresses.Count == 0;
    public IReadOnlyList<DesktopContactRelationType> ChannelTypes => ContactRelationTypes.Channels;
    public IReadOnlyList<DesktopContactRelationType> AddressTypes => ContactRelationTypes.Addresses;
    public AsyncCommand AddChannelCommand { get; private set; } = null!;
    public AsyncCommand EditChannelCommand { get; private set; } = null!;
    public AsyncCommand RemoveChannelCommand { get; private set; } = null!;
    public AsyncCommand AddAddressCommand { get; private set; } = null!;
    public AsyncCommand SaveRelationCommand { get; private set; } = null!;
    public AsyncCommand ConfirmChannelDeleteCommand { get; private set; } = null!;
    public AsyncCommand CancelRelationCommand { get; private set; } = null!;
    private IEnumerable<AsyncCommand> RelationCommands => [AddChannelCommand, EditChannelCommand, RemoveChannelCommand, AddAddressCommand,
        SaveRelationCommand, ConfirmChannelDeleteCommand, CancelRelationCommand];
    private bool CanStartRelation => _client is IDesktopContactRelationsClient && CanEdit && !IsSaving && !IsEditing && !HasRelationDraft;

    private void InitializeRelations()
    {
        AddChannelCommand = new((_, _) => { ChannelDraft = new() { Type = "phone" }; return System.Threading.Tasks.Task.CompletedTask; }, _ => CanStartRelation);
        EditChannelCommand = new((p, _) =>
        {
            var c = (DesktopContactChannel)p!;
            ChannelDraft = new() { Id = c.Id, Type = c.ChannelType, Value = c.Value, Label = c.Label ?? "", IsPrimary = c.IsPrimary };
            return System.Threading.Tasks.Task.CompletedTask;
        }, p => CanStartRelation && p is DesktopContactChannel c && Channels.Any(item => item.Id == c.Id));
        RemoveChannelCommand = new((p, _) => { _deleteChannel = (DesktopContactChannel)p!; NotifyRelationDraft(); return System.Threading.Tasks.Task.CompletedTask; },
            p => CanStartRelation && p is DesktopContactChannel c && Channels.Any(item => item.Id == c.Id));
        AddAddressCommand = new((_, _) => { AddressDraft = new() { Type = "work" }; return System.Threading.Tasks.Task.CompletedTask; }, _ => CanStartRelation);
        SaveRelationCommand = new(ChangeRelationAsync, _ => CanChangeRelationDraft && (HasChannelDraft || HasAddressDraft));
        ConfirmChannelDeleteCommand = new(ChangeRelationAsync, _ => CanChangeRelationDraft && IsConfirmingChannelDelete);
        CancelRelationCommand = new((_, _) => { ClearRelationDraft(); return System.Threading.Tasks.Task.CompletedTask; }, _ => HasRelationDraft && !IsSaving);
    }

    private async System.Threading.Tasks.Task ChangeRelationAsync(object? _, CancellationToken ct)
    {
        if (_client is not IDesktopContactRelationsClient client || Details is not { } original) return;
        var channel = ChannelDraft is { } c ? new DesktopContactChannel(c.Id, c.Type, c.Value, c.Label, c.IsPrimary) : null;
        var address = AddressDraft is { } a ? new DesktopContactAddress(Guid.Empty, a.Type, a.Value, a.Country, a.Region, a.City, a.Street, a.Postal, a.IsPrimary) : null;
        var error = channel is not null ? DesktopWorkApiClient.ValidateChannel(channel) : address is not null ? DesktopWorkApiClient.ValidateAddress(address) : null;
        if (error is not null) { Message = error; return; }
        var generation = _generation;
        using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _request = request; _relationBusy = true; Message = null; NotifyCommands(); OnPropertyChanged(nameof(IsSaving));
        try
        {
            var id = original.Contact.Id; var version = original.Contact.Version;
            var result = channel is not null ? await _closeOperations.TrackAsync(() => client.SaveChannelAsync(id, version, channel, request.Token))
                : address is not null ? await _closeOperations.TrackAsync(() => client.AddAddressAsync(id, version, address, request.Token))
                : await _closeOperations.TrackAsync(() => client.DeleteChannelAsync(id, version, _deleteChannel!.Id, request.Token));
            if (!Current(generation, request.Token) || !_canWrite()) return;
            if (result is DesktopWorkResult<DesktopContactDetails>.Succeeded success
                && success.Value.Contact.Id == id && success.Value.Contact.Version > version)
            {
                Details = success.Value; FillDraft(); ClearRelationDraft();
                Message = "Изменения подтверждены сервером. Карточка обновлена.";
                Saved?.Invoke(success.Value.Contact);
            }
            else
            {
                if (result is DesktopWorkResult<DesktopContactDetails>.Succeeded) result = new DesktopWorkResult<DesktopContactDetails>.MalformedResponse();
                ShowFailure(result);
                if (result is not DesktopWorkResult<DesktopContactDetails>.ValidationFailure) _conflict = true;
                if (!HasDetails) ClearRelationDraft();
            }
        }
        catch (OperationCanceledException) when (request.IsCancellationRequested)
        {
            if (generation == _generation) { _conflict = true; Message = "Запрос отменён. Результат не подтверждён. Отмените ввод и обновите карточку перед повтором."; }
        }
        catch (Exception)
        {
            if (generation == _generation) { _conflict = true; Message = "Результат операции не подтверждён. Отмените ввод и обновите карточку перед повтором."; }
        }
        finally
        {
            if (generation == _generation) _request = null;
            _relationBusy = false; NotifyCommands(); OnPropertyChanged(nameof(IsSaving));
        }
    }

    private void ClearRelationDraft() { _channelDraft = _addressDraft = null; _deleteChannel = null; OnPropertyChanged(nameof(ChannelDraft)); OnPropertyChanged(nameof(AddressDraft)); NotifyRelationDraft(); }
    private void NotifyRelationDraft()
    {
        OnPropertyChanged(nameof(HasChannelDraft)); OnPropertyChanged(nameof(HasAddressDraft)); OnPropertyChanged(nameof(HasRelationDraft));
        OnPropertyChanged(nameof(HasEditableRelationDraft)); OnPropertyChanged(nameof(RelationSaveLabel));
        OnPropertyChanged(nameof(IsConfirmingChannelDelete));
        // May be called before the base commands have been constructed.
        if (ReloadCommand is not null) NotifyCommands();
    }
    private void NotifyRelations()
    {
        OnPropertyChanged(nameof(Channels)); OnPropertyChanged(nameof(Addresses)); OnPropertyChanged(nameof(ChannelsEmpty)); OnPropertyChanged(nameof(AddressesEmpty));
    }
}
