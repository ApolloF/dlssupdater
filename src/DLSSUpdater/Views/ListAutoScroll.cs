using System.Collections.Specialized;
using System.Windows.Controls;
using System.Windows.Threading;

namespace DLSSUpdater.Views;

/// <summary>Keeps a ListBox scrolled to its newest item.</summary>
public static class ListAutoScroll
{
    /// <summary>
    /// Deferred: scrolling inside the change notification forces item generation before the ListBox has processed
    /// the change, which threw at startup when the log filled up quickly.
    /// </summary>
    public static void FollowNewest(ListBox list, INotifyCollectionChanged items) =>
        items.CollectionChanged += (_, _) => list.Dispatcher.BeginInvoke(() =>
        {
            if (list.Items.Count > 0) list.ScrollIntoView(list.Items[^1]);
        }, DispatcherPriority.Background);
}
