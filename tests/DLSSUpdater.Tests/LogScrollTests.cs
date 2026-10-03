using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DLSSUpdater.ViewModels;
using DLSSUpdater.Views;

namespace DLSSUpdater.Tests;

/// <summary>Regression tests for the startup crash loop fixed in b5f78ef (log ListBox scrolled inside CollectionChanged).</summary>
public class LogScrollTests
{
    [Fact]
    public void AppendLogLine_KeepsNewestLines()
    {
        var lines = new List<string>();
        for (var i = 0; i < MainViewModel.MaxLogLines + 25; i++) MainViewModel.AppendLogLine(lines, $"line {i}");

        Assert.Equal(MainViewModel.MaxLogLines, lines.Count);
        Assert.Equal("line 25", lines[0]);
        Assert.Equal($"line {MainViewModel.MaxLogLines + 24}", lines[^1]);
    }

    [Fact]
    public void FollowNewest_ScrollsAfterTheChange_NotDuringIt()
    {
        RunOnSta(() =>
        {
            var lines = new ObservableCollection<string>();
            var (window, list) = ShowLogList(lines);
            ListAutoScroll.FollowNewest(list, lines);
            for (var i = 0; i < 50; i++) lines.Add($"line {i}");
            Pump();
            var viewer = FindScrollViewer(list);
            var before = viewer.VerticalOffset;
            Assert.True(before > 0);

            var posted = new List<DispatcherPriority>();
            list.Dispatcher.Hooks.OperationPosted += (_, e) => posted.Add(e.Operation.Priority);
            lines.Add("newest");
            Assert.Equal(before, viewer.VerticalOffset);
            Assert.Contains(DispatcherPriority.Background, posted);

            Pump();
            Assert.True(viewer.VerticalOffset > before);
            window.Close();
        });
    }

    [Fact]
    public void FollowNewest_SurvivesAStartupBurstAtTheCap()
    {
        RunOnSta(() =>
        {
            var lines = new ObservableCollection<string>();
            var (window, list) = ShowLogList(lines);
            ListAutoScroll.FollowNewest(list, lines);

            // Startup logs a burst of lines; once the cap is hit every Add is followed by a RemoveAt(0).
            for (var i = 0; i < MainViewModel.MaxLogLines + 100; i++)
            {
                MainViewModel.AppendLogLine(lines, $"line {i}");
                if (i % 50 == 0) Pump();
            }
            Pump();

            var last = (ListBoxItem?)list.ItemContainerGenerator.ContainerFromItem(lines[^1]);
            Assert.NotNull(last);
            Assert.True(last.IsVisible);
            window.Close();
        });
    }

    private static (Window, ListBox) ShowLogList(ObservableCollection<string> lines)
    {
        var list = new ListBox { ItemsSource = lines, Height = 120 };
        VirtualizingPanel.SetIsVirtualizing(list, true);
        var window = new Window
        {
            Content = list, Width = 300, Height = 160, ShowInTaskbar = false, ShowActivated = false,
            WindowStyle = WindowStyle.None, Left = -10000, Top = -10000,
        };
        window.Show();
        Pump();
        return (window, list);
    }

    private static ScrollViewer FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer sv) return sv;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (FindScrollViewerOrNull(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        throw new InvalidOperationException("ListBox has no ScrollViewer");
    }

    private static ScrollViewer? FindScrollViewerOrNull(DependencyObject root)
    {
        try { return FindScrollViewer(root); }
        catch (InvalidOperationException) { return null; }
    }

    /// <summary>Runs every queued dispatcher operation, down to Background priority.</summary>
    private static void Pump() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void RunOnSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception ex) { failure = ex; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new Xunit.Sdk.XunitException($"Failed on the UI thread: {failure}");
    }
}
