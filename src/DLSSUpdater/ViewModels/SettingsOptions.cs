using System.Collections.ObjectModel;
using System.Globalization;
using DLSSUpdater.Core;

namespace DLSSUpdater.ViewModels;

/// <summary>Every friendly option in Settings: which ini key it edits, its choices and what each choice does.</summary>
public static class SettingsOptions
{
    /// <summary>OptiScaler writes floats with six decimals; matching that keeps "value we wrote" detection exact.</summary>
    private static string F(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);

    private static OptionChoice C(string label, string? value, string description) => new(label, value, description);

    public static IniOptionViewModel[] ReShade(ObservableCollection<IniOverride> rs) =>
    [
        IniOptionViewModel.Toggle("Skip the tutorial", "Start without ReShade's first-run guide.", "OVERLAY", "TutorialProgress", "4", rs).About("skip-tutorial"),
        IniOptionViewModel.Toggle("Performance mode", "More FPS; effect settings can't be tweaked while on.", "GENERAL", "PerformanceMode", "1", rs).About("performance-mode"),
        IniOptionViewModel.Toggle("Show FPS", "ReShade's own FPS counter.", "OVERLAY", "ShowFPS", "1", rs).About("show-fps"),
        IniOptionViewModel.Toggle("Load MFG Unlock early", "Needed by games that start Streamline before ReShade loads add-ons (e.g. Cyberpunk).",
            "ADDON", "LoadFromDllMain", ComponentStore.MfgFile, rs).About("load-early"),
    ];

    public static IniOptionViewModel[] Mfg(ObservableCollection<IniOverride> rs) =>
    [
        new IniOptionViewModel("Force frame multiplier", "Only for games with just an FG on/off switch.", "RenoDX.MFGUnlock", "ForceMultiplier", rs,
            C("Game setting", null, "Use the multiplier the game's own menu selects."),
            C("2x", "2", "One generated frame per rendered frame."),
            C("3x", "3", "Two generated frames per rendered frame."),
            C("4x", "4", "Three generated frames; the usual RTX 50 maximum."),
            C("5x", "5", "Four generated frames. May need Max frame count 5+; not every game accepts it."),
            C("6x", "6", "Five generated frames. Highest the add-on allows; most latency and artifacts.")).About("force-multiplier"),
        new IniOptionViewModel("Max frame count", "Highest multiplier reported to the game.", "RenoDX.MFGUnlock", "MaxCount", rs,
            C("Default (4)", null, "Report up to 4x, like an RTX 50 card."),
            C("3", "3", "Game offers up to 3x."),
            C("4", "4", "Game offers up to 4x."),
            C("5", "5", "Game may offer 5x if its menu is built from the reported maximum."),
            C("6", "6", "Game may offer 6x; some games break with values above 4.")).About("max-count"),
        IniOptionViewModel.Toggle("Dynamic MFG", "Needs DLSS 310.9.1 FG + the matching Streamline set. Overrides the forced multiplier.",
            "RenoDX.MFGUnlock", "DynamicMFG", "1", rs).About("dynamic-mfg"),
        new IniOptionViewModel("Dynamic target FPS", "What Dynamic MFG aims for.", "RenoDX.MFGUnlock", "DynamicTargetFPS", rs,
            C("Refresh rate", null, "Follow the display's refresh rate."),
            C("60", "60", "Hold 60 FPS output."),
            C("90", "90", "Hold 90 FPS output."),
            C("120", "120", "Hold 120 FPS output."),
            C("144", "144", "Hold 144 FPS output."),
            C("165", "165", "Hold 165 FPS output."),
            C("240", "240", "Hold 240 FPS output.")).About("dynamic-target"),
        new IniOptionViewModel("Runtime selection", "Which DLSS-G files NVIDIA's runtime may use.", "RenoDX.MFGUnlock", "RuntimeSelectionMode", rs,
            C("Game default", null, "Keep the game's own over-the-air policy. NVIDIA may swap in its downloaded FG."),
            C("Prefer local files", "1", "Disable OTA so the DLLs installed in the game folder are used. Recommended with pinned DLSS or the Streamline swap."),
            C("Force NVIDIA OTA", "2", "Force NVIDIA's downloaded runtime even if the game disables it.")).About("runtime-selection"),
        new IniOptionViewModel("HDR compatibility", "Workarounds when FG breaks HDR UI or colours.", "RenoDX.MFGUnlock", "HDRCompatibilityMode", rs,
            C("Native", null, "No workaround. Use when HDR looks right."),
            C("UI Composition", "1", "Handle the HUD separately from the generated frames. First thing to try for broken or smeared UI."),
            C("Auto guard + UI", "2", "UI Composition plus automatic detection of problem frames."),
            C("Final color fallback", "3", "Most invasive fallback for games the other modes don't fix.")).About("hdr-compat"),
    ];

