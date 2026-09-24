# DLSS Updater

Windows app that keeps an OptiScaler-NR + ReShade + DLSS setup current across all your games.

It pulls the latest releases of:

- [OptiScaler-DLSSNR-PreSR-Multipass](https://github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass/releases) (standard package, prereleases included)
- [MFGAdaUnlock-RenoDx](https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases) (`renodx-mfgunlock.addon64`)
- [NVIDIA DLSS](https://github.com/NVIDIA/DLSS) runtime DLLs: `nvngx_dlss.dll` (SR), `nvngx_dlssd.dll` (RR), `nvngx_dlssg.dll` (FG)
- [NVIDIA Streamline](https://github.com/NVIDIA-RTX/Streamline) (optional, signed `sl.*.dll` set for Dynamic MFG)

and installs them next to each game's executable, together with your own `nvngx_dlssnr.dll` and `ReShade64.dll`.

## What an install does

1. Finds the real game exe (Unreal `*-Win64-Shipping.exe`, Unity players, etc.; you can pick another folder).
2. Copies `OptiScaler.dll` as the chosen proxy (`dxgi.dll` by default) plus its `OptiScaler\` backend folder.
3. Builds `OptiScaler.ini` from the release ini plus your overrides (default: ReShade loading on, overlay on **Del**, DLSS preset override, DLSSNR settings) and patches `ReShade.ini` (keybinds, tutorial, MFG Unlock options).
4. Places `ReShade64.dll`, `renodx-mfgunlock.addon64` and `nvngx_dlssnr.dll` beside it.
5. Replaces every `nvngx_dlss*.dll` in the game with the latest NVIDIA build, or with a pinned version (globally or per game, also downgrading).
6. Optionally replaces all `sl.*.dll` the game ships with one matching Streamline release (never mixed, never adds plugins).
7. Moves anything it replaces into `.dlssupdater\backup` next to the exe. **Uninstall** and **Restore DLSS** put the originals back.

Existing configs (Settings → General): **Keep game settings** (default: values the app wrote follow your settings, anything the game had or you changed in-game stays), **Apply app settings** (your settings overwrite the same keys everywhere) or **Fresh config** (OptiScaler.ini rebuilt from the release; old one backed up).

Settings also pre-configures DLSSNR (passes, model resolution, style, strengths) and its HDR tone mapping (curve, white point, paper white, highlight protection, max brightening) plus OptiScaler's HDR output. Every option has an ⓘ tooltip and a guide entry on the About page that explains each dropdown choice.

Settings has key-capture fields for the OptiScaler hotkeys (menu, FPS overlay, frame generation, DLSSNR) and ReShade (overlay, effects, screenshot, reload).

**Update all** re-applies everything to each managed game after a new release or after a game patch reverts the DLLs.

Games are discovered from Steam, Epic, GOG, EA, Ubisoft and `XboxGames` folders. Standalone games can be added one by one or as a library folder whose subfolders are games.

Games with anti-cheat (EAC, BattlEye, GameGuard, …) are flagged and need an extra confirmation before anything is injected. DLSS-only swaps work without it.

## First run

`nvngx_dlssnr.dll` and `ReShade64.dll` (full add-on build) can't be redistributed. Put them next to `DLSSUpdater.exe` on first launch, or import them under **Settings → Local components**. They're stored in `%LocalAppData%\DLSSUpdater\components`.

## Build

```
dotnet test
dotnet publish src/DLSSUpdater -c Release -o publish
```

Produces a single self-contained `publish\DLSSUpdater.exe` (.NET 8, WPF). Tagging `v*` builds it on GitHub Actions and attaches it to a release.

`DLSSU_INTEGRATION=1` enables a test that downloads the real releases and installs them into a temporary folder.
