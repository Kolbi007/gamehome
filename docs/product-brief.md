# Reviewerbrief til Claude: controller-first launcher for Windows-handheld

**Dato:** 1. november 2026  
**Produkteier og fysisk tester:** Kolbi007  
**Din rolle:** Hovedutvikler og teknisk arkitekt  
**Copilots rolle:** Kritisk reviewer, kildekontroll og teststøtte

> **Viktig:** Dette dokumentet er ikke en besluttet arkitektur eller implementasjonsspesifikasjon. Det beskriver ønsket sluttbrukeropplevelse, produktkrav, observasjoner, mulige løsningsspor, identifiserte risikoer og spørsmål vi ønsker at du vurderer. Du står fritt til å forkaste, kombinere eller erstatte forslagene dersom du finner en enklere, sikrere eller mer vedlikeholdbar løsning.

---

## 1. Formålet med dokumentet

Vi ønsker din selvstendige tekniske vurdering før permanent prosjektstruktur og produksjonskode etableres.

Skill tydelig mellom:

- krav fra produkteier
- ønsket brukeropplevelse
- mulige implementasjoner
- dokumenterte plattformmekanismer
- observasjoner fra community- eller referanseprosjekter
- hypoteser og antakelser
- forhold som må testes fysisk
- beslutninger du anbefaler

Ikke anta at et forslag er riktig fordi det står her. Utfordre også krav dersom de er teknisk urealistiske, men forklar konsekvensen for brukeropplevelsen og foreslå et praktisk alternativ.

---

## 2. Produktvisjonen

Vi ønsker en åpen kildekode, controller-first spillopplevelse for en Windows-handheld, primært Lenovo Legion Go.

Den ønskede opplevelsen er:

```text
Slå på Legion Go
    ↓
Windows logger inn
    ↓
Xbox mode / Full Screen Experience
    ↓
Vår launcher vises som Gaming Home
    ↓
Brukeren navigerer hele biblioteket med kontroller
    ↓
Velger et spill
    ↓
Spiller lokalt, streamer fra gaming-PC eller bruker emulator
    ↓
Avslutter eller kobler fra økten
    ↓
Kommer sømløst tilbake til samme spill og posisjon i launcheren
```

Appen skal oppleves som ett konsollgrensesnitt, ikke som en kjede av Windows-vinduer, butikkklienter, Moonlight-menyer, Playnite-sider og skrivebord.

---

## 3. Absolutte produktkrav

Følgende er produktkrav. Dersom ett av dem ikke er teknisk mulig, ønsker vi en tydelig forklaring og nærmeste realistiske alternativ.

### 3.1 Controller-first

- Daglig bruk skal kunne gjøres kun med de innebygde kontrollene på Legion Go.
- Ekstern Xbox-kontroller skal også fungere.
- Berøringsskjermen kan brukes til førstegangsoppsett, API-nøkler, mappevalg og feilsøking.
- Mus og tastatur skal ikke være nødvendig i normal bruk.
- Fokus skal alltid være synlig og forutsigbart.
- Dialoger, feilmeldinger, søk, innstillinger og gjenoppretting skal kunne styres med kontroller.

### 3.2 Ett samlet bibliotek

Biblioteket skal kunne representere:

- lokale spill på Legion Go
- spill installert på en annen Windows-PC
- Steam
- Xbox / PC Game Pass
- Epic Games Store
- GOG
- Ubisoft
- EA
- Battle.net
- manuelt registrerte spill og apper
- Moonlight/Vibepollo-streaming
- emulatorer og ROM-er brukeren selv har

### 3.3 Ett spill, flere spillemåter

Et spill skal normalt vises én gang, selv når det kan spilles på flere måter.

Eksempel:

```text
007 First Light

Spill her
  Steam på Legion Go

Stream
  Gaming-PC via Vibepollo og Moonlight
```

Vi ønsker ikke separate, urelaterte fliser som:

```text
007 First Light - Steam
007 First Light - Moonlight
007 First Light - Desktop
```