    public static IniOptionViewModel[] Nr(ObservableCollection<IniOverride> o) =>
    [
        IniOptionViewModel.Toggle("Neural rendering", "Run the DLSS 5 NR model (nvngx_dlssnr.dll).", "DlssNr", "Enabled", "true", o).About("nr-enabled"),
        IniOptionViewModel.Toggle("Run before upscaling", "Edit the low-res input before SR / RR instead of the upscaled output.", "DlssNr", "RunBeforeSR", "true", o).About("nr-before-sr"),
        IniOptionViewModel.Toggle("Apply to finished picture", "Apply after the game's lighting and effects (DX12 only, can touch the HUD).", "DlssNr", "FinishedPicture", "true", o).About("nr-finished"),
        new IniOptionViewModel("Passes", "Model layers per frame.", "DlssNr", "Passes", o,
            C("Default (1)", null, "One model pass. Normal."),
            C("1", "1", "One model pass. Normal."),
            C("2", "2", "Two stacked passes, about 2x the model cost. Deliberately stronger."),
            C("3", "3", "Three stacked passes, about 3x the cost. Strongest.")).About("nr-passes"),
        new IniOptionViewModel("Model resolution", "Fraction of the frame the model works at.", "DlssNr", "WorkingScale", o,
            C("Default (100%)", null, "Model runs at the resolution of its stage."),
            C("50%", F(0.5), "Quarter of the model cost, softer detail."),
            C("75%", F(0.75), "About half the model cost."),
            C("100%", F(1.0), "Full resolution."),
            C("150%", F(1.5), "Supersampled: runs above native and averages down. Very expensive.")).About("nr-scale"),
        new IniOptionViewModel("Style", "Built-in model profile.", "DlssNr", "Style", o,
            C("Default (standard)", null, "Standard profile."),
            C("Standard", "0", "Standard profile."),
            C("Natural", "1", "More restrained look."),
            C("Cinematic", "2", "More stylised, graded look.")).About("nr-style"),
        new IniOptionViewModel("Intensity", "Overall model strength.", "DlssNr", "Intensity", o,
            C("Default (1.0)", null, "Model's normal strength."),
            C("0.5", F(0.5), "Half strength."), C("0.75", F(0.75), "Slightly weaker."), C("1.0", F(1.0), "Normal."),
            C("1.25", F(1.25), "Stronger."), C("1.5", F(1.5), "Much stronger.")).About("nr-strengths"),
        new IniOptionViewModel("Local structure", "How much fine detail and texture the model adds.", "DlssNr", "LocalStructure", o,
            C("Default (1.0)", null, "Model default."),
            C("0.25", F(0.25), "Very subtle detail."), C("0.5", F(0.5), "Subtle."), C("0.7", F(0.7), "Moderate."),
            C("1.0", F(1.0), "Model default."), C("1.5", F(1.5), "Heavy detail synthesis.")).About("nr-strengths"),
        new IniOptionViewModel("Local tone", "How much the model changes local contrast and lighting.", "DlssNr", "LocalTone", o,
            C("Default (1.0)", null, "Model default."),
            C("0", F(0), "Keep the game's tones; detail only."), C("0.25", F(0.25), "Light touch."),
            C("0.5", F(0.5), "Moderate."), C("1.0", F(1.0), "Model default.")).About("nr-strengths"),
        new IniOptionViewModel("Skin structure", "Detail strength on skin.", "DlssNr", "SkinStructure", o,
            C("Default (follow)", null, "Same as local structure."),
            C("Follow structure", F(-1), "Same as local structure."),
            C("0", F(0), "No added skin detail (smooth skin)."), C("0.5", F(0.5), "Reduced skin detail."), C("1.0", F(1.0), "Full skin detail.")).About("nr-strengths"),
        new IniOptionViewModel("Colour strength", "Whether the model's colour comes with its light.", "DlssNr", "ColourStrength", o,
            C("Default (1.0)", null, "Model's colour fully."),
            C("0", F(0), "Keep the game's hues exactly; only brightness changes."),
            C("0.2", F(0.2), "Mostly the game's colour."), C("0.5", F(0.5), "Half and half."), C("1.0", F(1.0), "Model's colour fully.")).About("nr-colour"),
    ];

