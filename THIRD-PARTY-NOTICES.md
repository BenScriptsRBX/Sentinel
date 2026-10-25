# Third-Party Notices

Sentinel is licensed under the [MIT License](LICENSE). It builds on the following software and data.

## Software

| Component | Used for | License |
| --- | --- | --- |
| [.NET 10 runtime and WPF](https://github.com/dotnet/runtime) | Application framework (bundled in the self-contained download) | MIT, © .NET Foundation and contributors |
| [System.Management](https://www.nuget.org/packages/System.Management) | Hardware detection through WMI | MIT, © .NET Foundation and contributors |
| [xUnit.net](https://xunit.net/) | Tests only (not shipped) | Apache-2.0 |
| Segoe UI Variable, Segoe Fluent Icons | Fonts and icons | Windows system fonts, used from your Windows installation and not redistributed |

## Data

- **Steam game list:** the names, app IDs and popularity order of the 10,000 most-owned Steam games come from the public [SteamSpy](https://steamspy.com/) API.
- **GOG game list:** names, release years and cover-image addresses come from GOG's public catalog listing.
- **Classification:** engine, genre, demand ratings, feature support and frame-rate locks were written by hand for some games and estimated with AI (Claude) for the rest. AI-estimated entries are marked `"q": 1`.
- **Artwork:** game artwork is downloaded at runtime from Steam's and GOG's public image servers. It is not included in this repository or in releases, and it belongs to its respective owners.

## Trademarks

Steam is a trademark of Valve Corporation. GOG is a trademark of GOG sp. z o.o. Epic Games is a trademark of Epic Games, Inc. Windows and Xbox are trademarks of Microsoft Corporation. NVIDIA, GeForce, DLSS and Reflex are trademarks of NVIDIA Corporation. AMD, Radeon, FSR and FidelityFX are trademarks of Advanced Micro Devices, Inc. Intel, Arc and XeSS are trademarks of Intel Corporation. Game titles are trademarks of their respective owners. Sentinel is not affiliated with or endorsed by any of them.