### 3.4 Respekt for butikkene

Appen skal ikke:

- omgå DRM
- etterligne butikkautentisering
- omgå oppdateringer
- hente brukerens private bibliotek gjennom uautoriserte skytjenester
- endre butikkontoer automatisk uten tydelig, eksplisitt handling

Spill skal startes gjennom butikkens registrerte og legitime mekanisme der det er mulig.

### 3.5 Sømløs streaming

Fra Legion Go skal brukeren kunne velge et bestemt spill som bare finnes på gaming-PC-en, for eksempel 007 First Light, og få denne flyten:

```text
Velg 007 First Light i launcheren
    ↓
Moonlight starter direkte mot riktig remote-app
    ↓
Vibepollo starter riktig spill på hosten
    ↓
Brukeren ser normalt bare en oppstartsflate og selve spillet
    ↓
Spillet avsluttes eller streamen kobles fra
    ↓
Brukeren kommer tilbake til launcheren på Legion Go
```

I normalflyten skal brukeren ikke måtte se eller navigere i:

- Moonlights appvelger
- Playnite
- Vibepollos webgrensesnitt
- hostens Windows-skrivebord
- Steam/Epic/GOG-klientene

Førstegangsinnlogging, oppdateringer, UAC og uforutsette launcher-dialoger kan være unntak. Disse skal håndteres som vedlikeholds- eller feilsituasjoner, ikke normalopplevelse.

### 3.6 Trygg Windows-integrasjon

Vi ønsker ikke:

- erstatning av Winlogon Shell
- erstatning av explorer.exe
- permanent avhengighet av ViVeTool
- direkte endring av Windows StateRepository
- spoofing av enhetstype
- egen kjernedriver
- DirectX-hooking eller kodeinjeksjon i spill
- et generelt, usikret remote-control-API

### 3.7 Åpen kildekode

- Prosjektet skal publiseres åpent på GitHub eller tilsvarende.
- Andre skal gjerne kunne bruke og bidra til koden.
- Lisens skal velges før ekstern kode kopieres inn.
- Tredjepartskode, definisjoner og media skal dokumenteres med kilde, lisens og versjon/commit.

---

## 4. Ønsket controller-opplevelse

En foreløpig knappemodell er:

```text
Venstre stikke / D-pad  Naviger
A                       Velg / spill / bekreft
B                       Tilbake
X                       Kontekstmeny / velg spillemåte
Y                       Søk
LB / RB                 Bytt kategori eller modus
Menu                    Innstillinger
Guide / Xbox            Åpne session-/kontekstmeny hvis plattformen tillater det
```

Dette er et forslag, ikke en låst binding. Vurder konflikter med spill, Steam Input, Legion Space, Moonlight, Windows og emulatorer.

---

## 5. Guide-/Xbox-knappen som produktidé

Produkteier mener Guide-/Xbox-knappen er sentral for konsollopplevelsen.

Ønsket opplevelse under spill:

```text
Kort Guide-trykk
    ↓
Launcherens session-/kontekstmeny
    ↓
Fortsett
Koble fra stream
Koble til igjen
Avslutt spill
Åpne Home
Vis øktstatus
```

Vi ønsker helst ikke at brukeren først må gå gjennom hele Xbox Game Bar-grensesnittet.

### Mulige spor som er diskutert

- GameInput system-button callback for Guide.
- En liten bakgrunnskomponent/Input Broker.
- Aktivering av eksisterende launcher-instans med en kommando som `ShowSessionMenu`.
- En Legion-spesifikk knapp eller hold-kombinasjon dersom Guide-signalet konsumeres av Legion Space eller Windows.
- En valgfri Game Bar-widget som kompatibilitetsflate, men ikke som hovedopplevelse.

### Dette er ikke besluttet

Vurder selv:

