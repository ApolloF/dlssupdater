namespace DLSSUpdater.ViewModels;

public sealed record HelpTopic(string Id, string Group, string Title, string Body);

/// <summary>A guide row: the topic and, for dropdowns, what each choice does.</summary>
public sealed record GuideEntry(HelpTopic Topic, IReadOnlyList<OptionChoice> Choices)
{
    public bool HasChoices => Choices.Count > 0;
    public string? Advice => HelpTopics.AdviceFor(Topic.Id);
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

        // ---------- NVIDIA driver ----------
        new("nv-sr-preset", "NVIDIA", "DLSS Super Resolution preset",
            "Tells the driver which DLSS SR model preset to use for this game, whatever the game asks for (like the NVIDIA App's DLSS Override - Model Presets). " +
            "The preset and the SR override switch are written together, as the NVIDIA App stores them; Off writes both as off; Use global removes both. " +
            "OptiScaler has its own preset override (RenderPresetOverride in the profile); if both are set, OptiScaler's wins in games it handles."),
        new("nv-sr-mode", "NVIDIA", "DLSS render resolution",
            "Forces the internal render resolution DLSS upscales from, independent of the game's quality menu. Custom uses an exact percentage per axis."),
        new("nv-rr-preset", "NVIDIA", "DLSS Ray Reconstruction preset",
            "Model preset for Ray Reconstruction (the DLSS denoiser in path-traced games). Only matters in games that use RR."),
        new("nv-fg-preset", "NVIDIA", "DLSS Frame Generation preset",
            "Which DLSS FG model the driver loads, together with the FG override switch. NVIDIA default is what the NVIDIA App calls 'Default'; A and B are the presets drivers currently offer."),
        new("nv-mfg", "NVIDIA", "Multi frame generation",
            "Driver-side multiplier override for DLSS FG (the NVIDIA App's 'Multi Frame Generation' override). Native 3x/4x needs an RTX 50 card; " +
            "on RTX 40 use MFG Unlock instead."),
        new("nv-smooth-motion", "NVIDIA", "Smooth Motion",
            "Driver-level frame generation that works in games without DLSS FG (DX11, DX12, Vulkan). Needs RTX 40/50 and driver 571.86+. " +
            "Don't combine with in-game FG."),
        new("nv-rtx-hdr", "NVIDIA", "RTX HDR",
            "AI conversion of SDR games to HDR. Needs Windows HDR on. If it doesn't kick in, enable RTX HDR once in the NVIDIA App so its global flags exist. " +
            "Doesn't work together with DLDSR or some overlays."),
        new("nv-vibrance", "NVIDIA", "RTX Dynamic Vibrance",
            "AI saturation / clarity boost for SDR output. The SDR counterpart of RTX HDR; not used when RTX HDR is on."),
        new("nv-fps", "NVIDIA", "Max frame rate",
            "Driver FPS limiter. Values a few frames under refresh keep G-Sync active; with frame generation the cap applies to the output frame rate."),
        new("nv-vsync", "NVIDIA", "Vertical sync",
            "Driver VSync override. With G-Sync / VRR the usual setup is VSync On here, in-game VSync off, and an FPS cap a few frames under refresh."),
        new("nv-power", "NVIDIA", "Power management",
            "How the GPU manages clocks. Prefer max performance keeps clocks high, avoiding clock-change stutter in light games at the cost of power."),

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

