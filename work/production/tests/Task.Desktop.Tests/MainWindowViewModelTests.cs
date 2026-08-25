using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests;

public class MainWindowViewModelTests
{
    [Fact]
    public void Sections_ArePopulated_WithAtLeastEight()
    {
        var vm = new MainWindowViewModel();

        Assert.NotNull(vm.Sections);
        Assert.NotEmpty(vm.Sections);
        Assert.True(vm.Sections.Count >= 8, "The shell must expose at least eight navigation sections.");
    }

    [Fact]
    public void SelectedSection_DefaultsToFirst()
    {
        var vm = new MainWindowViewModel();

        Assert.NotNull(vm.Sections);
        Assert.Same(vm.Sections[0], vm.SelectedSection);
    }

    [Fact]
    public void ChangingSelectedSection_RaisesPropertyChanged()
    {
        var vm = new MainWindowViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        var target = vm.Sections[^1];
        vm.SelectedSection = target;

        Assert.Contains(nameof(MainWindowViewModel.SelectedSection), raised);
        Assert.Same(target, vm.SelectedSection);
    }

    [Fact]
    public void EverySection_HasNonEmptyTitleAndPlaceholder()
    {
        var vm = new MainWindowViewModel();

        foreach (var section in vm.Sections)
        {
            Assert.False(string.IsNullOrWhiteSpace(section.Title),
                "Section title must be non-empty and Russian.");
            Assert.False(string.IsNullOrWhiteSpace(section.PlaceholderText),
                "Section placeholder must be non-empty.");
        }
    }

    [Fact]
    public void EverySection_HasUniqueNonEmptyRoute()
    {
        var vm = new MainWindowViewModel();
        var routes = vm.Sections.Select(section => section.Route).ToArray();

        Assert.All(routes, route => Assert.False(string.IsNullOrWhiteSpace(route)));
        Assert.Equal(routes.Length, routes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Sections_ExposeCanonicalRoutes_InExactOrder()
    {
        var vm = new MainWindowViewModel();
        var expected = new[]
        {
            "today", "inbox", "calendar", "tasks", "projects", "catalog",
            "contacts", "notifications", "archive", "trash", "settings",
        };

        var actual = vm.Sections.Select(section => section.Route).ToArray();

        Assert.Equal(expected.Length, actual.Length);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void SectionText_DoesNotContainMojibakeCharacters()
    {
        var vm = new MainWindowViewModel();

        foreach (var section in vm.Sections)
        {
            Assert.DoesNotContain('Ð', section.Title);
            Assert.DoesNotContain('Ñ', section.Title);
            Assert.DoesNotContain('Ð', section.PlaceholderText);
            Assert.DoesNotContain('Ñ', section.PlaceholderText);
        }
    }

    [Fact]
    public void ConnectionStatus_IsPlainRussianText()
    {
        var vm = new MainWindowViewModel();

        Assert.Equal("Нет подключения — только просмотр", vm.ConnectionStatus);
        Assert.DoesNotContain('Ð', vm.ConnectionStatus);
        Assert.DoesNotContain('Ñ', vm.ConnectionStatus);
    }

    [Fact]
    public void AssigningSameSelectedSection_DoesNotRaisePropertyChanged()
    {
        var vm = new MainWindowViewModel();
        var raisedCount = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.SelectedSection))
            {
                raisedCount++;
            }
        };

        vm.SelectedSection = vm.SelectedSection;

        Assert.Equal(0, raisedCount);
    }

    [Fact]
    public void IsReadOnlyMode_IsAlwaysTrue()
    {
        var vm = new MainWindowViewModel();

        Assert.True(vm.IsReadOnlyMode);
    }

    [Fact]
    public void ReadOnlyNotice_IsRussianAndCoversAllRestrictions()
    {
        var vm = new MainWindowViewModel();

        Assert.False(string.IsNullOrWhiteSpace(vm.ReadOnlyNotice));

        var notice = vm.ReadOnlyNotice.ToLowerInvariant();
        Assert.Contains("сервер не подключён", notice);
        Assert.Contains("синхронизация не выполняется", notice);
        Assert.Contains("изменение данных недоступно", notice);
    }

    [Fact]
    public void ReadOnlyNotice_DoesNotContainMojibakeCharacters()
    {
        var vm = new MainWindowViewModel();

        Assert.DoesNotContain('Ð', vm.ReadOnlyNotice);
        Assert.DoesNotContain('Ñ', vm.ReadOnlyNotice);
    }

    [Fact]
    public void AuthenticatedShell_ShowsConfirmedServerSession()
    {
        using var vm = new MainWindowViewModel(
            new Uri("https://task.company.local/"),
            _ => global::System.Threading.Tasks.Task.CompletedTask);

        Assert.Equal("https://task.company.local", vm.ServerAddress);
        Assert.Equal("Сессия подтверждена · https://task.company.local", vm.ConnectionStatus);
        Assert.Contains("просмотр задач доступен", vm.ReadOnlyNotice);
        Assert.True(vm.LogoutCommand.CanExecute(null));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task LogoutCommand_BlocksSecondSubmission()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var vm = new MainWindowViewModel(
            new Uri("https://task.company.local/"),
            async cancellationToken =>
            {
                Interlocked.Increment(ref calls);
                entered.SetResult();
                await release.Task.WaitAsync(cancellationToken);
            });

        var first = vm.LogoutCommand.ExecuteAsync();
        await entered.Task;
        var second = await vm.LogoutCommand.ExecuteAsync();
        release.SetResult();

        Assert.True(await first);
        Assert.False(second);
        Assert.Equal(1, calls);
    }
}
