using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

/// <summary>
/// One driver setting of a game profile. Choice values are "ID=VALUE;ID=VALUE" (hex) and are written as a block,
/// the way the NVIDIA App writes a preset together with its override switch. The first choice (Value null)
/// removes every id in <see cref="Ids"/> from the game profile so the global / NVIDIA default applies again.
/// </summary>
public sealed partial class NvOptionViewModel(string label, string hint, string topic, uint[] ids, OptionChoice[] choices) : ObservableObject
{
    public string Label { get; } = label;
    public string Hint { get; } = hint;
    public string Topic { get; } = topic;
    public uint[] Ids { get; } = ids;
    public OptionChoice[] Choices { get; } = choices;
    public OptionChoice? Loaded { get; private set; }

    /// <summary>Stable list for the dropdown (rebuilt only on load, so the ComboBox never resets the selection).</summary>
    public ObservableCollection<OptionChoice> DisplayChoices { get; } = new(choices);

    private OptionChoice? _selected;

    public OptionChoice? Selected
    {
        get => _selected;
        set
        {
            if (value is null || Equals(value, _selected)) return; // ComboBox pushes null while its items change
            _selected = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsDirty));
        }
    }

    public bool IsDirty => Selected is not null && Loaded is not null && !Equals(Selected, Loaded);

    public static IEnumerable<(uint Id, uint Value)> Pairs(string? value) =>
        (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('='))
            .Select(p => (uint.Parse(p[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                          uint.Parse(p[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture)));

    private OptionChoice? Match(Func<uint, uint?> value) =>
        Choices.Skip(1).FirstOrDefault(c => Pairs(c.Value).All(p => value(p.Id) == p.Value));

    /// <summary>
    /// <paramref name="user"/>: values set in this game's profile. <paramref name="fallback"/>: what applies without them
    /// (NVIDIA's shipped value for this game, else the global profile). <paramref name="gameDefault"/>: fallback comes from NVIDIA's game profile.
    /// </summary>
    public void SetLoaded(IReadOnlyDictionary<uint, uint?> user, IReadOnlyDictionary<uint, uint?> fallback, bool gameDefault)
    {
        string Describe(Func<uint, uint?> v) =>
            Match(v)?.Label
            ?? (Ids.All(id => v(id) is null) ? "not set" : string.Join(", ", Ids.Where(id => v(id) is not null).Select(id => $"0x{v(id):X}")));

        var inheritLabel = $"{(gameDefault ? "Game default" : "Use global")} ({Describe(id => fallback.GetValueOrDefault(id))})";
        var inherit = new OptionChoice(inheritLabel, null, gameDefault
            ? "No value of your own: NVIDIA's shipped setting for this game applies."
            : "No per-game value: the global driver profile applies.");

        OptionChoice current;
        if (Ids.All(id => user.GetValueOrDefault(id) is null)) current = inherit;
        else
        {
            uint? Effective(uint id) => user.GetValueOrDefault(id) ?? fallback.GetValueOrDefault(id);
            current = Match(Effective)
                      ?? new OptionChoice($"Custom ({Describe(Effective)})",
                          string.Join(";", Ids.Where(id => user.GetValueOrDefault(id) is not null).Select(id => $"{id:X}={user[id]:X}")),
                          "Set by the NVIDIA App or Profile Inspector; left as is unless you pick another value.");
        }

        DisplayChoices.Clear();
        DisplayChoices.Add(inherit);
        foreach (var c in Choices.Skip(1)) DisplayChoices.Add(c);
        if (!DisplayChoices.Contains(current)) DisplayChoices.Add(current);

        Loaded = current;
        _selected = current;
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(IsDirty));
    }
}

/// <summary>Editor for one game's NVIDIA driver profile. The global profile is only read, never written.</summary>
public sealed partial class DriverProfileViewModel : ObservableObject
{
    private readonly string _exePath;
    private readonly string _gameName;

    public DriverProfileViewModel(string exePath, string gameName)
    {
        _exePath = exePath;
        _gameName = gameName;
        Options = new(NvSettings.Create());
        foreach (var o in Options) o.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NvOptionViewModel.IsDirty)) OnPropertyChanged(nameof(IsDirty));
        };
    }

    public ObservableCollection<NvOptionViewModel> Options { get; }
    public bool IsDirty => Options.Any(o => o.IsDirty);

    [ObservableProperty] private string _status = "Not loaded";
    [ObservableProperty] private bool _loaded;
    [ObservableProperty] private bool _busy;

    private sealed record Snapshot(string Status, Dictionary<uint, uint?> User, Dictionary<uint, uint?> Fallback, HashSet<uint> GameDefaults);

    private Snapshot ReadAll()
    {
        using var drs = NvDrs.Open();
        var baseProfile = drs.BaseProfile();
        var profile = drs.FindProfileForExe(_exePath);
        var ids = Options.SelectMany(o => o.Ids).Distinct().ToList();
        var user = new Dictionary<uint, uint?>();
        var fallback = new Dictionary<uint, uint?>();
        var gameDefaults = new HashSet<uint>();
        foreach (var id in ids)
        {
            var global = drs.Read(baseProfile, id)?.Value;
            var own = profile is { } p ? drs.Read(p, id) : null;
            if (own is { IsUserValue: true } u) user[id] = u.Value;
            if (own is { Location: 0, Predefined: true } pre)
            {
                fallback[id] = pre.Value;
                gameDefaults.Add(id);
            }
            else fallback[id] = global;
        }
        var status = profile is { } gp
            ? $"Driver profile: {drs.ProfileName(gp)}"
            : $"No driver profile for {Path.GetFileName(_exePath)} yet · one is created when you apply";
        return new Snapshot(status, user, fallback, gameDefaults);
    }

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
            var snap = await Task.Run(ReadAll);
            foreach (var o in Options) o.SetLoaded(snap.User, snap.Fallback, o.Ids.Any(snap.GameDefaults.Contains));
            Status = snap.Status;
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

    /// <summary>
    /// Writes only the changed options to this game's profile in one session and saves once, so a failure
    /// leaves the driver untouched. Afterwards every written id is read back and compared.
    /// </summary>
    [RelayCommand]
    private async Task Apply()
    {
        var changed = Options.Where(o => o.IsDirty).ToList();
        if (changed.Count == 0) return;
        var expected = new Dictionary<uint, uint?>();
        foreach (var o in changed)
        {
            if (o.Selected!.Value is null) foreach (var id in o.Ids) expected[id] = null;
            else foreach (var (id, v) in NvOptionViewModel.Pairs(o.Selected.Value)) expected[id] = v;
        }

        Busy = true;
        try
        {
            await Task.Run(() =>
            {
                using var drs = NvDrs.Open();
                var profile = drs.GetOrCreateProfileForExe(_exePath, _gameName);
                foreach (var (id, value) in expected)
                {
                    if (value is { } v) drs.Set(profile, id, v);
                    else drs.Delete(profile, id);
                }
                drs.Save();
            });

            var after = await Task.Run(ReadAll);
            var wrong = expected.Where(e => after.User.GetValueOrDefault(e.Key) != e.Value).ToList();
            Log.Info($"NVIDIA profile {_gameName}: {string.Join(", ", changed.Select(o => $"{o.Label} = {o.Selected!.Label}"))}");
            if (wrong.Count > 0)
            {
                var detail = string.Join(", ", wrong.Select(w => $"0x{w.Key:X8} expected {(w.Value is { } v ? $"0x{v:X}" : "unset")}, got {(after.User.GetValueOrDefault(w.Key) is { } g ? $"0x{g:X}" : "unset")}"));
                Log.Error($"NVIDIA profile {_gameName}: verification mismatch: {detail}");
                Views.Dialog.Show("NVIDIA profile", "The driver didn't keep every value:\n" + detail);
            }
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
            Views.Dialog.Show("NVIDIA profile", ex.Message + "\nNothing was saved.");
        }
    }

    [RelayCommand]
    private void Revert()
    {
        foreach (var o in Options)
            if (o.Loaded is not null) o.Selected = o.Loaded;
    }
}

