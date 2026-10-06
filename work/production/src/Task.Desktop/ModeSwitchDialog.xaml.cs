using System.Windows;
using Task.Desktop.Modes;

namespace Task.Desktop;

public partial class ModeSwitchDialog : Window
{
    private ModeSwitchDecision _decision = ModeSwitchDecision.Cancel;
    private ModeSwitchDialog(IReadOnlyList<string> editors, bool canSave)
    {
        InitializeComponent();
        EditorsText.Text = string.Join(Environment.NewLine, editors);
        SaveButton.Visibility = canSave ? Visibility.Visible : Visibility.Collapsed;
        SaveNotice.Text = canSave
            ? "Сохранить формы в текущем пространстве, отбросить их или отменить переключение? Данные не переносятся между режимами."
            : "Сохранение всех форм сейчас недоступно. Можно отбросить черновики или отменить переключение. Данные не переносятся между режимами.";
    }
    internal static ModeSwitchDecision Choose(Window owner, IReadOnlyList<string> editors, bool canSave)
    {
        var dialog = new ModeSwitchDialog(editors, canSave) { Owner = owner };
        dialog.ShowDialog();
        return dialog._decision;
    }
    private void OnSave(object sender, RoutedEventArgs e) { _decision = ModeSwitchDecision.Save; DialogResult = true; }
    private void OnDiscard(object sender, RoutedEventArgs e) { _decision = ModeSwitchDecision.Discard; DialogResult = true; }
}
