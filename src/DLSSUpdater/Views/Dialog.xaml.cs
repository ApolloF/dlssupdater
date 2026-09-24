using System.Windows;
using System.Windows.Input;

namespace DLSSUpdater.Views;

public partial class Dialog : Window
{
    private Dialog(string title, string message, string ok, string? cancel, bool danger)
    {
        InitializeComponent();
        if (Application.Current.MainWindow is { IsLoaded: true } main && main != this) Owner = main;
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = ok;
        if (cancel is null) CancelButton.Visibility = Visibility.Collapsed;
        else CancelButton.Content = cancel;
        if (danger)
        {
            Mark.Background = (System.Windows.Media.Brush)FindResource("Danger");
            OkButton.Style = (Style)FindResource("BtnDanger");
        }
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }

    public static bool Confirm(string title, string message, string ok, bool danger = false) =>
        new Dialog(title, message, ok, "Cancel", danger).ShowDialog() == true;

    public static void Show(string title, string message) =>
        new Dialog(title, message, "OK", null, false).ShowDialog();

    /// <summary>Asks for a line of text; null when cancelled or left empty.</summary>
    public static string? Prompt(string title, string message, string initial, string ok = "Save")
    {
        var d = new Dialog(title, message, ok, "Cancel", false);
        d.InputBox.Visibility = Visibility.Visible;
        d.InputBox.Text = initial;
        d.Loaded += (_, _) => { d.InputBox.Focus(); d.InputBox.SelectAll(); };
        return d.ShowDialog() == true && !string.IsNullOrWhiteSpace(d.InputBox.Text) ? d.InputBox.Text.Trim() : null;
    }

    private void OnOk(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}
