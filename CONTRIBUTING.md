# Contributing to Sentinel

Thanks for helping. Bug reports, catalog fixes and code changes are all welcome.

## Getting set up

You need Windows 10 or 11 and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```bash
git clone https://github.com/BenScriptsRBX/Sentinel.git
cd Sentinel
dotnet test Sentinel.slnx
dotnet run --project src/Sentinel
```

Two developer switches are useful while working on the UI or detection:

- `Sentinel.exe --report out.json` runs a headless scan and writes hardware, games, plans and tweaks to a file.
- `Sentinel.exe --snapshot <folder> [catalog search]` renders every page, plus a scrolled-to-bottom view and the loading screen, to PNGs and exits.
- `Sentinel.exe --demo` shows a made-up PC and game library instead of scanning yours. Nothing is saved and no button changes Windows or a game. The README screenshots come from `Sentinel.exe --snapshot shots --demo`.

## Project layout

| Path | What's there |
| --- | --- |
| `src/Sentinel/Services/Hardware` | CPU/GPU/RAM/disk/display detection and the GPU/CPU score tables (`Data/gpus.json`, `Data/cpus.json`) |
| `src/Sentinel/Services/Games` | Launcher scanners, engine detection, artwork loading |
| `src/Sentinel/Services/Optimizer` | The performance model and the settings optimizer |
| `src/Sentinel/Services/Config` | Writers for each game config format, plus backups |
| `src/Sentinel/Services/Tweaks` | PC tweak checks with apply and revert |
| `src/Sentinel/Data/Catalog` | The game catalog (one JSON object per line) |
| `src/Sentinel/Themes` | Theme styles and the logo (`Logo.xaml`) |
| `tests/Sentinel.Tests` | xUnit tests |

## Fixing or adding catalog games

Each line in `src/Sentinel/Data/Catalog/*.json` is one game:

```json
{"n": "Game Name", "s": [123450], "e": "Unreal 5", "g": "Action RPG", "gpu": 7, "cpu": 5, "y": 2025,
 "f": ["dlss", "fsr", "tsr", "rt"], "cap": 60, "t": "One practical settings tip."}
```

- `gpu` / `cpu` are demand ratings from 1 to 10. **5** means about 130 fps (GPU) / 200 fps (CPU) at 1080p High on an RTX 3070 + Ryzen 5 5600X. **1** is about 600 fps and **10** about 40 fps.
- `f` lists the modern features the game really supports. The codes are listed in `GameFeature` in `src/Sentinel/Models/Games.cs`. Leave a feature out rather than guess.
- `cap` is only for games that hard-lock their frame rate.
- `"q": 1` marks AI-estimated entries, which get a wider safety margin. If you have checked an entry against real benchmarks, remove `q`.
- Only Steam app IDs (`s`) you're sure of. A wrong ID can mislabel someone's installed game.

You can also use the **Catalog correction** issue form.

## Code changes

- Match the style of the surrounding code: naming, comment density and idioms.
- Add or update tests in `tests/Sentinel.Tests` for behaviour changes. `dotnet test Sentinel.slnx` must pass.
- Keep the honesty rules intact. The optimizer must never show more fps than the conservative estimate, the hardware ceiling or a game's own fps lock. The tests check this for every catalog game.
- Anything that changes Windows or game files needs a backup or revert path.
- UI changes: include before/after screenshots. Crop out anything personal, such as your installed game list.

## Pull requests

1. Fork and create a branch from `main`.
2. Make your change with tests.
3. Open a pull request describing what changed and why. CI builds and tests it on Windows.

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE) and that you'll follow the [Code of Conduct](CODE_OF_CONDUCT.md).
