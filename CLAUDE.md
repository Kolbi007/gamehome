# GameHome – instruksjoner for Claude Code

## Grunnlag

- `docs/product-brief.md` er kravdokumentet for brukeropplevelsen.
- `CLAUDE.md` og ADR-ene i `docs/adr/` gjelder for tekniske valg.
- Ved konflikt mellom dem: spør produkteier (Kolbi007) før du velger.
- `docs/architecture.md` beskriver planen. `docs/assessment.md` har vurderingen og svarene fra produkteier.
- Nye beslutninger skrives som korte ADR-er.

## Bygg og test

- Nøytrale prosjekter: `dotnet test --project tests/GameHome.Core.Tests` (virker på Mac og Linux).
- Windows-app og MSIX: bare på Windows eller i CI (`.github/workflows/ci.yml`).
- Advarsler er feil i CI (`CI=true`). Kjør med `CI=true` lokalt før push.
- Release: tagg `vX.Y.Z` på `main` gir en GitHub-release med signert MSIX (`.github/workflows/release.yml`).

## Produktregler

- **Kontroller:** daglig bruk skal fungere med bare de innebygde kontrollerne på Legion Go. Alt testes med dem. Ekstern Xbox-kontroller er ikke et krav i v1 («kan virke, ikke testet»).
- **Host:** host er et eget begrep. En `PlayAction` som kjører på en annen maskin, peker på en `HostId`. Tilgjengelighet hører til hosten. v1 har én host (gaming-PC-en). Wake-on-LAN støttes der Moonlight klarer det. Timeout gir `Unknown`, aldri antatt «sover».
- **Playnite på hosten:** tillatt, så lenge det er skjult og lett. Det skal aldri synes i normalflyten; unntak er bare oppdatering, innlogging og feil.

### Spillskanning

- blokkerer aldri Home
- kjører aldri mens et spill eller en stream kjører
- en kilde som feiler, påvirker ikke de andre
- hver kilde har synlig helsestatus (`SourceHealth`)
- automatisk sammenslåing slår aldri sammen eksisterende spill
- brukerens overstyringer overskrives aldri

### Meny under spill

- **Xbox-knappen:** Windows eier den. Vi bruker aldri udokumenterte API-er som `XInputGetStateEx`. GameInputs dokumenterte system-knapp-callback kan brukes bare hvis POC B viser at den virker uten å forstyrre Game Bar og Task View.
- **Ett trykk er målet:** brukeren legger knappene om i Handheld Companion (ADR 0006) til sjeldne tastekombinasjoner. Vi registrerer dem med `RegisterHotKey`: menyhurtigtastene bare mens en økt er aktiv, Home-hurtigtasten (Ctrl+Alt+Shift+F11) hele tiden. Vi legger aldri om kontrollere selv, og vi lukker aldri andre programmer med makt.
- **Reservevei:** Xbox → Game Bar → Hjem. Er en økt aktiv, åpner Home-aktiveringen et fullskjermspanel «Kjører nå». Spillet kjører videre bak. Ingen overlay eller injeksjon.
- **Protokoll:** `gamehome://session` henter appen frem via `AppInstance`.
- **Fokus:**
    - «Tilbake til spillet» bruker `SetForegroundWindow`.
    - Vinduet i forgrunnen spores med `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)`, ikke med timer.
    - Ingen omveier som `AttachThreadInput`.
- **Dvale:** hurtigveien krever at appen ligger i dvale, ikke er avsluttet, mens spillet kjører. Ta det med i valget av dvalestrategi.

### Metadata

- Kilder som ikke er `TermsStatus.Approved`, er av som standard. Brukeren slår dem på under Avansert (se ADR 0003).
- IGDB er standard for detaljer, spilletid og score.
- HowLongToBeat og Metacritic er valgfrie, uoffisielle kilder. Innlogging i oppsettet er tillatt.
- Lokale butikkbilder brukes før nett. Mangler bildet, vises en plassholder.

### Steam

- **Kjører-signal:** `HKCU\Software\Valve\Steam\RunningAppID`, lest med `RegNotifyChangeKeyValue`. Oppførselen når Steam krasjer må verifiseres.
- **Reserve:** prosessnavn satt av brukeren i `UserOverrides`.

## Tekniske regler

- **Plattform:**
    - Kode i `GameHome.Core`, `Data`, `Sources`, `Runners` og `Metadata` er plattformnøytral (`net10.0`).
    - Win32 og WinRT hører hjemme i `GameHome.Windows*` og `GameHome.App`.
- **Trimming:** koden skal være trimming-sikker. Bruk source-genererte JSON-serialiserere og ingen refleksjonsbasert avhengighetsinjeksjon.
- **Én prosess på Legion Go:** ingen bakgrunnstjeneste og ikke noe tray-ikon.
- **Mens et spill kjører:** ingen nettverk, disk-IO eller polling. Alt er hendelsesdrevet.
- **Avslutning:** tvungen avslutning av spill bare etter eksplisitt bekreftelse.
- **IPC:** ingen kommunikasjon mellom prosesser i v1. Trengs det senere: named pipe med tilgang begrenset til brukeren eller pakke-SID, og en smal kontrakt. Aldri localhost-HTTP, CORS-wildcard eller loopback-unntak.
- **Personvern:**
    - ingen telemetri og ingen maskinvare-ID
    - alle nettverkskall er dokumentert
    - hemmeligheter ligger i Windows Credential Locker
    - diagnostikk fjerner tokens, brukernavn, IP-adresser og personlige stier
    - brukeren kan se og slette cachen
- **Oppdateringer:** i v1 installerer brukeren nye GitHub-releaser manuelt (`scripts/Install-DevBuild.ps1`).

## Clean room og lisens

- **Lukket programvare:**
    - Aldri dekompilert kode, disassembly, interne symbolnavn eller adresser fra lukket programvare (for eksempel One Game Launcher) som kilde.
    - Ingenting slikt i repoet.
    - Fakta derfra er hypoteser til de er bekreftet med offentlig dokumentasjon, åpen kildekode eller egne tester.
- **Lisens:** GPL-3.0-or-later.
    - Hver `.cs`- og `.xaml`-fil starter med `SPDX-License-Identifier: GPL-3.0-or-later` (testet i `RepositoryRulesTests`).
    - Lånt kode beholder copyright-linjen og registreres med prosjekt, lisens, URL og commit i `THIRD-PARTY-NOTICES.md`.
    - Aldri kode fra prosjekter uten lisens eller med ikke-kommersiell lisens.
- **Pakker:** bruk versjoner som er minst to uker gamle, og legg versjonen i `Directory.Packages.props`.
- **Parsere:** nye parsere for butikkfiler trenger tester med anonymiserte eksempelfiler.

## Commit og PR

- Små PR-er mot `main`. CI må være grønn før merge.
- Copilot reviewer bare på forespørsel fra produkteier (manuelt, med `docs/review-prompt.md`). Foreslå review for større endringer: arkitektur, sikkerhet, nye kilder eller runners. Funn fra Copilot vurderes og besvares punkt for punkt, slik som for produktnotatet.
- Commit-meldinger på engelsk, korte og i imperativ.
