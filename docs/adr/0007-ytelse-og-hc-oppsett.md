# 0007: Ytelse i menyen og oppsett av Handheld Companion

Dato: 2026-11-05. Status: vedtatt av produkteier.

## Kontekst

Etter byttet til Handheld Companion (ADR 0006) savnet produkteier en enkel vei til TDP, Hz og oppløsning, slik
Legion Space hadde. HC har ingen dokumentert måte for andre programmer å styre den på (ingen API eller kommandolinje).
TDP krever administrator eller driver og styres allerede av HC; to programmer som setter TDP, overstyrer hverandre.

## Beslutning

- Menyen under spill («Kjører nå») får en Ytelse-del:
  - Hz og oppløsning settes av GameHome med de dokumenterte API-ene `EnumDisplaySettingsW` og
    `ChangeDisplaySettingsExW`, uten administrator.
  - «TDP, lysstyrke og mer» sender Ctrl+Alt+Shift+F9 med `SendInput`; HC er satt opp til å åpne QuickTools på den.
    GameHome setter aldri TDP selv.
- `scripts/Set-HandheldCompanion.ps1` setter opp HCs hurtigtaster én gang, på produkteiers forespørsel. Det kjøres
  mens HC er avsluttet, tar sikkerhetskopi og skriver bare egne filer (`HOTKEY_USER*.json` med navn «GameHome: …»),
  pluss at venstre Legion-knapp fjernes fra «åpne HC-vinduet». GameHome-appen skriver fortsatt aldri HCs filer.
- Knappene: M2 → Ctrl+Alt+Shift+F12, venstre Legion → Ctrl+Alt+Shift+F11, høyre Legion → QuickTools (HCs standard).

## Konsekvenser

- HCs filformat er internt. Endres det i en ny HC-versjon, må skriptet oppdateres; det advarer når versjonen ikke er
  1.3. Feltnavnene er lest fra HCs filer og offentlige kildekode; ingen HC-kode er tatt inn (HC er CC BY-NC-SA).
- Virker ikke hurtigtasten, viser GameHome at brukeren kan trykke høyre Legion-knapp i stedet.
- HCs bevegelsessensor holdt skjermen våken (Windows så inndata hele tiden; målt 2026-11-05). Den er slått av i HC;
  gyro kan slås på per spill i HC-profilen.
