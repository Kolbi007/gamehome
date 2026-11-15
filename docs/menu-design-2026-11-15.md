# Meny for GameHome på Legion Go

Forslag til gjennomgang etter Beta, 15. november 2026. Dette er design, ikke en beskrivelse av ferdige nye innstillinger. Grunnlag: `MainWindow.xaml`, `MainWindow.Menu.cs`, `QuickSettings`, `Setup`, `Update`, `Trailer` og `Feel`, samt produktbriefen og designforskningen i repoet. Koden kan endres under Beta-arbeidet; kontroller status før implementering.

## Det som finnes

- Strømikonet på Home åpner «Innstillinger og strøm»: oppdateringskanal og -sjekk, menylyder, kontoer/kilder, feilsøking, dvale, omstart, avslåing og avslutt GameHome. Lukk får trygt startfokus. Menyen har også en inngang fra «Kjører nå».
- M2-menyen uten aktiv økt har kontoer/kilder, hent manglende bilder/info, legg til spill og feilsøking; betingede valg omfatter Handheld Companion og gjenoppretting av PC-skjerm. Ytelsespanelet har lysstyrke, volum, Wi-Fi og Bluetooth. Disse inngangene bør samordnes, ikke dupliseres som uavhengige innstillinger.
- Kontooppsettet har blant annet Steam-bibliotek, SteamGridDB, IGDB og Vibepollo med innlegging/bytte av nøkler. Kildevisningen viser eksisterende status. Status som mangler nøkkel er ikke nødvendigvis en faktisk tilkoblingstest.
- Home har allerede en automatisk bildefremvisning ved inaktivitet. Stille trailer starter etter tre sekunder ved fokus: Steam først, deretter YouTube-ID fra IGDB; kun på lader, aktivt vindu, uten økt og med Windows-animasjoner aktivert. Dette er ikke dokumentasjon på at brukervalg for disse finnes.

## Foreslått struktur

Behold strømikonet og tilgjengelig navn «Strøm og innstillinger». Åpne en rolig, mørk flate med dempet spillbilde bak, samme typografi som Home og én tydelig fokusring. Topp: «Innstillinger og strøm», klokke/batteri, Lukk. Vis en enkelt vertikal liste med korte forklaringer; strøm ligger sist og åpner eget nivå:

1. **Hurtiginnstillinger** — lysstyrke, volum, Wi-Fi, Bluetooth og tilgjengelig ytelse. Samme kontroller og tilstand som eksisterende sidepanel.
2. **Utseende og lyd** — menylyder; foreslått «Bevegelse», «Lysbildefremvisning» og «Trailere» med nåværende status og detaljer på undersider.
3. **Bibliotek og kilder** — kontoer/kilder, legg til spill, hent manglende bilder og info. Hver kilde viser formål og status; avanserte kilder på neste nivå.
4. **System** — oppdateringskanal, se etter oppdateringer, versjon og feilsøking; betinget gjenoppretting og Handheld Companion der relevant.
5. **Strøm** — Dvale, Start på nytt, Slå av, Avslutt GameHome. Beskriv forskjellen mellom enhet og app.

På bred skjerm kan en venstre kategoriliste og høyre detaljflate vises samtidig; på liten høyde brukes ett nivå om gangen med fast topp og rullbart innhold. Unngå horisontal scrolling. Valg er store rader med etikett, aktuell verdi og eventuelt kort status, ikke ikon alene. Sikt mot minst 56 effektive WinUI-dip i høyde, 48 i bredde og minst 8 dip mellom handlinger; test fysisk på Legion Go med Windows-skalering og større tekst. Dette er et praktisk startpunkt, ikke en garanti for fysisk målestørrelse. Microsoft anbefaler større fysiske berøringsmål og god avstand; mål faktisk størrelse og feiltrykk på enheten ([XAG 107](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/107)).

## Fokus, sikkerhet og aktivt spill

