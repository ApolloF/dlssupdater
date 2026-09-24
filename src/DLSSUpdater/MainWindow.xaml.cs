using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        Loaded += async (_, _) =>
        {
            await _vm.InitializeAsync();
            if (Environment.GetEnvironmentVariable("DLSSU_SNAPSHOT") is { Length: > 0 } shot) await Snapshot(shot);
        };
        StateChanged += (_, _) => UpdateMaximized();
        PreviewKeyDown += OnPreviewKeyDown;
        SourceInitialized += (_, _) => RoundCorners();
        ((INotifyCollectionChanged)_vm.LogLines).CollectionChanged += (_, _) =>
        {
            if (LogList.Items.Count > 0) LogList.ScrollIntoView(LogList.Items[^1]);
        };
    }

    /// <summary>While a keybind button is waiting, the next key press becomes the binding.</summary>
    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_vm.SettingsVm.Capturing is null) return;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        var vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        var mods = System.Windows.Input.Keyboard.Modifiers;
        e.Handled = _vm.SettingsVm.HandleKey(vk,
            mods.HasFlag(System.Windows.Input.ModifierKeys.Control),
            mods.HasFlag(System.Windows.Input.ModifierKeys.Shift),
            mods.HasFlag(System.Windows.Input.ModifierKeys.Alt));
    }

    private void UpdateMaximized()
    {
        // A chrome-less maximized window overhangs the screen by the resize border.
        RootGrid.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Dev aid: DLSSU_SNAPSHOT=file.png renders the window to disk (DLSSU_SNAPSHOT_STEPS=settings,log,game:N).</summary>
    private async Task Snapshot(string path)
    {
        var steps = (Environment.GetEnvironmentVariable("DLSSU_SNAPSHOT_STEPS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var step in steps)
        {
            if (step == "settings") _vm.SettingsOpen = true;
            else if (step.StartsWith("tab:")) _vm.SettingsVm.Tab = step[4..];
            else if (step == "about") _vm.AboutOpen = true;
            else if (step == "log") _vm.LogOpen = true;
            else if (step.StartsWith("game:") && int.TryParse(step[5..], out var i))
                _vm.SelectedGame = _vm.GamesView.Cast<GameViewModel>().ElementAtOrDefault(i);
        }
        await Task.Delay(600);
        var dpi = VisualTreeHelper.GetDpi(this);
        var bmp = new RenderTargetBitmap((int)(RootGrid.ActualWidth * dpi.DpiScaleX), (int)(RootGrid.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bmp.Render(RootGrid);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        await using (var fs = File.Create(path)) enc.Save(fs);
        if (Environment.GetEnvironmentVariable("DLSSU_SNAPSHOT_EXIT") == "1") Close();
    }

    private void RoundCorners()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var round = 2; // DWMWCP_ROUND
        _ = DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int)); // DWMWA_WINDOW_CORNER_PREFERENCE (Win11 only)
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
