# Changelog

All notable changes to Sentinel are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added

- **Demo mode:** `Sentinel.exe --demo` shows a made-up PC and game library, so the app can be shown or screenshotted without revealing your hardware or games. Saved settings aren't loaded or written, and no button changes Windows or a game's files.
- **README screenshots** of the main pages, taken in demo mode.

### Fixed

- **Upscaling estimates were too optimistic.** DLSS / FSR / XeSS gains now follow GPU-bound benchmarks: about 1.4× at Quality, 1.75× at Performance and 2× at Ultra Performance. Version 1.0.0 assumed up to 4×, which inflated plans that use upscaling and the "max achievable" figure in demanding games. Ray-traced work is per-pixel, so it still gains more from upscaling.
- Long game names are no longer cut off in the game page header.
- `--snapshot` ignores the mouse, so hover highlights don't end up in captures, and gives game art time to load.

## [1.0.0] - 2026-10-25

First public release.

### Added

- **Hardware detection:** CPU, every GPU (with real 64-bit VRAM), memory speed and channels, SSD/HDD/NVMe per drive, all displays with current and maximum refresh rate, and Windows gaming settings. Everything is scored against built-in GPU and CPU performance tables.
- **Game detection** across Steam, Epic, GOG, Ubisoft Connect, EA app, Xbox / Microsoft Store, Battle.net, Riot, Rockstar, Minecraft Java and Roblox, with engine fingerprinting from install folders.
- **A 15,802-game catalog:** hand-written entries, the 10,000 most-owned Steam games, GOG-only games and other-launcher exclusives. Each entry has demand ratings, modern feature support, frame-rate locks and store availability.
- **FPS-target optimizer:** pick a range and Sentinel chooses graphics settings plus modern options for your GPU. Those options cover upscaling (DLSS, FSR 4, FSR, XeSS, TSR), frame generation, Reflex / Anti-Lag 2, ray and path tracing, VRR-friendly frame caps and VRAM-aware textures.
- **Honest estimates:** plans use the conservative end of the expected range and respect each game's own frame-rate lock. When you ask for more than the hardware can reach, Sentinel says so instead of claiming it. Out-of-reach presets are greyed out.
- **Competitive mode:** keeps native resolution and drops visual clutter first.
- **Auto-apply with backups** for Unreal Engine 4/5 games, Counter-Strike 2, Apex Legends, GTA V, Red Dead Redemption 2, League of Legends, Minecraft, Roblox, and Unity games (beta).
- **PC Tweaks** with a PC-health summary and one-click revert. It covers power plan, Game Mode, background recording, GPU scheduling, windowed-game optimizations, refresh rate, mouse acceleration, per-game dedicated GPU, driver age, XMP/EXPO, memory channels, games on hard drives, free space, overlays and startup apps.
- **Game artwork** from Steam and GOG, loaded on demand and cached.
- **Dark UI** with the Sentinel logo, Windows 11 rounded corners, a game-art hero and an FPS range bar.

[Unreleased]: https://github.com/BenScriptsRBX/Sentinel/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/BenScriptsRBX/Sentinel/releases/tag/v1.0.0