Start på **Lukk**, aldri på strømhandling. D-pad/venstre spak flytter synlig fokus, A velger, B går ett nivå tilbake; B fra roten lukker og returnerer fokus til knappen som åpnet menyen. Berøring og tastatur når alle handlinger. Rulling følger fokus, og skjulte/deaktiverte valg kan ikke få fokus. Juster eksplisitt XY-retninger der to kolonner gir uventede hopp. Skyveknapper engasjeres med A, justeres med venstre/høyre og frigjøres med B; vis dette på skjermen. Bekreftelsesdialoger starter på Avbryt og returnerer til handlingen ved lukking. Test i praksis, inkludert skjermtastatur og skjermleser ([WinUI gamepad](https://learn.microsoft.com/en-us/windows/apps/develop/input/gamepad-and-remote-interactions)).

Under aktiv økt åpnes først eksisterende «Kjører nå», med **Tilbake til spillet** i fokus og B tilbake til spillet. Hurtiginnstillinger og strøm kan nås derfra. Bibliotekshenting, oppdateringer og API-tester skjules eller vises som utilgjengelige med «Tilgjengelig etter at spillet er avsluttet»; ikke start nettverk, disk-IO eller polling under spill. Strømvalg skal tydelig advare om aktivt spill og kreve bekreftelse før handling; avslutt aldri spillet med makt uten eksplisitt bekreftelse. Behold GameHomes dokumenterte snarveier og Windows' eierskap til Xbox-knappen.

## Bilder, film og tilgjengelighet

Foreslåtte valg, ikke eksisterende løfter: lysbildefremvisning av/på, start etter inaktivitet og bildefrekvens/intervall; trailere av/på, kilde (Steam, eventuelt YouTube), forsinkelse og «bare på lader» som standard. Vis også hvorfor noe er pauset: batteri, redusert bevegelse eller aktiv økt. Ingen lyd som standard. Ved redusert bevegelse: statiske bilder, ingen automatiske overganger/video; la brukeren åpne manuelt om dette senere støttes. Behold enkel knappetekst, tydelig kontrast og fokus uten at farge alene formidler status. Følg systemets tekstskalering og test ved høy skalering.

YouTube innebærer tredjepartstrafikk og automatisk avspilling kan dele avspillingsdata ved lasting. Gjør valg og konsekvens tydelig før aktivering; undersøk vilkår, spillerkrav og nåværende implementasjons samsvar før levering. Fallback til statisk bilde når video mangler eller feiler ([YouTube-spiller](https://developers.google.com/youtube/player_parameters)).

## Kilder og personvern

Hver kildeside bør vise hva som hentes, hvilke nettverksmottakere som brukes, status «Ikke konfigurert / lagret / testet / feil», sist testet og handlingene Konfigurer, Test, Bytt og Fjern. Ikke kall lagrede nøkler «tilkoblet» før en test er vellykket. Masker hemmeligheter, bruk Windows Credential Locker, aldri logg nøkler/tokens eller persondata, og rens feil og diagnostikk. Fjern lokalt og forklar separat hvordan leverandørens nøkkel tilbakekalles. Åpne offisiell registrering i nettleser; tilby lim inn og kontrollerstyrt skjermtastatur. Ingen påstått støtte for pålogging som ikke er implementert.

IGDB bruker Twitch-klient-ID og klienthemmelighet for tilgangstoken ([IGDB](https://api-docs.igdb.com/#authentication)); del aldri en felles hemmelighet i distribuert app. Steam-oppsett er en annen flyt enn SteamGridDB; forklar Steam-ID og brukerens API-nøkkel, og legg aldri inn en publisher-nøkkel i klienten ([Steam](https://partner.steamgames.com/doc/webapi_overview/auth)). Verifiser gjeldende tilgangsvilkår før nye integrasjoner. Uoffisielle kilder, inkludert eventuell scraping av HowLongToBeat/Metacritic, ligger under Avansert, er av som standard når vilkår ikke er godkjent og krever vurdering av vilkår, stabilitet, lisens og attribusjon før implementering. Vis kilde på metadata; lokale butikkbilder først, plassholder ved mangel, og overskriv aldri brukerens valg. Cache-størrelse, vis/slett cache og separate kildestatus er foreslåtte forbedringer, med sletting bak bekreftelse.

## Gjennomføring og åpne spørsmål

1. Etter Beta: verifiser gjeldende funksjoner og tegn navigasjonskart. Flytt/lenk eksisterende handlinger uten å endre atferd; test fokus, B, berøring, lange tekster og aktiv økt på Legion Go.
2. Legg til tilstandsvisning og trygge bekreftelser; samordne status og feilhåndtering for eksisterende kilder. Test hemmelighetsfjerning i diagnostikk og at aktive økter ikke starter arbeid.
3. Innfør faktiske, vedvarende innstillinger for fremvisning og trailer med batteri- og tilgjengelighetsregler; test strømbytte, inaktivitet, avslutning og nettverksfeil. Avklar YouTube-vilkår først.
4. Vurder nye kilder og cachekontroll separat, med vilkårs- og sikkerhetsgjennomgang før de aktiveres.

Avklar med produkttest om brukere forventer strømikonet å åpne strøm direkte, hvilke hurtigvalg som trengs oftest, og om menyen fungerer fysisk med håndholdt grep. Mål batteribruk og kontrollér strømhandlinger, skjermleser og reell fokusoppførsel på maskinen før designet låses.
