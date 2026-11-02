# OneGame – teknisk sammendrag og plan (for Copilot-review)

Nov 1, 2026 · Kolbi007

## Endringer etter Copilot-review

Copilot har rett i det meste: planen beskrev FSE, prosesseierskap og avslutning av streamøkter sikrere enn kildene gir grunnlag for. Jeg har tatt inn det meste, og i tillegg kuttet komponenter og støy for å holde OneGame lett og ryddig.

| Endring | Hvorfor | Grunnlag |
| --- | --- | --- |
| FSE beskrevet som community-observert mekanisme og isolert i en eksperimentell adapter | Ingen offentlig kontrakt; SCCD med `AllowAny` er ikke en godkjenning fra Microsoft | Copilot |
| Fase 0 tester utviklermodus av, omstart, oppgradering og reinstallasjon | At installasjonen virker, beviser ikke at den overlever oppdateringer | Copilot |
| Host Agent utsatt; Vibepollos Playnite-integrasjon testes først | Funksjonen finnes allerede; én komponent mindre å bygge og vedlikeholde | Begge |
| Ingen global "drep spillet"-kombinasjon; feilsøking via Desktop-appen | Unngår kollisjon med Moonlight og Game Bar, og at et spill avsluttes ved et uhell | Begge |
| Ingen fallback til filskriving i Vibepollo-konfigurasjonen | API med begrenset token er tryggere og enklere | Copilot |
| `OwnsProcess` erstattet av `RunnerCapabilities` og `ISession` | Moonlight-prosessen er ikke spillet på hosten, og avslutning har flere former | Copilot |
| Job Objects uten kill-on-close og med flere ferdig-signaler | Et OneGame-krasj skal ikke ta med seg spillet; completion-varsler er ikke garantert | Copilot |
| `ExternalIdentity`-liste, `Availability.Unknown` og egne terskler for sammenslåing | Ett spill har flere ID-er; timeout betyr ikke "sover"; feil sammenslåing er dyrt å rette | Copilot |
| `Windows.Gaming.Input` i stedet for GameInput i v1; WinUIs gamepad-ruting testes først | Ingen ekstra redistributable og mindre egen kode | Begge |
| Single-instance og videresending av Home- og Library-aktivering | FSE løser ikke all aktivering | Copilot |
| "Lett og ryddig"-prinsipper, enklere Home og én innstillingsside | Ditt ønske om lav ressursbruk og lite støy | Lettvekt |
| Mappeskanner flyttet fra fase 1 til fase 4; manuelt tillegg først | Kortere vei til et brukbart første bygg | Begge |
| Ingen bakgrunnsovervåking av mapper | Unngår en prosess som alltid kjører | Lettvekt |
| ScreenScraper og LaunchBox-dumpen står som ikke godkjent | Vilkårene er ikke avklart | Copilot |
| Ubisoft-launch uten `/0`, Xbox via pakkeaktivering, IGDB-grenser lagt inn | Rettet mot Playnite-kode og IGDB-dokumentasjon | Copilot |
| Faseplanen er en tabell | Copilot kunne ikke lese tegningen | Copilot |
| Driverprinsippet presisert | Vibepollo bruker drivere på hosten; OneGame installerer ingen | Copilot |

**Der jeg ikke følger Copilot fullt ut**

- Copilot foreslo fem nye begreper i datamodellen. Jeg har tatt inn flere ID-er per spill og en egen sesjonsmodell, men utsatt utgaver/varianter og DLC-relasjoner, fordi de gir kompleksitet før noe konkret spill trenger dem.
- Copilot foreslo en ufarlig menyhandling med bekreftelse for feilsøking. I v1 brukes Vibepollos Desktop-app i stedet, fordi det ikke krever noen ny komponent på hosten.

## Beslutninger før bygging

OneGame lisensieres under GPL-3.0-or-later, bygges som egen frontend med ytelse som førstekrav, og utvikles med Claude Code som hovedutvikler og Copilot som reviewer.

**Lisens og gjenbruk**

- OneGame: GPL-3.0-or-later. Det gjør det mulig å låne fra både MIT- og GPLv3-prosjekter, og lar andre bruke koden under samme vilkår.
- Hver fil har SPDX-header (`SPDX-License-Identifier: GPL-3.0-or-later`). Lånt kode beholder opprinnelig copyright-linje og lisensidentifikator i filen.
- `THIRD-PARTY-NOTICES.md` lister prosjekt, lisens, kilde-URL, commit og hvilke filer som bygger på det, med lisenstekstene der lisensen krever det.
- Kode fra GPL-3.0-only-prosjekter gjør det samlede verket GPL-3.0-only; slike filer merkes.
- Ingen kopiering fra prosjekter uten lisens eller med ikke-kommersielle lisenser. Lisensen verifiseres i hvert repo før kopiering.

| Prosjekt | Lisens | Brukes til |
| --- | --- | --- |
| Playnite / PlayniteExtensions | MIT | Butikk-providere, emulatordefinisjoner |
| AnyFSE, FullScreenExperienceShell | MIT | FSE-manifest, SCCD, installer |
| ES-DE | MIT | Systemdefinisjoner |
| ValveKeyValue | MIT | VDF/ACF-parsing som NuGet-avhengighet |
| Heroic, Legendary | GPLv3 | Epic- og GOG-flyt |
| Steam ROM Manager | GPLv3 | ROM-parsing og SteamGridDB |
| moonlight-qt, Vibepollo | GPLv3 | CLI og API, kalles som eksterne prosesser |

**Egen frontend, ytelse først**

Playnite er referanse for logikk, ikke for grensesnitt. Målene under er foreslåtte utgangspunkt som måles i fase 0B og justeres etter første måling:

| Mål | Foreslått grense |
| --- | --- |
| Kaldstart til første kontrollerfokus | under 1,5 s |
| Retur fra spill til Home | under 300 ms |
| Fokusflytt etter knappetrykk | innen ett bilde (16,7 ms ved 60 Hz) |
| Scrolling i bibliotek med 1 000 spill | stabil 60 FPS |
| Bakgrunns-CPU mens spill kjører | tilnærmet 0 % |
| Minne (working set) med 1 000 spill | under 250 MB |

- ReadyToRun og Native AOT vurderes i 0B. Native AOT krever trimming-sikker kode: source-genererte JSON-serialiserere og ingen refleksjonsbasert avhengighetsinjeksjon.
- `x:Bind`, virtualiserte lister, bilder dekodet til visningsstørrelse og all IO utenfor UI-tråden.
- Home tegnes fra cache før noe annet skjer.

**Visuell retning:** konsoll-aktig og cover-fokusert. Store cover i horisontale rader, det fokuserte spillet fremhevet med skalering og hero-bilde i bakgrunnen, mørk bakgrunn og minimalt med tekst og rammer. Designet tar utgangspunkt i Legion Go Z1E og skal være lesbart på armlengdes avstand. Skjermen går i 144 Hz, så fokusflytt bør helst skje innen ett bilde også der (6,9 ms).

**Ressursbruk mens spillet kjører:** spillet eier enheten, og alt OneGame bruker, tas fra spillet. Mens et spill eller en stream kjører, skal OneGame koste så nær null som mulig.

- Dvale ved oppstart av spill: ingen rendering, animasjoner, timere eller input-polling; bildecache frigjøres etter at posisjonen er lagret.
- Dokumenterte Win32-API-er senker prioriteten: EcoQoS, lav minneprioritet, lav prosessprioritet og trimmet working set. Alt gjenopprettes ved retur.
- Hendelsesdrevet: venting på prosess- og job-handles, aldri polling. Reservesjekken går maks hvert 30. sekund.
- Null disk- og nettverksaktivitet og null GPU-arbeid fra OneGame mens spillet kjører. Ingen video- eller blur-effekter og ingen WebView2.
- Mål: under 50 MB working set etter trimming, verifiseres i 0B.
- To strategier måles i 0B: bli liggende i dvale, eller avslutte under spilling og kaldstarte ved retur. Målingene avgjør standarden; det andre alternativet er å avslutte, ikke å bytte rammeverk.
- Dvalen bygges allerede i 0A, med Notepad som stand-in for et spill, slik at ressursbruken måles før noe annet bygges.

**Verktøy og arbeidsform**

