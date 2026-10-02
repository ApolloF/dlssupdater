using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

/// <summary>"What's new" behind a component chip: a short summary of the latest release and a link to its notes.</summary>
public sealed partial class ReleaseNewsViewModel(Component component, string name, AppSettings settings) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Available))]
    private string? _tag;

    [ObservableProperty] private string? _meta;
    [ObservableProperty] private string? _url;
    [ObservableProperty] private ReleaseSummary _summary = ReleaseSummary.Empty;
    /// <summary>Released after the last one the user looked at; shown as a dot on the chip.</summary>
    [ObservableProperty] private bool _isNew;
    [ObservableProperty] private bool _isOpen;

    public bool Available => Tag is not null;

    public void Load(ReleaseInfo? r)
    {
        var previous = Tag;
        Tag = r?.Tag;
        if (r is null)
        {
            IsOpen = IsNew = false;
            return;
        }

        Url = ReleaseNotes.Url(component, r.Tag);
        Summary = ReleaseNotes.Summarize(r.Notes, r.Tag);
        Meta = string.Join(" · ", new[]
        {
            r.Prerelease ? "Pre-release" : null,
            r.Published?.ToLocalTime().ToString("d MMM yyyy", CultureInfo.CurrentCulture),
        }.OfType<string>());

        var seen = settings.SeenReleases.Count;
        IsNew = ReleaseNotes.IsUnseen(settings.SeenReleases, component, r.Tag);
        if (settings.SeenReleases.Count != seen) settings.Save();
        if (IsNew && previous != r.Tag)
        {
            var what = Summary.Headline ?? Summary.Points.FirstOrDefault();
            Log.Info($"New {Name} {r.Tag}{(what is null ? "" : ": " + what)} (click its version for what's new)");
        }
    }

    /// <summary>Closing the popup counts as having read it.</summary>
    partial void OnIsOpenChanged(bool value)
    {
        if (value || !IsNew || Tag is null) return;
        ReleaseNotes.MarkSeen(settings.SeenReleases, component, Tag);
        settings.Save();
        IsNew = false;
    }

    [RelayCommand]
    private void OpenNotes()
    {
        if (Url is not null) Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
        IsOpen = false;
    }
}
