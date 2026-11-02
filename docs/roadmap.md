# Veikart

Fra der vi er til en barebones-versjon. «Barebones» er suksessdefinisjonen i `docs/product-brief.md`:
Legion Go starter i Xbox mode med GameHome, alt styres med kontroller, ett lokalt Steam-spill og 007 First Light
via stream fra gaming-PC-en kan startes, øktmenyen åpnes med Legion-knappen, og brukeren kommer tilbake til samme flis.

Hver testrunde tar 15–45 minutter på Legion Go. Resultater skrives i `docs/tests/`.

## Faser

| Fase | Mål | Testrunder | Produkteier gjør | Status |
| --- | --- | --- | --- | --- |
| 0B: input og ytelse | Kontroller, JIT mot AOT, dvale mens spill kjører, Legion-knapp som hurtigtast, `gamehome://session`, «Tilbake til spillet» | 2–3 | Legge om venstre Legion-knapp i Legion Space | **Ferdig** (3 runder) |
| 0A: FSE | Valgbar som Gaming Home. Overlever omstart, oppgradering og at utviklermodus slås av. Hjem i Game Bar åpner «Kjører nå». | 1–2 | Slå på utviklermodus ved installasjon. Velge GameHome under «Choose home app». | **Bestått** (3 runder) |
| 1: lokalt spill | Steam-spill og manuelt spill: start, kjører-signal, avslutning, retur til samme flis, feil gir valg, Home fra cache | 2–3 | Ha et Steam-spill installert | **Ferdig** |
| **Milepæl 1** | **Lokal barebones** | **≈ 6–8 totalt** | | |
| 2: stream | 007 First Light via Moonlight og Vibepollo uten appvelger, Playnite eller skrivebord. Koble fra, koble til igjen, avslutt, vekking. | 3–4 | Moonlight på Legion Go (gjort). Én Vibepollo-innstilling: synk alle installerte Playnite-spill. | Forundersøkelse ferdig |
| **Barebones med stream** | **Suksessdefinisjonen** | **≈ 10–12 totalt** | | |
| 3: retro | Ett system og én emulator | 1–2 | Lovlig testinnhold | Senere |
| 4: flere kilder og metadata | Epic, Xbox, GOG, IGDB, HowLongToBeat, Metacritic | Løpende | IGDB-nøkkel | Senere |

## Plan for fase 2: stream

Funn og kilder står i `docs/architecture.md`, «Fase 2: funn fra kildekoden». Alt er hypoteser til det er testet.

**Runde 1 på Legion Go: listing**

1. Synk i Vibepollo er satt til alle installerte spill, og Playnite er startet minimert én gang.
2. GameHome henter host-listen med `Moonlight.exe list <host> --csv` fra skrivebordet. Forventet: 007 First Light og
   Dragon Quest står i listen, og kallet tar under 5 s. Loggen viser exit-kode og tid.
3. Host-spill vises i Home: 007 First Light som egen flis, Broforce som samme flis med to spillemåter (lokalt og
   stream).
4. Gaming-PC-en avslått eller utilgjengelig: GameHome viser host-spillene fra cache som utilgjengelige, uten å henge.

**Runde 2: start og avslutning**

5. A på 007 First Light (stream): Moonlight starter streamen, og Playnite vises ikke på streamen.
6. M2 under stream: «Kjører nå» viser «Strømmer fra gaming-PC» med valgene «Koble fra» og «Avslutt fjernspill».
7. «Koble fra»: Moonlight lukkes, spillet fortsetter på PC-en, og flisen viser det. A igjen kobler til samme økt.
8. «Avslutt fjernspill» (med bekreftelse): spillet avsluttes normalt på PC-en.
9. Spillet avsluttes fra spillets meny: streamen ender og GameHome er tilbake på flisen.

**Runde 3: vekking og feil**

10. PC-en i dvale: GameHome vekker den med Wake-on-LAN og starter når den svarer.
11. En annen app kjører på PC-en: GameHome spør før den avslutter den.

**Resultater 2026-11-02 (0.1.45.0–0.1.47.0)**

