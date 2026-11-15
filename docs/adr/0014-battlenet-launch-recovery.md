# 0014: Spillstart og gjenoppretting i Battle.net

GameHome sender én startforespørsel til Battle.net og venter på et synlig spillvindu. Det synlige vinduet bekrefter at oppstarten gikk videre. GameHome har ingen støttet måte å lese innloggingsstatusen til den lokale Battle.net-klienten på.

Battle.net håndterer innlogging, MFA og oppdateringer. Hvis spillvinduet uteblir, tilbyr GameHome å åpne Battle.net eller å prøve igjen etter brukerens valg. Tidsgrensen kan skyldes flere ting, så meldingen ber brukeren sjekke Battle.net uten å anta at kontoen er logget ut. GameHome sender ingen automatisk gjentakelse og leser eller lagrer ingen Battle.net-innloggingsdata.
