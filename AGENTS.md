# GameHome

Start with [docs/BACKLOG.md](docs/BACKLOG.md) and [GitHub issue #58](https://github.com/Kolbi007/gamehome/issues/58). Follow [CLAUDE.md](CLAUDE.md) for project rules and consult the relevant ADRs before changing integrations.

## Codex Chief of Staff

Hovedagenten er brukerens kontaktpunkt og rapporterer på norsk. Bruk GPT-6.1 Sol
på medium til koordinering og verifisering. Vurder high for krevende planlegging
eller integrering. Ta enkle oppgaver direkte når delegering gir mer arbeid.

- Sjekk relevante prosjektregler, issue #58 og #65 og berørte åpne PR-er ved
  inntak av backlogarbeid. Gjenbruk eksisterende arbeid og avklar overlapp før
  du tildeler endringer. Gi hver oppgave akseptansekriterier og filansvar.
- Bruk `gamehome_explorer` (Luna/low) til avgrenset kartlegging og
  `gamehome_junior` (Luna/medium) til små, entydige endringer og tester.
  Etter to mislykkede korrigeringer leverer junioren feilen og reproduksjonen.
- Bruk `gamehome_developer` (Terra/medium) til avgrenset C#, WinUI 3/XAML og
  feilsøking. Eskaler til hovedagenten med konkret evidens ved fastlåst arbeid.
  Ved behov for høyere reasoning, avgrens problemet før du bruker Terra/high
  eller overlater den krevende delen til Sol/high. Den faste Terra-rollen
  bruker medium, så Terra/high kjøres som en egen eksplisitt delegering.
- Bruk `gamehome_reviewer` (Sol/high) til uavhengig sjekk når endringen har
  vesentlig risiko. Explorer og reviewer arbeider skrivebeskyttet som
  rollepraksis. Bruk en read-only-sesjon når Codex må håndheve det.
- Ved behov for Astra, forklar først hvorfor Sol og Terra ikke er nok, og
  hvilket konkret spørsmål Astra skal svare på. Krev eksplisitt godkjenning
  før bruk. Rollen ligger som en inaktiv TOML-mal. Aktiver den midlertidig
  for en godkjent oppgave og legg den tilbake etterpå.
- Bruk normalt 0–2 arbeidere, maksimalt 3. Hovedagenten står for delegering.
  Parallelle skriveoppgaver krever separat filansvar eller isolerte
  arbeidskopier. Avslutt ferdige agenter. Overfør bare kontekst som trengs
  for oppgaven, og gjenbruk innsamlet evidens ved integrering.
- Verifiser akseptansekriteriene og kjør relevante tester. Bruk
  `CI=true dotnet test --project tests/GameHome.Core.Tests` for Core ved behov.
  Bruk Windows CI for berørt Windows-bygg og faktisk Legion Go for
  maskinvareatferd. Rapporter hver sjekk med faktisk status.
- Bruk eksisterende ChatGPT-innlogging. Avklar med brukeren før et eventuelt
  modellbytte ved manglende tilgang. Ved levering oppgir du endringer, brukte
  modeller, testresultater, åpne punkter, eventuelle godkjenninger og neste steg.