- Listen: 37 apper, 33 spill på 1,8 s, etter at listen regnes som ferdig når utskriften stopper (Moonlight avslutter
  ikke selv etter `list`). Playnite måtte oppdateres for at Vibepollo-tillegget skulle lastes (krevde SDK 6.15).
- 007 First Light lagt til fra undermenyen og strømmet. Playnite ble aldri vist.
- «Avslutt spillet på Gaming Laptop» avsluttet spillet (`quit` exit 0 etter 6,2 s), men Moonlight ble stående med
  «connection terminated -1», så økten og ▶ på flisen ble hengende. Rettet: Moonlight lukkes etter et vellykket `quit`.

**Hypotese: vis streamen først når spillet faktisk vises (ønske 2026-11-02)**

I dag ser brukeren hostens låseskjerm (hvis den er låst), så skrivebordet, før spillet kommer i fullskjerm.
Mulige veier, i rekkefølge etter hvor lite som må til:

1. **Lasteskjermen i GameHome ligger over Moonlight til spillet er klart.** GameHome kan ikke se hva streamen viser,
   men kan bruke Vibepollos `GET /api/session/status` (`appRunning`) eller vente til Moonlights vindu har vært i
   forgrunnen en stund. Krever enten API-token (valgfritt oppsett) eller en grov tidsgrense.
2. **Vibepollo viser svart skjerm til appen har startet.** Må sjekkes i Vibepollos kildekode og innstillinger
   (virtuell skjerm, «launch overlay» eller lignende). Ingen endring i GameHome.
3. **Låseskjermen:** følger av at PC-en er låst. Alternativer er Vibepollos opplåsing via streamen (dagens oppførsel),
   eller at PC-en ikke låses når den står hjemme. Autologon lagrer passordet og anbefales ikke.

Når: etter runde 3 (vekking), fordi vekking og låseskjerm henger sammen. Neste steg er å lese Vibepollos kildekode for
punkt 2 før noe bygges.

## Beslutningspunkter som kan endre planen

1. **FSE virker ikke uten utviklermodus:** GameHome blir en fullskjermsapp som starter ved pålogging. Resten står.
2. **Venstre Legion-knapp kan ikke legges om:** vi prøver GameInputs system-knapp-callback, og deretter en bakknapp.
3. **Playnite blir synlig ved stream:** da trengs en liten egen komponent på gaming-PC-en (+1–2 runder).
4. **JIT eller Native AOT:** avgjort 2026-11-02, Native AOT (ADR 0005).
5. **WinUIs innebygde kontrollerruting** (`GamepadKeyRouting`): testes når en WinUI-versjon med den er minst to uker
   gammel (tidligst rundt 2026-11-10). Virker den, erstatter den det meste av vår egen kontroller-adapter.
6. **Venstre Legion-knapp** kan ikke legges om i Legion Space. GameHome kan lese den fra Lenovos HID-kanal, men
   Legion Space åpner seg likevel (runde 3). M2 (Ctrl+Alt+Shift+F12) er hurtigknappen for øktmenyen inntil videre.
7. **Hypotese: Handheld Companion i stedet for Legion Space.** Mulige gevinster: mindre minne (Legion Space bruker
   ca. 1,2 GB), fri omlegging av Legion-knappene og TDP per spill via profiler knyttet til `.exe` (for eksempel
   Moonlight lavt, krevende lokale spill 25–30 W). GameHome integrerer ikke med den; den bytter profil selv når
   programmet i forgrunnen endres. Egen, valgfri test etter fase 0B: laveste TDP Legion Go tillater, at profilen
   byttes når GameHome starter programmet, rekkefølge ved oppstart, og om den dekker det høyre Legion-knapp brukes
   til i dag (skjerm, profil, strøm). Lisens og drivere vurderes før anbefaling.

## Ønsker fra produkteier

### Lasteskjerm med spillets logo (2026-11-02)

Fra A trykkes til spillet vises, går det ofte noen sekunder, særlig med Steam. En lasteskjerm viser at trykket ble
registrert og gir en konsollfølelse (inspirert av Stremio).

