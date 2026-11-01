# Vurdering av produktnotatet mot det som er bygget

Dato: 2026-11-01. Grunnlag: `docs/product-brief.md`, `docs/architecture.md`, ADR 0001–0003 og koden i `src/`.
Notatet styrer brukeropplevelsen; CLAUDE.md og ADR-ene styrer tekniske valg. Konflikter står under «Spørsmål».

## Sluttbrukeropplevelsen

1. Legion Go starter rett i Xbox mode med GameHome som Gaming Home. Virker ikke FSE, starter GameHome som vanlig fullskjermsapp.
2. Home vises fra cache med en gang. Alt styres med innebygde kontroller eller en ekstern Xbox-kontroller, med synlig fokus.
3. Ett spill er én flis. «Spill via» velger lokalt, stream eller emulator, og siste valg huskes per spill.
4. Lokale spill startes gjennom butikkens egen mekanisme. GameHome går i dvale og tar ingen ressurser fra spillet.
5. Et spill som bare finnes på gaming-PC-en, starter direkte i Moonlight mot riktig Vibepollo-app, uten appvelger, Playnite eller skrivebord.
6. Under spill åpnes en øktmeny med en robust kontrollerhandling: Fortsett, Koble fra, Koble til igjen, Avslutt fjernspill, Hjem, Status.
7. Koble fra og avslutte er forskjellige handlinger. Flisen viser at spillet kjører videre på gaming-PC-en.
8. Når spillet avsluttes, kommer brukeren tilbake til samme flis, filter og scrollposisjon. En start som feiler, gir Prøv igjen, Åpne butikk eller Tilbake, aldri svart skjerm.
9. Usikre funn havner i «Nye funn». Hver kilde viser helsestatus. Rettelser brukeren har gjort, overskrives aldri.
10. Biblioteket virker offline. Ingen telemetri, og brukeren kan se og slette cachen.

## Avvik mellom notatet og det som er bygget

| Krav i notatet | Hva som mangler eller bryter | Hvor |
| --- | --- | --- |
| Host som eget begrep (7, 10) | `PlayAction` har ingen `HostId`. `Availability` ligger på spillemåten og ikke på hosten. | `src/GameHome.Core/Model/PlayAction.cs` |
| Separate livsløp i en økt (13) | `ISession` har bare én `Completion`. Moonlight-klienten, strømmen og spillet på hosten kan ikke rapporteres hver for seg. | `src/GameHome.Core/Runners/IRunner.cs` |
| Synlig helsestatus per kilde (8) | `IGameSource` har ingen helsestatus eller diagnostikk. | `src/GameHome.Core/Sources/IGameSource.cs` |
| Spill startet utenfor launcheren (7) | Ingen modell for å oppdage og vise slike økter | Core |
| Diagnostikk uten hemmeligheter, se og slett cache (18) | Ikke beskrevet i CLAUDE.md, og ingen kontrakt for det | CLAUDE.md, Core |
| Ekstern Xbox-kontroller (3.1) | Ikke nevnt i CLAUDE.md eller i testplanen | CLAUDE.md, `docs/architecture.md` |
| Oppdateringer uten Store (16) | CI lager artefakter, men det finnes ingen release-workflow eller versjonsnummerering | `.github/workflows/` |
| Lisens etter faktisk gjenbruk (19) | GPL-3.0-or-later er valgt fordi planen låner fra Heroic, Legendary og Steam ROM Manager. Ingen kode er lånt ennå. | `LICENSE`, `docs/architecture.md` |
| Clean room (din punkt 3) | Løst: analysemateriale er fjernet, og lærdommen er skrevet om til hypoteser med offentlige kilder | `docs/architecture.md`, «Tekniske hypoteser fra forundersøkelser» |
| FSE-pakking (6) | Single-project MSIX er valgt før det er vist at FSE ikke krever sparse- eller identity-pakke | ADR 0002 (merket «kan endres etter 0A») |

## Åpne punkter

**Meny under spill (5, 14).** Det finnes ett dokumentert spor vi ikke har testet: `IGameInput::RegisterSystemButtonCallback` i GameInput, som rapporterer Guide-knappen. Det er en offentlig API, ikke `XInputGetStateEx`. Hvis den leverer Guide på PC mens et spill har fokus, uten å stoppe Game Bar, er det nærmest ønsket opplevelse. Jeg forventer at den ikke gjør det. Derfor:

- **Hovedvei:** Xbox → Game Bar → Hjem.
- **Hurtigvei:** Legion-knapp → hurtigtast.
- **Test:** GameInput-sporet testes i POC B. Det tas i bruk bare hvis det virker uten å forstyrre Game Bar og Task View.

