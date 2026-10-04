# DLSS Updater

Windows app that keeps an OptiScaler-NR + ReShade + DLSS setup current across all your games.

It pulls the latest releases of:

- [OptiScaler-DLSSNR-PreSR-Multipass](https://github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass/releases) (standard package, prereleases included)
- [MFGAdaUnlock-RenoDx](https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases) (`renodx-mfgunlock.addon64`)
- [NVIDIA DLSS](https://github.com/NVIDIA/DLSS) runtime DLLs: `nvngx_dlss.dll` (SR), `nvngx_dlssd.dll` (RR), `nvngx_dlssg.dll` (FG)
- [NVIDIA Streamline](https://github.com/NVIDIA-RTX/Streamline) (optional, signed `sl.*.dll` set for Dynamic MFG)
- [ReShade](https://reshade.me) with full add-on support (`ReShade64.dll` taken from the official setup)

and installs them next to each game's executable, together with your own `nvngx_dlssnr.dll`.

## Download

Each [release](https://github.com/ApolloF/dlssupdater/releases/latest) has two builds of the same app:

- **`DLSSUpdater-Setup-<version>.exe`**: installer. Installs for your user without admin rights (or for all users), adds a Start menu entry and an optional desktop icon, and has an uninstaller. Run a newer setup to update.
- **`DLSSUpdater.exe`**: portable. A single self-contained exe; put it anywhere and run it.

Both keep settings and downloads in `%LocalAppData%\DLSSUpdater`, so you can switch between them. Uninstalling keeps that folder and removes the Seaglass add-on link if it pointed at the installed copy.

## What an install does

1. Finds the real game exe (Unreal `*-Win64-Shipping.exe`, Unity players, etc.; you can pick another folder).
2. Copies `OptiScaler.dll` as the chosen proxy (`dxgi.dll` by default) plus its `OptiScaler\` backend folder.
3. Builds `OptiScaler.ini` from the release ini plus your settings and patches `ReShade.ini` (keybinds, MFG Unlock options). Out of the box nothing is changed from what the upstream projects ship, except `LoadReshade=true` when ReShade is installed and `[DlssNr] Enabled=true` when the DLSSNR runtime is installed (both default to off upstream).
4. Places `ReShade64.dll`, `renodx-mfgunlock.addon64` and `nvngx_dlssnr.dll` beside it.
5. Replaces every `nvngx_dlss*.dll` in the game with the latest NVIDIA build, or with a pinned version (globally or per game, also downgrading). Games that ship no DLSS have this off by default; OptiScaler still gets an `nvngx_dlss.dll` so it can offer DLSS.
6. Optionally replaces all `sl.*.dll` the game ships with one matching Streamline release (never mixed, never adds plugins).
7. Moves anything it replaces into `.dlssupdater\backup` next to the exe. **Uninstall** and **Restore DLSS** put the originals back.

Existing configs (Settings → General): **Keep game settings** (default: values the app wrote follow your settings, anything the game had or you changed in-game stays), **Apply app settings** (your settings overwrite the same keys everywhere) or **Fresh config** (OptiScaler.ini rebuilt from the release; old one backed up).

Settings also pre-configures DLSSNR (passes, model resolution, style, strengths) and its HDR tone mapping (curve, white point, paper white, highlight protection, max brightening) plus OptiScaler's HDR output. Every option has an ⓘ tooltip and a guide entry on the About page that explains each dropdown choice.

Settings has key-capture fields for the OptiScaler hotkeys (menu, FPS overlay, frame generation, DLSSNR) and ReShade (overlay, effects, screenshot, reload).

**Install mode**: OptiScaler-NR (default, the main purpose) or **ReShade + add-ons**, which installs ReShade itself as the proxy (`dxgi.dll`, `d3d12.dll`, `d3d11.dll`, `d3d9.dll`, `dinput8.dll` or `opengl32.dll`) with MFG Unlock and the DLSS / Streamline updates, without OptiScaler or DLSSNR. Set the default in Settings and override it per game; switching removes the other mode's files.

**Presets**: save the whole OptiScaler / ReShade configuration (options, keybinds, overrides) under a name, load it, or assign it to single games. The built-in **Recommended (ApolloF)** preset holds the tuned setup (DLSS upscaler, preset override, overlay on **Del**, DLSSNR strengths and HDR tone mapping, ReShade tutorial skipped). **Reset to defaults** goes back to the upstream defaults.

**Config check**: before installing a new OptiScaler-NR release the app compares its OptiScaler.ini with every key it manages and warns when keys disappeared or the release is a newer major line than tested.

**NVIDIA profile** (per game only; the global driver profile is read, never written): DLSS SR / RR / FG preset overrides, DLSS render resolution, multi frame generation count, Smooth Motion, RTX HDR, RTX Dynamic Vibrance, FPS limiter, VSync and power mode, written straight to the driver profile via NvAPI like NVIDIA Profile Inspector. Nothing is loaded until the tab is opened. The scan detects which DLSS features a game ships (SR, RR, FG, Reflex, from `nvngx_*` and Streamline `sl.*` files); the tab lists them, moves presets for missing features into a collapsed group, and suggests Smooth Motion for games without FG. The game list shows RR / FG / Reflex badges.

**Update all** re-applies everything to each managed game after a new release or after a game patch reverts the DLLs.

Games are discovered from Steam, Epic, GOG, EA, Ubisoft and `XboxGames` folders. Standalone games can be added one by one or as a library folder whose subfolders are games.

Games with anti-cheat (EAC, BattlEye, GameGuard, …) are flagged and need an extra confirmation before anything is injected. DLSS-only swaps work without it.

## First run

`nvngx_dlssnr.dll` can't be redistributed. Put it next to `DLSSUpdater.exe` on first launch, or import it under **Settings → Local components**. It's stored in `%LocalAppData%\DLSSUpdater\components`.

ReShade is downloaded from reshade.me on the first install that needs it (add-on build, cached per version). To use your own build, import `ReShade64.dll` the same way; **Use download** switches back.

## Seaglass add-on

DLSS Updater also works as an add-on for [Seaglass](https://github.com/ApolloF/Seaglass) (formerly WaterLauncher). Add-on support is being reworked in Seaglass and lives on its [`feature/dlss-addon`](https://github.com/ApolloF/Seaglass/tree/feature/dlss-addon) branch for now; Seaglass 1.5 releases don't load add-ons.

- Seaglass shows each game's DLSS and OptiScaler versions.
- Before a game starts, DLSS Updater puts DLSS back when a game update replaced it.
- A game's page in Seaglass can update DLSS, install OptiScaler (with your DLSS Updater settings), restore the original DLSS files or open DLSS Updater.

To connect them, use *Settings → General → Connect to Seaglass*, or run `DLSSUpdater.exe --register-addon`. This writes `%LOCALAPPDATA%\Seaglass\addons\dlssupdater\addon.json` (or WaterLauncher's folder while an older WaterLauncher hasn't updated to Seaglass yet). Then turn it on in Seaglass under *Settings → Add-ons*. Seaglass pins this exe's hash, so it asks again after an update.

Seaglass runs `DLSSUpdater.exe --addon`, which has no window and speaks JSON-RPC on stdin/stdout ([protocol](https://github.com/ApolloF/Seaglass/blob/feature/dlss-addon/docs/addon-protocol.md)). It never runs elevated. Games with anti-cheat that you haven't confirmed in DLSS Updater are left alone.

## Build

```
dotnet test
dotnet publish src/DLSSUpdater -c Release -o publish
```

Produces a single self-contained `publish\DLSSUpdater.exe` (.NET 8, WPF), the portable build. The installer wraps it with [Inno Setup](https://jrsoftware.org/isinfo.php):

```
iscc /DAppVersion=1.5.1 installer\DLSSUpdater.iss
```

Tagging `v*` (matching `<Version>` in the csproj) builds both on GitHub Actions and attaches them to a release.

`DLSSU_INTEGRATION=1` enables a test that downloads the real releases (including ReShade from reshade.me) and installs them into a temporary folder.

`DLSSUpdater.exe --demo` opens the app with made-up games and releases, for screenshots and trying the interface. It keeps its data in `%TEMP%\DLSSUpdater-demo`, doesn't scan, download or read driver profiles, and its installs only pretend: nothing is written to game folders.