- om Guide-knappen kan mottas pålitelig når et spill har fokus
- om den kan mottas fra de innebygde Legion Go-kontrollerne
- om Windows eller Legion Space konsumerer signalet
- om det er mulig å skille kort og langt trykk uten å ødelegge Windows Gaming Task View
- om launcheren kan aktiveres og returnere til spillet uten synlig skrivebord eller Game Bar
- om en dedikert Legion-knapp/-kombinasjon gir en bedre og mer robust opplevelse
- om en bakgrunnsprosess er nødvendig eller unødvendig kompleksitet

---

## 6. Windows Xbox mode / FSE

Det overordnede målet er at appen kan velges som Gaming Home under Windows Xbox mode/FSE på en Legion Go.

Community-prosjekter peker på en manifestkombinasjon med blant annet:

- `windows.gamingApp`
- `Microsoft.appCategory.gamingHome_8wekyb3d8bbwe`
- en custom capability/SCCD
- pakket appidentitet og AUMID

Dette er foreløpig et observert implementasjonsspor, ikke en antatt stabil tredjepartskontrakt.

### Vi ønsker at du vurderer

- om tredjeparts FSE-home fortsatt krever Microsoft-eid custom capability
- om en SCCD med utviklermodus er nødvendig
- om dette fungerer med valgt UI/runtime-teknologi
- om appen blir værende valgbar når utviklermodus deaktiveres
- hvordan pakkeoppgradering påvirker registreringen
- minimumsbuild og relevante package contracts
- hvordan Home- og Library-aktivering fungerer
- om Microsoft Store-signering eller annen godkjenning kan gi en mer støttet vei
- om FSE-integrasjonen bør isoleres som en valgfri adapter

### Fallback-krav

Selv om FSE-registreringen skulle slutte å fungere etter en Windows-oppdatering, skal launcheren fortsatt kunne fungere som en vanlig fullskjermsapp med controller.

---

## 7. Konseptuell domenemodell

Vi ønsker å skille spillidentitet fra hvordan spillet startes og hvordan en aktiv økt oppfører seg.

Vi har diskutert disse begrepene:

- `Game`: det brukeren oppfatter som spillet
- `PlayAction`: én mulig måte å spille det på
- `GameSession`: en konkret aktiv økt
- `Source`: noe som oppdager spill eller spillemåter
- `Runner`: noe som starter og eventuelt observerer en spillemåte
- `Host`: en PC eller annen enhet et streambart spill finnes på

Dette er ikke besluttede typer eller grensesnitt.

### Modellen må kunne håndtere

- flere butikk-ID-er for samme spill
- lokale og remote installasjoner
- flere hoster
- flere utgaver eller regioner
- DLC og ekspansjoner
- mods og alternative executables
- emulatorer og flere emulatorvalg
- stream som fortsetter etter at klienten kobler fra
- reconnect til eksisterende remote-økt
- spill som startes utenfor launcheren
- usikker eller motstridende discovery
- brukerens manuelle overstyringer

### Viktig brukerregel

Brukerens korrigering og overstyring skal aldri overskrives av ny scanning eller metadataoppdatering.

---

## 8. Lokal spilloppdagelse

En mulig strategi er én scanner/provider per butikk som leser lokale registre, manifestfiler og konfigurasjon uten butikkinnlogging.

Mulige kilder som er diskutert:

- Steam `libraryfolders.vdf` og `appmanifest_*.acf`
- Epic manifest-JSON
- Xbox/MSIX package identity og GameConfig
- GOG-register og Galaxy-data
- Ubisoft-register/config
- EA-data
- Battle.net `product.db` eller andre lokale data
- egne mapper som `C:\Spill`
- Playnite-plugins som implementasjonsreferanse

### Vi ønsker at du vurderer

- hva som er robust nok for første versjon
- hvilke formater som er de facto og kan endres
- hvordan parserne bør versjons- og regresjonstestes
- om scanning bør bo i klienten, et delt bibliotek eller en companion
- om Microsoft Store/Xbox-spill kan identifiseres uten uoffisielle katalogkall
- hvordan ukjente pakker skal presenteres
- hvordan lokale executables skal rangeres uten å velge uninstallers, setup-programmer eller crash handlers

### Produktkrav for scanning