**Stream-livsløpet og Playnite (10–13).** Jeg anbefaler å starte med Playnite og Vibepollo-integrasjonen på hosten. Moonlight startes med `stream <host> "<app>"`, og appnavnene hentes fra `list --csv`.

- **Ferdig-signaler:** Moonlight-prosessen som avslutter betyr at klienten er ferdig. Vibepollos `/api/apps` betyr at appen på hosten er ferdig. Ingen av dem behandles som sannhet for den andre.
- **Usikkert:** om Vibepollo eller Playnite kan bære en stabil ID per spill, i stedet for appnavn. Inntil det er avklart, lagrer vi koblingen spill → host → appnavn selv, og en usikker kobling går til «Nye funn».
- **Playnite synes:** det aksepteres ved oppdatering, innlogging og krasj, men ikke i normalflyten. En companion bygges bare hvis POC D viser et konkret gap.

**Anbefalt rekkefølge for POC-ene:**

1. **A (FSE):** avgjør pakking og teknologi, og er billigst.
2. **B (øktmeny):** samme bygg og samme dager. Testes med Notepad som stand-in for et spill.
3. **D (remote-spill):** størst usikkerhet og størst verdi, og det er selve suksessdefinisjonen.
4. **C (Steam lokalt):** godt kjent og lav risiko. Kan bygges mens host-oppsettet for D venter.
5. **E (retro):** uavhengig av resten.

## Urealistisk eller motstridende, med alternativ

- **Kort Guide-trykk rett til vår meny, og skille mellom kort og langt trykk:** urealistisk så lenge Windows eier knappen. Alternativ: Hjem i Game Bar (to trykk) og én Legion-knapp (ett trykk).
- **«Brukeren ser bare spillet» i alle tilfeller:** butikkene på hosten viser oppdateringer, innlogging og UAC. Alternativ: definer dette som vedlikehold, og vis en tydelig status «Hosten trenger oppmerksomhet».
- **Overgang uten flimmer:** spill i eksklusiv fullskjerm kan minimeres når fokus byttes. Alternativ: test 2–3 spill og dokumenter det. Anti-cheat er ikke et problem, fordi vi ikke injiserer noe.
- **Alle butikker (3.2) samtidig med begrenset scope (20):** ikke en reell konflikt, men begge må stå i faseplanen. EA og Battle.net kommer sist.
- **Store-signering som støttet vei for FSE (6):** det finnes ingen offentlig vei. Alternativ: behold FSE som valgfri adapter, med fullskjermsapp som reserve.

## Svar fra produkteier (2026-11-01)

Vurderingen er godkjent. Svarene er skrevet inn i CLAUDE.md.

| # | Spørsmål | Svar |
| --- | --- | --- |
| 1 | Lisens | GPL-3.0-or-later beholdes. Lån fra Heroic, Legendary og Steam ROM Manager er tillatt, med kilde i `THIRD-PARTY-NOTICES.md`. |
| 2 | Guide-knappen | Ett trykk har prioritet. En bakknapp kan brukes som siste utvei. |
| 3 | Knapper og testing | Venstre Legion-knapp legges om fra å åpne Legion Space til å være «Xbox-knappen» (tastekombinasjon → `RegisterHotKey`). Bare innebygde kontroller testes. Ekstern kontroller tas ut av kravene for v1. |
| 4 | Hoster | Én gaming-PC. Wake-on-LAN er ønsket der det er mulig, men ikke et krav. |
| 5 | Playnite | Tillatt på gaming-PC-en, så lenge det er skjult og lett. Det er et godkjenningskrav i POC D. |
| 6 | Clean room utenfor repoet | Analysematerialet er slettet. Lærdommen står som hypoteser i `docs/architecture.md`. |
| 7 | HowLongToBeat og Metacritic | Valgfrie, uoffisielle kilder som er av som standard. Innlogging i oppsettet er tillatt. IGDB er standard. |
| 8 | Synlighet | Offentlig repo. Produkteier står som Kolbi007. |

**Endret rekkefølge i POC B:**

1. Venstre Legion-knapp via Legion Space og `RegisterHotKey`
2. GameInputs system-knapp-callback
3. Bakknapp
4. Reserveveien Xbox → Game Bar → Hjem, som testes uansett

**Rettet i samme omgang:** `Host` og `HostId`, separate livsløp i `IStreamSession`, `SourceHealth` på kildene, og en release-workflow.