/// <summary>
/// Driver settings in the per-game NVIDIA tab. Ids are from NVIDIA's NvApiDriverSettings.h (Smooth Motion, RTX HDR and
/// Vibrance from Profile Inspector's CustomSettingNames.xml). Preset choices write the preset and its override switch
/// together, matching what the NVIDIA App stores per game.
/// </summary>
public static class NvSettings
{
    private const uint SrPreset = 0x10E41DF3, RrPreset = 0x10E41DF7, FgPreset = 0x10E41DF1;
    private const uint SrOverride = 0x10E41E01, RrOverride = 0x10E41E02, FgOverride = 0x10E41E03;
    private const uint SrMode = 0x10AFB768, SrRatio = 0x10E41DF5, MfgCount = 0x104D6667;

    private static OptionChoice C(string label, string? value, string description) => new(label, value, description);
    private static string W(uint id, uint value) => $"{id:X}={value:X}";
    private static string W(uint id, uint value, uint id2, uint value2) => $"{W(id, value)};{W(id2, value2)}";

    private static OptionChoice Preset(uint presetId, uint overrideId, char letter, string description) =>
        C(letter.ToString(), W(presetId, (uint)(letter - 'A' + 1), overrideId, 1), description);

    private static OptionChoice Inherit => C("Use global", null, "No per-game value.");

