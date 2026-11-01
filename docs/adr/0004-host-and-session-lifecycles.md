# 0004: Host som eget begrep og separate livsløp i stream-økter

Dato: 2026-11-01. Status: vedtatt.

## Kontekst

Produktnotatet (seksjon 7, 10 og 13) skiller mellom spillet, spillemåten, hosten og en aktiv økt. Det peker på at
en stream-økt har flere livsløp: Moonlight-klienten, forbindelsen og appen på hosten. Utkastet i
`docs/architecture.md` hadde hoststatus i `Availability` på `PlayAction` og én `Completion` per økt.

## Beslutning

- `Host` er en egen modell (`Id`, `Name`, `Address`, `MacAddress`) med `HostState` (`Unknown`, `Online`, `Asleep`,
  `Unreachable`, `Waking`). Timeout gir `Unknown`.
- `PlayAction` får `HostId`. Null betyr lokalt på Legion Go. `Availability` gjelder bare installasjonen
  (`Unknown`, `Installed`, `Missing`).
- `ISession.Completion` er den lokale prosessens slutt. For stream er det Moonlight-klienten.
- `IStreamSession` har `Connection` og `RemoteApp` som egne tilstander, og egne handlinger for å koble fra,
  koble til igjen og avslutte appen på hosten.
- `IGameSource` har `SourceHealth`, som vises for brukeren.

## Konsekvenser

Koden i `src/GameHome.Core` er nå fasit for datamodellen. Kodeblokkene i `docs/architecture.md` er det
opprinnelige utkastet.
