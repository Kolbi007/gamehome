# 0002: Single-project MSIX i stedet for eget Package-prosjekt

Dato: 2026-11-01. Status: vedtatt, kan endres etter fase 0A.

## Kontekst

Planen hadde et eget `OneGame.Package`-prosjekt for identity package, manifest og SCCD.
Windows App SDK støtter single-project MSIX: pakkemanifestet ligger i app-prosjektet, og
`dotnet publish` lager en MSIX uten Visual Studio (via `Microsoft.Windows.SDK.BuildTools.MSIX`).

## Beslutning

`GameHome.App` inneholder `Package.appxmanifest` og bygger MSIX direkte. Ett prosjekt mindre.
Pakken signeres med et fast utviklersertifikat (`CN=GameHome Dev`); den offentlige delen ligger i `certs/`,
den private som GitHub-secret.

Windows App SDK refereres som komponenter (`WinUI`, `Foundation`, `InteractiveExperiences`), ikke metapakken,
for å unngå AI- og ML-komponentene. Første self-contained x64-MSIX er 78,6 MB.

## Konsekvenser

- Hvis fase 0A viser at FSE-registreringen krever en sparse/identity package (som AnyFSE) eller en egen SCCD
  i pakkeroten, gjeninnføres et eget pakkeprosjekt i en ny ADR.
- Pakkestørrelse og oppstartstid måles i fase 0B; ReadyToRun er på i Release, Native AOT vurderes der.
