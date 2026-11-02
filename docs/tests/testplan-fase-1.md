# Testplan fase 1, del 1: spill lagt til manuelt

Testspill: Broforce (DRM-fri utgave, eid av produkteier). Steam testes i del 2.

| # | Test | Forventet | Logges som |
| --- | --- | --- | --- |
| 1 | «+ Legg til spill» → velg `Broforce.exe` | Flisen «Broforce» vises og får fokus | `[bibliotek] La til` |
| 2 | Lukk og åpne GameHome | Flisen er der fortsatt (lest fra `library.json`) | `[bibliotek] Lest 1 spill` |
| 3 | A på flisen | Broforce starter, flisen viser ▶ | `[økt] Startet` |
| 4 | M2 i spillet | «Kjører nå» med Broforce | `[hurtigtast]`, `[økt]` |
| 5 | «Tilbake til spillet» | Broforce i forgrunnen | `[fokus]` |
| 6 | Avslutt fra spillets egen meny | GameHome kommer tilbake med fokus på Broforce-flisen | `[økt] avsluttet: Exited` |
| 7 | Start igjen, M2 → «Avslutt spillet» | Broforce lukker normalt; ellers tilbys tvangsavslutning etter 10 s | `[økt] avsluttet: ClosedByUser` |
| 8 | Lagring i spillet | Lagringsfilen havner i spillets vanlige mappe, ikke inne i GameHome-pakken | sjekkes over SSH |
| 9 | Y på flisen → «Fjern fra GameHome» | Flisen forsvinner; ingen filer slettes | `[bibliotek] Fjernet` |