- Utvikling skjer på en Mac uten tilknytning til spilloppsettet; testing skjer på Legion Go Z1E (Gen 1, 8,8", 2560×1600, 144 Hz).
- Plattformnøytrale prosjekter (Core, Data, Sources, Metadata, Runners-abstraksjoner og tester) målretter `net10.0` og bygges og testes på Mac med Claude Code.
- Windows-prosjektene (App, Windows, Windows.Fse, Package) bygges og installeres på Legion Go via SSH fra Mac, med Windows' innebygde OpenSSH-server og nøkkelautentisering. Selve testingen gjøres med kontrolleren i hånden.
- GitHub Actions bygger alt på `windows-latest` ved hver pull request og lager en testsignert MSIX som artefakt.
- Copilot reviewer pull requests. Dette dokumentet flyttes til `docs/architecture.md`; endringer framover går som korte beslutningsnotater (ADR) og GitHub-issues.
- Agenten kjører uten adminrettigheter. Installasjonsskriptet for FSE kjøres manuelt etter gjennomlesing.
- Pakkens `Identity Name` er nøytralt og permanent; visningsnavnet kan endres senere.

**Innarbeidet fra Copilot, runde 2**

- Scope frosset til én vertikal flyt i milepæl 1.
- Fem krav designes inn fra start: gjenoppretting etter feil, bevart posisjon, tydelig øktstatus, offline-first og manuell korrigering.
- Tre separate stream-handlinger: koble fra, koble til igjen og avslutt fjernspill.
- Spillemåte huskes per spill; batteri og hoststatus gir bare forslag, og en aktiv økt flyttes aldri.

**Posisjonering og navn (runde 3)**

- **Navnet OneGame forlates.** One Game Launcher (OGL) er et etablert produkt i samme kategori. Kode, prosjekter og pakkens `Identity Name` bruker kodenavnet `GameHome`; visningsnavnet velges separat og kan endres senere. Forslag: Mantel. Overalt i dette dokumentet der det står OneGame, gjelder GameHome.
- **Posisjonering:** et åpent Gaming Home for Windows Xbox mode, ikke enda en universell multi-butikk-launcher. Slike finnes allerede (One Game Launcher, Open Game Launcher, Playnite).
- **Tre definerende egenskaper:** native FSE home app, Game/PlayAction-modellen og Lokalt + Stream + Retro som én opplevelse. Alt annet er ute av v1.
- **Designregel: ett spill, ikke tre kopier.** Én flis og én spillside per spill, med én Spill-knapp og et valg «Spill via» (lokalt, stream, emulator).
- **Senere, ikke v1:** kontroller-markør for vinduer uten kontrollerstøtte, men bare hvis Windows og Legion Space ikke dekker det, og uten global remapping. Lokalt API for tredjeparts-UI-er og integrasjoner via named pipe med tilgang kun for brukeren, ikke HTTP på localhost.

## Mål og rammer

OneGame er en personlig, controller-first game launcher for Lenovo Legion Go, laget for å kjøre som valgbar home app i Windows 11 Xbox mode (Full Screen Experience, FSE). Den distribueres som åpen kildekode på GitHub, ikke via Microsoft Store.

Dokumentet gir Copilot kontekst til å vurdere designet kritisk og verifisere sjekklisten nederst før vi skriver kode. Ikke anta at noe her er riktig fordi det står skrevet; flere punkter bygger på community-observasjoner, ikke dokumentasjon.

**Kjernekrav**

- Daglig bruk krever kun kontroller. Oppsett kan bruke berøringsskjermen på Legion Go.
- Ett bibliotek på tvers av butikker, lokale spill, streaming fra desktop-PC og emulatorer.
- OneGame starter spill via butikkenes egne mekanismer og omgår aldri autentisering, DRM eller oppdateringer.
- Tre bruksmoduser: Lokalt, Stream (Moonlight på Legion Go mot Vibepollo på desktop-PC) og Retro (emulatorer).
- Ingen erstatning av Winlogon-shell eller explorer.exe, ingen ViVeTool, ingen endring i StateRepository og ingen spoofing av enhetstype.
- OneGame installerer ingen egne drivere. Vibepollo på hosten bruker egne drivere for virtuell skjerm og kontroller; det er en bevisst avhengighet, ikke en del av OneGame.

**Lett og ryddig**

- Én prosess på Legion Go: ingen bakgrunnstjeneste, ikke noe tray-ikon og ingen autostart utover FSE home app.
- Butikkklienter kjører bare når et spill trenger dem (se Lettvekts oppstart).
- Ingen bakgrunnsovervåking av mapper; skanning skjer på forespørsel.
- Ingen telemetri.
- Grensesnittet viser spill, ikke infrastruktur: butikk, klient og spillemåte vises bare når brukeren faktisk må velge.
- Nye komponenter, for eksempel en Host Agent, bygges bare når eksisterende løsninger ikke dekker et dokumentert behov.

**Miljø**

| Maskin | Rolle | Programvare |
| --- | --- | --- |
| Lenovo Legion Go | Klient og launcher | Windows 11 24H2 eller nyere, Xbox mode via Legion Space, OneGame, Moonlight PC |
| Desktop-PC | Streaming-host | Windows, Vibepollo, Playnite via Vibepollos integrasjon |

## FSE-registrering: status

En tredjepartsapp kan bli valgbar under Settings → Gaming → Xbox mode → Choose home app, men bare via en Microsoft-eid custom capability som låses opp med utviklermodus. Det finnes ingen offentlig registreringsflyt for ikke-OEM-apper ([Microsoft Q&A](https://learn.microsoft.com/en-za/answers/questions/5582690/expose-the-means-to-register-an-app-as-an-option-f)); dokumentasjonen nevner bare at OEM-er velger standardappen ([Handheld launchers](https://learn.microsoft.com/en-us/xbox/gdk/docs/gdk-dev/pc-dev/handheld/handheld-launchers)).

Mekanismen er observert i [AnyFSE sitt manifest](https://github.com/ashpynov/AnyFSE/blob/main/AppxManifest.xml), som bygger på [FullScreenExperienceShell](https://github.com/driver1998/FullScreenExperienceShell):

```xml
<Application Id="App" Executable="OneGame.exe" EntryPoint="Windows.FullTrustApplication">
  <Extensions>
    <uap3:Extension Category="windows.appExtension">
      <uap3:AppExtension Name="windows.gamingApp" Id="App"
        DisplayName="OneGame" Description="..." PublicFolder="Public" />
    </uap3:Extension>
  </Extensions>
</Application>
<Capabilities>
  <rescap:Capability Name="runFullTrust" />
  <rescap:Capability Name="unvirtualizedResources" />
  <uap4:CustomCapability Name="Microsoft.appCategory.gamingHome_8wekyb3d8bbwe" />
</Capabilities>
```

- AnyFSE er en sparse/identity package (`uap10:AllowExternalContent`) med `TargetDeviceFamily MinVersion="10.0.26100.8039"`.
- `8wekyb3d8bbwe` er Microsofts publisher-ID. En custom capability krever en SCCD signert av eieren; under utviklermodus håndheves ikke signaturen.
- AnyFSE-installeren slår midlertidig på utviklermodus, legger et midlertidig sertifikat i Trusted People, installerer pakken og rydder opp etterpå ([README](https://github.com/ashpynov/AnyFSE)).
- Valgt home app lagres per bruker i `HKCU\Software\Microsoft\Windows\CurrentVersion\GamingConfiguration\GamingHomeApp` ([ElevenForum](https://www.elevenforum.com/t/enable-or-disable-xbox-mode-in-windows-11.42303/)). OneGame skal ikke skrive dit.

**Klassifisering:** community-observert registreringsmekanisme, ikke en offentlig og støttet kontrakt for tredjeparts home apps. FSE-støtten bygges derfor som en eksperimentell adapter (`IHomeIntegration`), og OneGame skal fungere som vanlig fullskjermsapp uten den.

- SCCD-en i AnyFSE har `AuthorizedEntities AllowAny="true"` og en plassholder i `Catalog`, og ligger i pakkeroten. Den er ikke en godkjenning fra Microsoft; pakkesignering og autorisasjon til capability-en er to forskjellige ting.
- Minimumskrav dokumenteres hver for seg: installasjon av OneGame, SDK for FSE-API-ene (10.0.26100.3916 eller nyere), OS-builds der Xbox mode er testet, og Xbox/Game Bar-versjon.
- Library-knappen i Game Bar er et eget akseptansekriterium, ikke noe som følger automatisk av registreringen.
- AnyFSE brukes bare som referanse for den minimale registreringen. Tjenester, DLL-injeksjon for ASUS-knapper og aktivering av handheld-modus arves ikke.
- Microsoft Store er ikke en planlagt vei; en Store-oppføring beviser ikke at capability-en er godkjent.

## Arkitektur og datamodell

Spillet og måten det spilles på er skilt: ett `Game` kan ha flere `PlayAction`, for eksempel Steam lokalt og stream fra desktop. Modusene Lokalt, Stream og Retro er filtrerte visninger med egne standardprofiler, ikke separate kodebaser.

**Datamodell (utkast)**

```csharp
public sealed record Game(
    string Id,
    string Title,
    IReadOnlyList<ExternalIdentity> Identities, // Steam appid, GOG-id, IGDB-id, ROM-hash ...
    ArtSet Art,
    UserOverrides Overrides);                   // vinner alltid over skanning og metadata

public sealed record ExternalIdentity(string Namespace, string Value); // ("steam", "1091500")

public enum PlayKind { Store, Executable, Stream, Emulator }
public enum LaunchMode { Direct, ClientOnDemand, ClientRequired }
public enum Availability { Installed, HostOnline, HostAsleep, Unreachable, Unknown, Missing }

public sealed record PlayAction(
    string Id,
    string GameId,
    PlayKind Kind,
    string RunnerId,             // steam, xbox, exe, moonlight, retroarch ...
    string Target,               // appid, exe-sti, host-app-navn, ROM-sti
    string? Arguments,
    string? WorkingDirectory,
    LaunchMode LaunchMode,
    string? ProfileId,
    Availability Availability);  // timeout gir Unknown, aldri antatt HostAsleep

public sealed record DiscoveryResult(
    string SourceId,
    PlayAction Candidate,
    string? SuggestedTitle,
    IReadOnlyList<ExternalIdentity> Identities,
    double Confidence,
    IReadOnlyList<string> Evidence);
```

Oppdagelse og sammenslåing har separate terskler: høy konfidens kan legge til en ny PlayAction, men to eksisterende spill slås aldri sammen automatisk. Utgaver/varianter og DLC-relasjoner utsettes til et konkret spill krever det.

**Kjernegrensesnitt**

```csharp
[Flags]
public enum RunnerCapabilities
{
    None = 0,
    ObserveExit = 1,        // kan oppdage at spillet er ferdig
    RequestClose = 2,       // kan be om normal avslutning
    ForceTerminate = 4,     // kan tvangsavslutte, kun etter bekreftelse
    OwnsProcessGroup = 8,   // spillet kjører i OneGames Job Object
    Detach = 16             // kan koble fra uten å stoppe spillet (stream)
}

public interface IRunner
{
    string RunnerId { get; }
    RunnerCapabilities Capabilities { get; }
    Task<ISession> LaunchAsync(PlayAction action, Profile profile, CancellationToken ct);
}

public interface ISession
{
    SessionKind Kind { get; }          // Local | Stream
    Task<SessionEnd> Completion { get; }
    Task RequestCloseAsync();
    Task ForceTerminateAsync();        // kun etter eksplisitt bekreftelse
}
// Stream: "Moonlight-prosessen er ferdig" og "spillet på hosten er ferdig" er to separate hendelser.

public interface IGameSource
{
    string SourceId { get; }
    IAsyncEnumerable<DiscoveryResult> DiscoverAsync(CancellationToken ct);
}

public interface IMetadataProvider
{
    Task<IReadOnlyList<MetadataCandidate>> ResolveAsync(MetadataQuery query, CancellationToken ct);
}

public interface IPlayActionSelector
{
    PlayAction? SelectDefault(Game game, DeviceContext context); // strøm, nettverk, hoststatus
}

public interface IHomeIntegration      // eksperimentell FSE-adapter, kan mangle helt
{
    bool IsAvailable { get; }
    bool IsFullScreenExperienceActive();
    event EventHandler FullScreenExperienceChanged;
}
```

**Prosjektstruktur**

```text
OneGame/
├── src/
│   ├── OneGame.App/          WinUI 3-klient: Home, Library, Search, Settings, Nye funn
│   ├── OneGame.Core/         Modeller, regler, profiler, sesjoner, launch-orkestrering
│   ├── OneGame.Windows/      Job Objects, skjerm- og strøm-API-er, single-instance
│   ├── OneGame.Windows.Fse/  Eksperimentell FSE-adapter, isolert
│   ├── OneGame.Data/         SQLite, art-cache, overstyringer
│   ├── OneGame.Sources/      Steam, Xbox, LocalFolder, Moonlight, Roms (flere i fase 4)
│   ├── OneGame.Runners/      Store, Executable, Moonlight, Emulator
│   ├── OneGame.Metadata/     IGDB, SteamGridDB (øvrige senere)
│   └── OneGame.Package/      Identity package, manifest, SCCD for utviklermodus
├── definitions/              systems/, emulators/, scanners/exclusions.yaml
├── THIRD-PARTY-NOTICES.md    Lånt innhold med kilde-commit
└── tests/                    Parser-tester med anonymiserte eksempelfiler
```

En eventuell Host Agent blir et eget prosjekt i samme repo, uten avhengighet til WinUI, klientens database eller klientens livsløp.

**Teknologivalg**

- C# på gjeldende .NET LTS, WinUI 3 og Windows App SDK med self-contained deployment.
- Packaged full-trust app som identity package; FSE-registreringen ligger i en egen adapter.
- Input i v1: WinUIs innebygde gamepad-ruting (opt-in) og XYFocus testes først, med `Windows.Gaming.Input` som egen adapter der plattformen ikke dekker behovet. GameInput utsettes, fordi det krever en egen redistributable.
- Single-instance via Windows App SDK (`AppInstance`), med videresending av Home- og Library-aktivering.
- SQLite i `%LocalAppData%\OneGame`, definisjoner som YAML, overstyringer eksporterbare som JSON.

## Kilder: butikker og lokale spill

Hver kilde returnerer funn med konfidens; høy konfidens legges til automatisk, lav konfidens går til "Nye funn"-køen der brukeren velger riktig treff med kontrolleren. Ingen butikk tilbyr et dokumentert lokalt API, så alt under unntatt Xbox er de facto-mekanismer som må verifiseres.

**Butikk-providere**

| Provider | Discovery | Launch | Fase |
| --- | --- | --- | --- |
| Steam | `libraryfolders.vdf` + `appmanifest_*.acf` | `steam.exe -silent -applaunch <appid>` | 1 |
| Xbox / Game Pass | Pakkeidentitet via `PackageManager`; `MicrosoftGame.config` som hint, ikke komplett API | AUMID via `IApplicationActivationManager` | 4, testes mot GDK- og eldre pakker |
| Epic | Manifest-JSON under `ProgramData\Epic\EpicGamesLauncher\Data\Manifests` | `com.epicgames.launcher://apps/...?action=launch&silent=true` | 4 |
| GOG | `goggame-*.info` og registernøkler under `GOG.com\Games` | Direkte exe fra `playTasks` | 4 |
| Ubisoft | Registernøkler og lokal konfigurasjonscache | `uplay://launch/<id>` (uten `/0`, som i Playnite) | 4 |
| EA | Ingen stabil manifestkilde | Protokoll via EA-appen | Senere |
| Battle.net | `product.db` (protobuf) | `Battle.net.exe --exec="launch <kode>"` | Senere |

Playnites provider-kode, nå flyttet til Codeberg, brukes som referanse knyttet til en fast commit. En provider godkjennes først når den har parser-test med anonymiserte eksempelfiler, launch-test på installert klient, og test av manglende klient, oppdatering og innloggingsdialog. PID fra AUMID-aktivering gjelder appinstansen som tar imot aktiveringen, ikke nødvendigvis hele spillets prosesslivsløp. Prototypens `ms-xboxgamingoverlay://` åpner Game Bar og fjernes.

**Lokale spill i C:\\Spill**

Hver undermappe (konfigurerbar dybde) er en kandidat. Signaler i prioritert rekkefølge:

| Signal | Gir |
| --- | --- |
| `goggame-*.info` | Eksakt navn, GOG-ID og riktig exe via `playTasks` |
| `steam_appid.txt` | Eksakt Steam-ID |
| `.itch/receipt.json.gz` | itch.io-identitet |
| PE-versjonsinfo (ProductName, FileDescription) | Titteltekst |
| Renset mappenavn | Fuzzy-søk mot IGDB |

Valg av exe:

- Ekskluder mønstre fra `definitions/scanners/exclusions.yaml`: `unins*`, `*setup*`, `vcredist*`, `dxsetup*`, `CrashReport*`, `UnityCrashHandler*`, `_CommonRedist`, anti-cheat-mapper.
- Ranger resten etter GUI-subsystem, nærhet til mapperoten, likhet med mappenavnet, filstørrelse og motormønstre (Unreal, Unity).
- Skanning skjer på forespørsel fra innstillinger eller "Nye funn", uten bakgrunnsovervåking. I fase 1 legges lokale spill til manuelt; skanneren kommer i fase 4.

**Metadata og grafikk**

- Identitet før fuzzy-søk: eksakt ID når den finnes, ellers tittelsøk med konfidens.
- IGDB for spillinfo: Client ID og Client Secret, maks 4 forespørsler per sekund og 8 samtidige, gratis for ikke-kommersiell bruk under gjeldende vilkår.
- SteamGridDB for grafikk: ingen antatt rate limit, men caching og håndtering av throttling.
- OneGames lisens gjelder ikke metadata og bilder fra disse tjenestene.
- Brukerens overstyringer lagres separat og overskrives aldri. Alt caches lokalt, og appen fungerer offline etter første henting.

## Lettvekts oppstart

OneGame starter spillet direkte når spillet tåler det, og starter ellers butikkens klient først når et spill trenger den, i stille modus. Klienten lukkes etter spillet bare hvis OneGame startet den.

| Kilde | Krever klient | Oppstart i OneGame | Merknad |
| --- | --- | --- | --- |
| GOG | Nei | Direkte exe fra `playTasks` i `goggame-*.info` | Galaxy trengs bare for skylagring og achievements |
| Lokale spill, itch.io | Nei | Direkte exe | Eid prosess i Job Object |
| Emulatorer | Nei | Direkte | Eid prosess i Job Object |
| Xbox / Game Pass | Nei, Xbox-appen trengs ikke | AUMID-aktivering | Lisens via Gaming Services, som kjører uansett |
| Steam | Ja, for spill som bruker Steamworks | `steam.exe -silent -applaunch <appid>` ved behov | Startes exe-en direkte, kan spillet selv starte Steam på nytt |
| Epic | Varierer per spill | Epic-klienten ved behov; Legendary som valgfritt alternativ | Spill med eierskapssjekk krever innlogging |
| Ubisoft, EA, Battle.net | Ja | Klienten ved behov | Klienten er en del av DRM og innlogging |

```csharp
public enum ClientLifecycle { LeaveRunning, CloseIfStartedByOneGame, AlwaysClose }
// LaunchMode er definert i datamodellen; hver provider oppgir standardverdi, brukeren kan overstyre per spill
```

- **Ingen klienter ved pålogging:** autostart skrus av i Steam, Epic og de andre klientene, slik at OneGame bestemmer når de kjører.
- **Standard livssyklus** er `CloseIfStartedByOneGame`, slik at en klient brukeren selv har åpnet aldri lukkes.
- **Lukking etter spill** skjer først når skylagring er synkronisert. Inntil det kan oppdages pålitelig, brukes en konfigurerbar forsinkelse.
- **Bedre sporing:** direkte oppstart gjør spillet til en eid prosess i Job Object, med pålitelig avslutningsvarsel.
- **Tap ved direkte oppstart** vises i kontekstmenyen, for eksempel at GOG-skylagring krever Galaxy.
- **Legendary** er en uoffisiell Epic-klient som bruker Epics egen innlogging. Den kjøres som ekstern prosess, er av som standard, og brukeren tar selv stilling til Epics vilkår.
- **Samme prinsipp på hosten:** Host Agent starter klienter i stille modus, så ingen klientvinduer dukker opp i streamen.

## Stream-modus på Legion Go

Moonlight kjøres som ekstern prosess fra OneGame, og hvert streambart spill blir en ekstra PlayAction på samme flis som den lokale versjonen. Brukeren ser ett spill med valgene "Spill her" og "Stream fra desktop".

- **Lisens og isolasjon:** moonlight-qt er GPLv3. Ingen kode bygges inn; `Moonlight.exe` startes som egen prosess.
- **Klientvalg:** exe-stien er konfigurerbar. Vanlig Moonlight fungerer mot Vibepollo for ordinær H.264-, HEVC- og AV1-streaming, mens VRR-pacing og DualSense-haptikk krever Nonarys Moonlight-fork ([Vibepollo README](https://github.com/donny110/Vibepollo)). Om forken har samme CLI, er uavklart.
- **Paring:** gjøres én gang i Moonlights eget grensesnitt. OneGame implementerer ikke paring.
- **Versjonsfestet CLI-adapter:** `list` (med `--csv`), `stream` og `quit`, med `--resolution`, `--fps`, `--bitrate` (Kbps) og `--video-codec`. `quit` avslutter appen på hosten, ikke bare den lokale klienten. Adapteren testes mot den støttede binærfilen: `--help`, stdout, stderr, tegnsett, CSV-felt og exit-koder.
- **App-liste:** hentes med `list --csv` og caches i SQLite. Navnene kommer fra Vibepollos Playnite-synk og matches mot biblioteket; usikre treff går til "Nye funn".
- **Oppstart og vekking:** `stream <host> "<app>"` med flagg fra profilen. Moonlight PC 6.1.0 vekker hosten ved CLI-start ([release notes](https://github.com/moonlight-stream/moonlight-qt/releases)), men Wake-on-LAN avhenger av strømtilstand, maskinvare og nettverk; dvale, hvile og avslått maskin behandles ulikt.
- **Avslutning:** "Moonlight-prosessen er ferdig" og "spillet på hosten er ferdig" er separate hendelser og spores hver for seg. Job Object-varselet er et signal, ikke eneste sannhetskilde.
- **Standardvalg:** sist valgte spillemåte huskes per spill. For spill uten valg foreslås stream på batteri med host tilgjengelig, og lokalt på lader. Endringer gjelder neste oppstart, og en aktiv økt flyttes aldri. Ved utilgjengelig eller ukjent host skjules stream-valget.
- **Batteriprofil:** skjermfrekvensen senkes til stream-FPS via `ChangeDisplaySettingsEx` og gjenopprettes etterpå. Moonlights avslutningskombinasjon (LB + RB + View + Menu) vises ved oppstart.
- **Nåbarhet:** timeout gir `Unknown`, aldri antatt "sover". Sunshine-standardportene (TCP 47984–47990 og 48010, UDP 47998–48000) er utgangspunkt og verifiseres mot valgt Vibepollo-versjon.

**Tre separate stream-handlinger**

- **Koble fra:** streamen lukkes, og spillet fortsetter på desktop-PC-en. Flisen viser at spillet kjører på desktop.
- **Koble til igjen:** gjenopptar den eksisterende fjernøkten.
- **Avslutt fjernspill:** egen handling med bekreftelse, som avslutter appen på hosten (`quit`).
- Øktstatus vises tydelig: Starter, Kjører, Stream koblet fra, Spill avsluttet.
- Det må verifiseres om Moonlights LB + RB + View + Menu bare kobler fra, eller også avslutter appen på hosten.

## Host-siden: Vibepollo og Playnite

v1 bygger ingen egen agent på desktop-PC-en. Vibepollos eksisterende Playnite-integrasjon testes først for å starte bare spillet og avslutte økten ryddig; en Host Agent bygges bare hvis testen avdekker et dokumentert behov. Eksempelet gjennom avsnittet er 007 First Light.

**Problemet som må løses:** butikk-launch via protokoll (`steam://` og lignende) returnerer umiddelbart. Uten en prosess som lever like lenge som spillet, vet ikke hosten når spillet er ferdig, og brukeren ender på host-skrivebordet. Playnite-integrasjonen skal være den prosessen; Host Agent er reserveplanen.

**Utgangspunkt:** Vibepollo har allerede Playnite-integrasjon som ifølge README håndterer grafikk, oppstart og ryddig avslutning, også for emulatorer ([Vibepollo](https://github.com/Nonary/Vibepollo)). Repoet har også et eget playnite\_launcher-verktøy, men det finnes feilrapporter om prosessporing og connector-kommunikasjon, så integrasjonen må testes før vi stoler på den.

**Testes i fase 2**

- 007 First Light og to–tre spill fra andre butikker: oppstart viser bare spillet, ikke skrivebordet.
- Spillet avsluttes normalt: økten avsluttes, Moonlight lukkes, og OneGame er tilbake.
- Disconnect og reconnect uten at spillet stopper.
- Vekking fra hvile og innlogging fra låseskjermen.
- App-innstillingene `wait-all`, `auto-detach` og exit-timeout settes eksplisitt, fordi en launcher som avslutter raskt ellers kan behandles som detached.

**Feilsøking i v1**

- Ingen global "drep spillet"-kombinasjon. Moonlights LB + RB + View + Menu avslutter streamen på klienten.
- For å avslutte spillet og gå rett til skrivebordet: koble fra og start Vibepollos "Desktop"-app. Moonlight spør da om det kjørende spillet skal avsluttes (verifiseres). Desktop-appen er skjult i vanlig visning, men tilgjengelig under Innstillinger.
- Et eventuelt feilsøkingspanel i en senere Host Agent starter med en ufarlig menyhandling og krever eksplisitt bekreftelse før tvungen avslutning.

**Vibepollo-API**

- `GET /api/apps`, `POST /api/apps`, `DELETE /api/apps/{index}` og `POST /api/apps/close`.
- Tokenet begrenses til disse stiene og metodene og lagres i Credential Locker.
- Ingen fallback til direkte skriving i konfigurasjonsfiler; feiler API-et, vises en tydelig feil.

**Reserveplan: Host Agent**

Bygges bare hvis testene over viser et behov integrasjonen ikke dekker. Designet beholdes i kortform: én generisk forwarder som starter spillet bak en svart splash, følger spillets prosess, avslutter når spillet er ferdig, bruker ikke kill-on-close og snakker bare med Vibepollo via API-et.

**Innlogging etter vekking:** hosten kan stå på låseskjermen. Vibepollo kan fange innloggingsskjermen, så innlogging med PIN via streamen er å foretrekke framfor autologon, som lagrer passordet på maskinen.

## Fase 2: funn fra kildekoden (2026-11-02)

Lest fra åpen kildekode (GPLv3), bare lest, ingenting bygget eller kjørt: moonlight-qt `1ced9dd9` og Vibepollo
`d46e23c7` (med Playnite-tillegget i `plugins/playnite/SunshinePlaynite`). Stier under er relative til repoene.
Alt er **hypoteser** til det er testet på Legion Go. Installert på gaming-PC-en: Vibepollo 2.0.2-nightly
(2026-10-30) og Playnite-tillegget 0.5.2.

### Moonlight-kommandolinjen

| Kommando | Funn | Kilde |
| --- | --- | --- |
| `list <host> --csv` | Header `Name, ID, HDR Support, App Collection Game, Hidden, Direct Launch, Boxart URL`, så én rad per app. Navn står i anførselstegn, men anførselstegn inni navnet escapes ikke. Exit 0 ved suksess, -1 når hosten ikke finnes eller ikke er paret, 1 ved feil i app-listen. Søket gir opp etter 30 s. | `app/cli/listapps.cpp:9,63-141`, `app/main.cpp:1033-1039` |
| `stream <host> "<app>"` | Navnet må matche eksakt, uten hensyn til store/små bokstaver. App-ID kan ikke brukes. Kjører en annen app, vises en Ja/Nei-dialog; ukjent app gir en feildialog. Prosessen avslutter når streamen er ferdig. Trenger skrivebord (QML-vindu). | `app/cli/startstream.cpp:103-151`, `app/gui/CliStartStreamSegue.qml:20-90`, `app/gui/StreamSegue.qml:76-78` |
| `quit <host>` | Ingen bekreftelse, exit 0 ved suksess. Feil vises som dialog som må lukkes. Trenger skrivebord. | `app/cli/quitstream.cpp:67-103` |
| `--quit-after` | Avslutter appen på hosten når streamen slutter normalt. Brukes ikke som standard, fordi «koble fra» og «avslutt fjernspill» skal være separate. | `app/streaming/session.cpp:1271-1274` |
| Paring og vertsliste | `HKCU\Software\Moonlight Game Streaming Project\Moonlight` (QSettings). Kommandolinjen bruker samme paring som Moonlight-appen; hosten kan oppgis med navn, UUID eller IP. GameHome leser aldri nøkler eller sertifikater herfra. | `app/main.cpp:431-443`, `app/backend/computerseeker.cpp:50-66` |

**Hvorfor `list` hang over SSH (hypotese):** Moonlight er et GUI-program og starter SDL-video, `QGuiApplication` og
en QML-motor også for `list` (`app/main.cpp:681-700,765,995`). I SSH-økten (sesjon 0, uten skrivebord) blir det
sannsynligvis stående. Konsekvens for GameHome: kall Moonlight fra GameHome (som kjører på skrivebordet) med
`CreateNoWindow`, les både stdout og stderr asynkront (stderr brukes til logging og kan ellers fylles og blokkere,
`app/main.cpp:146-152,463-479`), og avbryt selv etter 70 s.

### Vibepollo og Playnite

- **Hvilke spill synkes:** standard er bare de **10 sist spilte** innenfor 30 dager (`playnite_recent_games`,
  `playnite_recent_max_age_days`). `playnite_sync_all_installed` finnes, men er av. Automatisk lagt til spill slettes
  etter 14 dager hvis de ikke er spilt (`playnite_autosync_delete_after_days`, 0 slår det av).
  Kilde: `src/config_playnite.h:18-73`, `src/platform/windows/playnite_sync_policy.cpp:293-395,575-590`.
- **Synkede spill skrives til `apps.json`** med Playnite-navnet og Playnite-ID-en som UUID, og kommer dermed med i
  Moonlights `list` (`src/confighttp.cpp:263-283`, `src/platform/windows/playnite_sync_policy.cpp:95-185`). At listen
  i dag bare har Desktop og Steam Big Picture tyder på at synken ikke har kjørt mens Playnite var oppe.
- **Oppstart:** Vibepollo kjører `tools\playnite-launcher.exe --game-id <id>`, som starter Playnite ved behov og ber
  tillegget starte spillet (`src/process.cpp:2549-2631`, `tools/playnite_launcher/launcher.cpp:93-103,1022-1037`).
  Playnite startes uten argumenter, så **Playnites egne innstillinger avgjør om det vises**. Endret 2026-11-02 etter
  godkjenning: start minimert i systemstatusfeltet, ikke i fullskjerm, og minimer etter at et spill er startet (kopi
  av innstillingene ligger ved siden av `config.json`).
- **Avslutning:** når spillet slutter, avslutter hjelperen og streamen ender. `quit` sender `stop` til Playnite, og
  hjelperen lukker prosessene i spillets mappe, først normalt, så med tvang (`src/process.cpp:3259-3280`,
  `tools/playnite_launcher/launcher.cpp:1208-1214,1672-1679`).
- **Synk krever at Playnite er oppe når Vibepollo kobler til** (ved oppstart, endring av innstillinger, API-kall og
  under en økt), ikke hele tiden (`src/platform/windows/playnite_integration.cpp:330-341,518-556,817`).
- **Web-API** på HTTPS 47990, LAN tillatt som standard: `GET /api/apps`, `POST /api/apps/launch` (starter uten stream),
  `POST /api/apps/close`, `GET /api/session/status`, `POST /api/playnite/force_sync`. **API-token** med regex-avgrensede
  stier og metoder, sendt som `Authorization: Bearer`; bare hashen lagres (`src/confighttp.cpp:5937-5953,6461-6600`,
  `src/http_auth.cpp:201-256`). Ingen Wake-on-LAN-endepunkt.
- **Moonlight-protokollen** godtar `appuuid` ved `/launch`, men Moonlights kommandolinje bruker bare navn
  (`src/nvhttp.cpp:3288-3289`).

### Anbefalt løsning (fase 2)

| Hvor | Hva |
| --- | --- |
| **GameHome (Legion Go)** | Ny kilde `moonlight`: `list <host> --csv` gir host-spillene, cachet i biblioteket. Hvert spill får en `Stream`-PlayAction med `HostId`; samme tittel som et lokalt spill blir en ekstra spillemåte på samme flis, usikre treff går til «Nye funn». Start med `stream <host> "<navn>"`; økten er Moonlight-prosessen. «Avslutt fjernspill» = `quit <host>` etter bekreftelse. Egen CSV-leser som tåler anførselstegn i navn, og egen tidsgrense. |
| **Gaming-PC-en** | **Én innstilling i Vibepollo:** synk alle installerte Playnite-spill (`playnite_sync_all_installed=true`) og slå av automatisk sletting (`playnite_autosync_delete_after_days=0`). Playnite minimert i bakgrunnen (gjort). Ellers ingenting nytt. |
| **Legion Go** | Moonlight er installert og paret (gjort). |
| **Senere, valgfritt** | API-token i Vibepollo med bare lesetilgang til `/api/session/status` og `/api/apps`, lagret i Credential Locker, for å vise «kjører på PC-en» og hente UUID. Wake-on-LAN sendes av GameHome selv med MAC-adressen fra Moonlights vertsliste. |

**Hull og hva vi gjør med dem**

- Dialoger i `stream` og `quit` når noe er galt (annen app kjører, ukjent app): GameHome sjekker listen før start og
  kjører `quit` først ved behov. Bruker kan svare på Moonlights dialog med kontrolleren. Ingen fork.
- CSV uten escaping av anførselstegn: vår leser håndterer det. Mulig PR til moonlight-qt senere (riktig CSV-escaping,
  eventuelt en `--no-dialogs`-modus). Fork er siste utvei og ikke nødvendig nå.

### Skjermoppsett på gaming-PC-en (funn 2026-11-02)

Lest fra Vibepollo `d46e23c7` (nyeste commit 2026-11-01), bare lest. Alt er **hypoteser** til det er testet.
Gaming-PC-ens oppsett i dag: `dd_configuration_option = ensure_only_display`, Vibepollos virtuelle skjermdriver på,
ingen globale eller app-egne prep-kommandoer.

**Hvordan skjermen settes opp**

- `dd_configuration_option` har verdiene `disabled`, `verify_only` (standard), `ensure_active`, `ensure_primary` og
  `ensure_only_display`. Kan også settes per app (`dd-configuration-option`) og per klient
  (`src/config.cpp:523-533,945`, `src/process.cpp:4761-4778`, `docs/configuration.md:1722-1740`).
- **Med virtuell skjerm styres oppsettet av `virtual_display_layout`**, ikke av `dd_configuration_option`:
  `exclusive` (standard) gjør den virtuelle skjermen til eneste aktive skjerm; `extended`, `extended_primary`,
  `extended_isolated` og `extended_primary_isolated` holder laptopskjermen på
  (`src/config.cpp:835-846,935`, `src/platform/windows/display_helper_request_helpers.cpp:54-68,317-323,496,569-574`).
- **Konsekvens:** oppsettet i dag gir allerede det målet krever: spillet kan bare havne på den virtuelle skjermen, og
  ingenting fra laptopskjermen vises. Det skrivebordet som vises ved tilkobling, er **den virtuelle skjermens eget
  skrivebord** (bakgrunn, ikoner og oppgavelinje) før spillvinduet kommer. Forslaget om virtuell skjerm som primær
  med laptopskjermen på (`extended_primary`) gir en ekstra skjerm der spill kan åpnes, og løser ikke problemet.
- Vibepollo flytter ikke spillvinduer; det er oppsettet som sørger for at spillet havner riktig. Spill som husker
  forrige skjerm, håndteres bare ved at det ikke finnes noen annen skjerm (`tools/playnite_launcher/focus_utils.cpp:179-182`).

**Tilkobling, frakobling og gjenoppretting**

- Før endring lagres en grunnlinje (`display_session_current.json` i en mappe under ProgramData som bare SYSTEM og
  administratorer har tilgang til), og det opprettes en planlagt oppgave `VibepolloDisplayRestore` som gjenoppretter
  ved pålogging (`tools/display_settings_helper.cpp:3295-3307,5213,5348`, `tools/display_helper_paths.h:173-180`).
- Gjenoppretting skjer når appen avsluttes. Ved frakobling bare hvis `dd_config_revert_on_disconnect` er på (av som
  standard); ellers beholdes den virtuelle skjermen i opptil 2 timer (`dd_paused_virtual_display_timeout_secs = 7200`)
  så økten kan gjenopptas (`src/process.cpp:3453-3456`, `src/stream.cpp:3837-3888`).
- Stopper Vibepollo å svare, gjenoppretter hjelperen etter ca. 2,5 minutter. Ved oppstart ryddes en virtuell skjerm
  uten stream (`tools/display_settings_helper.cpp:2724-2726,2842-2859`, `src/main.cpp:940-945`).

**Skjule skrivebordet**

- **Ingen innebygd funksjon** for svart skjerm, dekkbilde, skjult skrivebord eller vent på spillvindu. Det nærmeste er
  at Vibepollo setter spillvinduet i forgrunnen (`app_focus_attempts`, `app_focus_timeout_secs`).
- **Prep-kommandoer** kan brukes: `global_prep_cmd` (alle apper) eller `prep-cmd` per app, med `do`, `undo` og
  `elevated`. De kjører som den påloggede brukeren, så de kan endre brukerens bakgrunn og oppgavelinje. Feiler `do`,
  avbrytes oppstarten. `undo` kjøres når appen avsluttes, ikke ved frakobling, og ikke etter et krasj
  (`src/process.cpp:2469-2498,3326-3375,4569-4597`, `src/platform/windows/misc.cpp:716-740,1544-1548`).
- Miljøvariabler som `SUNSHINE_APP_NAME` og `SUNSHINE_CLIENT_WIDTH` er tilgjengelige for kommandoene
  (`src/process.cpp:1804`).

### Anbefalt skjermoppsett (hypotese, ikke innført)

| Del | Hva | Krever administrator |
| --- | --- | --- |
| Skjermoppsett | Behold dagens: virtuell skjerm, `virtual_display_layout = exclusive` (standard). Ingen endring. | – |
| Bakgrunn | Rolig bakgrunn **bare for den virtuelle skjermen** via Windows' bakgrunn per skjerm (`IDesktopWallpaper`). Windows husker valget per skjerm, så laptopskjermen beholder sin. Hypotese: den virtuelle skjermen får samme ID hver gang. | Nei |
| Ikoner og oppgavelinje | `global_prep_cmd` med et lite PowerShell-skript fra repoet (`tools/host/`): `do` skjuler skrivebordsikoner og setter oppgavelinjen til automatisk skjult og lagrer forrige verdier; `undo` setter dem tilbake. Skriptet avslutter alltid med kode 0, så en feil aldri stopper spillet. | Å legge inn `global_prep_cmd` i Vibepollo (nettsiden eller `sunshine.conf`). Skriptet kjører som bruker. |
| Sikkerhetsnett | Samme skript med `restore` kjøres ved pålogging (planlagt oppgave for brukeren), siden `undo` ikke kjøres etter krasj eller ved frakobling. | Nei (brukeroppgave) |

**Risiko**

- **Strømmen brytes midt i:** skjermen gjenopprettes av Vibepollo (logon-oppgave, hjertelyd, opprydding ved oppstart).
  Ved vanlig frakobling står den virtuelle skjermen i opptil 2 timer, med laptopskjermen av; det er meningen.
- **Ikoner og oppgavelinje** blir stående skjult til appen avsluttes, eller til neste pålogging hvis Vibepollo krasjer.
  Sikkerhetsnettet over dekker det.
- **Alternativ uten endringer i Windows:** GameHomes lasteskjerm dekker Moonlight til Vibepollo melder at appen kjører
  (`GET /api/session/status`). Krever API-token, men ingen endring på skrivebordet. Ser grovere ut enn en rolig
  bakgrunn, men har ingen risiko for å etterlate skjulte ikoner.

## Lasteskjerm: lærdom fra Stremio og Nuvio (2026-11-02)

Lest fra åpen kildekode; bare teknikkene tas med, ingen kode kopieres: Stremio web `fd9a6250` (GPL-2.0) og Nuvio
`46febb56` (GPL-3.0, Kotlin Multiplatform).

| | Stremio | Nuvio |
| --- | --- | --- |
| Bakgrunn | Fanart som fyller skjermen, opasitet 0,6 over mørk bunn, ingen uskarphet (`src/routes/Player/styles.less:48-55`) | Bakgrunn eller plakat, justert øverst til høyre, under et slør fra topp til bunn: svart 0,3 → 0,6 (35 %) → 0,8 (70 %) → 0,9 (`OpeningOverlay.kt:96-117`) |
| Logo | Midt på, maks 15rem; tegnes to ganger, en svak kopi (0,2) og en som «fylles» fra venstre etter fremdrift (`Buffering/Buffering.tsx:13-33`) | Midt på, maks 320 × 180 dp; tittel i ExtraBold som reserve, deretter spinner (`OpeningOverlay.kt:85-86,150-208`) |
| Animasjon | «Pulse» 2 s: opasitet 0,4↔1 og skala 1↔1,05 (`Buffering.less:14,23-36`) | Toner inn 700 ms etter 400 ms, så «puster» 1,0↔1,04 over 2 s (`OpeningOverlay.kt:70-84`) |
| Status | Ingen tekst | Statuslinje rett under logoen, 72 % hvit, krysstoning 260 ms (`OpeningOverlay.kt:210-235`) |
| Skjules | Når videoen er lastet, uten uttoning (`Player.js:960-977`) | Første ferdig innlastede bilde, med uttoning; vises bare én gang per avspilling |

**Brukt i GameHome (0.1.62.0):** sløret og logoplasseringen fra Nuvio, logoen toner inn (650 ms etter 350 ms) og puster
1,0↔1,04 over 2 s, statuslinje rett under logoen, bakgrunnen glir sakte fra 1,00 til 1,06 over 20 s (egen idé), og
tittel i ExtraBold når logo mangler. **Ikke brukt ennå:** «logo som fylles» krever et ekte fremdriftsmål, som vi ikke
har for spillstart; uttoning ved skjuling venter til tilstandshåndteringen i Home er ryddet opp. Stream-spill har
ingen bilder på Legion Go ennå; de kan hentes fra Vibepollo (Playnite-omslag) eller fra metadata i fase 4.

## Retro-modus

Systemer og emulatorer defineres som YAML-data, ikke kode, slik at nye systemer kan legges til via pull request uten kodeendring. v1 starter med ett system og én emulator. Definisjoner lånt fra ES-DE eller Playnite (begge MIT på repo-nivå) sjekkes per fil og registreres med kilde-commit i `THIRD-PARTY-NOTICES.md`.

**Eksempel på definisjon (utkast)**

```yaml
# definitions/systems/snes.yaml
id: snes
name: Super Nintendo
folders: [snes, sfc, "Super Nintendo"]
extensions: [.sfc, .smc, .zip, .7z]
identify: crc32            # cartridge: hash mot No-Intro
emulators:
  - id: retroarch-snes9x
    default: true

# definitions/emulators/retroarch-snes9x.yaml
id: retroarch-snes9x
runner: emulator
executable: "{emulators}/RetroArch/retroarch.exe"
arguments: '-L "{emulators}/RetroArch/cores/snes9x_libretro.dll" -f "{rom}"'
exitHint: "Select + Start"
```

**Skanning**

- ROM-mapper med alias per system, for eksempel `C:\Spill\ROMs\snes`.
- Disk-spill: vis `.cue`, ikke `.bin`. Flerdisk grupperes, eventuelt via autogenerert `.m3u`. `.chd` og zip/7z støttes.
- Overstyring av emulator per spill fra start.

**Identifisering og metadata**

- Cartridge-ROM-er: hash mot No-Intro-navn for eksakt treff.
- Store disk-images: filnavn eller serienummer mot Redump, ikke full hashing.
- Boxart fra libretro-thumbnails i v1.
- ScreenScraper krever både utviklerlegitimasjon for programvaren og brukerens egen konto, og tas med først når vilkårene for open source-bruk er avklart.
- LaunchBox Games DB-dumpen er ikke en godkjent avhengighet før vilkårene er avklart.

**BIOS og innhold**

- OneGame sjekker om nødvendige BIOS-filer finnes og varsler hvis de mangler, men leverer dem aldri.
- Appen indekserer bare filer brukeren har selv; repoet inneholder ingen ROM-er eller BIOS.

**Avslutning:** normal avslutning først, via emulatorens egen hurtigtast eller en lukkeforespørsel til vinduet. Tvungen avslutning av prosessgruppen er en reservehandling som krever bekreftelse. Hver definisjon har et avslutningshint som vises ved oppstart.

## Controller, lifecycle og brukeropplevelse

Windows eier vindusbytte i FSE, men OneGame må fortsatt håndtere vanlig aktivering riktig. Retur etter et spill skjer fordi FSE viser home-appen når spillvinduet lukkes; OneGame tvinger seg ikke frem, men håndterer single-instance og videresending av Home- og Library-aktivering.

**Controller**

| Knapp | Handling |
| --- | --- |
| Venstre stikke / D-pad | Navigere |
| A | Velg / spill |
| B | Tilbake |
| X | Kontekstmeny (spillemåte, profil, skjul, rett treff) |
| Y | Søk |
| LB / RB | Bytt kategori eller modus |
| Menu | Innstillinger |
| Xbox-knapp | Reservert for Game Bar og Task View; fanges ikke |

- Rekkefølge: test WinUIs opt-in gamepad-ruting (`GamepadKeyRouting`) og XYFocus i valgt stabil SDK-versjon, deretter dialoger, søk, kontekstmenyer og virtualiserte lister. Egen adapter med `Windows.Gaming.Input` brukes bare der plattformen ikke dekker behovet.
- Samme knappetrykk skal aldri håndteres både av key routing og av egen adapter.
- Input ignoreres når OneGame ikke er i forgrunnen.
- Xbox-knappen, langt trykk og View + Menu (videresender Guide til aktiv app) fanges ikke.

**Lifecycle**

- Job Objects brukes til sporing og samlet avslutning, uten `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, slik at et OneGame-krasj ikke tar med seg spillet. Barneprosesser, nesting og breakaway håndteres per runner.
- Ferdig-signalet kombineres: venting på hovedprosessens handle, `JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO` og periodisk spørring av aktive prosesser i jobben, siden completion-port-varsler ikke er garantert levert.
- Avslutningspolicy per runner: normal avslutning først, tvungen bare som bekreftet reservehandling.
- Butikk-launch spores bare for metadata (sist spilt, "kjører nå"), ikke for fokusstyring.
- Full-trust WinUI 3 suspenderes ikke av Windows. OneGame stopper rendering når vinduet er skjult og frigjør art-cache.
- FSE-tilstand leses via `IHomeIntegration` (`IsGamingFullScreenExperienceActive`, `RegisterGamingFullScreenExperienceChangeNotification`) når adapteren er tilgjengelig.

**Brukeropplevelse**

- Home har to deler: "Fortsett å spille" og ett bibliotek-rutenett. Lokalt, Stream og Retro er filtre, ikke egne skjermer.
- En spillflis viser cover og tittel. Butikk, klient og spillemåte vises bare når det finnes mer enn ett valg, eller i kontekstmenyen.
- Førstegangsveiviseren foreslår det den finner, og brukeren bekrefter. Det som kan oppdages automatisk, spørres det ikke om.
- "Nye funn" vises bare når det finnes noe å bekrefte.
- Ingen varsler, merker eller popup-er under spilling.
- Innstillinger er én side med få valg; definisjoner, profiler og API-nøkler ligger samlet under "Avansert".
- Game Bar-widget er utsatt til senere.

## Sikkerhet, lisenser og distribusjon

Den største sikkerhetsavveiningen er at FSE-registreringen krever utviklermodus under installasjon; alt annet kan holdes innenfor vanlige brukerrettigheter.

- **Installer:** åpen kildekode, ett lesbart skript. Slår på utviklermodus, legger inn midlertidig sertifikat i Trusted People, installerer pakken og fjerner begge etterpå. Fase 0 tester kjøring etter at utviklermodus er slått av, omstart, pakkeoppgradering, reparasjon og reinstallasjon.
- **Avinstallering:** fjerner pakke, sertifikat og lokal data etter bekreftelse.
- **Hemmeligheter:** API-nøkler og Vibepollo-token lagres i Windows Credential Locker, aldri i konfigurasjonsfiler eller repoet.
- **Vibepollo-token:** begrenset til de API-stiene og HTTP-metodene OneGame faktisk bruker.
- **Ingen nettverkslytter** i OneGame.
- **Brukerdefinerte skript** i profiler kjøres med brukerens rettigheter og er av som standard.
- **Drivere:** OneGame installerer ingen. Vibepollo bruker egne drivere for virtuell skjerm og kontroller på hosten.
- **AnyFSE** brukes bare som referanse for den minimale registreringen.
- **Lisenser:** OneGame under GPL-3.0-or-later. Moonlight og Vibepollo (GPLv3) kjøres som separate prosesser. Lånte definisjoner føres i `THIRD-PARTY-NOTICES.md` med kilde-commit.
- **Innhold:** ingen ROM-er, BIOS-filer eller nedlasting av slikt i repoet eller appen.

## Utenfor scope i første versjon

- **TDP-styring:** går via Lenovos udokumenterte WMI-grensesnitt; Legion Space og Handheld Companion eier området.
- **Egen streamingprotokoll, paring eller encoder:** Moonlight og Vibepollo dekker dette.
- **Host Agent og nettverkstjeneste mellom klient og host:** til et dokumentert behov finnes.
- **GameInput:** `Windows.Gaming.Input` brukes i v1.
- **Utgaver, varianter og DLC** som egne begreper i modellen.
- **Global "drep spillet"-kombinasjon** og bakgrunnsovervåking av mapper.
- **ScreenScraper og LaunchBox-dumpen** før vilkårene er avklart.
- **EA- og Battle.net-providere:** kommer sist, fordi de brekker oftest.
- **Achievements, skysynkronisering og innholdsnedlasting** utover metadata og grafikk via API.

* **Pluginbutikk, nedlasting av kjørbare utvidelser og global controller-remapping.**

## Faseplan

| Fase | Leveranse | Må være bevist før neste fase |
| --- | --- | --- |
| 0A | Minimal WinUI-app med FSE-identitet | Valgbar som home app, aktivering, omstart, pakkeoppgradering og reinstallasjon, også med utviklermodus av |
| 0B | Controller-testflate og måling | Alle dialoger og fokusoverganger uten mus; målt kald og varm oppstart, minne og bakgrunns-CPU i Release-build |
| 1 | Steam og manuelt lagt til lokalt spill | Start, avslutning, retur og bevart bibliotekposisjon; Steam startes ved behov og lukkes etterpå |
| 2 | Moonlight mot Vibepollo med Playnite | Bare spillet vises, spillslutt avslutter økten, disconnect og reconnect, host-vekking |
| 3 | Ett system og én emulator | Normal avslutning og trygg feilhåndtering |
| 4 | Flere providers, mappeskanner og metadata | Testede parsere, matching og overstyringer |
| Senere | Host Agent, Game Bar-widget, flere emulatorer | Et dokumentert behov eksisterende løsninger ikke dekker |

Fase 0A og 0B er de eneste punktene som kan endre teknologivalget. Faseplanen er en tabell i stedet for en tegning, fordi Copilot bare så en embed-referanse og ikke kunne vurdere den.

## Milepæl 1: vertikal flyt

Milepæl 1 er ferdig når denne flyten virker på Legion Go med kontroller alene: innlogging → Xbox mode → OneGame → start spill → avslutt spill → tilbake til samme flis. Den dekker fase 0A, 0B og 1; stream og emulator kommer etterpå.

**Scope:** FSE-adapter, kontrollernavigasjon, ett Steam-spill og ett manuelt lagt til lokalt spill. Ingenting annet.

**Akseptansekriterier**

- [ ] OneGame kan velges under Choose home app og starter ved innlogging i Xbox mode.
- [ ] Hele flyten brukes med kontroller, uten mus, tastatur eller berøring.
- [ ] Steam-spillet og det lokale spillet starter, avsluttes og gir retur til samme flis, samme filter og samme scrollposisjon.
- [ ] Et spill som ikke starter, gir valgene Prøv igjen, Åpne butikk og Tilbake, aldri en svart skjerm.
- [ ] Øktstatus er synlig: Starter, Kjører, Spill avsluttet.
- [ ] Home vises fra cache uten å vente på skanning eller metadata.
- [ ] Et lokalt spill kan legges til, få riktig exe valgt og rettes uten å redigere filer.
- [ ] Flyten virker etter hvile og gjenoppvåkning og etter omstart; feil logges med hvilket lag som feilet (Windows, FSE eller OneGame).
- [ ] Ytelsesmålene er målt i Release-build, og resultatet er dokumentert.

**Designes inn nå, bygges senere**

- Eksporterbar diagnostikk uten tokens eller hemmeligheter.
- Backup og gjenoppretting av favoritter, matching, profiler og overstyringer.
- Dock- og TV-profil med egen skalering.

## Tekniske hypoteser fra forundersøkelser

Forundersøkelser av hvordan spill-launchere på Windows løser de samme oppgavene, ga hypotesene under. De er ikke kilder. Hver hypotese bekreftes med offentlig dokumentasjon, åpen kildekode eller egne tester før den brukes i kode, og kilden noteres i ADR-en eller testen som tar den i bruk.

### Butikker

| Område | Hypotese | Bekreftes med | Fase |
| --- | --- | --- | --- |
| Steam: kjører nå | Steam skriver AppID-en til spillet som kjører i en verdi under `HKCU\Software\Valve\Steam`, og nullstiller den når spillet avsluttes. Det gir et kjører-signal for spill startet via protokoll, også på hosten. | Egen test på Legion Go og host, inkludert at Steam krasjer. Les ved endring med `RegNotifyChangeKeyValue` (dokumentert), ikke med timer. | 1 |
| Steam: start og lukk | `steam://rungameid/<appid>` starter et spill og `steam://exit` lukker klienten ryddig. | Valves dokumentasjon for Steam browser protocol, og egen test sammen med `steam.exe -shutdown` | 1 |
| Steam: bilder uten nett | Steam lagrer bibliotekbilder per AppID lokalt i klientens cache-mappe. | Egen inspeksjon av mappen i dagens Steam-klient (strukturen kan ha endret seg) | 1 |
| Xbox: hvilke pakker er spill | Gaming Services har lokale registeroppføringer for installerte GDK-spill, som kan skille spill fra vanlige apper uten nettkall. | Egen inspeksjon av registeret på Legion Go, med GDK-spill og eldre UWP-spill. Kombineres med `PackageManager` og `MicrosoftGame.config`. | 4 |
| Xbox: start med PID | AUMID kommer fra `Package.GetAppListEntries()` (dokumentert). Aktivering med `IApplicationActivationManager.ActivateApplication` gir PID. Aktivering via `shell:AppsFolder\<AUMID>` virker også, men gir ingen PID. | Microsoft-dokumentasjon, og egen test av hvilken prosess PID-en peker på for GDK-spill | 4 |
| Xbox: nettkatalog | Microsofts offentlige produktkatalog kan klassifisere produkt-ID-er, men er udokumentert. | Brukes ikke i v1. Lokale signaler først. Tas det i bruk, med brukerens region og som `TermsStatus.Unofficial`. | – |
| Epic | Manifestfilene per spill har installasjonsmappe, appnavn og exe-sti. `com.epicgames.launcher://apps/<app>?action=launch&silent=true` starter via klienten. | Åpen kildekode (Playnite, Legendary) og egen test | 4 |
| GOG | Registeret under `GOG.com\Games` har installasjonssti og en standard startkommando, i tillegg til `goggame-*.info`. | Åpen kildekode (Playnite, Heroic) og egen inspeksjon | 4 |
| Battle.net | Avinstallasjonsoppføringen og klientens kommandolinje (`--exec="launch <kode>"`) kan gi start uten å tolke `product.db`. | Åpen kildekode (Playnite) og egen test | Senere |

### Prinsipper

- **Prosessporing:** sporing bare på prosessnavn mister spillet når en launcher starter det som en ny prosess. Bruk Job Object for prosesser vi eier, butikkens eget signal der det finnes (Steam), og PID fra aktivering (Xbox). Et prosessnavn brukeren setter i `UserOverrides`, er reserve.
- **Fokus:** Windows lar en prosess ta forgrunnen når den har mottatt siste input-hendelse (dokumentert for `SetForegroundWindow`). Omveier som `AttachThreadInput` brukes ikke.
- **Identitet:** innstillinger per spill lagres aldri under en nøkkel som avhenger av tittel eller parametere. Da forsvinner de når tittelen rettes. Bruk `Game.Id` og `ExternalIdentity`.
- **Manuelt tillegg:** minimum er tittel, exe, snarvei eller URL, parametere, arbeidsmappe og bilde. Det tilsvarer `PlayAction`.
- **Bilder:** lokale butikkbilder brukes før nett, og et manglende bilde gir en plassholder.
- **Game Bar-widget (senere):** en widget må være en UWP-app med `microsoft.gameBarUIExtension`. Arbeid som krever full tillit, må gjøres av en separat komponent. Bekreftes med Microsofts dokumentasjon og eksempler for Xbox Game Bar SDK. WinUI 3-appen kan ikke selv være widget.
- **Sikkerhet:**
    - ingen lokal nettverkslytter, ingen CORS-wildcard og ingen loopback-unntak med administratorrettigheter
    - ingen COM-server eller annen IPC som andre apper kan aktivere
    - ingen telemetri eller maskinvare-ID

### Spørsmål å avklare med test

- [ ] Gir varsler på Steams kjører-verdi pålitelige signaler, også når Steam krasjer?
- [ ] Peker PID fra `ActivateApplication` på selve spillet eller på en hjelpeprosess for GDK-spill?
- [ ] Har Gaming Services registeroppføringer for alle Game Pass-spill på Legion Go, også eldre UWP-spill?
- [ ] Hvilken mappestruktur har Steams lokale bildecache i dagens klient?
- [ ] Er GOGs standard startkommando i registeret alltid satt, og stemmer den med første `playTask`?
- [ ] Hvilket Ubisoft-format virker: `uplay://launch/<id>` med eller uten `/0`? Bekreftes mot Playnite og test.
- [ ] Hvordan finnes Battle.net-koden for `--exec="launch <kode>"` fra lokale data?

## Meny under spill

Brukeren når en kontekstmeny under spill på to måter: Xbox-knappen via Game Bar til Hjem (to trykk, ingen kostnad), eller en ledig Legion Go-knapp som er lagt om til en hurtigtast (ett trykk). Xbox-knappen fanges ikke direkte. Menyen bygges fra økten GameHome selv har startet.

### Slik når brukeren menyen

| Vei | Trykk | Hvordan | Kostnad mens spillet kjører | Status |
| --- | --- | --- | --- | --- |
| Xbox → Game Bar → Hjem | 2 | GameHome får Home-aktivering. Er en økt aktiv, åpnes «Kjører nå» i stedet for biblioteket. | Ingen | Hovedvei. Testes i 0A. |
| Ledig knapp på Legion Go → hurtigtast | 1 | Knappen legges om i Legion Space til en sjelden kombinasjon (for eksempel `Ctrl+Alt+Shift+F13`). GameHome registrerer den med `RegisterHotKey` og får `WM_HOTKEY`. | Ingen, hendelsesdrevet | Hurtigvei. Testes i 0B. |
| Kontrollerkombinasjon | 1 | GameInput-callback eller XInput, bare mens en økt er aktiv | Lav, men spillet ser også knappene | Senere, for eksterne kontrollere |
| Xbox-knappen direkte | 1 | Bare via udokumentert `XInputGetStateEx` (ordinal 100) | – | Forkastet: kolliderer med Game Bar og Xbox mode |

Hvorfor hurtigtast og ikke kontrollerkombinasjon:

- Windows sender en registrert hurtigtast til GameHome og ikke til spillet.
- Prosessen som mottar siste input-hendelse får lov til å ta fokus. Da trengs ingen triks med `AttachThreadInput`.
- En kontrollerkombinasjon går også inn i spillet, og via Moonlight helt til hosten.

### Slik vises menyen

- GameHome kommer i forgrunnen som et fullskjermspanel mens spillet kjører videre bak. Det er ingen overlay, injeksjon eller gjennomsiktig vindu øverst, fordi det gir dobbel input.
- «Tilbake til spillet» kaller `SetForegroundWindow` på spillvinduet.
- Når utløseren kommer utenfra (for eksempel protokollen `gamehome://session`), aktiveres appen via `AppInstance`. Om aktiveringen gir appen forgrunnen fra et spill i fullskjerm, testes i 0A.
- Utøver dette ligger GameHome i dvale som planlagt.

### Valg per runner

Panelet bygges av `ISession` og `RunnerCapabilities`. Kjører noe GameHome ikke startet selv, gjenkjennes det via vinduet i forgrunnen (`GetForegroundWindow` → PID), og for Steam via `RunningAppID`. Runner-spesifikke valg står øverst, avslutt-valg nederst.

| Runner | Valg | Hvordan |
| --- | --- | --- |
| Moonlight | Koble fra (spillet fortsetter på hosten) | Lukk Moonlight-prosessen |
|  | Avslutt fjernspill (med bekreftelse) | Vibepollo `POST /api/apps/close` |
|  | Vis eller skjul ytelsesstatistikk | Send Moonlights hurtigtast (`Ctrl+Alt+Shift+S` i moonlight-qt) til Moonlight-vinduet |
|  | Bytt strømmeprofil | Koble fra og koble til igjen med nye CLI-flagg |
| Steam | Steam-overlay | Send `Shift+Tab` til spillvinduet |
|  | Lukk Steam etter spillet | `CloseIfStartedByOneGame`, deretter `steam://exit` |
| Emulator | Lagre og laste tilstand, tilbakestill | Hurtigtaster fra YAML-definisjonen |
| Xbox / Game Pass | Ingen klientvalg | PID fra AUMID-aktiveringen |
| Alle | Tilbake til spillet, Gå til Hjem, Avslutt spill | Avslutning med `WM_CLOSE` først, tvungen avslutning bare etter bekreftelse |

### Legges til i testene

- [ ] 0A: Aktiverer Hjem i Game Bar GameHome mens et spill kjører, og kommer GameHome da i forgrunnen?
- [ ] 0A: Starter `gamehome://session` (protokollaktivering) GameHome i forgrunnen fra et spill i fullskjerm?
- [ ] 0B: Kan Legion Space legge en bakknapp om til en tastekombinasjon, og virker det i Xbox mode?
- [ ] 0B: Tar `RegisterHotKey` kombinasjonen før spillet, og før Moonlight når «capture system keys» er på?
- [ ] 0B: Tar GameHome fokus fra 2–3 spill uten ramme og ett i eksklusiv fullskjerm, og kommer det tilbake med «Tilbake til spillet»?
- [ ] Fase 2: Moonlights hurtigtaster i versjonen vi bruker, inkludert Nonarys fork.

## Sjekkliste for Copilot

Første runde er besvart av Copilot, og svarene er innarbeidet over. Det som gjenstår, krever i hovedsak test på Legion Go og desktop-PC-en. Svar per punkt med bekreftet, avkreftet eller uavklart, med kilde eller testresultat.

**Fase 0A: FSE på Legion Go**

- [ ] Vises en minimal C#/WinUI-app som identity package med `windows.gamingApp` og custom capability under Choose home app?
- [ ] Fungerer den etter at utviklermodus er slått av, etter omstart, pakkeoppgradering, reparasjon og reinstallasjon?
- [ ] Hvilken OS-build og Xbox/Game Bar-versjon kjørte testen på?
- [ ] Hvordan mottar home-appen Library-aktivering fra Game Bar, og brukes `PublicFolder` til noe?

**Fase 0B: input og ressursbruk**

- [ ] Er `GamepadKeyRouting` tilgjengelig i valgt stabil Windows App SDK, og dekker den dialoger, søk, kontekstmenyer og virtualiserte lister?
- [ ] Fungerer `Windows.Gaming.Input` med Legion Go sine innebygde kontrollere?
- [ ] Kald og varm oppstart til første kontrollerfokus, private bytes, working set, GPU-minne og bakgrunns-CPU, med lite og stort bibliotek.

**Lettvekts oppstart**

- [ ] Virker `steam.exe -silent -applaunch` fra kald Steam, og virker `-no-browser` eller tilsvarende fortsatt?
- [ ] Hvordan oppdager vi at Steam- og Galaxy-skylagring er ferdig før klienten lukkes, og lukkes Steam ryddig med `steam.exe -shutdown`?
- [ ] Starter Game Pass-spill uten at Xbox-appen lastes?
- [ ] Hvordan avgjøres per Epic-spill om det kan startes uten Epic-klienten, og er Legendary fortsatt vedlikeholdt?

**Fase 2: stream**

- [ ] Hvilke CLI-kommandoer i Nonarys Moonlight-fork avviker fra upstream?
- [ ] Viser Vibepollo med Playnite bare spillet for 007 First Light og to–tre andre spill, og avsluttes økten når spillet lukkes?
- [ ] Hvilke verdier for `wait-all`, `auto-detach` og exit-timeout gir riktig oppførsel?
- [ ] Spør Moonlight om å avslutte det kjørende spillet når Desktop-appen startes?
- [ ] Faktisk portsett for valgt Vibepollo-versjon, og vekking fra hvile, dvale og avslått maskin.

**Vilkår**

- [ ] ScreenScraper: vilkår for utviklerlegitimasjon i open source-prosjekter.
- [ ] LaunchBox Games DB-dumpen: tillatt bruk.
- [ ] SteamGridDB: dokumentert rate limit, hvis den finnes.

## Kilder

- [Microsoft: Windows Gaming full screen experience (KB5070297)](https://support.microsoft.com/en-au/servicing/os/windows/docs/2025/10/windows-gaming-full-screen-experience)
- [Microsoft: Handheld launchers (GDK)](https://learn.microsoft.com/en-us/xbox/gdk/docs/gdk-dev/pc-dev/handheld/handheld-launchers)
- [Microsoft: GamingHomeApp (unattend)](https://learn.microsoft.com/en-us/windows-hardware/customize/desktop/unattend/microsoft-windows-gaming-configuration-gaminghomeapp)
- [Microsoft Q&A: registrering av tredjeparts home app](https://learn.microsoft.com/en-za/answers/questions/5582690/expose-the-means-to-register-an-app-as-an-option-f)
- [AnyFSE (README)](https://github.com/ashpynov/AnyFSE) og [AppxManifest.xml](https://github.com/ashpynov/AnyFSE/blob/main/AppxManifest.xml)
- [FullScreenExperienceShell](https://github.com/driver1998/FullScreenExperienceShell)
- [Vibepollo (Nonary)](https://github.com/Nonary/Vibepollo)
- [Moonlight PC releases](https://github.com/moonlight-stream/moonlight-qt/releases)
- [ElevenForum: Xbox mode-registerverdi](https://www.elevenforum.com/t/enable-or-disable-xbox-mode-in-windows-11.42303/)
