# Veikart

Fra der vi er til en barebones-versjon. «Barebones» er suksessdefinisjonen i `docs/product-brief.md`:
Legion Go starter i Xbox mode med GameHome, alt styres med kontroller, ett lokalt Steam-spill og 007 First Light
via stream fra gaming-PC-en kan startes, øktmenyen åpnes med Legion-knappen, og brukeren kommer tilbake til samme flis.

Hver testrunde tar 15–45 minutter på Legion Go. Resultater skrives i `docs/tests/`.

## Faser

| Fase | Mål | Testrunder | Produkteier gjør | Status |
| --- | --- | --- | --- | --- |
| 0B: input og ytelse | Kontroller, JIT mot AOT, dvale mens spill kjører, Legion-knapp som hurtigtast, `gamehome://session`, «Tilbake til spillet» | 2–3 | Legge om venstre Legion-knapp i Legion Space | Pågår (runde 1–3 gjort) |
| 0A: FSE | Valgbar som Gaming Home. Overlever omstart, oppgradering og at utviklermodus slås av. Hjem i Game Bar åpner «Kjører nå». | 1–2 | Slå på utviklermodus ved installasjon. Velge GameHome under «Choose home app». | Ikke startet |
| 1: lokalt spill | Steam-spill og manuelt spill: start, kjører-signal, avslutning, retur til samme flis, feil gir valg, Home fra cache | 2–3 | Ha et Steam-spill installert | Ikke startet |
| **Milepæl 1** | **Lokal barebones** | **≈ 6–8 totalt** | | |
| 2: stream | 007 First Light via Moonlight og Vibepollo uten appvelger, Playnite eller skrivebord. Koble fra, koble til igjen, avslutt, vekking. | 3–4 | Moonlight på Legion Go. Vibepollo og Playnite på gaming-PC-en. | Ikke startet |
| **Barebones med stream** | **Suksessdefinisjonen** | **≈ 10–12 totalt** | | |
| 3: retro | Ett system og én emulator | 1–2 | Lovlig testinnhold | Senere |
| 4: flere kilder og metadata | Epic, Xbox, GOG, IGDB, HowLongToBeat, Metacritic | Løpende | IGDB-nøkkel | Senere |

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

## Gjort

- 2026-11-01: repo, CI, signert test-MSIX, SSH til Legion Go, testflate.
- 2026-11-01, 0B runde 1 (JIT): installasjon og oppgradering virker. Kontroller og berøring virker. Kaldstart 2,1 s
  (mål under 1,5 s). WinUI leverer ikke kontrollertaster selv. Se `docs/tests/2026-11-01-legion-go.md`.
- 2026-11-02, 0B runde 2: L2/R2 virker. Native AOT: oppstart 300 ms mot 1 663 ms, minne 107 MB mot 139 MB,
  pakke 24 MB mot 79 MB. En AOT-krasj (ItemsSource) er funnet og rettet.
- 2026-11-02, 0B runde 3: øktmenyen virker med M2 som hurtigtast, inkludert «Tilbake til spillet» og «Avslutt økt».
  Venstre Legion-knapp kan leses via HID, men Legion Space reagerer også.