    public static IniOptionViewModel[] Tonemap(ObservableCollection<IniOverride> o) =>
    [
        new IniOptionViewModel("Tone mapping", "How linear / HDR frames are mapped into the model's range and back.", "DlssNr", "ReversibleMode", o,
            C("Default (soft knee)", null, "Soft knee, composed."),
            C("Soft knee", "0", "Simple highlight roll-off. Cheapest, can dull very bright HDR highlights."),
            C("Neutwo composed", "1", "Neutral 'Neutwo' curve; the model's answer is blended back against the original luminance."),
            C("Neutwo replace", "2", "Neutral curve; the model's picture replaces the frame. Stronger, highlights can shift."),
            C("Hybrid composed", "3", "Mix of soft knee and Neutwo, blended back. Balanced choice for HDR."),
            C("Hybrid replace", "4", "Mix of both curves; model picture replaces the frame.")).About("tm-mode"),
        new IniOptionViewModel("White point source", "What decides display white for the encode.", "DlssNr", "WhitePointSource", o,
            C("Default (manual)", null, "Measured from the frame, scaled by Paper white."),
            C("Manual", "0", "Measured from the frame, scaled by Paper white."),
            C("Game exposure", "1", "Use the exposure value the game hands to the upscaler."),
            C("Auto HDR exposure", "3", "Measure HDR exposure automatically each frame. Best for HDR games without usable exposure.")).About("tm-white"),
        new IniOptionViewModel("Paper white", "Multiplier on the white point: brighter or darker model input.", "DlssNr", "WhitePointScale", o,
            C("Default (1.0)", null, "No change."),
            C("0.5", F(0.5), "Treat the frame as twice as bright: protects highlights, darker midtones for the model."),
            C("0.75", F(0.75), "Slightly more highlight headroom."),
            C("1.0", F(1.0), "No change."),
            C("1.5", F(1.5), "Lift midtones for the model; highlights clip sooner."),
            C("2.0", F(2.0), "Strong lift; for very dark games.")).About("tm-paper"),
        new IniOptionViewModel("Highlight protection", "Automatic exposure: how much to protect bright areas.", "DlssNr", "AutoExposureHighlightProtection", o,
            C("Default (0)", null, "No extra protection."),
            C("0", "0", "No extra protection."), C("25", "25", "Light."), C("50", "50", "Medium."),
            C("75", "75", "Strong."), C("100", "100", "Maximum: bright lights stay untouched, darker overall.")).About("tm-highlight"),
        new IniOptionViewModel("Max brightening", "Cap on how much a pixel may brighten.", "DlssNr", "MaxRatio", o,
            C("Default (2x)", null, "A pixel can at most double."),
            C("1.5x", F(1.5), "Tighter cap: fewer bright artifacts, less punch."),
            C("2x", F(2.0), "Default cap."),
            C("3x", F(3.0), "Looser."),
            C("4x", F(4.0), "Loosest; bright lights may break into coloured cells.")).About("tm-maxratio"),
        new IniOptionViewModel("Transfer strength", "How far the frame moves toward the model's picture.", "DlssNr", "TransferStrength", o,
            C("Default (1.0)", null, "The model's picture."),
            C("0.5", F(0.5), "Halfway between game and model."),
            C("0.75", F(0.75), "Mostly the model."),
            C("1.0", F(1.0), "The model's picture."),
            C("1.25", F(1.25), "Past the model: exaggerated.")).About("tm-transfer"),
        IniOptionViewModel.Toggle("HDR brightness transfer", "Experimental: measured HDR transfer for before-SR + finished picture.", "DlssNr", "HdrTransfer", "true", o).About("tm-hdrtransfer"),
    ];

    public static IniOptionViewModel[] HdrOutput(ObservableCollection<IniOverride> o) =>
    [
        IniOptionViewModel.Toggle("Force HDR color space", "Make the swapchain HDR even if the game doesn't ask.", "HDR", "ForceHDR", "true", o).About("hdr-force"),
        IniOptionViewModel.Toggle("Use HDR10", "10-bit HDR10 instead of 16-bit scRGB when forcing HDR.", "HDR", "UseHDR10", "true", o).About("hdr-10"),
        IniOptionViewModel.Toggle("Skip color space", "Don't set the HDR color space (for games that set it themselves).", "HDR", "SkipColorSpace", "true", o).About("hdr-skip"),
    ];

    /// <summary>What to do when a game already has its own config values.</summary>
    public static readonly OptionChoice[] IniModes =
    [
        C("Keep game settings", "keep", "Values a game's OptiScaler.ini / ReShade.ini already has, or you changed in-game, stay. Values this app wrote follow your settings."),
        C("Apply app settings", "apply", "Your settings here overwrite the same keys in every game. Other keys in the game's config stay."),
        C("Fresh config", "fresh", "OptiScaler.ini is rebuilt from the release plus your settings; everything else from the old one is dropped (backed up first). ReShade.ini is only overwritten on your keys."),
    ];
}
