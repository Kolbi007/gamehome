# 0005: Native AOT er standard

Dato: 2026-11-02. Status: vedtatt.

## Kontekst

Planen skulle velge mellom JIT og Native AOT etter målinger i fase 0B. Målt på Legion Go med samme testflate
(`docs/tests/2026-11-01-legion-go.md`):

| Måling | JIT med ReadyToRun | Native AOT |
| --- | --- | --- |
| Oppstart til første aktivering | 1 663 ms | 300–377 ms |
| Minne, working set / privat | 139 / 89 MB | 107 / 76 MB |
| MSIX | 79 MB | 24 MB |

Produkteier opplevde også kortere tid fra trykk til respons.

## Beslutning

- Testbygg og releaser til Legion Go bruker Native AOT (`-p:GameHomeAot=true`) for både x64 og ARM64.
- En JIT-variant for x64 bygges i CI for sammenligning og feilsøking. Den er ikke med i releaser.
- Debug-bygg lokalt bruker JIT, fordi det bygger raskere og er lettere å feilsøke.
- WinRT-marshalling må være AOT-sikker. `CsWinRTAotWarningLevel` 2 gjør usikre mønstre til byggefeil i CI.
  Eksempel: en .NET-samling som `ItemsSource` krever generert marshalling; uten den krasjer appen ved oppstart.

## Konsekvenser

- Koden må være trimming- og AOT-sikker (allerede en regel i CLAUDE.md).
- AOT-bygg i CI tar noen minutter lenger og krever C++-byggeverktøy (finnes på GitHub-runnerne).
- Versjonering: AOT får `.0`, JIT `.1` i siste ledd, så en JIT-pakke kan installeres over AOT fra samme bygg ved
  feilsøking.
