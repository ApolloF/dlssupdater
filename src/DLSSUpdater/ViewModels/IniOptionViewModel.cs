using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

/// <summary>A labelled value; Value null means "leave the key alone" (no override).</summary>
public sealed record OptionChoice(string Label, string? Value)
{
    public override string ToString() => Label;
}

/// <summary>
/// A friendly editor for one ini key, backed by an override list. Toggles use Choices[0] = off, Choices[1] = on.
/// </summary>
public sealed partial class IniOptionViewModel : ObservableObject
{
    private readonly ObservableCollection<IniOverride> _list;

    public IniOptionViewModel(string label, string hint, string section, string key, ObservableCollection<IniOverride> list, params OptionChoice[] choices)
    {
        Label = label;
        Hint = hint;
        Section = section;
        Key = key;
        _list = list;
        Choices = choices;
    }

    public static IniOptionViewModel Toggle(string label, string hint, string section, string key, string on, ObservableCollection<IniOverride> list) =>
        new(label, hint, section, key, list, new OptionChoice("Off", null), new OptionChoice("On", on));

    /// <summary>HelpTopics id behind the ⓘ button.</summary>
    public string? Topic { get; private set; }
    public string Tip => HelpTopics.Tip(Topic);

    public IniOptionViewModel About(string topic)
    {
        Topic = topic;
        return this;
    }

    public string Label { get; }
    public string Hint { get; }
    public string Section { get; }
    public string Key { get; }
    public OptionChoice[] Choices { get; }
    public bool IsToggle => Choices.Length == 2 && Choices[0].Value is null && Choices[1].Label == "On";

    private IniOverride? Entry => _list.FirstOrDefault(o =>
        o.Section.Equals(Section, StringComparison.OrdinalIgnoreCase) && o.Key.Equals(Key, StringComparison.OrdinalIgnoreCase));

    public OptionChoice Selected
    {
        get
        {
            var v = Entry?.Value;
            return Choices.FirstOrDefault(c => string.Equals(c.Value, v, StringComparison.OrdinalIgnoreCase))
                   ?? (v is null ? Choices[0] : new OptionChoice(v, v));
        }
        set
        {
            var e = Entry;
            if (value?.Value is null)
            {
                if (e is not null) _list.Remove(e);
            }
            else if (e is null) _list.Add(new IniOverride(Section, Key, value.Value));
            else _list[_list.IndexOf(e)] = new IniOverride(Section, Key, value.Value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsOn));
        }
    }

    public bool IsOn
    {
        get => Selected.Value is not null;
        set => Selected = value ? Choices[^1] : Choices[0];
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Selected));
        OnPropertyChanged(nameof(IsOn));
    }
}
