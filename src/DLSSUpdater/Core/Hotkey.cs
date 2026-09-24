using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace DLSSUpdater.Core;

/// <summary>A virtual-key binding with optional modifiers, in the two ini dialects we write.</summary>
public readonly record struct Hotkey(int Vk, bool Ctrl = false, bool Shift = false, bool Alt = false)
{
    public static readonly Hotkey None = new(0);
    public bool IsNone => Vk <= 0;

    /// <summary>OptiScaler: "0x2e", "-1" disables.</summary>
    public string ToOpti() => IsNone ? "-1" : "0x" + Vk.ToString("x2");

    /// <summary>ReShade: "vk,ctrl,shift,alt" in decimal, "0,0,0,0" disables.</summary>
    public string ToReShade() => $"{Math.Max(Vk, 0)},{(Ctrl ? 1 : 0)},{(Shift ? 1 : 0)},{(Alt ? 1 : 0)}";

    public static Hotkey? ParseOpti(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase)) return null;
        var v = value.Trim();
        if (v == "-1") return None;
        var ok = v.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(v[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var vk)
            : int.TryParse(v, out vk);
        return ok ? new Hotkey(vk) : null;
    }

    public static Hotkey? ParseReShade(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var p = value.Split(',');
        if (!int.TryParse(p[0], out var vk)) return null;
        bool Flag(int i) => p.Length > i && p[i].Trim() == "1";
        return new Hotkey(vk, Flag(1), Flag(2), Flag(3));
    }

    public string Display()
    {
        if (IsNone) return "Off";
        var sb = new StringBuilder();
        if (Ctrl) sb.Append("Ctrl + ");
        if (Shift) sb.Append("Shift + ");
        if (Alt) sb.Append("Alt + ");
        sb.Append(KeyName(Vk));
        return sb.ToString();
    }

    private static readonly Dictionary<int, string> Names = new()
    {
        [0x08] = "Backspace", [0x09] = "Tab", [0x0D] = "Enter", [0x13] = "Pause", [0x14] = "Caps Lock", [0x1B] = "Esc",
        [0x20] = "Space", [0x21] = "Page Up", [0x22] = "Page Down", [0x23] = "End", [0x24] = "Home",
        [0x25] = "Left", [0x26] = "Up", [0x27] = "Right", [0x28] = "Down", [0x2C] = "Print Screen",
        [0x2D] = "Insert", [0x2E] = "Delete", [0x90] = "Num Lock", [0x91] = "Scroll Lock",
        [0x6A] = "Num *", [0x6B] = "Num +", [0x6D] = "Num -", [0x6E] = "Num .", [0x6F] = "Num /",
    };

    /// <summary>Human name; OEM keys go through the active keyboard layout so "\" shows as it is printed.</summary>
    public static string KeyName(int vk)
    {
        if (Names.TryGetValue(vk, out var n)) return n;
        if (vk is >= 0x70 and <= 0x87) return "F" + (vk - 0x6F);
        if (vk is >= 0x60 and <= 0x69) return "Num " + (vk - 0x60);
        if (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A) return ((char)vk).ToString();
        var scan = MapVirtualKey((uint)vk, 0);
        if (scan != 0)
        {
            var sb = new StringBuilder(32);
            if (GetKeyNameText((int)(scan << 16), sb, sb.Capacity) > 0) return sb.ToString();
        }
        return "0x" + vk.ToString("X2");
    }

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameText(int lParam, StringBuilder name, int size);
}
