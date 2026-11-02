# Forundersøkelse: neste faser (2026-11-02)

Tre undersøkelser av åpen kildekode og offentlig dokumentasjon, i tråd med clean room-regelen: bare teknikker, ingen
kopiert kode, og ingenting fra lukket programvare. Alt er **hypoteser** til det er testet. «[ubekreftet]» betyr at
kilden ikke ga et sikkert svar.

## Hovedanbefaling og rekkefølge

| # | Hva | Hvorfor nå | Innsats | Produkteier gjør |
| --- | --- | --- | --- | --- |
| 1 | **API-nøkkel til Vibepollo** | Sikre omslag for alle spill fra gaming-PC-en (`/api/apps/<uuid>/cover`), avslutt spill uten Moonlights vindu, og vis at et spill fortsatt kjører på PC-en etter «Koble fra» | S | Lage en nøkkel med smal tilgang i Vibepollos nettside |
| 2 | **Bilder og beskrivelse fra Steam** (CDN og appdetails) | Bakgrunn, logo og beskrivelse for Steam-spill, også på gaming-PC-en; gir også Metacritic-score | S | Avgjøre om en uoffisiell kilde skal være på som standard |
| 3 | **Retro: Game Boy Advance med RetroArch og mGBA** | Fase 3; ett system, én emulator, lovlig homebrew finnes | M (6–9 dager) | Installere RetroArch på Legion Go |
| 4 | **IGDB og SteamGridDB** med brukerens egne nøkler | Spilletid, score og bilder for spill som ikke er på Steam | M | Lage nøkler (Twitch-utviklerkonto, SteamGridDB-konto) |
| 5 | **Flere butikker på Legion Go** | Først når det ligger spill fra dem på Legion Go; spillene på gaming-PC-en dekkes allerede av Playnite via stream | S–L per butikk | Si hvilke butikker som brukes på Legion Go |
| – | WinUIs innebygde kontrollerstyring | Ikke utgitt i noen versjon ennå; vent | – | – |
| – | Game Bar-widget som overlegg | Kan ikke åpnes fra hurtigtasten vår; behold dagens løsning | – | – |

## 1. Vibepollo-API med begrenset nøkkel

Fra Vibepollo `d46e23c7` (GPL-3.0), `src/confighttp.cpp`:

- `GET /api/apps` (L1735): alle apper med `name`, `uuid`, `image-path`, `playnite-id`, og øverst `host_name`.
  Inneholder også kommandoene som er satt opp per app, så nøkkelen kan lese dem.
- `GET /api/apps/<uuid>/cover` (L2234, rute L6516): omslaget; `?v=` for å omgå buffer.
  `GET /api/apps/<uuid>/icon` gir Playnite-ikonet.
- `GET /api/session/status` (L3718): `activeSessions`, `appRunning`, `appName`, `paused`.
- `POST /api/apps/close` (L2341): avslutter appen som kjører. Krever `Content-Type: application/json`.
- **Nøkkel:** lages på `https://localhost:47990/api-tokens` på gaming-PC-en, vises én gang, lagres som hash.
  Sendes som `Authorization: Bearer <nøkkel>`. Stiene er regulære uttrykk som må passe hele stien
  (`src/http_auth.cpp` L217–226). Minste nødvendige tilgang:

  ```json
  {"scopes":[
   {"path":"/api/apps","methods":["GET"]},
   {"path":"/api/apps/[A-Fa-f0-9-]+/(cover|icon)","methods":["GET"]},
   {"path":"/api/session/status","methods":["GET"]},
   {"path":"/api/apps/close","methods":["POST"]}]}
  ```

- **TLS:** egensignert sertifikat på port 47990. Ikke slå av kontrollen; fest sertifikatets SHA-256 første gang
  (som SSH). GameHome kaller gaming-PC-en over LAN, så Vibepollos standard `origin_web_ui_allowed = lan` trengs.
- **Lagring:** nøkkelen i Windows Credential Locker, aldri i logg eller fil.
- **Risiko:** en som får tak i nøkkelen på LAN, kan avslutte spill og lese app-kommandoer. Smal tilgang begrenser det.

## 2. Bilder og metadata

