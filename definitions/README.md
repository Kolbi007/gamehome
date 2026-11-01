# Definisjoner

Data, ikke kode. Nye systemer og emulatorer legges til via pull request uten kodeendring.

- `systems/`: ett YAML-dokument per system (for eksempel `snes.yaml`).
- `emulators/`: ett YAML-dokument per emulator og kjerne, med oppstart, hurtigtaster og avslutningshint.
- `scanners/exclusions.yaml`: mønstre for exe-filer som ikke er spill (`unins*`, `*setup*`, `vcredist*` …).

Definisjoner lånt fra ES-DE eller Playnite sjekkes per fil og registreres med kilde-commit i
`THIRD-PARTY-NOTICES.md`. Første system og emulator kommer i fase 3.
