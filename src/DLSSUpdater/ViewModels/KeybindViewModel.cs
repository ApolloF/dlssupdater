using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

public sealed record KeybindDef(string Label, bool ReShade, string Section, string Key, int DefaultVk)
{
    public static readonly KeybindDef[] Opti =
    [
        new("OptiScaler menu", false, "Menu", "ShortcutKey", 0x2D),
        new("FPS overlay", false, "Menu", "FpsShortcutKey", 0x21),
        new("FPS overlay type", false, "Menu", "FpsCycleShortcutKey", 0x22),
        new("Frame generation on / off", false, "Menu", "FGShortcutKey", 0x23),
        new("DLSSNR on / off", false, "DlssNr", "ToggleKey", 0),
    ];

    public static readonly KeybindDef[] ReShadeKeys =
    [
        new("ReShade overlay", true, "INPUT", "KeyOverlay", 0x24),
        new("Effects on / off", true, "INPUT", "KeyEffects", 0),
        new("Screenshot", true, "INPUT", "KeyScreenshot", 0x2C),
        new("Reload effects", true, "INPUT", "KeyReload", 0),
    ];
}

/// <summary>One hotkey row in settings; reads and writes the matching entry of an ini override list.</summary>
public sealed partial class KeybindViewModel(KeybindDef def, ObservableCollection<IniOverride> list) : ObservableObject
{
    public KeybindDef Def { get; } = def;
    public string Label => Def.Label;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ButtonText))]
    private bool _capturing;

    private IniOverride? Entry => list.FirstOrDefault(o =>
        o.Section.Equals(Def.Section, StringComparison.OrdinalIgnoreCase) && o.Key.Equals(Def.Key, StringComparison.OrdinalIgnoreCase));

    public Hotkey? Current => Entry is { } e ? (Def.ReShade ? Hotkey.ParseReShade(e.Value) : Hotkey.ParseOpti(e.Value)) : null;

    public bool IsDefault => Current is null;

    public string ButtonText => Capturing ? "Press a key…"
        : Current is { } k ? k.Display()
        : Def.DefaultVk > 0 ? $"Default · {Hotkey.KeyName(Def.DefaultVk)}" : "Default";

    public void Set(Hotkey? key)
    {
        var e = Entry;
        if (key is null)
        {
            if (e is not null) list.Remove(e);
        }
        else
        {
            var value = Def.ReShade ? key.Value.ToReShade() : key.Value.ToOpti();
            if (e is null) list.Add(new IniOverride(Def.Section, Def.Key, value));
            else
            {
                // Replace the object so the overrides grid refreshes too.
                list[list.IndexOf(e)] = new IniOverride(Def.Section, Def.Key, value);
            }
        }
        Capturing = false;
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(ButtonText));
        OnPropertyChanged(nameof(IsDefault));
    }
}
