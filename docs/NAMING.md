# Name candidates

"DLSS Updater" leads with an NVIDIA trademark and is already taken: `Recol.DLSSUpdater` is on winget (Recol/DLSS-Updater), and Drommedhar/DlssUpdater also exists. This page lists names without "DLSS", "NVIDIA", "RTX", "GeForce" or other NVIDIA marks. "DLSS" can still describe what the app does in a tagline, e.g. "Shimsmith: keeps DLSS, OptiScaler and ReShade current". **Nothing has been renamed.**

Checked on 2026-10-04 with `winget search "<name>"`, `gh search repos "<name>"` and a web search. No formal trademark search (EUIPO / USPTO / DPMA) has been done yet.

| # | Name | winget | GitHub | Web | Collision |
|---|------|--------|--------|-----|-----------|
| 1 | **Shimsmith** | no package | no repos | no product found | None. "Shim" (a drop-in compatibility DLL) fits the job. |
| 2 | **Swapwright** | no package | samuelarogbonlo/SwapWright (0 stars, DeFi swap bot) | only that bot | Minor. It suggests crypto "swaps", and "wright" is easy to misspell. |
| 3 | **Upscalekeeper** | no package | no repos | nothing specific | None, but it is long and generic, so the brand is weak. |
| 4 | **Pixelwright** | no package | 5 small repos (0 stars, e.g. R3mmurd/PixelWright, a pixel-art editor) | Pixelwright Digital, an Apple app development and audit company | Moderate. An active software company uses the name, so check the trademark registers first. |
| 5 | **Kitkeeper** | no package | 4 small repos (0 stars) | kitkeeper.co.uk (active); KitKeeper library SaaS (shut down 2024) | Minor to moderate. Several commercial uses, and "kit" says little about graphics. |

**Top two:** Shimsmith, then Swapwright.

Rejected after the same checks:
- **Framewright**: many same-name video, photo and AI-film tools.
- **Lensforge**: an established paid lens-design app (Ripplon LensForge).
- **Fresko**: several unrelated commercial apps.
- **Modkeeper**: same niche; Nexus Mods already has a "Mod Keeper" tool.
- **Tuneframe**: several existing apps, and it reads as audio.

Before picking one:
1. Search EUIPO TMview and USPTO in classes 9 and 42, and check the domain and the GitHub org name.
2. A new name also means a new exe name, installer AppId and data folder (`%LocalAppData%\DLSSUpdater`), so plan a migration.
