# Stabilisering: første kontrollpunkt, 2026-11-15

Utgangspunkt: `acd7d63`, PR #51. Arbeidsgren: `codex/stability-foundation`.

## Leveranse og review

- GPT-6.1 Sol: bibliotekbackup, gjenoppretting, skrivebeskyttelse og regresjonstester.
- GPT-6.1 Sol: isolerte skannefeil, delvise resultater, kansellering ved spillstart og regresjonstester.
- GPT-6 Luna: README og veikart; Astra rettet statuspåstander før godkjenning.
- GPT-6 Astra: risikoreview, loggrotasjon/diskfeil, sluttintegrasjon og validering.

Review avviste en ubegrenset venting på synkron skanning før spillstart. Kansellering er derfor samarbeidsbasert: neste kilde stoppes og resultater forkastes, mens et påbegynt systemkall kan fullføres. Spillstart venter ikke på dette.

Bibliotekreview kontrollerte at ukjent versjon aldri overskrives, at ødelagte originalbytes beholdes når backup brukes, og at brukerens overstyringer overlever gjenoppretting. En skrivebeskyttet økt har et vedvarende varsel. Backup er normalt forrige lagrede versjon, så de siste endringene kan mangle ved gjenoppretting.

## Validering

- `CI=true dotnet test --project tests/GameHome.Core.Tests -c Release`: 165 bestått, 0 feil (18 flere enn utgangspunktet).
- Windows x64 JIT-bygg med `CI=true`: bestått, 0 advarsler og 0 feil. Native AOT og Legion Go må verifiseres før Stable.
- Ingen host-skript, enhetsoppsett eller installerte appversjoner er endret.

## Neste avgrensede oppgaver

1. Sol: tidsfrist og avbrytbarhet for oppdateringsnedlasting; Astra reviewer installasjonsflyten før samling.
2. Sol: avgrenset Hide/Restore på hosten, med tester av gjenoppretting; Astra reviewer prosesseierskap og feilforløp.
3. Sol: utvide kanselleringen til metadata/trailere og håndtere resterende stille kildefeil.
4. Sol: monoton versjonspolicy med tester for Beta/Stable/manuelle bygg; Astra reviewer én samlet diff.
5. Luna: oppdater teststatus fra faktiske resultater. Terra brukes bare når UI-observasjon gir ekstra verdi.
6. Legion Go: verifiser varsler med kontroller, oppstart mens skanning går, retur og gjentatt dvale. Deretter måles ressursbruk.

Sentral anonymisering av logger, full kildehelseside og bred arkitekturoppdeling er fortsatt planlagt. Unngå å kalle skanning helt IO-fri under spill før alle aktuelle kilder kan avbrytes.
