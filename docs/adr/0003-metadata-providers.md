# 0003: Metadatakilder med fasetter og vilkårsstatus

Dato: 2026-11-01. Status: vedtatt.

## Kontekst

Vi vil vise spillinfo, cover, hvor lang tid et spill tar (HowLongToBeat) og vurderinger (Metacritic).
Kildene er ulike: noen har offisielle API-er med vilkår, andre har ingen API og må leses fra nettsiden.

| Kilde | Fasetter | Offisielt API | Status |
| --- | --- | --- | --- |
| IGDB | Detaljer, bilder, spilletid (`game_time_to_beats`), vurderinger (`aggregated_rating`) | Ja, Twitch-nøkkel, maks 4 forespørsler per sekund | Approved |
| SteamGridDB | Bilder | Ja, API-nøkkel | Approved |
| Steams lokale bildecache | Bilder | Lokale filer | Approved |
| Steam Store `appdetails` | Detaljer, bilder, Metacritic-score der Steam har den | Udokumentert | Unofficial |
| HowLongToBeat | Spilletid | Nei | Unofficial |
| Metacritic | Kritiker- og brukerscore | Nei | Unofficial |
| ScreenScraper | Detaljer og bilder for retro | Ja, men vilkår for open source uavklart | PendingReview |

## Beslutning

- `IMetadataProvider` beskriver seg selv med `MetadataProviderInfo`: hvilke fasetter den leverer
  (`Details`, `Art`, `PlayTime`, `Ratings`), `TermsStatus`, om den trenger API-nøkkel, maks forespørselsrate og
  attribusjonslenke.
- Resultatet er felles: `GameDetails`, `ArtSet`, `PlayTime` (hovedhistorie, med ekstra, alt) og `Rating`
  (0–100, kritiker eller bruker, med kildelenke).
- Bare kilder med `TermsStatus.Approved` og uten API-nøkkel er på uten oppsett (i dag Steams lokale bildecache).
  Kilder med nøkkel slås på når brukeren legger inn nøkkelen. Uoffisielle kilder slås på manuelt under Avansert,
  med tydelig merking om at de kan slutte å virke.
- Første valg for spilletid og vurderinger er IGDB, fordi det har et offisielt API.
  HowLongToBeat og Metacritic blir valgfrie tillegg.
- Vurderinger og spilletid vises med kilde og lenke.
- Alt caches lokalt (`GameMetadata` med tidsstempel). Ingen oppslag mens et spill eller en stream kjører.

## Konsekvenser

- Uoffisielle kilder skal ha egne parser-tester med lagrede eksempelsvar, slik at endringer på nettsiden oppdages i CI
  og ikke på enheten.
- Før en uoffisiell kilde aktiveres som standard, eller før offentlig utgivelse, må vilkårene sjekkes på nytt.
- Implementasjonen av klientene kommer i fase 4. Kontraktene og policyen finnes nå (`GameHome.Metadata.KnownProviders`).
