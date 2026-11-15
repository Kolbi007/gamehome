# 0013: YouTube-trailere i Home

Dato: 2026-11-15. Status: vedtatt.

- IGDBs offisielle API (`games`, `external_games`, `game_videos`) finner YouTube-ID-er for spillet når IGDB er konfigurert. Den kombinerte Sonic-utgaven har en reserve-ID fra [IGDBs presskit](https://www.igdb.com/games/sonic-the-hedgehog-3-and-knuckles/presskit).
- WebView2 lastes først når fokus blir stående på en spillflis og automatisk avspilling er aktivert. Spilleren bruker [YouTubes IFrame API](https://developers.google.com/youtube/iframe_api_reference) og identifiserer appen med et lokalt HTTPS-opphav som matcher MSIX-identiteten. HTML-filen ligger i apppakken. WebView2 leverer den uten en lokal server eller et nettverksoppslag til verten.
- YouTubes spiller henter skript og media fra YouTube og Googles medietjenester. GameHome sender video-ID-en og appens opphav til spilleren. Den får ingen IGDB-nøkkel eller tilgang til spillbiblioteket. Detaljvisningen tilbyr en nettleserlenke ved behov.
- Ved avspilling vokser spilleren i hovedfeltet mens spilltittel og fliser glir ned. Flisene beholder kontrollerfokuset, og B eller Escape lukker avspillingen.
- Bakgrunnsbildet beholdes under avspilling. Forhåndsvisningen lukkes ved feil, tidsgrense eller når brukeren forlater spillet i Home eller starter en økt. Spilleren frigjøres slik at WebView2 ikke er aktiv under spilling.
