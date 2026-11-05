# 0010: Oppdatering fra GitHub

Dato: 2026-11-05. Status: vedtatt av produkteier.

## Kontekst

Nye versjoner ble installert av Claude over SSH. Produkteier skal kunne oppdatere selv, uten PC og uten kommandoer.

## Beslutning

- Hver sammenslåing til `main` gir en GitHub-release `v0.2.<nummer>` (pre-release) med signert MSIX (release.yml).
  Testbygg fra grener er 0.1.N og ligger alltid under.
- GameHome ser etter en nyere release høyst hver sjette time, når Home blir aktiv og aldri mens et spill kjører
  (`GET https://api.github.com/repos/Kolbi007/gamehome/releases`). Finnes det en, vises «Oppdater til …» i M2-menyen
  og et varsel i Home.
- «Oppdater» laster ned pakken til appens midlertidige mappe og åpner den i Windows' App Installer, som sjekker
  signaturen (samme utgiver som installert pakke) og ber brukeren bekrefte. GameHome installerer ikke selv og trenger
  ingen begrensede rettigheter (`packageManagement`).

## Konsekvenser

- Mange pre-releases på GitHub; de er små og kan ryddes.
- Signeringen bruker fortsatt utviklersertifikatet; første installasjon krever at det er tiltrodd (Install-DevBuild.ps1).
