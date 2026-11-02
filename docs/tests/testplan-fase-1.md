# Testplan fase 1, del 1: spill lagt til manuelt

Testspill: Broforce, lagt til manuelt som programfil. Steam testes i del 2.

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

# Del 2: Steam

Testspill: Ball x Pit (appid 2062430), installert via Steam.

| # | Test | Forventet | Logges som |
| --- | --- | --- | --- |
| 1 | Åpne GameHome | Flisen «BALL x PIT» dukker opp av seg selv | `[steam] Fant 1 installerte spill; 1 nye` |
| 2 | A på flisen | Steam starter spillet. «Kjører nå» viser «Starter via Steam» til spillet kjører | `[økt] Startet`, `Steam melder at spillet kjører` |
| 3 | M2 → «Tilbake til spillet» | Spillet i forgrunnen | `[fokus]` |
| 4 | Avslutt fra spillets meny | Tilbake til samme flis | `Steam melder at spillet er ferdig`, `avsluttet: Exited` |
| 5 | Start, M2 → «Avslutt spillet» | Spillet lukker normalt; ellers tilbys tvangsavslutning etter 10 s | `avsluttet: ClosedByUser` |
| 6 | Avinstaller et Steam-spill, åpne GameHome | Flisen forsvinner (spillet beholdes i biblioteket, merket som borte) | `[steam] … 1 borte` |
