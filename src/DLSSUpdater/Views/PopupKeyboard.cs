using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace DLSSUpdater.Views;

/// <summary>Keyboard access for a popover: focus moves into it when it opens, Escape closes it and returns focus to its owner.</summary>
public static class PopupKeyboard
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(PopupKeyboard), new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(Popup popup) => (bool)popup.GetValue(EnabledProperty);
    public static void SetEnabled(Popup popup, bool value) => popup.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Popup popup) return;
        if (e.NewValue is true)
        {
            popup.Opened += OnOpened;
            popup.PreviewKeyDown += OnPreviewKeyDown;
        }
        else
        {
            popup.Opened -= OnOpened;
            popup.PreviewKeyDown -= OnPreviewKeyDown;
        }
    }

    private static void OnOpened(object? sender, EventArgs e)
    {
        if (sender is not Popup { Child: { } child }) return;
        // After the popup's window is shown, otherwise there is nothing to focus yet.
        child.Dispatcher.BeginInvoke(() => child.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)), DispatcherPriority.Input);
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || sender is not Popup popup) return;
        popup.IsOpen = false;
        (popup.TemplatedParent as IInputElement ?? popup.PlacementTarget)?.Focus();
        e.Handled = true;
    }
}
