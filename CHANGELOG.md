# Changelog

All notable changes to Sentinel are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and versions follow [Semantic Versioning](https://semver.org/).

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

[1.0.0]: https://github.com/BenScriptsRBX/Sentinel/releases/tag/v1.0.0