    /// <summary>"Which should I pick" guidance shown in the guide under each topic.</summary>
    private static readonly Dictionary<string, string> Advice = new()
    {
        ["proxy"] = "Start with dxgi.dll. If the game doesn't show the OptiScaler overlay or crashes at start, try winmm.dll, then version.dll. " +
                    "If the game already has a dxgi.dll from another mod (ReShade, Special K), OptiScaler takes over that name and loads ReShade itself, so that is fine.",
        ["dlss-version"] = "Keep Latest globally. Pin a game only when a new DLSS build looks worse there (ghosting, shimmer), or for Dynamic MFG pin exactly 310.9.1 " +
                           "together with the Streamline swap.",
        ["prereleases"] = "Leave on: the OptiScaler-NR fork publishes nothing else.",
        ["streamline"] = "Only turn on for games where you want Dynamic MFG and the game ships an older Streamline (the DLSS files list shows the SL version). " +
                         "If FG disappears or the game crashes afterwards, use Restore DLSS on that game.",
        ["add-missing"] = "Leave on. It only adds a file to games that have none, and Uninstall removes it again.",
        ["ini-mode"] = "Keep game settings for everyday use: updates never undo tuning you did in-game. Apply app settings once after you changed something here and want " +
                       "it everywhere, then switch back. Fresh config when a game's ini got messy or a new OptiScaler release changed a lot.",
        ["local-components"] = "After downloading a new nvngx_dlssnr.dll or ReShade build, import it here and press Update all.",
        ["keybinds"] = "Pick keys the game doesn't use: Delete, Insert, End, Page Up/Down and \\ are usually free. Avoid Home if you use ReShade's default overlay key.",
        ["skip-tutorial"] = "On, unless you are new to ReShade.",
        ["performance-mode"] = "Off while you set up effects, on for playing. With only add-ons (RenoDX, MFG Unlock) and no effects it makes no difference.",
        ["show-fps"] = "Off if you use OptiScaler's or the NVIDIA overlay; on for a quick counter.",
        ["load-early"] = "Off by default. Turn on only if MFG Unlock's panel says the game initialised Streamline before the add-on loaded (e.g. Cyberpunk).",
        ["force-multiplier"] = "Game setting whenever the game has its own MFG menu. Force 3x or 4x for games that only have an FG on/off switch. " +
                               "Above 4x rarely looks good below 60 base FPS.",
        ["max-count"] = "Default. Raise only if you want to force 5x/6x in a game that builds its menu from this value.",
        ["dynamic-mfg"] = "Try it for high-refresh displays when the base frame rate varies a lot. Keep off when you prefer a fixed multiplier or it isn't accepted " +
                          "(the add-on log says why).",
        ["dynamic-target"] = "Refresh rate for G-Sync / VRR displays. A fixed value slightly below refresh (e.g. 138 for 144 Hz) if you cap FPS anyway.",
        ["runtime-selection"] = "Prefer local files when you use a pinned DLSS version or the Streamline swap, otherwise NVIDIA may silently replace them. Game default otherwise.",
        ["hdr-compat"] = "Native until you see HUD smearing or wrong colours with FG in HDR, then UI Composition, then Auto guard + UI, then Final color fallback.",
        ["nr-enabled"] = "On if nvngx_dlssnr.dll is installed; toggle it in-game with the DLSSNR key to compare.",
        ["nr-before-sr"] = "On (cheaper, cleaner). Turn off if a game shows smeared detail or odd colours with RR, and compare.",
        ["nr-finished"] = "Off. Try on for games whose post-processing (bloom, grading) fights the model, and accept that the HUD may change.",
        ["nr-passes"] = "1. 2 only for a deliberately stronger look with headroom to spare; 3 is mostly for experiments.",
        ["nr-scale"] = "100% at 1440p output. 75% at 4K or on RTX 40 / lower cards to keep the cost down; 50% if FPS drops too much.",
        ["nr-style"] = "Standard. Natural if the result looks over-processed, Cinematic for a graded look.",
        ["nr-strengths"] = "Start at the app defaults (local structure 0.7, local tone 0.25, skin 0.5): detail without changing the game's lighting too much. " +
                           "Lower local tone first if the image looks re-lit; lower skin structure if faces look aged.",
        ["nr-colour"] = "Low (0.2) keeps the game's art direction. Raise toward 1 only if you like the model's colour grading.",
        ["tm-mode"] = "SDR games: leave it, it doesn't apply. HDR games: Hybrid composed as the safe default; Neutwo composed if highlights look dull; " +
                      "replace modes only if composed looks too weak.",
        ["tm-white"] = "Auto HDR exposure for HDR games; Game exposure if the game reports exposure to the upscaler reliably (fewer brightness jumps); Manual as fallback.",
        ["tm-paper"] = "1.0. Lower (0.75) if bright skies or lights lose detail; raise (1.5) only for very dark games.",
        ["tm-highlight"] = "0 to 25. Raise if lamps and the sky get blown out after NR.",
        ["tm-maxratio"] = "2x. Lower to 1.5x if bright lights break into coloured blocks.",
        ["tm-transfer"] = "1.0. 0.75 for a subtler effect without changing the model's strengths.",
        ["tm-hdrtransfer"] = "Off unless you use Run before upscaling + Apply to finished picture in an HDR game and brightness looks wrong.",
        ["hdr-force"] = "Off. Only with an HDR mod that expects OptiScaler to make the swapchain HDR.",
        ["hdr-10"] = "Off (scRGB, more precision). On if your capture tool or display chain works better with HDR10.",
        ["nv-sr-preset"] = "Use global for most games. Per game: K for Quality / DLAA, M for Performance, L for Ultra Performance, Off to stop any driver override there.",
        ["nv-sr-mode"] = "Use the game's setting. DLAA if you have headroom; Performance with an L/M preset at 4K looks close to Quality with older presets.",
        ["nv-rr-preset"] = "Latest. Try D/E if the newest preset shows smearing in a specific game.",
        ["nv-fg-preset"] = "Use global or NVIDIA default. Try A or B only when a game shows FG artifacts that a different model fixes.",
        ["nv-mfg"] = "Leave it to the game unless it only offers 2x; RTX 40 owners use MFG Unlock in the ReShade & MFG tab instead.",
        ["nv-smooth-motion"] = "Per game, for titles without DLSS FG and a base frame rate above ~50 FPS.",
        ["nv-rtx-hdr"] = "Per game for SDR-only titles on an HDR monitor. Off for games with native HDR or a RenoDX HDR mod.",
        ["nv-vibrance"] = "Taste. Per game for dull-looking SDR games; off when using RTX HDR.",
        ["nv-fps"] = "Use global (set a cap 3–4 FPS under your refresh in the NVIDIA App, e.g. 237 for 240 Hz). Per game for a lower cap, e.g. 117 for a game that can't hold 140+.",
        ["nv-vsync"] = "Use global. Per game Off only where you want the lowest latency and accept tearing.",
        ["nv-power"] = "Use global. Prefer max performance for a game that stutters when the GPU clocks down.",
        ["hdr-skip"] = "Off. On only if colours look wrong while another HDR mod is active.",
    };

    public static string? AdviceFor(string id) => Advice.GetValueOrDefault(id);

    private static readonly Dictionary<string, HelpTopic> ById = All.ToDictionary(t => t.Id);

    public static HelpTopic? Get(string? id) => id is not null && ById.TryGetValue(id, out var t) ? t : null;
    public static string Tip(string? id) => Get(id)?.Body ?? "";
}
