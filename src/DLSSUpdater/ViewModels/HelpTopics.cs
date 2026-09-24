namespace DLSSUpdater.ViewModels;

public sealed record HelpTopic(string Id, string Group, string Title, string Body);

/// <summary>A guide row: the topic and, for dropdowns, what each choice does.</summary>
public sealed record GuideEntry(HelpTopic Topic, IReadOnlyList<OptionChoice> Choices)
{
    public bool HasChoices => Choices.Count > 0;
}

/// <summary>Text behind the ⓘ buttons; the About page lists all of them as the guide.</summary>
public static class HelpTopics
{
    public static readonly HelpTopic[] All =
    [
        // ---------- General ----------
        new("proxy", "General", "Proxy DLL name",
            "The file name OptiScaler is saved as so the game loads it. dxgi.dll works for most DX12 games. " +
            "Pick another (winmm.dll, version.dll, dbghelp.dll) when the game or another mod already needs dxgi.dll, " +
            "or when a guide says so (e.g. Cyberpunk uses dbghelp.dll). Existing OptiScaler installs keep their name."),
        new("dlss-version", "General", "DLSS version",
            "Latest only ever upgrades: a game DLL that is already newer is left alone. " +
            "A pinned version is installed exactly, also downgrading, useful when a newer DLSS preset looks worse in a game. " +
            "Older SDKs lack files (before 310.4 no FG, 3.x only SR); missing ones are skipped. A game can pin its own version above its DLSS file list."),
        new("prereleases", "General", "Include prereleases",
            "The OptiScaler-NR fork only publishes prereleases, so turning this off usually means no OptiScaler updates. Applies to every source."),
        new("streamline", "General", "Replace Streamline files",
            "Streamline (sl.*.dll) is NVIDIA's layer between the game and DLSS Frame Generation. " +
            "Dynamic MFG only works with FG 310.9.1 plus the complete Streamline 2.14.1 set. This replaces every sl.*.dll the game already ships " +
            "with the signed files from one SDK release, never mixing versions and never adding plugins. Restore DLSS puts the originals back. " +
            "Off by default: an unusual game setup can break frame generation until restored."),
        new("add-missing", "General", "Add nvngx_dlss.dll when missing",
            "Games without DLSS (FSR/XeSS only) have no nvngx_dlss.dll. OptiScaler needs it to use DLSS as the upscaler, so it is placed next to the exe."),
        new("ini-mode", "General", "Existing game configs",
            "What happens when a game already has an OptiScaler.ini or ReShade.ini. The app always remembers which values it wrote itself, " +
            "and new keys from a new OptiScaler release are always added. Whatever is replaced is backed up first and Uninstall restores it."),
        new("local-components", "General", "Local components",
            "nvngx_dlssnr.dll (DLSS 5 neural rendering model, ShortFuse compat build for RTX 20–40) and ReShade64.dll (add-on build) can't be downloaded " +
            "automatically, so they are imported once and copied into each game. Import a newer file here and Update all rolls it out."),

        // ---------- Keybinds ----------
        new("keybinds", "Keybinds", "Keybinds",
            "OptiScaler keys are single keys (no modifiers); ReShade keys may include Ctrl / Shift / Alt. " +
            "Default leaves the game's own default, Off disables the key. Changes apply on the next install or update; " +
            "a key you already changed in-game keeps your in-game choice."),

        // ---------- ReShade ----------
        new("skip-tutorial", "ReShade", "Skip the tutorial",
            "Marks ReShade's first-run guide as finished (TutorialProgress=4), so the overlay opens straight to the effect list."),
        new("performance-mode", "ReShade", "Performance mode",
            "ReShade compiles effects with their current values baked in as constants. Gives more FPS, but effect settings can't be tweaked in the " +
            "overlay while it's on. Tweak with it off, then turn it on for playing. Only affects ReShade effects, not OptiScaler or DLSS."),
        new("show-fps", "ReShade", "Show FPS",
            "ReShade's own FPS counter in a screen corner. OptiScaler has a separate, more detailed overlay on its own key."),
        new("load-early", "ReShade", "Load MFG Unlock early",
            "Adds the add-on to ReShade's LoadFromDllMain list so it loads while ReShade itself starts. Some games (e.g. Cyberpunk) start Streamline before " +
            "ReShade normally loads add-ons; without this, MFG Unlock can't apply its runtime settings there. Leave off unless a game needs it."),

        // ---------- DLSSNR ----------
        new("nr-enabled", "DLSSNR", "Neural rendering",
            "DLSS 5 neural rendering: NVIDIA's model synthesises detail and lighting on top of the upscaled image, before frame generation sees it. " +
            "Needs nvngx_dlssnr.dll and an NVIDIA driver with NGX feature 18. Toggle in-game with the DLSSNR key. Undocumented feature, so results vary per game."),
        new("nr-before-sr", "DLSSNR", "Run before upscaling",
            "On: the model edits the lower-resolution input before DLSS SR / RR, which is cheaper and the upscaler cleans up its output. " +
            "Off: it runs on the upscaled image, sharper but costlier. Before RR it sees noisy ray-traced colour, so test per game."),
        new("nr-finished", "DLSSNR", "Apply to finished picture",
            "Applies NR after the game's post-processing (bloom, grading) instead of at the upscaler. DX12 and the D3D11 bridge only. " +
            "Can also alter the HUD and menus. With Run before upscaling on, the edit is made early and carried to the final image."),
        new("nr-passes", "DLSSNR", "Passes",
            "Number of model layers per frame, each feeding the next. Costs roughly 1x / 2x / 3x the model time. The result is composed once, " +
            "so colours don't compound, but 2 and 3 are deliberately over-processed looks. Multipass is D3D12 only."),
        new("nr-scale", "DLSSNR", "Model resolution",
            "Fraction of the image the model works at, relative to its stage (before SR: render resolution; after: output). " +
            "Cost falls with the square: 50% is about a quarter. Above 100% supersamples and averages down."),
        new("nr-style", "DLSSNR", "Style",
            "Built-in model profiles inside the same NVIDIA DLL. Changing it rebuilds the model feature, which causes a short hitch in-game."),
        new("nr-strengths", "DLSSNR", "Model strengths",
            "Intensity scales everything. Local structure adds fine detail and texture. Local tone changes local contrast and lighting. " +
            "Skin structure sets detail on faces separately (follow = same as local structure). Values are undocumented; 1.0 is the model's default."),
        new("nr-colour", "DLSSNR", "Colour strength",
            "Whether the model's colour arrives with its light. 0 keeps the game's hues exactly and lets only brightness change; 1 takes the model's colour. " +
            "Low values keep the game's art direction."),

        // ---------- Tone mapping ----------
        new("tm-mode", "Tone mapping", "Tone mapping",
            "The model expects a display-like image. For linear / HDR frames the pass maps the frame into that range, lets the model work, and maps it back " +
            "(a reversible tone map). The curve decides how highlights are compressed; composed modes blend the model's answer against the original " +
            "luminance, replace modes use the model's picture directly. Frames the game already tone-mapped are passed through untouched."),
        new("tm-white", "Tone mapping", "White point source",
            "Which linear value counts as display white for that mapping. Games' exposure differs hugely (measured means from 0.065 to 185 in one game), " +
            "so a wrong white point makes the model see a too dark or blown-out picture."),
        new("tm-paper", "Tone mapping", "Paper white",
            "Multiplies the white point before the model sees the frame. Below 1 treats the scene as brighter (more highlight headroom, darker midtones for the model); " +
            "above 1 lifts dark scenes. Changes the result, not only what the model sees."),
        new("tm-highlight", "Tone mapping", "Highlight protection",
            "For automatic HDR exposure: 0–100 how much bright areas are protected from being pushed toward white. Higher keeps lamps and sky intact but darkens overall."),
        new("tm-maxratio", "Tone mapping", "Max brightening",
            "The most the pass may brighten any pixel, as a multiple. Darkening is never capped. Guards against bright lights turning into coloured blocks."),
        new("tm-transfer", "Tone mapping", "Transfer strength",
            "How far the frame moves toward the model's picture: 0 is exactly the upscaler's image, 1 the model's, above 1 exaggerates."),
        new("tm-hdrtransfer", "Tone mapping", "HDR brightness transfer",
            "Experimental. Measures the HDR brightness transfer when the edit is generated before SR and applied to the finished picture. " +
            "HDR10 / scRGB with scene-linear input only; adds a clean-frame copy and a small analysis pass."),

        // ---------- HDR output ----------
        new("hdr-force", "HDR output", "Force HDR color space",
            "OptiScaler makes the game's swapchain HDR even if the game only outputs SDR. Useful with RenoDX-style HDR mods; otherwise leave off."),
        new("hdr-10", "HDR output", "Use HDR10",
            "When forcing HDR, use the 10-bit HDR10 format instead of 16-bit float scRGB. Less bandwidth; scRGB keeps more precision."),
        new("hdr-skip", "HDR output", "Skip color space",
            "Don't set the HDR color space; for games or mods that set it themselves and get wrong colours when OptiScaler does too."),

        // ---------- MFG Unlock ----------
        new("force-multiplier", "MFG Unlock", "Force frame multiplier",
            "Game setting uses whatever multiplier the game's menu selects. 2x–6x forces it, for games that only offer an FG on/off switch. " +
            "Ignored while Dynamic MFG is on."),
        new("max-count", "MFG Unlock", "Max frame count",
            "The highest multiplier reported to the game (DLSS-G MultiFrameCountMax). Games build their FG menu from it, so 4 shows up to 4x."),
        new("dynamic-mfg", "MFG Unlock", "Dynamic MFG",
            "Lets NVIDIA's runtime pick the multiplier every frame to hold a target frame rate. Needs D3D12, nvngx_dlssg.dll 310.9.1, the matching " +
            "Streamline 2.14.1 set (see Replace Streamline files), driver 595.41+, and the NVIDIA App's DLSS Frame Generation override set to " +
            "'Use the 3D application setting'. Cold-restart the game. Takes priority over the forced multiplier."),
        new("dynamic-target", "MFG Unlock", "Dynamic target FPS",
            "The frame rate Dynamic MFG aims for. Refresh rate follows the display. Ignored while VSync is on."),
        new("runtime-selection", "MFG Unlock", "Runtime selection",
            "NVIDIA can deliver DLSS FG over the air and use that instead of the game's files. Game default keeps the game's policy. " +
            "Prefer local files turns OTA off so the DLLs installed here are really used; recommended with pinned DLSS or the Streamline swap. " +
            "Force NVIDIA OTA does the opposite. Needs a game restart. If the add-on log shows a path under ProgramData\\NVIDIA\\NGX\\models, the OTA copy is active."),
        new("hdr-compat", "MFG Unlock", "HDR compatibility",
            "Workarounds for HDR games where frame generation breaks the UI or colors. Native changes nothing. UI Composition handles the HUD separately. " +
            "Auto guard + UI adds automatic detection on top. Final color fallback is the most invasive, for games the others don't fix. Try in that order."),
    ];

    private static readonly Dictionary<string, HelpTopic> ById = All.ToDictionary(t => t.Id);

    public static HelpTopic? Get(string? id) => id is not null && ById.TryGetValue(id, out var t) ? t : null;
    public static string Tip(string? id) => Get(id)?.Body ?? "";
}