- Biblioteket skal lastes fra lokal cache umiddelbart.
- Scanning skal skje i bakgrunnen.
- En ødelagt provider skal ikke blokkere resten av appen.
- Usikre treff skal vises i en `Nye funn`-kø.
- Providers skal ha synlig helsestatus og diagnostikk.

---

## 9. Metadata og grafikk

Mulige metadata- og artwork-kilder som er diskutert:

- IGDB
- SteamGridDB
- ScreenScraper
- LaunchBox Games Database
- libretro thumbnails
- lokale butikkbilder

### Vi ønsker at du vurderer

- API-vilkår, rate limits og nøkkelhåndtering
- om brukeren skal legge inn egne nøkler
- om en appspesifikk developer credential er nødvendig
- hvilke data som kan caches og distribueres
- lisens og attribusjon for bilder og metadata
- offline-first-opplevelse
- hvordan metadata skal separeres fra brukerens overstyringer

Metadata skal ikke være en forutsetning for at det installerte biblioteket kan brukes.

---

## 10. Streaming via Vibepollo og Moonlight

Vi ønsker å bruke etablerte streamingkomponenter, ikke utvikle egen streamingprotokoll, encoder eller pairing.

### Ønsket sluttflyt

```text
Launcher på Legion Go
    ↓
Velg remote PlayAction for 007 First Light
    ↓
Moonlight kobler direkte til riktig Vibepollo-app
    ↓
Vibepollo starter riktig host-side kommando
    ↓
Spillet starter
    ↓
Streamen viser spillet
    ↓
Spillet avsluttes eller brukeren kobler fra
    ↓
Klienten returnerer kontrollert til launcheren
```

### Mulige implementasjoner som er diskutert

1. Playnite og Vibepollo Playnite Connector som skjult backend.
2. En egen companion som scanner og publiserer apps til Vibepollo.
3. En hybrid hvor Playnite brukes for discovery/start, mens en liten companion bare løser mapping eller lifecycle-gap.
4. Direkte bruk av Vibepollos app-API.
5. Moonlight CLI for `list`, `stream` og `quit`.

Ingen av disse er besluttet.

### Vi ønsker at du vurderer

- hva Playnite-integrasjonen allerede dekker
- om Playnite kan forbli usynlig i normalflyten
- hvordan ett bestemt spill publiseres som egen Moonlight-app
- om streamen avsluttes når spillet avsluttes
- hvordan disconnect og reconnect fungerer
- hvordan host-appens prosessmodell påvirker streamen
- om en ikke-detached wrapper faktisk trengs
- Vibepollo-felter som `wait-all`, `auto-detach` og timeout-innstillinger
- om appoppføringer kan bære en stabil launcher-ID
- om app-API-token kan begrenses til nødvendige metoder
- om Moonlight upstream og Nonary-forken har kompatibel CLI
- hvordan Wake-on-LAN oppfører seg ved sleep, hibernate og shutdown
- hvordan hoststatus skal klassifiseres uten å gjette

---

## 11. Playnite som mulig skjult backend

Vi er åpne for å bruke Playnite på hosten dersom det gir en stabil og usynlig normalflyt.

Ønsket oppførsel:

- Playnite kan kjøre minimert eller skjult.
- Brukeren ser ikke Playnites grensesnitt på Legion Go.
- Vibepollo/connectoren starter rett spill.
- Spillet får fokus.
- Når spillet avsluttes, avsluttes eller frigjøres streamøkten korrekt.
- Playnite kan gjerne fortsette i bakgrunnen på hosten.

### Før en egen companion bygges ønsker vi svar på

- Dekker Vibepollos Playnite-integrasjon oppdagelse, artwork, launch, fokus og avslutning godt nok?
- Hvilke feiltilfeller gjør Playnite eller skrivebordet synlig?
- Kan løsningen håndtere Steam, Xbox, Epic, GOG og emulatorer konsekvent?
- Hvordan håndteres oppdatering, login-dialoger og egne launchere?
- Er en host-side splash nødvendig?
- Hvor stabil er connectoren på en eksplisitt versjonskombinasjon?

