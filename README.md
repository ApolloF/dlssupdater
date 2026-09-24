# DLSS Updater

Windows app that keeps an OptiScaler-NR + ReShade + DLSS setup current across all your games.

It pulls the latest releases of:

- [OptiScaler-DLSSNR-PreSR-Multipass](https://github.com/wilsjo2/OptiScaler-DLSSNR-PreSR-Multipass/releases) (standard package, prereleases included)
- [MFGAdaUnlock-RenoDx](https://github.com/mavismmg/MFGAdaUnlock-RenoDx/releases) (`renodx-mfgunlock.addon64`)
- [NVIDIA DLSS](https://github.com/NVIDIA/DLSS) runtime DLLs: `nvngx_dlss.dll` (SR), `nvngx_dlssd.dll` (RR), `nvngx_dlssg.dll` (FG)

and installs them next to each game's executable, together with your own `nvngx_dlssnr.dll` and `ReShade64.dll`.

## What an install does

1. Finds the real game exe (Unreal `*-Win64-Shipping.exe`, Unity players, etc.; you can pick another folder).
2. Copies `OptiScaler.dll` as the chosen proxy (`dxgi.dll` by default) plus its `OptiScaler\` backend folder.
3. Builds `OptiScaler.ini` from the release ini, keeps the game's own non-default values, then applies your overrides (default: ReShade loading on, overlay on **Del**, DLSS preset override, DLSSNR settings).
4. Places `ReShade64.dll`, `renodx-mfgunlock.addon64` and `nvngx_dlssnr.dll` beside it.
5. Replaces every older `nvngx_dlss*.dll` in the game with the latest NVIDIA build.
6. Moves anything it replaces into `.dlssupdater\backup` next to the exe. **Uninstall** and **Restore DLSS** put the originals back.

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
