# Testflate for fase 0A og 0B

Testflaten i `GameHome.App` viser og logger det vi skal måle på Legion Go. Testene kjøres bare med de
innebygde kontrollerne. Loggen ligger i appens lokale mappe (`LocalState\gamehome.log`) og hentes over SSH.

## Hva skjermen viser

- **Øverst:** versjon og om bygget er JIT eller Native AOT.
- **Midten:** seks fliser med kontrollerfokus.
- **Under:** siste input og de nyeste linjene fra loggen.

## Tester

| # | Test | Forventet | Logges som |
| --- | --- | --- | --- |
| 1 | Start GameHome fra Start-menyen | Fokus på Flis 1. Oppstartstid og minne vises. | `[ytelse]`, `[fokus]` |
| 2 | D-pad og venstre stikke | Fokus flytter seg mellom flisene | `[input]`, `[fokus]` |
| 3 | A på en flis | «Valgt: Flis N (kontroller)» | `[valg]` |
| 4 | Trykk på en flis med fingeren | «Valgt: Flis N (berøring)» | `[valg]` |
| 5 | B, X, Y, Menu, View, LB og RB | Knappenavnet vises | `[input]` |
| 6 | Bytt til en annen app og tilbake | Lesing av kontroller stopper og starter igjen | `[fokus]` |
| 7 | Med GameHome åpen, åpne `gamehome://session` fra Kjør (Win+R) eller en annen app | GameHome kommer i forgrunnen. Det startes ingen ny GameHome. | `[aktivering]`, `[fokus]` |
| 8 | Samme som 7, men med et annet vindu i fullskjerm | GameHome tar forgrunnen, eller Windows avslår | `[fokus]` |

Test 2 og 3 viser også om WinUI leverer kontrollertaster selv («innebygd ruting finnes»), eller om alt går
gjennom Windows.Gaming.Input.

## Resultater

Resultater skrives i en egen fil per testrunde i denne mappen, for eksempel `2026-11-02-legion-go.md`, med
versjon, variant (JIT eller AOT), hva som virket og utdrag fra loggen.