Vi ønsker ikke å bygge en stor Host Agent bare fordi det virker arkitektonisk elegant. Den skal løse et dokumentert gap.

---

## 12. Mulig Companion / Host Agent

En companion er et mulig verktøy, ikke et krav.

Mulige oppgaver:

- scanne installerte host-spill
- lage stabil mapping mellom launcher, butikk, Playnite og Vibepollo
- synkronisere appoppføringer til Vibepollo
- holde artwork og launch target oppdatert
- starte en host-side kommando
- observere at riktig spill faktisk starter
- holde en wrapper levende dersom Vibepollo trenger det
- observere avslutning
- vise en kontrollert oppstartsflate
- gi diagnostics

### Begrensninger vi foretrekker

- kjører uten administratorrettigheter i normal bruk
- ingen egen nettverkslytter i første versjon
- ingen generell remote-control-tjeneste
- ingen vilkårlig remote ShellExecute
- ingen usikret localhost-API
- endrer bare appoppføringer den selv eier
- bruker minst mulige Vibepollo-tokenrettigheter

Vurder om vanlig filsynkronisering, Vibepollos appdata, Moonlight-listen, Playnite-data eller et begrenset API kan gjøre companion-kommunikasjon unødvendig.

---

## 13. Session lifecycle

Dette er et område vi mener er lett å modellere feil.

En streamingøkt kan inneholde flere separate livsløp:

- local launcher-prosess på Legion Go
- Moonlight-klientprosessen
- streamingforbindelsen
- Vibepollo-appøkten
- Playnite eller host-wrapper
- butikkklienten
- selve spillprosessen

Ingen av disse skal automatisk behandles som samme sannhetskilde.

### Brukerhandlinger som må skilles

```text
Fortsett spillet
Koble fra streamen, men la remote-spillet kjøre
Koble til igjen
Be remote-spillet avslutte normalt
Tving remote-spillet til å stoppe
Avslutt lokal Moonlight-klient
Åpne hostens skrivebord for feilsøking
```

### Vi ønsker at du vurderer

- hvilke signaler som er autoritative per runner
- hvordan crash og abnormal exit håndteres
- om Job Objects er egnet for lokale prosesser
- hva som skjer hvis launcheren krasjer
- hvordan langvarige oppdateringer eller launcher-restart skilles fra spillavslutning
- hvordan navigasjonsposisjon og session state gjenopprettes
- om launcheren skal forsøke å ta fokus eller stole på FSE/window switching

---

## 14. Session-/kontekstmenyen

Vi ønsker en enkel, controller-first meny under aktiv spilling.

Mulig innhold:

```text
007 First Light
Streaming fra Gaming-PC

Fortsett
Koble fra stream
Koble til igjen
Vis øktstatus
Endre profil ved neste oppstart
Avslutt spillet
Åpne launcher Home
```

Menyen trenger ikke være et injisert grafikk-overlay. Den kan eventuelt være en separat fullskjermsflate dersom overgang til og fra spillet oppleves sømløs.

Vurder:

- teknisk realisme på Windows/FSE
- fokus og retur til eksakt spillvindu
- exclusive fullscreen
- anti-cheat
- skjermflimring
- hvordan menyen aktiveres dersom Guide-knappen ikke kan eies

---

## 15. Retro og emulatorer

Retro skal bruke samme overordnede bibliotek og spillemåtemodell som andre spill.

Vi har diskutert datafiler/YAML for:

- systemnavn og mapper
- filendelser
- foretrukket emulator
- kommandomaler
- BIOS-krav
- avslutningshint

### Produktkrav

- Ingen ROM-er eller BIOS-filer leveres.
- Appen kan kun indeksere brukerens egne filer.
- Manglende BIOS skal oppdages og forklares.
- Emulator skal kunne overstyres per spill.
- Multidisc-spill skal kunne grupperes.
- `cue` skal prioriteres foran tilhørende `bin`.
- Avslutning skal være kontrollerbar og forståelig.

