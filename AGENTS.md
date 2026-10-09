# DLSS Updater

Windows desktop app that pulls the latest OptiScaler-NR, DLSS, MFG Unlock, Streamline and ReShade releases and installs them into the user's games. It also runs as a Seaglass add-on (`Addon/`).

## Stack
- Language/runtime: C# 12, .NET 8 (`net8.0-windows`, `win-x64`)
- UI: WPF with CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`). Theme tokens (colors, fonts, styles) are in `src/DLSSUpdater/Theme/Nvidia.xaml`. Reuse them, don't hard-code colors.
- Data: JSON files under `%LocalAppData%\DLSSUpdater` (`AppPaths`), serialized through the source-generated `JsonCtx` in `Core/AppSettings.cs`. Register new serialized types there.
- Tests: xUnit (`tests/DLSSUpdater.Tests`)
- Installer: Inno Setup 6 (`installer/DLSSUpdater.iss`)

## Commands
- Build (warnings are errors): `dotnet build -warnaserror`
- Test: `dotnet test` (needs the .NET 8 runtime and Windows Desktop runtime installed, not just a newer SDK)
- Integration test (downloads real releases, off by default): `$env:DLSSU_INTEGRATION=1; dotnet test`. `DLSSU_COMPONENTS` can point at a folder that holds `nvngx_dlssnr.dll`.
- Publish the portable single-file exe: `dotnet publish src/DLSSUpdater -c Release -o publish` → `publish\DLSSUpdater.exe`
- Installer (after publish): `iscc /DAppVersion=<version> installer\DLSSUpdater.iss` → `publish\DLSSUpdater-Setup-<version>.exe`

## Release flow
1. Bump `<Version>` in `src/DLSSUpdater/DLSSUpdater.csproj`, and the version in the `iscc` example lines of `installer/DLSSUpdater.iss` and `README.md`. All three must say the same thing.
2. Commit, then tag `v<version>` (for example `v1.5.1`) and push the tag.
3. `.github/workflows/release.yml` (runs on `v*` tags only; PRs and pushes are covered by `ci.yml`) checks that the tag equals `v` + the csproj version and fails the build if it doesn't. It then runs the tests, publishes, builds the installer and attaches both exes to the GitHub release.

## Layout
- `src/DLSSUpdater/Core`: business logic with no UI: GitHub client (ETag cache), component store, installer, ini merging, NVIDIA driver profiles (`NvDrs`), settings
- `src/DLSSUpdater/Scan`: game discovery (launchers, Steam VDF) and inspection (DLSS dlls, exe ranking, anti-cheat)
- `src/DLSSUpdater/ViewModels`: MVVM view models. `MainViewModel` owns the `Services` (settings, store, installer).
- `src/DLSSUpdater/Views`: dialogs, converters, small WPF helpers
- `src/DLSSUpdater/Addon`: the Seaglass add-on protocol (stdin/stdout JSON)
- `tests/DLSSUpdater.Tests`: one file per area. Tests that touch disk point `AppPaths.Root` at a temp folder.

## Conventions
- Keep logic in `Core`/`Scan` as plain static or instance methods that can be tested without WPF. View models only orchestrate.
- Async UI code keeps continuations on the UI thread (no `ConfigureAwait(false)` in view models). Background work goes through `Task.Run`, and `Log.Line` is marshalled with `Dispatcher.BeginInvoke`.
- Never do work inside a `CollectionChanged` handler that makes a bound `ItemsControl` re-read its items (scrolling, for example). Defer it with `Dispatcher.BeginInvoke` (see `Views/ListAutoScroll.cs` and the startup crash fixed in b5f78ef).
- GitHub API calls go through `GitHubClient.GetApiAsync` so they share the ETag cache. When GitHub is unreachable or rate-limited, fall back to cached data and never throw at the user.
- Use `FileUtil.AtomicWriteText` for every file the app writes.

## Hard rules
- **`nvngx_dlssnr.dll` must never be committed, bundled, uploaded or redistributed** (NVIDIA's terms don't allow it). The user supplies it, and it stays in `%LocalAppData%\DLSSUpdater\components`. `.gitignore` blocks `*.dll`, `*.exe`, `*.addon64` and `*.zip`. Don't force-add binaries.
- Tests and agents must not write NVIDIA driver profiles (`NvDrs` / NvAPI `Save`), launch real games or install into real game folders. Use temp directories.

## Check command
`dotnet build -warnaserror && dotnet test`
Takes about 40 seconds. `.github/workflows/ci.yml` (job `build-test`, windows-latest) runs this same pair on every pull request and push to main. Keep it fast and keep it passing.

## Definition of done
- The check command passes, with no new warnings.
- Tests were added or updated for changed behaviour.
- UI changes are checked in the running app.

## Git workflow
- Remote: GitHub ApolloF/dlssupdater.
- Branch, PR, CI (`build-test`), then merge only when the user says "ship it" (auto-merge, squash).
- Releases: tag `v<version>` as described in "Release flow"; `release.yml` publishes.

## Secrets
- No config or secrets in the repo; the app keeps its data in `%LocalAppData%\DLSSUpdater`. If a key is ever needed it comes from env vars, and `.env.example` lists the keys with no values.
- Never commit real values. gitleaks runs on every commit.
