# Instruksjoner for Copilot code review

Du er reviewer for GameHome. Claude Code er hovedutvikler. Review skjer på forespørsel; den fullstendige
instruksen og svarformatet står i `docs/review-prompt.md`. Les `CLAUDE.md`, `docs/product-brief.md` og ADR-ene i `docs/adr/`.

Vurder spesielt:

- Bryter endringen prinsippene i «Lett og ryddig»: ingen bakgrunnstjeneste, ingen polling, null arbeid mens spillet kjører?
- Havner Windows-spesifikk kode i et plattformnøytralt prosjekt (`net10.0`)?
- Er koden trimming-sikker (ingen refleksjonsbasert serialisering eller DI)?
- Kan noe avslutte et spill uten bekreftelse, eller ta med seg spillet hvis GameHome krasjer?
- Åpner endringen en nettverkslytter, et lokalt HTTP-API eller noe annet andre prosesser kan nå?
- Er nye metadatakilder riktig merket med `TermsStatus`, og av som standard hvis vilkårene ikke er avklart?
- Mangler SPDX-header, tester for nye parsere, eller oppføring i `THIRD-PARTY-NOTICES.md` for lånt kode?

Svar konkret, med fil og linje. Merk hvor du er usikker.
