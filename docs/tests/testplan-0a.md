# Testplan fase 0A: GameHome som home app i Xbox mode

Bygget har app-utvidelsen `windows.gamingApp` og capability-en `gamingHome` (se `Package.appxmanifest`). Capability-en
er ikke godkjent av Microsoft og godtas bare med utviklermodus på ved installasjon.

| # | Test | Forventet | Logges som |
| --- | --- | --- | --- |
| 1 | Slå på utviklermodus, installer MSIX | Installasjonen godtas | – |
| 2 | Innstillinger → Gaming → Xbox mode (Full screen experience) → velg home app | GameHome står i listen og kan velges | `[fse] Valgt home app` ved neste start |
| 3 | Start Xbox mode | GameHome åpnes som hjem | `[start] Aktivering` |
| 4 | Start testøkt, trykk Xbox-knappen, velg Hjem i Game Bar | GameHome kommer fram med «Kjører nå» | `[aktivering] Videresendt`, `[økt]` |
| 5 | Win+R `gamehome://session` under økt | Samme som 4 | `[aktivering] protokoll` |
| 6 | Omstart av Legion Go | GameHome er fortsatt valgt home app | `[fse]` |
| 7 | Oppgrader til nyere bygg | Fortsatt valgt home app | `[fse]` |
| 8 | Slå av utviklermodus, omstart | GameHome er fortsatt valgbar og starter | `[fse]` |

Når testen er ferdig, kan utviklermodus stå av hvis test 8 viser at det virker. Ellers avgjør vi om den skal stå på.
