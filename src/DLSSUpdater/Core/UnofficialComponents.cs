namespace DLSSUpdater.Core;

/// <summary>
/// The DLSSNR runtime (nvngx_dlssnr.dll, supplied by the user) and MFG Unlock (a third-party add-on that patches NVIDIA
/// frame generation) are not official NVIDIA software. They are opt-in: nothing installs or updates them until the
/// user turns them on in Settings, and turning them off never removes what a game already has (Uninstall does).
/// </summary>
public static class UnofficialComponents
{
    /// <summary>Settings saved before this version had both components on by default.</summary>
    public const int SettingsVersion = 2;

    public const string Title = "Unofficial components";

    public const string Warning =
        "The DLSSNR runtime (nvngx_dlssnr.dll) and MFG Unlock are not made or supported by NVIDIA, and DLSS Updater " +
        "doesn't provide them. The DLSSNR file is one you supply yourself; MFG Unlock is a third-party add-on that " +
        "patches NVIDIA frame generation.\n\n" +
        "• The files come from unofficial sources.\n" +
        "• They may break or crash games, and may trip anti-cheat and get an account banned.\n" +
        "• You are responsible for using them.";

    /// <summary>
    /// One-time upgrade of settings from 1.5.x and earlier. Anyone who already has either component keeps it on, so
    /// nothing stops working; everyone else starts with it off. Both get a notice explaining the change once.
    /// </summary>
    /// <param name="alreadyHas">An imported nvngx_dlssnr.dll / MFG Unlock add-on, or a downloaded MFG Unlock release.</param>
    /// <returns>True when <paramref name="s"/> changed and should be saved.</returns>
    public static bool Migrate(AppSettings s, Func<bool> alreadyHas)
    {
        if (s.SettingsVersion >= SettingsVersion) return false;
        s.AllowUnofficial = alreadyHas();
        s.UnofficialNoticePending = true;
        s.SettingsVersion = SettingsVersion;
        return true;
    }

    /// <summary>Whether this PC already has either component from an earlier version.</summary>
    public static bool PresentOnDisk() =>
        File.Exists(Path.Combine(AppPaths.Components, ComponentStore.DlssNrFile))
        || File.Exists(Path.Combine(AppPaths.Components, ComponentStore.MfgFile))
        || ComponentStore.AnyCached(Component.MfgUnlock);

    /// <summary>The one-time notice after an upgrade; <paramref name="kept"/> is whether they stayed on.</summary>
    public static string Notice(bool kept) =>
        "The DLSSNR runtime and MFG Unlock are no longer installed by default, because they come from unofficial sources, " +
        "may break games or trip anti-cheat, and are used at your own risk.\n\n" +
        (kept
            ? "You already use them, so they stay on and nothing changed in your games. To stop installing and updating them, " +
              "turn off Settings → General → Unofficial components."
            : "They are now off. Nothing was removed from your games. Turn them on under Settings → General → " +
              "Unofficial components if you want them.") +
        "\n\nFiles already in a game stay there until you uninstall that game's components.";
}