| Kilde | Tilgang | Gir | Status |
| --- | --- | --- | --- |
| Steams lokale bildebuffer | Ingen | Omslag, bakgrunn, logo for Steam-spill på maskinen | I bruk |
| Steam CDN + `store.steampowered.com/api/appdetails` | Ingen nøkkel | Omslag, bakgrunn, logo, kort beskrivelse, `metacritic.score` (testet: Hades gir 93) | Uoffisiell |
| IGDB v4 | Twitch client credentials (brukerens egne) | Beskrivelse, `total_rating`, spilletid (`game_time_to_beats`), omslag og kunstverk | Godkjent, 4 kall/s; ingen hemmelighet i repoet |
| SteamGridDB v2 | Brukerens API-nøkkel | Omslag, bakgrunn, logo, ikon, også for spill som ikke er på Steam | Godkjent; vilkår og grenser [ubekreftet] |
| HowLongToBeat | Ingen eller innlogging | Spilletid | Uoffisiell og ustabil (søkestien har flyttet seg flere ganger) |
| Metacritic | Nøkkel kopiert fra nettleser | Kritikerscore | Uoffisiell og skjør; bruk heller Steams `metacritic`-felt |

**Anbefalt kjede for ett spill:** lokal Steam-buffer → Steam CDN og appdetails → SteamGridDB → IGDB → (valgfritt)
HowLongToBeat og Metacritic. For et Steam-spill holder fire kall; SteamGridDB bare når bilder mangler.

**Beslutning som trengs:** Steam CDN og appdetails er uoffisielle. Etter regelen i CLAUDE.md er de av til brukeren slår
dem på. Alternativ: klassifisere bare **bildene** fra Steams CDN (offentlige butikkbilder, ingen nøkkel) som godkjent,
og la beskrivelse og score være valgfritt.

**Matching av titler:** eksterne ID-er først (Steam-appid via IGDB `external_games` og SteamGridDB `/steam/{id}`),
ellers normalisert tittel (Unicode-normalisering, fjern ™®©, «&» → «and», romertall, utgavesuffikser som GOTY og
Definitive), og en liste å velge fra med kontrolleren når treffet er usikkert.

**IGDB-vilkår:** gratis for ikke-kommersiell bruk, krever kildehenvisning («Data: IGDB.com»). Hver bruker lager sin egen
nøkkel; ingen hemmelighet i repoet. Om 24-timersregelen for lagring gjelder metadata, er uklart [ubekreftet; kan
avklares med IGDB].

## 3. Retro (fase 3)

**Anbefaling: Game Boy Advance med RetroArch og mGBA-kjernen.** Ingen BIOS eller juridiske spørsmål, god lovlig
homebrew, og samme oppskrift dekker senere SNES, NES og PS1.

- **Start:** `retroarch.exe -L "<RetroArch>\cores\mgba_libretro.dll" "<ROM>" -f --appendconfig="<GameHome>\ra-gamehome.cfg"`
  (RetroArch `b143f37`, `retroarch.c` L7876, L7941). Systemoppsett etter ES-DE (MIT), `es_systems.xml` L873–891:
  mappe `gba`, filtyper `.gba .gb .gbc .zip .7z` m.fl.
- **Egen konfig fra GameHome:** fullskjerm, ingen bekreftelse ved avslutning, avslutt med L1+R1+Start+Select, meny med
  L3+R3, ikke lagre konfig ved avslutning.
- **Ferdig-signal:** prosessen avslutter; Job Object er mulig siden GameHome starter emulatoren selv.
- **Lovlig testinnhold:** Butano Fighter og Varooom 3D (zlib og Creative Commons), lastes ned av brukeren; ingenting i
  repoet.
- **Unngå å pakke DuckStation:** lisensen er nå CC BY-NC-ND og passer ikke med GPL. Kan støttes senere som et program
  brukeren installerer selv (`-batch -fullscreen`).

## 4. Flere butikker

Viktig for prioriteringen: spillene på gaming-PC-en (Epic, Ubisoft, EA, Battle.net, Xbox) dekkes allerede av
Playnite-synken via stream. Butikkintegrasjon på Legion Go trengs bare for spill som installeres **på Legion Go**.

**Felles først (M):** en sporing av «kjører spillet?» som ser etter prosesser fra spillets installasjonsmappe, med en
oppstartsperiode. De fleste butikk-lenker returnerer med en gang, så prosessen GameHome starter, er ikke spillet.

