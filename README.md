# GameHome

Et controller-first spillbibliotek for Windows 11 Xbox mode (Full Screen Experience), laget for Lenovo Legion Go.
Ett bibliotek for lokale spill, stream fra desktop-PC (Moonlight mot Vibepollo) og emulatorer.

> Kodenavn: GameHome. Visningsnavnet velges senere.
> Status: skjelett før fase 0A. Se [docs/architecture.md](docs/architecture.md) for plan, beslutninger og faseplan.

## Bygge

Krever .NET SDK 10 (se `global.json`). Visual Studio trengs ikke.

```sh
# Plattformnøytrale prosjekter og tester (Windows, Mac, Linux)
dotnet test --project tests/GameHome.Core.Tests

# Test-MSIX for Windows (x64 eller arm64)
dotnet publish src/GameHome.App -c Release -r win-x64 -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true
```

GitHub Actions bygger alt ved hver pull request og lager en signert test-MSIX som artefakt
(`gamehome-msix-x64` og `gamehome-msix-arm64`).

## Installere på Legion Go

1. Last ned artefaktet `gamehome-msix-x64` fra siste CI-kjøring.
2. Første gang, som administrator: `scripts/Install-DevBuild.ps1 -MsixPath <sti til .msix>`. Skriptet stoler på
   `certs/GameHome-Dev.cer` og installerer pakken.
3. Senere oppdateringer: samme kommando, administrator trengs ikke.

## Prosjekter

| Prosjekt | Innhold | Plattform |
| --- | --- | --- |
| `GameHome.Core` | Modeller, regler, profiler, sesjoner, launch-orkestrering | Nøytral |
| `GameHome.Data` | SQLite, art-cache, overstyringer | Nøytral |
| `GameHome.Sources` | Steam, Xbox, lokale mapper, Moonlight, ROM-er | Nøytral |
| `GameHome.Runners` | Store, Executable, Moonlight, Emulator | Nøytral |
| `GameHome.Metadata` | IGDB, SteamGridDB og valgfrie kilder | Nøytral |
| `GameHome.Windows` | Job Objects, register, skjerm og strøm, single-instance | Windows |
| `GameHome.Windows.Fse` | Eksperimentell FSE-adapter | Windows |
| `GameHome.App` | WinUI 3-klient og MSIX-pakke | Windows |

## Lisens

GPL-3.0-or-later. Se [LICENSE](LICENSE) og [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
Metadata og bilder fra eksterne tjenester er ikke omfattet av lisensen.
