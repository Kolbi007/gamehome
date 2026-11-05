# 0009: Retro med RetroArch

Dato: 2026-11-05. Status: vedtatt av produkteier.

## Kontekst

Fase 3 i veikartet: ett system og én emulator. Produkteier eier Super Mario World (SNES) og Sonic 3 & Knuckles
(Mega Drive) og ville teste med dem.

## Beslutning

- RetroArch (GPL-3.0) kjører spillene som et eget program, med libretro-kjernene snes9x (SNES) og Genesis Plus GX
  (Mega Drive). Flere systemer (NES, Game Boy, GBA) er beskrevet i `RetroLibrary.Systems` og dukker opp når kjernen er
  installert.
- Spillene ligger i `%USERPROFILE%\Games\ROMs\<system>` (`snes`, `megadrive` …). Zip-filer støttes. GameHome laster
  aldri ned spill; brukeren legger inn spill brukeren eier.
- Tittelen kommer fra filnavnet uten region og merker i parentes. Bilder hentes som for andre spill (SteamGridDB).
- RetroArchs egen `retroarch.cfg` på Legion Go er satt til fullskjerm, avslutning uten dobbelttrykk, pause når et annet
  vindu er framme (M2-menyen) og automatisk lagring og lasting av tilstand («fortsett der du slapp»). GameHome skriver
  ikke RetroArchs filer; oppsettet ble gjort én gang ved installasjon.
- «Avslutt spillet» lukker RetroArch pent; tilstanden lagres automatisk.

## Konsekvenser

- Kjernene lastes ned fra libretros byggeserver (nattlige bygg; det finnes ikke stabile enkeltkjerner for Windows).
- Lisensen påvirker ikke GameHome: ingen RetroArch-kode tas inn.