Vurder lisens og egnethet for definisjoner fra ES-DE, Playnite eller andre prosjekter før gjenbruk.

---

## 16. UI- og runtime-teknologi

Mulige teknologier som er diskutert:

- C# og gjeldende .NET LTS
- WinUI 3 / Windows App SDK
- packaged full-trust Win32
- UWP-komponent kun hvis en Game Bar-widget blir aktuell
- native C++ interop for bestemte Windows-API-er
- SQLite

Dette er ikke valgt.

### Vi ønsker at du vurderer

- oppstartstid og minnebruk på Legion Go
- FSE-package compatibility
- controller-/focus behavior
- single-instance activation
- rendering når appen er i bakgrunnen
- self-contained versus framework-dependent deployment
- om native C++ faktisk er nødvendig noe sted
- om flere prosesser gir reell verdi eller bare kompleksitet
- hvordan appen bør oppdateres uten Store

---

## 17. IPC og sikkerhetsgrenser

Hvis løsningen trenger flere prosesser, ønsker vi en begrenset og eksplisitt kontrakt.

Mulige teknikker:

- app activation
- named pipes med ACL/package SID
- AppService
- COM/RPC med streng caller-validering

### Vi ønsker å unngå

- localhost HTTP uten autentisering
- `Access-Control-Allow-Origin: *`
- loopback exemption som krever admin
- API-er som tilbyr vilkårlig prosessstart
- vilkårlig filtilgang
- vilkårlig registerskriving
- COM-servere som kan aktiveres av alle AppContainers eller alle brukere uten nødvendig kontroll

Vurder om IPC i det hele tatt er nødvendig før det introduseres.

---

## 18. Personvern og diagnostics

- Telemetri skal være av som standard.
- Ingen maskinvare-ID skal brukes som stabil identifikator.
- Nettoverføringer og metadataoppslag skal være dokumentert.
- Hemmeligheter skal lagres i Windows Credential Locker/Credential Manager eller tilsvarende.
- API-nøkler og tokens skal ikke lagres i repo eller vanlig ukryptert JSON.
- Diagnostics-eksport skal fjerne tokens, brukernavn, IP-adresser og personlige filstier der det er mulig.
- Brukeren skal kunne se og slette lokal cache og metadata.

---

## 19. Lisensspørsmålet

Produkteier er komfortabel med at andre bruker og låner kode fra prosjektet, og ønsker samtidig å kunne gjenbruke eksisterende kode der det gir mening.

Mulige retninger:

- permissiv lisens som MIT for maksimal gjenbruk i både åpne og lukkede prosjekter
- GPLv3 dersom prosjektet faktisk integrerer GPL-kode og skal forbli copyleft
- separate, selvstendige biblioteker med permissiv lisens og hovedapp med annen lisens

### Vi ønsker at du vurderer

- hvilken kode som faktisk må kopieres versus brukes som separat prosess
- lisensene til Playnite, AnyFSE, ES-DE, Vibepollo, Moonlight og aktuelle definisjonskilder
- kompatibilitet mellom lisensene
- om arkitekturvalg påvirker lisensvalget
- hva som må stå i `THIRD-PARTY-NOTICES`

Ikke velg GPL bare fordi et separat GPL-program startes som en ekstern prosess. Ikke velg MIT hvis planen innebærer distribusjon av innkopiert GPL-kode i samme verk.

---

## 20. Funksjoner vi vurderer, men ikke vil prioritere tidlig

- Game Bar-widget
- global controller-to-mouse
- full theme/plugin marketplace
- TDP- og maskinvarestyring
- Steam-kontobytte
- gratisspill-/tilbudsfeed
- achievements
- egen savegame-sync
- egen streamingprotokoll
- egen pairing
- network service mellom Client og Companion
- alle stores samtidig
- EA og Battle.net tidlig
- generell localhost-API

Utfordre gjerne denne prioriteringen dersom en av funksjonene er nødvendig for kjerneflyten.

---

## 21. Kvalitetskrav

