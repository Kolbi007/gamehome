# Designspråk for GameHome: lekkert, inviterende, ikke rotete, ikke «AI-aktig»

Undersøkt 2026-11-02 på oppdrag fra produkteier: «Jeg vil at GUI skal inspirere til å ville spille mye.»
Kilder er offentlige retningslinjer og omtaler (lenker nederst). Vi tar prinsipper, ikke utseendet til et bestemt
produkt; GameHome skal ikke ligne en kopi av Xbox, PlayStation, Steam eller Switch.

## Hva GameHome har i dag (0.1.85.0)

- Fliser i 16:9 (352×198) med logo over bildet, fokus forstørrer 1,07 på 160 ms, kant i bildets hovedfarge.
- «Ambilight»: bildet til spillet i fokus, uskarpt og dempet, bak flisene.
- Lasteskjerm med logo, rolig «pust» og bakgrunn som glir sakte (lært fra Stremio og Nuvio).
- Overskrift, tekstlinje med siste input, minne/målinger og knapper i samme flate som spillene.

Grunnlaget er godt. Det som mangler for «lyst til å spille», er et tydelig **spill i fokus** som får plass, og en
roligere ramme rundt.

## Fem prinsipper

### 1. Spillet er helten, rammen er stille
Konsoller og Steams nye «Big Art»-visning gjør det samme: bildet til spillet du står på, får mest plass, og alt annet
trer tilbake ([Gameluster](https://gameluster.com/big-picture-mode-overhaul/),
[Digital Trends om PS5](https://digitaltrends.com/?p=3705583)). Steam la også inn «litt farge fra spillet rundt
flisen» i 2023, samme idé som vår ambilight ([GamingOnLinux](https://www.gamingonlinux.com/2023/11/steam-deck-steam-big-picture-and-steamvr-get-lots-of-visual-tweaks-in-new-beta/comment_id=252921)).

For GameHome: et **hovedfelt øverst** med det fokuserte spillets bakgrunnsbilde (hero) og logo i stor størrelse, og
én rad med fliser under. Ambilighten blir bakgrunnen til hele skjermen i stedet for et felt bak flisene.

### 2. Én farge fra spillet, ellers nøytralt
Det tydeligste «AI-tegnet» er lilla-til-blå gradienter, cyan på mørkt, glass-effekt og glødende kanter brukt som pynt
([Impeccable](https://www.mintlify.com/pbakaus/impeccable/concepts/anti-patterns),
[Superdesign](https://www.superdesign.dev/blog/why-ai-design-looks-generic)). Rådet er å velge farge etter hva
produktet er for, og la farge ha en jobb.

For GameHome: grunnfargen er en varm, nesten svart grå (ikke blåsvart). **Aksentfargen er spillets egen** (vi har den
allerede fra bildet) og brukes bare på fokus og på hovedfeltet. Ingen fast merkefarge, ingen gradienter som pynt,
ingen glass. «Bytt-navn-testen»: hvis GameHome kunne hett hva som helst og sett likt ut, er det for generisk; her er det
spillenes bilder som gjør det unikt.

### 3. Fokus skal være umulig å misforstå, og alene
Microsofts råd for TV og Xbox: fokus skal være «klart og utvetydig», bevegelsen forutsigbar og korteste vei til målet
([Microsoft Learn](https://learn.microsoft.com/pl-pl/windows/apps/design/devices/designing-for-tv)). Bare ett element
har fokus, og det må synes fra avstand.

For GameHome: behold forstørrelse og kantfarge, legg til **demping av de andre flisene** (for eksempel 80 % lysstyrke)
så blikket går dit fokus er. Ingen sprett (bounce) eller elastisk bevegelse; det leses som billig
([Impeccable](https://www.mintlify.com/pbakaus/impeccable/concepts/anti-patterns)).

### 4. Raskt og rytmisk: kort bevegelse, lyd som gir følelse
Nintendo beskrev Switch-menyen som et sted der målet er å kutte ventetid, med små ressurser, ingen bakgrunnsmusikk og
lydeffekter med rytme og tempo ([Nintendo Everything om CEDEC 2018](https://nintendoeverything.com/nintendo-talks-about-switchs-os-home-menu-design-resources-have-less-than-200kb/),
[Nintendo Wire](https://nintendowire.com/news/2018/08/22/nintendo-talks-about-the-design-of-the-switchs-os-at-cedec-2018/)).
Vanlige tider for grensesnitt: 100–150 ms for fokus og trykk, 150–250 ms for paneler, myk utgang (ease-out) inn,
raskere ut ([Thumbprint](https://thumbprint.design/guidelines/motion)).

For GameHome: fokus 120–150 ms (i dag 160), paneler inn 200 ms og ut 150 ms. **Valgfrie, lave lyder**: et mykt «tikk»
når fokus flytter seg og en varm tone når et spill startes. Av/på i innstillingene. Lyden er det som gjør at en meny
føles levende uten å bli rotete.

### 5. Inviter til å fortsette, ikke til å lese
Det som får folk til å spille, er korteste vei til «der jeg slapp». Steam-grensesnittet ble rost for hjemmeskjermen
der du fortsetter siste spill ([Windows Central](https://windowscentral.com/gaming/pc-gaming/valve-is-finally-replacing-big-picture-mode-on-desktops-with-the-steam-deck-ui)).
Kritikken mot både Steam og Switch var flate, store elementer og mye tomrom uten innhold
([WePC](https://www.wepc.com/news/steam-ui-update/), [Nintendo Life](https://nintendolife.com/news/2025/04/uh-oh-switch-2s-home-screen-looks-just-as-bland-as-we-feared)).

For GameHome: hovedfeltet viser **én kort, sann linje** om spillet i fokus, for eksempel «Sist spilt i går · 6 t
totalt» eller «▶ Kjører på Gaming Laptop», og én tydelig handling («Spill», «Fortsett»). Ingen merker, prestasjons-
kaskader eller tall som ikke betyr noe. Sist spilte spill står først.

## Hva vi bør unngå (sjekkliste)

- Lilla/blå/cyan gradienter, glass-effekt, glødende kanter, skygge og kant på samme element.
- Sprett og elastiske animasjoner, forstørring av alt man peker på.
- Tre like kort på rad med ikoner over, sentrert «hero» med slagord.
- Tekst som forklarer det åpenbare; plassholdertekst; tall uten mening.
- Mange skriftstørrelser. Én familie (Segoe UI Variable, som Windows selv bruker), 4–5 størrelser.
- Innhold helt ut i kanten: hold viktig innhold innenfor ca. 5 % marg
  ([Amazon Fire TV](https://developer.amazon.com/docs/fire-tv/design-and-user-experience-guidelines.html)).

## Forslag til rekkefølge (små steg, hvert kan testes alene)

1. **Fargeregler i én fil** (design-tokens i XAML): grunnfarger, tekstfarger, 4–5 skriftstørrelser, marger, tider.
   Ingen synlig endring, men alt senere blir konsistent.
2. **Hovedfelt for spillet i fokus**: hero-bilde, logo, én linje fakta, én handling. Flisene blir én rad under.
3. **Demping av fliser uten fokus** og kortere fokustid (120–150 ms).
4. **Ryddig ramme**: flytt teknisk tekst (siste input, minne) bort fra Home til en feilsøkingsvisning.
5. **Valgfrie menylyder** (tikk og start), med av/på.
6. Senere, med metadata (fase 4): spilletid og «sist spilt» fra egen logg; HowLongToBeat som valgfri kilde.

Ingen av stegene krever nye avhengigheter. Steg 2 og 3 gir mest «lyst til å spille» per arbeid.

## Kilder

- Microsoft Learn: [Designing for Xbox and TV](https://learn.microsoft.com/pl-pl/windows/apps/design/devices/designing-for-tv)
- Amazon: [Fire TV design and UX guidelines](https://developer.amazon.com/docs/fire-tv/design-and-user-experience-guidelines.html)
- Smashing Magazine: [Designing for TV, part 2](https://smashingmagazine.com/2025/09/designing-tv-principles-patterns-practical-guidance/)
- Gameluster: [Steam's Big Picture beta makes game art the main event](https://gameluster.com/big-picture-mode-overhaul/)
- Windows Central: [Valve replacing Big Picture with the Steam Deck UI](https://windowscentral.com/gaming/pc-gaming/valve-is-finally-replacing-big-picture-mode-on-desktops-with-the-steam-deck-ui)
- GamingOnLinux: [Steam UI visual tweaks beta (2023)](https://www.gamingonlinux.com/2023/11/steam-deck-steam-big-picture-and-steamvr-get-lots-of-visual-tweaks-in-new-beta/comment_id=252921)
- WePC: [New Steam UI update](https://www.wepc.com/news/steam-ui-update/)
- Digital Trends: [PS5 home screen](https://digitaltrends.com/?p=3705583)
- Nintendo Everything: [Switch OS design at CEDEC 2018](https://nintendoeverything.com/nintendo-talks-about-switchs-os-home-menu-design-resources-have-less-than-200kb/)
- Nintendo Wire: [Switch OS design talk](https://nintendowire.com/news/2018/08/22/nintendo-talks-about-the-design-of-the-switchs-os-at-cedec-2018/)
- Nintendo Life: [Switch 2 home screen reception](https://nintendolife.com/news/2025/04/uh-oh-switch-2s-home-screen-looks-just-as-bland-as-we-feared)
- Impeccable: [Anti-patterns](https://www.mintlify.com/pbakaus/impeccable/concepts/anti-patterns)
- Superdesign: [Why AI design looks generic](https://www.superdesign.dev/blog/why-ai-design-looks-generic)
- Thumbprint: [Motion guidelines](https://thumbprint.design/guidelines/motion)
