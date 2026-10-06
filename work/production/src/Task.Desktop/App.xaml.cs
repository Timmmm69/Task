using System.Configuration;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using Task.Desktop.Modes;

[assembly: InternalsVisibleTo("Task.Desktop.Tests")]

namespace Task.Desktop;

/// <summary>Mode selection and lifetime; corporate authentication belongs to its context.</summary>
public partial class App : global::System.Windows.Application
{
    private ApplicationModeLifecycle? _lifecycle;
    private bool _switching;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var directory = GetDesktopDataDirectory();
            _lifecycle = new(new ApplicationModePreference(directory), mode => mode switch
            {
                ApplicationMode.Personal => new PersonalApplicationContext(this, directory, Shutdown, SwitchMode, SwitchToCorporate),
                ApplicationMode.Corporate => new CorporateApplicationContext(this, directory, Shutdown, SwitchMode),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            });
            if (!await _lifecycle.RestoreAsync())
            {
                var selector = new ModeSelectorWindow();
                if (selector.ShowDialog() != true || selector.SelectedMode is not { } selected)
                {
                    Shutdown();
                    return;
                }
                await _lifecycle.SwitchAsync(selected);
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            MessageBox.Show("Task не удалось запустить. Проверьте доступ к локальным настройкам приложения.",
                "Ошибка запуска Task", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async void SwitchMode()
    {
        if (_switching || _lifecycle is null || _lifecycle.IsTransitioning) return;
        _switching = true;
        try
        {
            var selector = new ModeSelectorWindow { Owner = MainWindow };
            if (selector.ShowDialog() == true && selector.SelectedMode is { } mode)
            {
                var transition = _lifecycle.SwitchAsync(mode);
                _switching = false;
                // Startup may be waiting for a corporate server; allow switching away.
                // The lifecycle still locks concurrent editor-save transitions.
                await transition;
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            MessageBox.Show("Режим не удалось переключить. Проверьте доступ к локальным настройкам и повторите попытку.",
                "Смена режима Task", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { _switching = false; }
    }

    private async void SwitchToCorporate()
    {
        if (_lifecycle is null || _lifecycle.IsTransitioning) return;
        try { await _lifecycle.SwitchAsync(ApplicationMode.Corporate); }
        catch (Exception error)
        {
            MessageBox.Show("Corporate не удалось открыть.\n" + error.Message, "Task", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _lifecycle?.Dispose();
        base.OnExit(e);
    }

    private static string GetDesktopDataDirectory()
    {
        const string variable = "TASK_DESKTOP_DATA_DIRECTORY";
        var configured = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(configured))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Task");
        if (!Path.IsPathFullyQualified(configured))
            throw new ConfigurationErrorsException($"{variable} must be an absolute path.");
        return Path.GetFullPath(configured);
    }
}
