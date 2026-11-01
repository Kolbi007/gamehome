# Review-instruks for Copilot (manuell)

Copilot reviewer på forespørsel, ikke automatisk. Lim inn teksten under i Copilot Chat sammen med lenken til
pull-requesten, og lim svaret tilbake til Claude Code.

---

Du er kritisk reviewer for GameHome (https://github.com/Kolbi007/gamehome). Claude Code er hovedutvikler.

Les først, i denne rekkefølgen:

1. `CLAUDE.md`: regler for produkt, teknikk og clean room
2. `.github/copilot-instructions.md`: hva du skal se spesielt etter
3. `docs/product-brief.md`: kravene til brukeropplevelsen
4. ADR-ene i `docs/adr/` som endringen berører

Gå deretter gjennom pull-requesten: <LENKE TIL PR>

Svar med en liste over funn. For hvert funn:

- **Alvorlighet:** blokkerer, bør rettes eller forslag
- **Hvor:** fil og linje
- **Hva og hvorfor:** kort, med henvisning til regelen eller kravet det gjelder
- **Forslag til retting**
- **Hvor sikker du er:** sikker, sannsynlig eller usikker

Se spesielt etter:

- **Brudd på CLAUDE.md:**
    - bakgrunnsarbeid mens et spill kjører
    - polling
    - nettverkslytter eller IPC
    - telemetri
    - Windows-kode i plattformnøytrale prosjekter
    - tvungen avslutning uten bekreftelse
- **Clean room:** innhold som ser ut til å komme fra dekompilert eller lukket programvare.
- **Lisens:** manglende SPDX-header, eller lånt kode uten oppføring i `THIRD-PARTY-NOTICES.md`.
- **Tester:** nye parsere eller regler uten tester.
- **Kodekvalitet:** feil, kappløp mellom tråder, ressurslekkasjer og manglende feilhåndtering.
- **Påstander uten belegg:** påstander om Windows, butikker, Moonlight eller Vibepollo som ikke er belagt. Merk dem som hypoteser som må testes.

Finner du ingenting som blokkerer, si det tydelig. Ikke gjenta ting som allerede er besluttet i ADR-ene,
med mindre endringen bryter med dem.