Vi ønsker:

- reproducerbart bygg
- små, reviewbare commits
- testbar komponentdeling
- automatiserte parser- og domene-tester
- anonymiserte fixture-filer
- eksplisitt versjonsstøtte for eksterne formater og CLI-er
- installasjon og avinstallering som kan rulles tilbake
- diagnostics som ikke lekker hemmeligheter
- dokumenterte manuelle tester på Legion Go
- ingen skjulte systemendringer

---

## 22. Kritiske proof-of-concepts

Vi tror følgende må bevises tidlig, men ønsker din vurdering av rekkefølgen.

### POC A: FSE Gaming Home

Bevis eller avkreft at appen kan:

- installeres på en støttet Windows-build
- vises som valgbar Gaming Home
- starte i FSE
- fungere med controller
- overleve omstart
- oppgraderes
- avinstalleres rent

### POC B: Guide-/session-meny

Bevis eller avkreft:

- Guide-event fra ekstern Xbox-kontroller
- Guide-event fra innebygde Legion Go-kontrollere
- oppførsel når spill har fokus
- kort versus langt trykk
- overgang launcher ↔ spill uten Game Bar eller skrivebord
- robust fallback dersom Guide ikke kan brukes

### POC C: Ett lokalt spill

- oppdag ett Steam-spill
- start gjennom Steam
- observer running/ended uten å bruke én svak prosessantakelse
- gå tilbake til samme flis
- håndter startfeil

### POC D: Ett remote-spill

Med 007 First Light eller annet reelt spill:

- Playnite/Vibepollo publiserer en egen remote-app
- launcher starter Moonlight direkte mot den appen
- Moonlight-velger vises ikke
- Playnite er usynlig i normalflyten
- spillvindu får fokus
- disconnect er forskjellig fra end remote game
- streamen avslutter korrekt ved normal spillavslutning
- launcher kommer tilbake på Legion Go
- samme test lykkes gjentatte ganger

### POC E: Ett retrospill

- ett system
- én emulator
- ett lovlig tilgjengelig testinnhold
- BIOS-sjekk der relevant
- start, exit og retur

---

## 23. Risikoregister fra reviewer

Dette er identifiserte risikoer, ikke nødvendigvis blocker.

### Plattform

- FSE-home-registrering kan være udokumentert og endres av Windows Update.
- Custom capability kan kreve utviklermodus eller Microsoft-autorisasjon.
- Windows-build og device eligibility kan påvirke funksjonen.
- Home/Library-aktivering kan kreve ukjent kontrakt.

### Input

- Guide-knappen kan konsumeres av Windows/Game Bar.
- Legion Space kan konsumere eller transformere knappesignalet.
- GameInput kan levere input i bakgrunnen og skape dobbel input.
- Steam Input, Moonlight og emulator-hotkeys kan kollidere.

### Streaming

- Moonlight-klientens prosessliv er ikke remote-spillets liv.
- Vibepollo-appens prosessmodell kan være forskjellig fra spillets.
- Playnite kan bli synlig ved oppdatering, login eller crash.
- Remote-spillet kan fortsette når streamen kobles fra.
- Wake-on-LAN er ikke garantert fra alle strømtilstander.
- Nonary-fork og upstream Moonlight kan avvike.

### Discovery

- Butikkmanifestene er ofte de facto-formater.
- Prosessnavn er en svak identifikator.
- Samme spill kan ha ulike navn og IDs på tvers av kilder.
- Automatisk merge kan slå sammen feil edition eller region.

### Lifecycle

- Job Object completion notifications er ikke nødvendigvis eneste sannhetskilde.
- `KILL_ON_JOB_CLOSE` kan avslutte spill ved launcher-crash.
- Fokusforcing kan gi kamp mellom spill, launcher og Windows.
- FSE kan ha annen foreground-opførsel enn vanlig desktop.

### Sikkerhet

