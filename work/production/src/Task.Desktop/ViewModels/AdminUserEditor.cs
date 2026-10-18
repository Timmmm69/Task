using System.Net.Mail;
using Task.Application.Security;
using Task.Desktop.Administration;

namespace Task.Desktop.ViewModels;

/// <summary>In-memory profile draft. Only changed fields enter PATCH; no status or role edits.</summary>
public sealed class AdminUserEditor : ViewModelBase
{
    public DesktopManagedUser? Original { get; }
    private string _firstName, _lastName, _displayName, _login, _workEmail, _jobTitle;
    public AdminUserEditor(DesktopManagedUser? original = null)
    {
        Original = original;
        _firstName = original?.FirstName ?? ""; _lastName = original?.LastName ?? "";
        _displayName = original?.DisplayName ?? ""; _login = original?.Login ?? "";
        _workEmail = original?.WorkEmail ?? ""; _jobTitle = original?.JobTitle ?? "";
    }
    public string Title => Original is null ? "Создать пользователя" : $"Редактировать · {Original.DisplayName}";
    public string FirstName { get => _firstName; set => SetProperty(ref _firstName, value); }
    public string LastName { get => _lastName; set => SetProperty(ref _lastName, value); }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string Login { get => _login; set => SetProperty(ref _login, value); }
    public string WorkEmail { get => _workEmail; set => SetProperty(ref _workEmail, value); }
    public string JobTitle { get => _jobTitle; set => SetProperty(ref _jobTitle, value); }
    public string DisplayNameLabel => Original is null ? "Отображаемое имя (необязательно)" : "Отображаемое имя *";
    public string DepartmentHint => Original?.DepartmentId is null
        ? "Подразделение не указано. Выбор подразделений сейчас недоступен."
        : "Подразделение сохранится без изменений. Выбор подразделений сейчас недоступен.";
    private static string? Nullable(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        Check(FirstName, 1, 100, "Имя", errors); Check(LastName, 1, 100, "Фамилия", errors);
        Check(Login, 3, 100, "Логин", errors);
        if (Original is not null || DisplayName.Length != 0) Check(DisplayName, 1, 300, "Отображаемое имя", errors);
        if (Nullable(WorkEmail) is { } email && (email.Length > 320 || !MailAddress.TryCreate(email, out _)))
            errors.Add("Рабочий email: укажите корректный адрес длиной до 320 символов.");
        if (JobTitle.Length > 200) errors.Add("Должность: не более 200 символов.");
        return errors;
    }
    private static void Check(string value, int min, int max, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < min || value.Length > max)
            errors.Add($"{label}: требуется от {min} до {max} символов.");
    }
    public DesktopUserCreate Create() => new(FirstName, LastName, Login,
        DisplayName.Length == 0 ? null : DisplayName, Nullable(WorkEmail), null, Nullable(JobTitle));
    public DesktopUserPatch Patch()
    {
        var user = Original ?? throw new InvalidOperationException("A new account has no patch baseline.");
        return new()
        {
            FirstName = Changed(FirstName, user.FirstName),
            LastName = Changed(LastName, user.LastName),
            DisplayName = Changed(DisplayName, user.DisplayName),
            Login = Changed(Login, user.Login),
            WorkEmail = ChangedNullable(Nullable(WorkEmail), user.WorkEmail),
            JobTitle = ChangedNullable(Nullable(JobTitle), user.JobTitle),
        };
    }
    private static OptionalUserField<string> Changed(string? current, string? original) => new(current != original, current);
    private static OptionalUserField<string?> ChangedNullable(string? current, string? original) => new(current != original, current);
    public bool HasChanges => Original is null || Patch() is var p && (p.FirstName.IsSpecified || p.LastName.IsSpecified
        || p.DisplayName.IsSpecified || p.Login.IsSpecified || p.WorkEmail.IsSpecified || p.JobTitle.IsSpecified);
}
