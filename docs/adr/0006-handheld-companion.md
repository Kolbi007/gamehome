# 0006: Handheld Companion erstatter Legion Space

Dato: 2026-11-05. Status: vedtatt av produkteier.

## Kontekst

Fra 2026-11-02 kl. 21:56 tok Legion Space forgrunnen hver gang Moonlight eller et spill byttet vindu, og kontrolleren
fulgte med. Årsaken var at hovedappen til Legion Space var åpnet; den ble liggende i bakgrunnen etter at vinduet ble
lukket. Målt på Legion Go 2026-11-04:

- Å avslutte `LegionSpace.exe` med makt etterlot kontrollerne uten data (XInput-pakketelleren sto stille), også etter
  omstart, til Legion Space ble startet igjen.
- «Avslutt» fra Legion Space-ikonet i oppgavelinjen avsluttet alle prosessene, og kontrolleren sendte data videre.
- Å lukke vinduet var ikke nok.

Fokusendringene fra 2026-11-02 (gi forgrunnen tilbake, vindusrekkefølge, Legion Space bakerst) var forsøk på å
kompensere og er fjernet.

## Beslutning

- Legion Space avinstalleres på Legion Go. Handheld Companion (åpen kildekode, CC BY-NC-SA 4.0; ingen kode tas inn i
  GameHome) tar over kontroller, knapper, TDP og hurtigmeny (QuickTools). Brukeren installerer og setter den opp selv.
- Knappene legges om av brukeren i Handheld Companion til tastekombinasjoner GameHome lytter på:
  M2 → Ctrl+Alt+Shift+F12 (meny under spill), venstre Legion-knapp → Ctrl+Alt+Shift+F11 (Home), høyre Legion-knapp →
  QuickTools.
- Ctrl+Alt+Shift+F11 registreres hele tiden, ikke bare under en økt: uten spill henter den Home fram, under spill
  åpner den menyen. Øvrige hurtigtaster registreres fortsatt bare under en økt.
- GameHome lukker aldri Legion Space eller andre programmer med makt. Kjører Legion Space, viser GameHome et varsel med
  hvordan den avsluttes.
- Tastaturkroken (M2-reserve for spill som slår av hurtigtaster) er på som standard og kan slås av under Feilsøking.

## Konsekvenser

- Fastvareoppdateringer for kontrollerne kom via Legion Space. Trengs det, installeres Legion Space midlertidig og
  avsluttes fra oppgavelinjen etterpå.
- GameHome skriver ikke Handheld Companions konfigurasjon. Formatet (JSON i `%LOCALAPPDATA%\HandheldCompanion`) er
  internt og kan endres; et eventuelt oppsett fra GameHome krever egen vurdering.
