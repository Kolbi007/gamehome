# Beta: stabilisering 2026-11-15

Denne runden bygger på `bd96cdc`: bibliotekgjenoppretting, kildeisolering, skannekansellering og robust logging.

## Nye endringer

- Oppdateringer avbrytes ved spillstart, kanalbytte og avslutning. Overføringen har ti minutters totalfrist, grense på 512 MiB og kontroll mot publisert størrelse. Ufullstendige filer slettes; App Installer får bare en ferdig nedlasting.
- Stable viser når installert versjon er nyere enn siste Stable, fremfor å kalle installert Beta for Stable.
- Releaseversjon velges over eksisterende `vX.Y.Z`-tagger. Releasekjøringer serialiseres, Stable-downgrades avvises, og versjonerte pakker krever signeringssertifikat.
- Ved manuelt nummererte testbygg: sett GitHub-repovariabelen `GAMEHOME_MIN_PACKAGE_VERSION` til høyeste installerte testversjon, for eksempel `0.2.30.1`. Neste Beta går over denne. Upubliserte lokale versjoner kan ikke oppdages automatisk.
- Hostens Restore venter avgrenset på egne arbeidere og kontrollerer PID og opprettelsestid før avslutning. Original skrivebordstilstand lagres før dekkbildet kan skjule oppgavelinjen.

## Validering før push

- 172 Core-tester bestått med `CI=true`.
- Windows x64 JIT-bygg: 0 advarsler og 0 feil.
- Åtte releaseversjonstester bestått.
- Host: PowerShell 5.1-parsing, tre innebygde C#-blokker og isolerte prosesseierskapstester bestått. Ingen live endring av skrivebordet under test.
- Legion Go kunne ikke nås over SSH; fysisk testing er ikke bekreftet. Native AOT kontrolleres i PR/Release CI.

## Distribusjon og avgrensning

Push til `main` publiserer Beta-prerelease; `vX.Y.Z`-tagger publiserer Stable. Appen må ha Beta valgt for å se prereleasen.

Host-skriptet følger kildekoden, men installeres **ikke** på gaming-PC-en gjennom MSIX. Oppgrader det separat mellom strømmeøkter, uten gamle hjelpere i drift; gamle PID-filer uten opprettelsestid blir ikke brukt til å avslutte prosesser. Explorer/COM-kall under gjenoppretting kan fortsatt vente på Windows.

Menyforslaget i `menu-design-2026-11-15.md` er et separat designarbeid. Det endrer ikke dagens meny i denne Beta-utgaven. Full kansellering av metadata, sentral logganonymisering og fysisk batteri-/dvaletest gjenstår i veikartet.