- **Utseende:** fullskjerm med spillets logo over et mørkt eller uskarpt bakgrunnsbilde og en enkel pulserende
  animasjon. Uten bilder vises tittelen i stor tekst.
- **Forsvinner** når spillvinduet får forgrunnen. GameHome får det som hendelse fra Windows (`SetWinEventHook`,
  `EVENT_SYSTEM_FOREGROUND`), ingen polling.
- **Ingen blindvei:** B avbryter og går tilbake til flisen. Etter en tidsgrense vises «Det tar lang tid …» med Vent og
  Avbryt.
- **Ingen kamp om forgrunnen:** tar spillet forgrunnen tidlig, forsvinner lasteskjermen.
- **Bilder:** Steam-spill bruker Steams lokale bildecache (virker uten nett og API-nøkkel). Manuelle spill får bilder
  fra SteamGridDB eller IGDB i fase 4.

| Trinn | Innhold | Når |
| --- | --- | --- |
| 1 | Lasteskjerm med tittel, og logo for Steam-spill fra Steams lokale bilder | Fase 1 |
| 2 | Logo og bakgrunnsbilde for alle spill | Fase 4 |


### Strømvalg i GameHome (2026-11-02)

Det finnes ingen lett måte å starte Legion Go på nytt fra Xbox mode. GameHome får strømvalg som små ikonknapper i et
hjørne, ikke midt på skjermen: i Home og i «Kjører nå» (M2).

- **Valg:** Dvale, Start på nytt, Slå av. Hvert valg bekreftes med to tydelige knapper, og aldri med A alene på første
  trykk, så et feiltrykk ikke avslutter spill.
- **Kjører et spill:** bekreftelsen sier at spillet avsluttes og at ulagret fremgang kan gå tapt.
- **Teknisk:** dvale via `SetSuspendState`, omstart og avslutning via Windows' egen avslutning (ikke tvang), slik at
  spill og Steam får lukke normalt.

## Gjort

- 2026-11-01: repo, CI, signert test-MSIX, SSH til Legion Go, testflate.
- 2026-11-01, 0B runde 1 (JIT): installasjon og oppgradering virker. Kontroller og berøring virker. Kaldstart 2,1 s
  (mål under 1,5 s). WinUI leverer ikke kontrollertaster selv. Se `docs/tests/2026-11-01-legion-go.md`.
- 2026-11-02, 0B runde 2: L2/R2 virker. Native AOT: oppstart 300 ms mot 1 663 ms, minne 107 MB mot 139 MB,
  pakke 24 MB mot 79 MB. En AOT-krasj (ItemsSource) er funnet og rettet.
- 2026-11-02, 0B runde 3: øktmenyen virker med M2 som hurtigtast, inkludert «Tilbake til spillet» og «Avslutt økt».
  Venstre Legion-knapp kan leses via HID, men Legion Space reagerer også.
- 2026-11-02, 0A: GameHome er valgbar som home app, Legion Go starter rett inn i GameHome, Home i Game Bar gir
  «Kjører nå» (`windows.gaming:///home`). Overlever omstart, oppgradering og at utviklermodus slås av. Med Legion
  Space slått av ved oppstart bruker Lenovos prosesser 64 MB, og høyre Legion-knapp og M2 virker fortsatt.
  Beslutningspunkt 1 (FSE uten utviklermodus) er dermed ikke utløst.
- 2026-11-02, fase 1 del 1: spill lagt til manuelt startes, «Kjører nå» virker med ekte spill, avslutning fra spillet
  og fra menyen gir retur til samme flis. Oppgradering virker med utviklermodus av.
- 2026-11-02, fase 1 del 2: Steam-spill finnes av seg selv, startes via Steam, og kjører-signalet fra `RunningAppID`
  virker for start, avslutning og retur til flisen. Et oppstartskrasj ved nye fliser er funnet og rettet.
- 2026-11-02, fase 1 ferdig: lasteskjerm med Steam-logo, «Tilbake til spillet» også for spill som minimerer seg, og
  GameHome husker flisen etter omstart. **Milepæl 1 (lokal barebones) er nådd.**
