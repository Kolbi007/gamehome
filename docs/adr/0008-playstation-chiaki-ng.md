# 0008: PlayStation via chiaki-ng

Dato: 2026-11-05. Status: vedtatt av produkteier.

## Kontekst

Produkteier vil strømme fra PS5 til Legion Go fra GameHome, slik vi strømmer fra gaming-PC-en med Moonlight.
chiaki-ng (https://github.com/chiaki-ng/chiaki-ng, AGPL-3.0) er en åpen klient for PlayStation Remote Play.

## Beslutning

- GameHome starter chiaki-ng som et eget program, som Moonlight. Ingen kode fra chiaki-ng tas inn (lisensen skulle
  ellers gjort GameHome AGPL).
- Hver konsoll brukeren har koblet til i chiaki-ng, får én flis («PlayStation 5»). Remote Play kan ikke starte et
  bestemt spill; flisen åpner konsollens hjemmeskjerm.
- Før start søker GameHome etter konsollen på det lokale nettverket med en UDP-kringkasting («SRCH», port 9302 og 987)
  for å finne adressen. Det er GameHomes eneste nye nettverkskall; det går bare ut på det lokale nettverket.
- chiaki-ng startes med `--fullscreen --exit-app-on-stream-exit stream <navn> <adresse>`. «Koble fra» i menyen lukker
  chiaki-ng pent; spillet fortsetter på konsollen.
- GameHome leser bare chiaki-ngs innstillinger (registrerte konsoller i `HKCU\Software\Chiaki\Chiaki`), aldri nøklene,
  og skriver dem aldri.

## Konsekvenser

- Brukeren kobler konsollen til chiaki-ng én gang (PSN-innlogging og en kode fra konsollen).
- Konsoll i hvilemodus: første versjon ber brukeren slå den på. Vekking fra GameHome vurderes etter test med konsollen.
- Formatet på innstillingene er chiaki-ngs eget og kan endres; faller lesingen bort, forsvinner flisen, og ingenting
  annet påvirkes.
