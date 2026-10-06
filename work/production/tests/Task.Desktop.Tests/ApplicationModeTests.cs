using System.IO;
using System.Reflection;
using Task.Desktop.Modes;
using Task.Desktop.ViewModels;

namespace Task.Desktop.Tests;

public sealed class ApplicationModeTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "Task-mode-tests", Guid.NewGuid().ToString("N"));
    private ApplicationModePreference Preference => new(_directory);

    [Fact]
    public async global::System.Threading.Tasks.Task PendingCorporateStartup_CanBeCancelledBySwitchingToPersonal()
    {
        var wait = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var corporate = new FakeContext(ApplicationMode.Corporate) { Startup = wait.Task };
        var personal = new FakeContext(ApplicationMode.Personal);
        using var lifecycle = new ApplicationModeLifecycle(Preference, mode => mode == ApplicationMode.Corporate ? corporate : personal);
        var startup = lifecycle.SwitchAsync(ApplicationMode.Corporate);
        Assert.False(startup.IsCompleted);
        Assert.True(await lifecycle.SwitchAsync(ApplicationMode.Personal));
        Assert.True(corporate.Disposed);
        await startup;
        Assert.Same(personal, lifecycle.Current);
        Assert.Equal(ApplicationMode.Personal, Preference.Load());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task FactoryFailure_PreservesOldModeAndContext()
    {
        var personal = new FakeContext(ApplicationMode.Personal);
        using var lifecycle = new ApplicationModeLifecycle(Preference, mode => mode == ApplicationMode.Personal ? personal : throw new InvalidOperationException());
        await lifecycle.SwitchAsync(ApplicationMode.Personal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => lifecycle.SwitchAsync(ApplicationMode.Corporate));
        Assert.Same(personal, lifecycle.Current);
        Assert.False(personal.Disposed);
        Assert.Equal(ApplicationMode.Personal, Preference.Load());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task MissingPreference_RequestsSelectorWithoutCreatingAContext()
    {
        using var lifecycle = new ApplicationModeLifecycle(Preference, _ => throw new Exception("Must not initialize before selection"));
        Assert.False(await lifecycle.RestoreAsync());
        Assert.Null(lifecycle.Current);
    }

    [Theory]
    [InlineData(ApplicationMode.Personal)]
    [InlineData(ApplicationMode.Corporate)]
    public async global::System.Threading.Tasks.Task ChoicePersistsAndRestoresOnlyChosenContext(ApplicationMode mode)
    {
        using (var first = new ApplicationModeLifecycle(Preference, selected => new FakeContext(selected)))
            Assert.True(await first.SwitchAsync(mode));
        var created = new List<ApplicationMode>();
        using var restored = new ApplicationModeLifecycle(Preference, selected =>
        {
            created.Add(selected);
            return new FakeContext(selected);
        });
        Assert.True(await restored.RestoreAsync());
        Assert.Equal(new[] { mode }, created);
        Assert.Equal(mode, restored.Current!.Mode);
        Assert.Equal(mode, Preference.Load());
        Assert.False(File.Exists(Path.Combine(_directory, "credentials.bin")));
        Assert.False(File.Exists(Path.Combine(_directory, "server-settings.json")));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"version\":2,\"mode\":\"Personal\"}")]
    [InlineData("{\"version\":1,\"mode\":\"OfflineCorporate\"}")]
    [InlineData("{\"version\":1,\"mode\":0}")]
    [InlineData("{\"version\":1,\"mode\":\"personal\"}")]
    public async global::System.Threading.Tasks.Task InvalidPreference_RequestsSelector(string json)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "application-preferences.json"), json);
        using var lifecycle = new ApplicationModeLifecycle(Preference, _ => throw new Exception("Must not initialize"));
        Assert.False(await lifecycle.RestoreAsync());
        Assert.Null(lifecycle.Current);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Personal_DoesNotInvokeCorporateCompositionOrAnyAuthOperations()
    {
        var corporateInitializations = 0;
        using var lifecycle = new ApplicationModeLifecycle(Preference, mode =>
        {
            if (mode == ApplicationMode.Corporate)
            {
                corporateInitializations++;
                throw new Exception("Server probe/login/session restore/API would start here");
            }
            return new FakeContext(mode);
        });
        await lifecycle.SwitchAsync(ApplicationMode.Personal);
        Assert.Equal(0, corporateInitializations);
        using var personal = new PersonalApplicationModel();
        Assert.DoesNotContain(personal.Sections, section => section.Title == "Администрирование");
        var types = new[] { typeof(PersonalApplicationModel), typeof(PersonalApplicationContext) };
        Assert.All(types, type => Assert.DoesNotContain(type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic),
            field => field.FieldType.Namespace?.StartsWith("Task.Desktop.Security", StringComparison.Ordinal) == true
                || field.FieldType == typeof(Uri) || field.FieldType == typeof(System.Net.Http.HttpClient)));
    }

    [Fact]
    public async global::System.Threading.Tasks.Task CleanSwitch_DisposesOldContextBeforeStartingNewOne()
    {
        var old = new FakeContext(ApplicationMode.Personal);
        var next = new FakeContext(ApplicationMode.Corporate) { OnStart = () => Assert.True(old.Disposed) };
        using var lifecycle = new ApplicationModeLifecycle(Preference, mode => mode == ApplicationMode.Personal ? old : next);
        await lifecycle.SwitchAsync(ApplicationMode.Personal);
        Assert.True(await lifecycle.SwitchAsync(ApplicationMode.Corporate));
        Assert.True(old.Lifetime.IsCancellationRequested);
        Assert.Same(next, lifecycle.Current);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task Cancel_PreservesModeContextAndPreference()
    {
        var old = new FakeContext(ApplicationMode.Corporate) { AllowSwitch = false };
        using var lifecycle = new ApplicationModeLifecycle(Preference, mode => mode == ApplicationMode.Corporate ? old : throw new Exception("Unexpected context"));
        await lifecycle.SwitchAsync(ApplicationMode.Corporate);
        Assert.False(await lifecycle.SwitchAsync(ApplicationMode.Personal));
        Assert.Same(old, lifecycle.Current);
        Assert.False(old.Disposed);
        Assert.Equal(ApplicationMode.Corporate, Preference.Load());
    }

    [Fact]
    public async global::System.Threading.Tasks.Task PreferenceWriteFailure_PreservesContextAndDrafts()
    {
        var old = new FakeContext(ApplicationMode.Corporate);
        using var lifecycle = new ApplicationModeLifecycle(Preference, mode => mode == ApplicationMode.Corporate ? old : new FakeContext(mode));
        await lifecycle.SwitchAsync(ApplicationMode.Corporate);
        var path = Path.Combine(_directory, "application-preferences.json");
        File.Delete(path);
        Directory.CreateDirectory(path);
        var error = await Record.ExceptionAsync(() => lifecycle.SwitchAsync(ApplicationMode.Personal));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.Same(old, lifecycle.Current);
        Assert.False(old.Disposed);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task OfflineSave_IsNeverSentAndDraftIsPreserved()
    {
        var calls = 0;
        using var command = new AsyncCommand((_, _) => { calls++; return global::System.Threading.Tasks.Task.CompletedTask; });
        var editor = new ModeSwitchEditor("Corporate draft", () => true, () => false, command);
        var choices = 0;
        Assert.False(await ModeSwitchGuard.PrepareAsync([editor], () => false, (_, canSave) =>
        {
            choices++;
            Assert.False(canSave);
            return ModeSwitchDecision.Save;
        }));
        Assert.Equal(1, choices);
        Assert.Equal(0, calls);
        Assert.True(editor.HasState());
        Assert.True(await ModeSwitchGuard.PrepareAsync([editor], () => false, (_, _) => ModeSwitchDecision.Discard));
        Assert.True(editor.HasState()); // Actual disposal commits Discard; the guard does not mutate drafts.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async global::System.Threading.Tasks.Task Save_RequiresBackendConfirmation(bool succeeds)
    {
        var draft = "my draft";
        using var command = new AsyncCommand((_, _) =>
        {
            if (succeeds) draft = "";
            return global::System.Threading.Tasks.Task.CompletedTask;
        });
        Assert.Equal(succeeds, await ModeSwitchGuard.PrepareAsync(
            [new("Task", () => draft.Length > 0, () => false, command)], () => true,
            (_, canSave) => { Assert.True(canSave); return ModeSwitchDecision.Save; }));
        Assert.Equal(succeeds ? "" : "my draft", draft);
    }

    [Fact]
    public async global::System.Threading.Tasks.Task BusyWrite_CannotBeDiscardedDuringSwitch()
    {
        Assert.False(await ModeSwitchGuard.PrepareAsync(
            [new("Task", () => true, () => true, null)], () => false,
            (_, _) => throw new Exception("No dialog while write outcome is pending")));
    }

    internal static async global::System.Threading.Tasks.Task AssertCancelledAsync(MainWindowViewModel shell)
    {
        var editors = ModeSwitchGuard.Inspect(shell);
        Assert.Contains(editors, editor => editor.HasState());
        var promptCount = 0;
        Assert.False(await ModeSwitchGuard.PrepareAsync(editors, () => true, (_, _) =>
        { promptCount++; return ModeSwitchDecision.Cancel; }));
        Assert.Equal(1, promptCount);
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); }

    private sealed class FakeContext(ApplicationMode mode) : IApplicationExecutionContext
    {
        private readonly CancellationTokenSource _lifetime = new();
        public CancellationToken Lifetime => _lifetime.Token;
        public ApplicationMode Mode => mode;
        public bool Disposed { get; private set; }
        public bool AllowSwitch { get; init; } = true;
        public Action? OnStart { get; init; }
        public global::System.Threading.Tasks.Task? Startup { get; init; }
        public async global::System.Threading.Tasks.Task StartAsync()
        {
            OnStart?.Invoke();
            if (Startup is not null)
            {
                try { await Startup.WaitAsync(_lifetime.Token); }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            }
        }
        public global::System.Threading.Tasks.Task<bool> PrepareSwitchAsync() => global::System.Threading.Tasks.Task.FromResult(AllowSwitch);
        public void Dispose() { Disposed = true; _lifetime.Cancel(); }
    }
}
