# 0011: TDP fjernstyres i Handheld Companion

Dato: 2026-11-05. Status: vedtatt av produkteier. Bygger på ADR 0007.

## Kontekst

Målet er at man sjelden skal måtte gå ut av GameHome for å endre innstillinger. TDP var bare tilgjengelig via HCs
hurtigmeny. To alternativer ble vurdert og forkastet:

- **Lenovos eget WMI-grensesnitt** (LENOVO_GAMEZONE_DATA, LENOVO_OTHER_METHOD; beskrevet i Linux-kjernens åpne
  lenovo-wmi-drivere). Krever administrator (lest på Legion Go: «Access denied» uten), så GameHome ville trengt en egen
  hjelper med administratorrettigheter. Grensesnittet har også vifte- og overklokkingsmetoder der feil kan skade
  maskinen, og HC og GameHome ville overstyrt hverandre. Produkteier valgte bort direkte styring av sikkerhetshensyn.
- **Bare knappen til HCs hurtigmeny** (som før): trygt, men TDP er fortsatt et ekstra steg unna.

## Beslutning

- GameHome styrer aldri TDP selv. Knappene − og + ved «TDP» i Ytelse-panelet sender Ctrl+Alt+Shift+F7 / F8, som
  `scripts/Set-HandheldCompanion.ps1` binder til HCs egne funksjoner «TDP ned» og «TDP opp». HC endrer 1 W om gangen og
  holder seg innenfor sine grenser for maskinen.
- HCs TDP-knapper virker bare når den aktive strømprofilen har TDP-overstyring. Skriptet slår derfor på overstyring i HCs
  «Default»-strømprofil og lar spillprofilen «Default» bruke den på batteri og lader. Bare disse feltene endres, som
  tekst; HC må være avsluttet, og alle tre mappene sikkerhetskopieres først.
- Verdien som vises, leses fra `powerprofiles\Default.json`. GameHome leser filen, men skriver den aldri.

## Konsekvenser

- Avhenger av HCs interne filformat og funksjonsnavn (HC 1.3, lest fra HCs filer og offentlige kildekode; ingen HC-kode
  er tatt inn, HC er CC BY-NC-SA). Endres de, viser GameHome «–», og «Mer i Handheld Companion» virker fortsatt.
- Strømprofilen styres nå av HCs TDP-verdier (start 15/15/20 W) i stedet for Lenovos forhåndsvalgte modus. Vil man
  tilbake, legges sikkerhetskopiene tilbake mens HC er avsluttet.
