# Privacy Policy

_Last updated: 25 October 2026_

**Short version:** Sentinel has no account, no telemetry, no analytics and no ads. Your hardware details, game list and settings stay on your PC. The only network requests it makes are to load game artwork from Steam and GOG.

## What Sentinel reads on your PC

To recommend settings, Sentinel reads:

- **Hardware:** processor, graphics cards, memory, drives and displays, through Windows (WMI, the registry and display APIs).
- **Windows settings:** power plan, Game Mode, background recording, GPU scheduling, mouse acceleration, Memory Integrity status and similar.
- **Installed games:** from Steam, Epic, GOG, Ubisoft Connect, EA app, Xbox, Battle.net, Riot, Rockstar and the Windows uninstall list.
- **Game settings files:** only when you open a game in Sentinel or apply settings to it.
- **App names:** names of running overlay and monitoring apps (for example OBS or MSI Afterburner), used to show the "overlays running" tip.

**None of this leaves your PC.** Sentinel doesn't upload it, log it remotely or share it with anyone.

## What Sentinel stores

Everything is stored locally in `%LOCALAPPDATA%\Sentinel`:

| Item | Purpose |
| --- | --- |
| `settings.json` | The FPS target and options you chose for each game |
| `tweak-state.json` | Previous values of Windows settings you changed, so they can be reverted |
| `Backups\` | Copies of game settings files made before each change |
| `Icons\` | Cached game artwork |
| `sentinel.log` | A local error log, for troubleshooting |

To remove all of it, delete that folder. Uninstalling or deleting the app does not remove it automatically.

## Network requests

Sentinel only goes online to download game artwork for the tiles you scroll past:

| Host | What is requested |
| --- | --- |
| `cdn.cloudflare.steamstatic.com`, `shared.akamai.steamstatic.com` | A game's header image, by its public Steam app ID |
| `store.steampowered.com` | The public store entry for one app ID, to find its image address (only when the usual image address doesn't work) |
| `images.gog-statics.com` | A GOG game's cover image |

These requests contain only the public ID or address of the image. Like any web request, they also reveal your IP address and a user agent (`Sentinel-GameOptimizer/1.0`) to those services, which handle it under their own privacy policies. Sentinel sends no hardware details, game list or personal information. If you're offline, the app still works and shows letter tiles instead of artwork.

### Links you click

Some buttons open web pages in your own browser, such as the graphics driver download page and the project page on GitHub. That only happens when you click them.

## Reports you create

`Sentinel.exe --report report.json` writes a hardware and game report to a file **you** choose. Sentinel never sends it anywhere. If you attach it to a GitHub issue, remember that it lists your hardware and installed games.

## Children

Sentinel doesn't knowingly collect any data from anyone, including children, because it doesn't collect data at all.

## Changes

If a future version changes what Sentinel stores or sends, this policy will be updated in the same release, and the change will be listed in the [changelog](CHANGELOG.md).

## Contact

Privacy questions: open an issue at <https://github.com/BenScriptsRBX/Sentinel/issues>.
