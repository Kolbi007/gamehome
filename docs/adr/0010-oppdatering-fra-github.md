# 0010: Oppdatering fra GitHub

Dato: 2026-11-05. Status: vedtatt av produkteier.

## Kontekst

Nye versjoner ble installert av Claude over SSH. Produkteier skal kunne oppdatere selv, uten PC og uten kommandoer.

## Beslutning

- Hver sammenslåing til `main` gir en Beta-prerelease `v0.2.<nummer>` med signert MSIX. En versjonstagg `vX.Y.Z`
  gir en Stable-release.
- GameHome ser etter en nyere utgivelse høyst hver sjette time, når Home blir aktiv og aldri mens et spill kjører
  (`GET https://api.github.com/repos/Kolbi007/gamehome/releases`). Stable ignorerer prereleases; Beta følger både
  prereleases og stabile utgivelser. Kanalen velges på hovedsiden, lagres lokalt og har Stable som standard.
- «Oppdater» laster ned pakken til appens midlertidige mappe og åpner den i Windows' App Installer, som sjekker
  signaturen (samme utgiver som installert pakke) og ber brukeren bekrefte. Manuell nettlesernedlasting er ikke nødvendig.
  GameHome installerer ikke selv og trenger ingen begrensede rettigheter (`packageManagement`).

## Konsekvenser

### Stable and Beta channels (2026-11-14)

- Merging to `main` publishes a Beta prerelease. A `vX.Y.Z` tag publishes a Stable release.
- The app defaults to Stable. The power/settings menu on the Home screen exposes the channel selector, a manual update
  check, and any available update action without requiring a game session or the M2 menu. Switching to Beta immediately
  checks that channel. Stable ignores prereleases; Beta accepts both prereleases and Stable releases.
- The app downloads the selected MSIX itself and opens Windows App Installer for the user to confirm. The user no longer
  needs to find and download a release asset manually. Silent installation is intentionally not enabled.
- Switching from Beta to Stable does not downgrade an installed package. Stable will update it after a Stable package
  with a greater MSIX version is published.

- Keep recent Beta prereleases for diagnostics and rollback; older releases may be archived periodically.
- Signeringen bruker fortsatt utviklersertifikatet; første installasjon krever at det er tiltrodd (Install-DevBuild.ps1).