| Butikk | Finne installerte spill (uten nett) | Starte | Innsats |
| --- | --- | --- | --- |
| Epic | `%ProgramData%\Epic\EpicGamesLauncher\Data\Manifests\*.item` (JSON) | `com.epicgames.launcher://apps/<ns>%3A<item>%3A<app>?action=launch&silent=true` | S |
| Xbox / Game Pass | `PackageManager.FindPackagesForUser` (capability `packageQuery`), spill kjennes på `MicrosoftGame.config` eller `C:\XboxGames` | `shell:AppsFolder\<PFN>!<AppId>` eller `ActivateApplication` (gir PID) | M–L |
| Ubisoft | `HKLM\SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs\<id>` | `uplay://launch/<id>` | S |
| EA app | `<EA Games>\*\__Installer\installerdata.xml` | `origin2://game/launch?offerIds=<id>` [ubekreftet i nyeste EA app] | M |
| Battle.net | Avinstalleringsoppføringer med `--uid` + `product.db` | `Battle.net.exe --exec="launch <id>"` | M |
| GOG | Avinstalleringsoppføringer `<id>_is1` + `goggame-<id>.info` | Programfilen direkte (DRM-fri) | S |

Kilder: PlayniteExtensions (MIT) `886468f6`, Playnite (MIT) `3e8abb24`, Steam ROM Manager (GPL-3.0) `b142c53d`.
CtrlUI har ingen lisens og er ikke brukt.

## 5. Game Bar-widget som overlegg

- En widget er en egen UWP-app (ikke WinUI 3) med `microsoft.gameBarUIExtension`. Den kan festes over spill og har
  kontrollernavigasjon mens Game Bar er åpen.
- **Kan ikke åpnes fra GameHome eller hurtigtasten:** Game Bar støtter ikke URI-aktivering utenom fra widget til widget
  (Microsoft-dokumentasjonen for `ms-gamebar:`). Brukeren må åpne Game Bar selv.
- Innsats 1–2 uker pluss vedlikehold av en ekstra UI-teknologi. **Anbefaling:** behold hurtigtast og fullskjermspanel;
  vurder en valgfri widget senere.

## 6. WinUIs innebygde kontrollerstyring (`GamepadKeyRouting`)

- Slått sammen i microsoft-ui-xaml (PR #11853, ca. 2026-09-29), men **ikke med i noen utgitt Windows App SDK**
  (2.0.1–2.6.4, sjekket 2026-11-02). Med to-ukersregelen er 2.5.1 nyeste tillatte, og den mangler funksjonen.
- OS-API-et `Windows.UI.Input.GamepadKeyRoutingConfiguration` finnes i SDK 10.0.26100; om WinUI 3 håndterer tastene
  riktig uten rammeverksendringen, er [ubekreftet]. Kan testes bak et flagg.
- **Anbefaling:** behold vår egen kontrollerkode; ta i bruk funksjonen når en utgivelse med den har vært ute i to uker.

## Kilder

- Vibepollo (GPL-3.0): https://github.com/Nonary/Vibepollo
- IGDB: https://api-docs.igdb.com/ og Twitch Developer Agreement: https://www.twitch.tv/p/legal/developer-agreement/
- SteamGridDB-klient (MIT): https://github.com/SteamGridDB/node-steamgriddb
- HowLongToBeat-tillegg (MIT): https://github.com/Lacro59/playnite-howlongtobeat-plugin
- Metacritic-tillegg (MIT): https://github.com/darklinkpower/PlayniteExtensionsCollection
- ES-DE (MIT): https://gitlab.com/es-de/emulationstation-de
- RetroArch (GPL-3.0): https://github.com/libretro/RetroArch; mGBA (MPL-2.0): https://github.com/mgba-emu/mgba
- Butano (zlib): https://github.com/GValiente/butano
- PlayniteExtensions (MIT): https://github.com/JosefNemec/PlayniteExtensions; Playnite (MIT): https://github.com/JosefNemec/Playnite
- Steam ROM Manager (GPL-3.0): https://github.com/SteamGridDB/steam-rom-manager
- PackageManager.FindPackagesForUser: https://learn.microsoft.com/en-us/uwp/api/windows.management.deployment.packagemanager.findpackagesforuser
- Xbox Game Bar: https://learn.microsoft.com/en-us/gaming/game-bar/ og https://github.com/microsoft/XboxGameBarSamples (MIT)
- microsoft-ui-xaml PR #11853: https://github.com/microsoft/microsoft-ui-xaml/pull/11853
