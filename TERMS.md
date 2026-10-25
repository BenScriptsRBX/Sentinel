# Terms of Use

_Last updated: 25 October 2026_

These terms explain what Sentinel does on your PC and what you agree to when you download or use it. Sentinel is free, open-source software. Your rights to use, copy, modify and redistribute the code come from the [MIT License](LICENSE), and nothing here takes those rights away.

## 1. What Sentinel changes on your PC

- **Game settings files.** When you click **Apply to game**, Sentinel writes graphics settings into that game's own configuration file (for example `GameUserSettings.ini`, `options.txt`, `settings.xml`). It saves a backup of the file first, and **Restore backup** puts it back. Sentinel only edits files that games themselves use to store your settings. It never modifies game executables, game memory, or anti-cheat software, and it never injects code into a running game.
- **Windows settings.** On the **PC Tweaks** page, Sentinel changes a Windows setting only when you click **Apply** (or **Apply safe recommendations**). Each change records the previous value so you can **Revert** it. Tweaks that need administrator rights ask Windows for permission first, and you can decline.
- **Security settings.** Sentinel never changes security features such as Memory Integrity, Windows Defender or Secure Boot. It can open the relevant Windows settings page so you can review them yourself.

## 2. Your responsibilities

- **Close the game first.** Close a game before applying settings to it, because many games overwrite their settings file when they exit. Sentinel warns you when it detects that the game is running.
- **Follow each game's rules.** Many online games have terms of service and anti-cheat rules. Sentinel only writes settings that the game exposes in its own menus and config files, but you are responsible for making sure your settings follow the rules of every game you play.
- **Keep your own backups** of anything important. Sentinel's backups cover only the files it edits.

## 3. Estimates are not guarantees

FPS figures, "max achievable" values and recommendations come from a performance model and from catalog data. Much of that data was classified with the help of AI and has not been benchmarked. Real performance depends on the scene, drivers, game updates, background software and your exact hardware. Treat every number as an estimate and check it with the game's own benchmark or an FPS counter.

## 4. Third-party content and trademarks

- **Artwork.** Game artwork shown in Sentinel is loaded from Steam's and GOG's public image servers (see [PRIVACY.md](PRIVACY.md)). It belongs to its respective owners and is not part of this project.
- **Names and trademarks.** Game names, store names, and hardware and technology names (for example DLSS, FSR, XeSS and Reflex) are trademarks of their respective owners. They are used only to identify the games and features they refer to.
- **No affiliation.** Sentinel is an independent project. It is not affiliated with, endorsed by or sponsored by Valve, Epic Games, GOG, Microsoft, Ubisoft, Electronic Arts, Blizzard, Riot Games, Rockstar Games, NVIDIA, AMD, Intel or any game developer or publisher.

## 5. No warranty

Sentinel is provided **"as is", without warranty of any kind**, as set out in the [MIT License](LICENSE). To the maximum extent permitted by law, the authors are not liable for any damage, data loss, game account action or other harm arising from using Sentinel. You use it at your own risk.

## 6. Privacy

Sentinel has no accounts, no telemetry and no ads. See [PRIVACY.md](PRIVACY.md) for exactly what stays on your PC and the few network requests the app makes.

## 7. Changes to these terms

These terms may be updated as Sentinel changes. Each change is recorded in this repository's history, and the date at the top shows the latest revision. If you keep using a newer version of Sentinel, the terms that ship with that version apply.

## 8. Contact

Questions about these terms: open an issue at <https://github.com/BenScriptsRBX/Sentinel/issues>.
