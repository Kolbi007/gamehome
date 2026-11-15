# 0012: Bruk Lenovo Legion Quick Menu til ytelsesprofiler

Erstatter arbeidsflyten i ADR 0006, 0007 og 0011 for denne Betaen. Den tidligere løsningen leste en Handheld Companion-profil og sendte hurtigtaster for TDP. Produkteier ønsker å bruke Lenovos kontroller og Quick Menu på grunn av problemer med Handheld Companion.

GameHome bruker Windows til kontrolleravlesning, lysstyrke, volum og skjerminnstillinger. For effektprofil, TDP og Lenovo-innstillinger åpner brukeren Quick Menu med høyre Legion-knapp. GameHome lar Legion Space kjøre. Game Bar → Hjem og GameHomes valgfrie hurtigtaster gir tilgang til øktmenyen.

GameHome behandler kontrollerinput når eget vindu har forgrunnen. Bruk Game Bar → Hjem eller den valgfrie Home-hurtigtasten for å hente GameHome frem.

Lenovo dokumenterer Quick Menu og ytelsesvalg i sin enhetsveiledning. Direkte styring fra GameHome krever en støttet integrasjon som kan verifiseres på enheten. [Lenovo Legion Go 8APU1](https://support.lenovo.com/zm/en/solutions/ht515677-introduction-to-legion-go-8apu1)

Fysisk test gjenstår for innebygde kontrollere, Quick Menu, dvale og lokal spillstart mens Legion Space kjører.
