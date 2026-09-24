using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// One driver setting. Choice values are "ID=VALUE;ID=VALUE" in hex. Only <see cref="ReadIds"/> decide which choice
/// is shown; other ids in a choice are companions written alongside (e.g. the DLSS override switch the NVIDIA App
/// also sets with a preset). The default choice (Value null) removes ReadIds so the driver / global value applies.
/// </summary>
public sealed partial class NvOptionViewModel(string label, string hint, string topic, uint[] readIds, OptionChoice[] choices) : ObservableObject
{
    public string Label { get; } = label;
    public string Hint { get; } = hint;
    public string Topic { get; } = topic;
    public uint[] ReadIds { get; } = readIds;
    public OptionChoice[] Choices { get; } = choices;
    public OptionChoice? Loaded { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty), nameof(DisplayChoices))]
    private OptionChoice? _selected;

    public bool IsDirty => Selected is not null && !Equals(Selected, Loaded);

    public IReadOnlyList<OptionChoice> DisplayChoices =>
        Selected is null || Choices.Contains(Selected) ? Choices : [.. Choices, Selected];

    public static IEnumerable<(uint Id, uint Value)> Pairs(string? value) =>
        (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('='))
            .Select(p => (uint.Parse(p[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                          uint.Parse(p[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture)));

    public void SetLoaded(IReadOnlyDictionary<uint, uint?> values)
    {
        OptionChoice match;
        if (ReadIds.All(id => values.GetValueOrDefault(id) is null)) match = Choices[0];
        else
        {
            match = Choices.Skip(1).FirstOrDefault(c => Pairs(c.Value).Where(p => ReadIds.Contains(p.Id)).All(p => values.GetValueOrDefault(p.Id) == p.Value))
                    ?? new OptionChoice("Custom (" + string.Join(", ", ReadIds.Select(id => values.GetValueOrDefault(id) is { } v ? $"0x{v:X}" : "—")) + ")",
                        string.Join(";", ReadIds.Where(id => values.GetValueOrDefault(id) is not null).Select(id => $"{id:X}={values[id]:X}")),
                        "Set by the NVIDIA App or Profile Inspector.");
        }
        Loaded = match;
        Selected = match;
    }
}

/// <summary>Driver profile editor for the global (base) profile or one game's profile.</summary>
public sealed partial class DriverProfileViewModel : ObservableObject
{
    private readonly string? _exePath;
    private readonly string? _gameName;

    public DriverProfileViewModel(string? exePath = null, string? gameName = null)
    {
        _exePath = exePath;
        _gameName = gameName;
        Options = new(NvSettings.Create(global: exePath is null));
        foreach (var o in Options) o.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NvOptionViewModel.Selected)) OnPropertyChanged(nameof(IsDirty));
        };
    }

    public ObservableCollection<NvOptionViewModel> Options { get; }
    public bool IsGlobal => _exePath is null;
    public bool Supported => NvDrs.Available;
    public bool IsDirty => Options.Any(o => o.IsDirty);

    [ObservableProperty] private string _status = "Not loaded";
    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private bool _busy;

    [RelayCommand]
    public async Task Load()
    {
        if (!NvDrs.Available)
        {
            Status = "No NVIDIA driver found.";
            return;
        }
        Busy = true;
        try
        {
            var (status, values) = await Task.Run(() =>
            {
                using var drs = NvDrs.Open();
                IntPtr? profile = _exePath is null ? drs.BaseProfile() : drs.FindProfileForExe(_exePath);
                var ids = Options.SelectMany(o => o.ReadIds).Distinct();
                var vals = ids.ToDictionary(id => id, id => profile is { } p ? drs.Get(p, id) : null);
                var s = _exePath is null ? "Global profile · applies to every game without its own value"
                    : profile is { } gp ? $"Driver profile: {drs.ProfileName(gp)}"
                    : $"No driver profile for {Path.GetFileName(_exePath)} yet · created when you apply";
                return (s, vals);
            });
            foreach (var o in Options) o.SetLoaded(values);
            Status = status;
            Loaded = true;
        }
        catch (Exception ex) when (ex is NvApiException or InvalidOperationException or EntryPointNotFoundException)
        {
            Status = ex.Message;
            Log.Error("Reading NVIDIA profile failed", ex);
        }
        finally
        {
            Busy = false;
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    [RelayCommand]
    private async Task Apply()
    {
        var changed = Options.Where(o => o.IsDirty).ToList();
        if (changed.Count == 0) return;
        Busy = true;
        try
        {
            await Task.Run(() =>
            {
                using var drs = NvDrs.Open();
                var profile = _exePath is null ? drs.BaseProfile() : drs.GetOrCreateProfileForExe(_exePath, _gameName ?? Path.GetFileNameWithoutExtension(_exePath));
                foreach (var o in changed)
                {
                    if (o.Selected!.Value is null)
                        foreach (var id in o.ReadIds) drs.Delete(profile, id);
                    else
                        foreach (var (id, value) in NvOptionViewModel.Pairs(o.Selected.Value)) drs.Set(profile, id, value);
                }
                drs.Save();
            });
            Log.Info($"NVIDIA {(IsGlobal ? "global" : _gameName)} profile: {string.Join(", ", changed.Select(o => $"{o.Label} = {o.Selected!.Label}"))}");
            Busy = false;
            await Load();
        }
        catch (NvApiException ex) when (ex.NeedsAdmin)
        {
            Busy = false;
            if (Views.Dialog.Confirm("Administrator rights needed", "The NVIDIA driver only saves profile changes for administrators.", "Restart as admin"))
                App.RestartElevated();
        }
        catch (Exception ex) when (ex is NvApiException or InvalidOperationException)
        {
            Busy = false;
            Log.Error("Writing NVIDIA profile failed", ex);
            Views.Dialog.Show("NVIDIA profile", ex.Message);
        }
    }

    [RelayCommand]
    private void Revert()
    {
        foreach (var o in Options) o.Selected = o.Loaded;
    }
}

/// <summary>The driver settings exposed in the NVIDIA tabs (ids from nvapi's NvApiDriverSettings.h and Profile Inspector).</summary>
public static class NvSettings
{
    private const string SrOverride = "10E41E01=1";
    private const string RrOverride = "10E41E02=1";
    private const string FgOverride = "10E41E03=1";

    private static OptionChoice C(string label, string? value, string description) => new(label, value, description);

    private static OptionChoice Preset(char letter, string family, string ov, string id) =>
        C(letter.ToString(), $"{id}={letter - 'A' + 1:X};{ov}", family);

    public static NvOptionViewModel[] Create(bool global)
    {
        var inherit = global ? "Driver default" : "Use global";
        var inheritDesc = global ? "No override; games use their own setting." : "No per-game value; the global profile applies.";

        return
        [
            new("DLSS Super Resolution preset", "Force a DLSS SR model preset.", "nv-sr-preset", [0x10E41DF3],
            [
                C(inherit, null, inheritDesc),
                C("Off", "10E41DF3=0", "Explicitly no preset override."),
                C("Latest", $"10E41DF3=FFFFFF;{SrOverride}", "NVIDIA's recommended preset for the installed DLSS version."),
                Preset('K', "Transformer gen 1 (DLSS 4). The usual pick for Quality, Balanced and DLAA.", SrOverride, "10E41DF3"),
                Preset('J', "Transformer gen 1, earlier variant of K.", SrOverride, "10E41DF3"),
                Preset('L', "Transformer gen 2 (DLSS 4.5). NVIDIA's pick for Ultra Performance.", SrOverride, "10E41DF3"),
                Preset('M', "Transformer gen 2 (DLSS 4.5). NVIDIA's pick for Performance.", SrOverride, "10E41DF3"),
                Preset('E', "CNN (DLSS 3). Cheapest; softer in motion. Newer DLSS builds may ignore CNN presets.", SrOverride, "10E41DF3"),
                Preset('F', "CNN (DLSS 3), meant for Ultra Performance / DLAA.", SrOverride, "10E41DF3"),
            ]),
            new("DLSS render resolution", "Override the game's DLSS quality mode.", "nv-sr-mode", [0x10AFB768, 0x10E41DF5],
            [
                C(inherit, null, inheritDesc),
                C("Game setting", "10AFB768=3", "Explicitly let the game choose (what the NVIDIA App writes for 'Default')."),
                C("DLAA", $"10AFB768=4;{SrOverride}", "Native resolution, DLSS only as anti-aliasing. Highest cost."),
                C("Quality (67%)", $"10AFB768=2;{SrOverride}", "Renders at 67% per axis."),
                C("Balanced (58%)", $"10AFB768=1;{SrOverride}", "Renders at 58% per axis."),
                C("Performance (50%)", $"10AFB768=0;{SrOverride}", "Renders at 50% per axis. Good with transformer presets at 4K."),
                C("Ultra performance (33%)", $"10AFB768=5;{SrOverride}", "Renders at 33% per axis; for 8K or very weak GPUs."),
                C("Custom 77%", $"10AFB768=6;10E41DF5=4D;{SrOverride}", "Between Quality and DLAA."),
                C("Custom 85%", $"10AFB768=6;10E41DF5=55;{SrOverride}", "Close to native."),
            ]),
            new("DLSS Ray Reconstruction preset", "Force a DLSS RR model preset.", "nv-rr-preset", [0x10E41DF7],
            [
                C(inherit, null, inheritDesc),
                C("Off", "10E41DF7=0", "Explicitly no preset override."),
                C("Latest", $"10E41DF7=FFFFFF;{RrOverride}", "NVIDIA's recommended RR preset."),
                Preset('D', "Transformer (gen 1).", RrOverride, "10E41DF7"),
                Preset('E', "Transformer (gen 1), later revision.", RrOverride, "10E41DF7"),
                Preset('F', "Transformer (gen 2). Newest.", RrOverride, "10E41DF7"),
                Preset('C', "CNN. Cheapest, noisier.", RrOverride, "10E41DF7"),
            ]),
            new("DLSS Frame Generation preset", "Force a DLSS FG model preset.", "nv-fg-preset", [0x10E41DF1],
            [
                C(inherit, null, inheritDesc),
                C("Off", "10E41DF1=0", "Explicitly no preset override."),
                C("Latest", $"10E41DF1=FFFFFF;{FgOverride}", "Newest FG model the driver has."),
                C("NVIDIA default", $"10E41DF1=FFFFFE;{FgOverride}", "The driver's default FG model, as the NVIDIA App's 'Default'."),
                Preset('A', "FG model A (letters depend on the driver version).", FgOverride, "10E41DF1"),
                Preset('B', "FG model B.", FgOverride, "10E41DF1"),
                Preset('C', "FG model C.", FgOverride, "10E41DF1"),
            ]),
            new("Multi frame generation", "Override the frame generation multiplier (RTX 50).", "nv-mfg", [0x104D6667],
            [
                C(inherit, null, inheritDesc),
                C("Game setting", "104D6667=0", "Explicitly use the game's own multiplier."),
                C("2x", $"104D6667=1;{FgOverride}", "One generated frame per rendered frame."),
                C("3x", $"104D6667=2;{FgOverride}", "Two generated frames."),
                C("4x", $"104D6667=3;{FgOverride}", "Three generated frames."),
                C("5x", $"104D6667=4;{FgOverride}", "Four generated frames (newer drivers)."),
                C("6x", $"104D6667=5;{FgOverride}", "Five generated frames (newer drivers)."),
            ]),
            new("Smooth Motion", "Driver frame generation for games without DLSS FG.", "nv-smooth-motion", [0xB0D384C0],
            [
                C(inherit, null, inheritDesc),
                C("Off", "B0D384C0=0", "Off."),
                C("On", "B0D384C0=1", "Driver interpolates frames (RTX 40 / 50, driver 571.86+)."),
            ]),
            new("RTX HDR", "AI SDR-to-HDR conversion by the driver.", "nv-rtx-hdr", [0x00DD48FB],
            [
                C(inherit, null, inheritDesc),
                C("Off", "DD48FB=0", "Off."),
                C("On", "DD48FB=1", "Converts SDR games to HDR. Needs Windows HDR on; costs some FPS."),
            ]),
            new("RTX Dynamic Vibrance", "AI colour and saturation boost for SDR.", "nv-vibrance", [0x00980880],
            [
                C(inherit, null, inheritDesc),
                C("Off", "980880=0", "Off."),
                C("On", "980880=1", "Adds vibrance / clarity to SDR output."),
            ]),
            new("Max frame rate", "Driver FPS limiter.", "nv-fps", [0x10835002],
            [
                C(inherit, null, inheritDesc),
                C("Off", "10835002=0", "No limit."),
                .. new[] { 60, 90, 117, 120, 138, 141, 144, 157, 162, 165, 225, 237, 240 }
                    .Select(f => C($"{f} FPS", $"10835002={f:X}", f is 117 or 138 or 141 or 157 or 162 or 225 or 237
                        ? "A few FPS under a common refresh rate: keeps G-Sync / VRR active." : $"Cap at {f} FPS.")),
            ]),
            new("Vertical sync", "Driver VSync override.", "nv-vsync", [0x00A879CF],
            [
                C(inherit, null, inheritDesc),
                C("Use the app setting", "A879CF=60925292", "The game decides."),
                C("Off", "A879CF=8416747", "Force off. Lowest latency, tearing without VRR."),
                C("On", "A879CF=47814940", "Force on. Recommended with G-Sync + an FPS cap."),
                C("Fast", "A879CF=18888888", "Fast Sync: no tearing above refresh without VSync's latency."),
            ]),
            new("Power management", "GPU clock behaviour.", "nv-power", [0x1057EB71],
            [
                C(inherit, null, inheritDesc),
                C("Normal", "1057EB71=5", "Optimal power: clocks drop when idle."),
                C("Adaptive", "1057EB71=0", "Clocks follow load."),
                C("Prefer max performance", "1057EB71=1", "Holds high clocks; fewer stutters from clock changes, more power."),
            ]),
        ];
    }
}