- En companion kan bli en unødvendig remote-control-tjeneste.
- Bred COM/RPC/HTTP kan gi lokal privilege- eller command-execution-risiko.
- Artwork og metadata kan introdusere lisens- og personvernproblemer.
- Tokens kan lekke gjennom logs eller diagnostics.

### Scope

- For mange stores og emulatorer tidlig kan stoppe prosjektet før kjerneflyten fungerer.
- En egen Host Agent kan duplisere eksisterende Playnite/Vibepollo-funksjonalitet.
- Game Bar, themes, macros og hardware-control kan distrahere fra kjerneverdien.

---

## 24. Hva vi ønsker at du leverer først

Før du bygger hele appen, lever en selvstendig teknisk vurdering som inneholder:

1. Din forståelse av sluttbrukeropplevelsen.
2. Skille mellom absolutte krav og valgfrie forslag.
3. Kritikk av svakheter, motsetninger og urealistiske forventninger.
4. Hvilke antakelser som krever fysisk test.
5. Din anbefalte arkitektur med begrunnelse.
6. Minst ett realistisk alternativ til din anbefalte arkitektur.
7. Hvilke komponenter/prosesser du mener faktisk er nødvendige.
8. Hvilken UI/runtime- og packaging-teknologi du anbefaler.
9. Hvordan du vil modellere spill, spillemåter, hosts og sessions.
10. Hvordan du vil løse eller avgrense Guide-knappen.
11. Hvordan du vil løse remote discovery og direct launch.
12. Om Playnite bør være en del av sluttarkitekturen.
13. Om en Companion er nødvendig, valgfri eller bør droppes.
14. Sikkerhetsmodell og IPC-prinsipper.
15. Lisensanbefaling basert på faktisk planlagt gjenbruk.
16. Den minste proof of concept-en som reduserer mest risiko.
17. Go/no-go-kriterier før permanent repository-arkitektur låses.
18. Spørsmål du trenger besvart av Christian.

Ikke implementer hele produktet som første svar.

---

## 25. Beslutningsprinsipper

Når flere løsninger er mulige, prioriter:

1. Sømløs controller-opplevelse.
2. Stabilitet og recovery.
3. Minst mulig systeminngrep.
4. Sikkerhet og begrensede rettigheter.
5. Vedlikeholdbarhet ved Windows- og butikkoppdateringer.
6. Offline-first og lokal dataeierskap.
7. Lav bakgrunnsbruk på handheld.
8. Gjenbruk av modne komponenter fremfor unødvendig egenutvikling.
9. Enkel feilsøking.
10. En arkitektur som kan reduseres dersom en avansert funksjon viser seg ustabil.

---

## 26. Foreløpig suksessdefinisjon

Vi anser hovedideen som teknisk bevist når følgende kan demonstreres stabilt:

```text
Legion Go starter
    ↓
launcheren vises i ønsket gamingmodus
    ↓
brukeren navigerer kun med kontroller
    ↓
007 First Light velges
    ↓
spillet finnes bare på gaming-PC-en
    ↓
Moonlight starter direkte mot riktig Vibepollo-app
    ↓
brukeren ser normalt ikke Moonlight-velger, Playnite eller desktop
    ↓
spillet kan spilles
    ↓
session-meny kan åpnes med en robust controller-handling
    ↓
brukeren kan fortsette, koble fra eller avslutte remote-spillet
    ↓
ved avslutning returnerer brukeren til samme spillflis
```

Hvordan dette realiseres teknisk, ønsker vi at du avgjør etter kritisk vurdering og proof of concept.

---

## 27. Avsluttende instruks

Vær direkte. Ikke bekreft ideen bare fordi den er ambisiøs. Hvis en sentral del ikke er realistisk, si det tidlig.

Vi foretrekker:

- en liten løsning som faktisk fungerer
- dokumenterte kompromisser
- reelle tester på Legion Go og host-PC

fremfor:

- en imponerende arkitektur bygget på uverifiserte antakelser
- mange providers før kjerneflyten fungerer
- skjulte hacks som brekker ved neste Windows Update

Målet med første svar er beslutningskvalitet, ikke kodevolum.