    public static NvOptionViewModel[] Create() =>
    [
        new("DLSS Super Resolution preset", "Force a DLSS SR model preset (with the SR override switch, as the NVIDIA App does).", "nv-sr-preset", [SrPreset, SrOverride],
        [
            Inherit,
            C("Off", W(SrPreset, 0, SrOverride, 0), "No SR override for this game, even if the global profile has one."),
            C("Latest", W(SrPreset, 0xFFFFFF, SrOverride, 1), "NVIDIA's recommended preset for the driver's DLSS version."),
            Preset(SrPreset, SrOverride, 'K', "Transformer gen 1 (DLSS 4). The usual pick for Quality, Balanced and DLAA."),
            Preset(SrPreset, SrOverride, 'J', "Transformer gen 1, earlier variant of K."),
            Preset(SrPreset, SrOverride, 'L', "Transformer gen 2 (DLSS 4.5). NVIDIA's pick for Ultra Performance."),
            Preset(SrPreset, SrOverride, 'M', "Transformer gen 2 (DLSS 4.5). NVIDIA's pick for Performance."),
            Preset(SrPreset, SrOverride, 'E', "CNN (DLSS 3). Cheapest; newer DLSS builds may ignore CNN presets."),
            Preset(SrPreset, SrOverride, 'F', "CNN (DLSS 3), meant for Ultra Performance / DLAA."),
        ]),
        new("DLSS render resolution", "Override the game's DLSS quality mode.", "nv-sr-mode", [SrMode, SrRatio],
        [
            Inherit,
            C("Game setting", W(SrMode, 3), "Explicitly let the game choose (what the NVIDIA App stores by default)."),
            C("DLAA", W(SrMode, 4), "Native resolution, DLSS only as anti-aliasing. Highest cost."),
            C("Quality (67%)", W(SrMode, 2), "Renders at 67% per axis."),
            C("Balanced (58%)", W(SrMode, 1), "Renders at 58% per axis."),
            C("Performance (50%)", W(SrMode, 0), "Renders at 50% per axis."),
            C("Ultra performance (33%)", W(SrMode, 5), "Renders at 33% per axis."),
            C("Custom 77%", W(SrMode, 6, SrRatio, 77), "Between Quality and DLAA."),
            C("Custom 85%", W(SrMode, 6, SrRatio, 85), "Close to native."),
        ]),
        new("DLSS Ray Reconstruction preset", "Force a DLSS RR model preset (with the RR override switch).", "nv-rr-preset", [RrPreset, RrOverride],
        [
            Inherit,
            C("Off", W(RrPreset, 0, RrOverride, 0), "No RR override for this game."),
            C("Latest", W(RrPreset, 0xFFFFFF, RrOverride, 1), "NVIDIA's recommended RR preset."),
            Preset(RrPreset, RrOverride, 'D', "Transformer gen 1."),
            Preset(RrPreset, RrOverride, 'E', "Transformer gen 1, later revision."),
            Preset(RrPreset, RrOverride, 'F', "Transformer gen 2. Newest."),
        ]),
        new("DLSS Frame Generation preset", "Force a DLSS FG model (with the FG override switch).", "nv-fg-preset", [FgPreset, FgOverride],
        [
            Inherit,
            C("Off", W(FgPreset, 0, FgOverride, 0), "No FG override for this game."),
            C("NVIDIA default", W(FgPreset, 0xFFFFFE, FgOverride, 1), "The driver's default FG model (the NVIDIA App's 'Default')."),
            Preset(FgPreset, FgOverride, 'A', "FG model preset A."),
            Preset(FgPreset, FgOverride, 'B', "FG model preset B."),
        ]),
        new("Multi frame generation", "Override the frame generation multiplier (native 3x+ needs RTX 50).", "nv-mfg", [MfgCount],
        [
            Inherit,
            C("Game setting", W(MfgCount, 0), "Explicitly use the game's own multiplier."),
            C("2x", W(MfgCount, 1), "One generated frame per rendered frame."),
            C("3x", W(MfgCount, 2), "Two generated frames."),
            C("4x", W(MfgCount, 3), "Three generated frames."),
        ]),
        new("Smooth Motion", "Driver frame generation for games without DLSS FG.", "nv-smooth-motion", [0xB0D384C0],
        [
            Inherit,
            C("Off", "B0D384C0=0", "Off for this game."),
            C("On", "B0D384C0=1", "Driver interpolates frames (RTX 40 / 50, driver 571.86+)."),
        ]),
        new("RTX HDR", "AI SDR-to-HDR conversion by the driver.", "nv-rtx-hdr", [0x00DD48FB],
        [
            Inherit,
            C("Off", "DD48FB=0", "Off for this game."),
            C("On", "DD48FB=1", "Converts SDR to HDR. Needs Windows HDR on; costs some FPS."),
        ]),
        new("RTX Dynamic Vibrance", "AI colour and saturation boost for SDR.", "nv-vibrance", [0x00980880],
        [
            Inherit,
            C("Off", "980880=0", "Off for this game."),
            C("On", "980880=1", "Adds vibrance / clarity to SDR output."),
        ]),
        new("Max frame rate", "Driver FPS limiter.", "nv-fps", [0x10835002],
        [
            Inherit,
            C("Off", "10835002=0", "No limit for this game."),
            .. new[] { 30, 60, 90, 117, 120, 138, 141, 144, 157, 162, 165, 225, 237, 240 }
                .Select(f => C($"{f} FPS", $"10835002={f:X}", f is 117 or 138 or 141 or 157 or 162 or 225 or 237
                    ? "A few FPS under a common refresh rate: keeps G-Sync / VRR active." : $"Cap at {f} FPS.")),
        ]),
        new("Vertical sync", "Driver VSync override.", "nv-vsync", [0x00A879CF],
        [
            Inherit,
            C("Use the app setting", "A879CF=60925292", "The game decides."),
            C("Off", "A879CF=8416747", "Force off. Lowest latency, tearing without VRR."),
            C("On", "A879CF=47814940", "Force on. Recommended with G-Sync + an FPS cap."),
            C("Fast", "A879CF=18888888", "Fast Sync: no tearing above refresh without VSync's latency."),
        ]),
        new("Power management", "GPU clock behaviour.", "nv-power", [0x1057EB71],
        [
            Inherit,
            C("Optimal power", "1057EB71=5", "Clocks drop when the frame is unchanged."),
            C("Adaptive", "1057EB71=0", "Clocks follow load."),
            C("Prefer max performance", "1057EB71=1", "Holds high clocks; fewer stutters from clock changes, more power."),
        ]),
    ];
}
