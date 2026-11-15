# GameHome

**Your Legion Go. Your entire game library. One home screen.**

Pick up your Lenovo Legion Go, grab the controls, and pick a game. Play it locally, stream it from your gaming PC, jump into your PS5, or fire up a retro favorite. GameHome brings it all together in a controller-first, full-screen launcher built for Windows 11.

[Get GameHome](https://github.com/Kolbi007/gamehome/releases) · [See what's in development](docs/roadmap.md#stabilisering-2026-11-15) · [Build from source](#build-from-source)

## One game. More ways to play.

Got the same game installed on your Legion Go and your gaming PC? GameHome can put both launch options on the same tile. Choose **Play here** when you're out, switch to **Stream** when you're home, or save your preferred way to play. When the session ends, GameHome brings you back to the game you picked.

| What you want to play | How GameHome gets you there |
| --- | --- |
| **PC games on your Legion Go** | Bring in installed games from Steam, Xbox / PC Game Pass, Epic Games Store, GOG and Battle.net, or add a Windows executable yourself. |
| **Games on your gaming PC** | Pick a title and launch straight into its Moonlight stream from a paired host such as Vibepollo. Disconnect and pick up the running game later, or end it from GameHome. |
| **Your PS5** | Open your console's home screen in full-screen Remote Play through a paired chiaki-ng setup. |
| **Retro favorites** | Browse your own ROMs alongside your PC games and launch them through RetroArch with the matching core installed. |

## Made for the Legion Go

- **A home screen you can drive with the controls.** Browse an artwork-rich library, jump back into recent games, or open a title for its details and play options. With the right sources set up, GameHome can fill in covers, logos, backgrounds and trailers.
- **Quick controls while you play.** With Handheld Companion configured, the Legion Go's M2 button opens GameHome's session menu. Resume your game or reach brightness, volume, refresh rate and resolution controls. TDP adjustments go through Handheld Companion.
- **Display settings that follow the game.** Save a resolution and refresh rate for individual titles and restore your usual display settings when you return to Home.
- **A home for Windows Xbox mode.** Use GameHome as a standalone full-screen launcher or try the experimental Gaming Home integration on compatible Windows Full Screen Experience setups.

## Install on your Legion Go

1. Open [GitHub Releases](https://github.com/Kolbi007/gamehome/releases) and download the **x64** MSIX, `GameHome-Dev.cer` and `Install-DevBuild.ps1` from the same release. Keep the three files together.
2. Open PowerShell as administrator in that folder and run the installer with the downloaded MSIX filename. The script trusts the GameHome development certificate and installs the package.

   ```powershell
   .\Install-DevBuild.ps1 -MsixPath ".\<downloaded-file>.msix" -CerPath ".\GameHome-Dev.cer"
   ```

3. Open **GameHome** from Start. Local discovery uses the stores already installed on your device. Set up Moonlight and its host, chiaki-ng, RetroArch, or Handheld Companion for the features you want to use.

Choose **Stable** for official releases or **Beta** to also receive prereleases. Both channels are available in GameHome's power menu. GameHome checks while Home is idle and hands the downloaded update to Windows App Installer for confirmation.

GameHome is in active development. The [stabilization roadmap](docs/roadmap.md#stabilisering-2026-11-15) tracks the remaining Legion Go validation work.

## Build from source

Install the [.NET SDK version pinned by this repo](global.json). Visual Studio is optional.

```sh
# Platform-neutral tests on Windows, macOS or Linux
dotnet test --project tests/GameHome.Core.Tests

# Build an x64 test MSIX on Windows
dotnet publish src/GameHome.App -c Release -r win-x64 -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true
```

[GitHub Actions](https://github.com/Kolbi007/gamehome/actions) builds x64 and ARM64 test packages for pull requests. Look for `gamehome-msix-x64` and `gamehome-msix-arm64` in the workflow artifacts.

See [the architecture notes](docs/architecture.md) for the library, launchers and Windows integration.

## License

GameHome is licensed under [GPL-3.0-or-later](LICENSE). See [Third-Party Notices](THIRD-PARTY-NOTICES.md) for dependency details. External game artwork and metadata remain subject to their providers' terms.
