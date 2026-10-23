# 11. Vragenlijst om het plan strakker te maken

> Bij elke vraag staat **waarom** die ertoe doet en een **voorstel** (wat we doen als je niets
> anders aangeeft).
>
> **Prioriteit**
> - **P1**: nodig vóór de start van de bouw.
> - **P2**: nodig vóór de betreffende fase.
> - **P3**: kan later.
>
> De vraagnummers blijven gelijk, zodat eerdere antwoorden te herleiden zijn.

## Beantwoord (23-10-2026)

| # | Vraag | Antwoord |
|---|---|---|
| C1 | Hoe wordt de materialiteit bepaald? | **De gebruiker kiest de materiële posten zelf**; geen berekening |
| C2 | Op welk niveau wordt materieel aangevinkt? | **Per hoofdpost**, met doorklikken naar de subposts |
| C3 | Wat gebeurt er bij niet-materiële posten? | **Geen vragen en geen werk**: alleen zichtbaar in het rapport, waar de presentatie en opmaak aan te passen zijn |
| C4 | Doet het systeem een voorstel voor materiële posten? | **Nee**, de gebruiker kiest |
| D4 | Hoe wordt er gedeponeerd? | **Voorlopig negeren**; buiten scope |
| B4 | Hoe komen historische cijfers binnen? | Via het **standaard importsjabloon** met het betreffende boekjaar ([§3.3](03-gegevensimport-xaf-excel-rgs.md#33-standaard-importsjabloon-excelcsv)) |
| – | Import | **Standaard importsjabloon**: boekjaar plus grootboeknummer, omschrijving en bedrag; alleen importeren bij saldo nihil |
| – | Naamgeving | **Algemeen** houden: een eigen samenstelapplicatie, geen afgeleide van een bestaand pakket |

## A. Organisatie en gebruik

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| A1 | P1 | Hoeveel gebruikers krijgt het systeem in totaal, en hoeveel werken er tegelijk? | Serverformaat, rollen | 5–15 gebruikers, maximaal 10 tegelijk |
| A2 | P1 | **Werken medewerkers ook buiten kantoor (thuis, bij klanten)? Moet dat ook zonder internet- of VPN-verbinding kunnen?** | Bepaalt de architectuur: kantoorserver of offline desktop ([§7.2](07-technische-architectuur.md#72-architectuurkeuze-voor-meerdere-gebruikers)) | Thuis en bij klanten via VPN of beveiligde toegang; offline niet nodig |
| A3 | P1 | Is er een **kantoorserver** (Windows Server, VM, NAS) of een ICT-partner die die levert en beheert? Hoe is de back-up nu geregeld? | Hosting en back-up van de centrale database | Een server-VM bij de ICT-partner, met nachtelijke back-up en een kopie buiten de server |
| A4 | P1 | Welke **rollen** zijn er, en wie mag wat? Is **review door een tweede persoon** altijd verplicht, of afhankelijk van het opdrachttype? | Rechtenmodel en goedkeuringsflow | Rollen medewerker, reviewer, accountant en beheerder. Review verplicht bij samenstellen BV, optioneel bij winstaangifte EZ/VOF |
| A5 | P2 | Moeten medewerkers **alle klanten** zien, of alleen hun eigen team of portefeuille? | Autorisatie per klant | Iedereen ziet alles, met een filter "mijn klanten" |
| A6 | P2 | Is inloggen met **gebruikersnaam en wachtwoord** genoeg, of wil je ook **tweestapsverificatie** of inloggen met het **Microsoft 365-account**? | Beveiliging, gebruiksgemak | Wachtwoord, plus verplichte tweestapsverificatie bij toegang buiten kantoor |
| A7 | P2 | Hoeveel dossiers per jaar, ongeveer verdeeld over **BV micro, BV klein, EZ en VOF**? | Prioriteit van sjablonen, de pilot | Graag de aantallen |

## B. Administraties en import

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| B1 | P2 | **Welke boekhoudpakketten** gebruiken je klanten (top 5)? Voert het kantoor zelf administraties, en in welk pakket? | Testbestanden en koppelsjablonen. De import loopt eerst via het sjabloon, XAF volgt | Per pakket 2–3 voorbeeldbestanden |
| B2 | P1 | Kun je **10–20 saldibalansen of XAF-bestanden** van afgeronde dossiers aanleveren, met het bijbehorende **rapport uit het huidige pakket**? Bij voorkeur geanonimiseerd, of met afspraken over vertrouwelijkheid | Golden-master-tests: onze uitkomst moet gelijk zijn aan het huidige rapport | Ja, via een beveiligde map, niet via GitHub |
| B3 | P2 | Heeft het kantoor een **eigen standaard-rekeningschema**? Hebben de rekeningen RGS-codes? | Wordt het eerste koppelsjabloon | Als het bestaat, is dat koppelsjabloon nr. 1 |
| B5 | P2 | Hoe worden bij **EZ/VOF** privéonttrekkingen en -stortingen geboekt: aparte privérekeningen per vennoot? | Verloop ondernemingsvermogen en kapitaal per vennoot | Privérekeningen per vennoot herkennen via de rubrieken *Privé {vennoot}* |
| B6 | P2 | **Importsjabloon**: is een optionele kolom **RGS-code** handig (dan rubriceert het systeem automatisch), of liever alleen de drie vaste kolommen? | Gebruiksgemak tegenover automatisch rubriceren | Optionele vierde kolom; leeg mag |

## C. Werkprogramma en materiële posten

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| C5 | P2 | **Wie mag standaardstappen verwijderen**: iedereen, of alleen reviewer of accountant? Is een **reden** verplicht? | Dossierkwaliteit en review | Iedereen, met verplichte reden. De reviewer ziet een overzicht; verplichte stappen alleen op "n.v.t." |
| C6 | P2 | Hoe wordt **afgetekend**: per stap (initialen en datum) en per post goedkeuren? Twee niveaus of drie? | Goedkeuringsflow | Per stap "uitgevoerd", per post "gereviewd", het dossier als geheel "goedgekeurd door accountant" |
| C7 | P2 | Is er een **bestaand werkprogramma** (Word, Excel of uit het huidige pakket) waarmee we beginnen? Wie stelt het op, en hoeveel uur heeft die persoon? | De inhoud is minstens de helft van het werk ([§8.3 R1](08-roadmap-risico-s-en-beslispunten.md#83-risicos)) | Eén vaktechnisch verantwoordelijke, 1–2 dagen per week tijdens de bouw |
| C8 | P2 | Mogen eigen toegevoegde stappen worden **voorgesteld voor het kantoorsjabloon**? | Sjabloonbeheer | Ja, de beheerder beslist |
| C9 | P2 | **Doorklikken**: ik ga ervan uit dat het vinkje *materieel* alleen per hoofdpost staat. Doorklikken toont dan de cijfers per subpost en grootboekrekening, en een stap kan optioneel bij een subpost horen. Klopt dat, of wil je ook per subpost kunnen kiezen of die materieel is? | Opbouw van het planningsscherm en het sjabloon | Zoals beschreven: vinkje per hoofdpost, doorklikken voor cijfers en stappen |

## D. Opdrachttypen en rapporten

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| D1 | P1 | Welke **opdrachttypen** voeren jullie uit? Bijv. samenstellen BV (met verklaring), winstaangifte EZ/VOF (zonder verklaring), samenstellen EZ/VOF met verklaring, administratieve dienstverlening | Sjablonen voor werkprogramma en rapport, voetteksten | MVP: *Samenstellen BV (micro/klein)* en *Winstaangifte EZ/VOF* |
| D2 | P1 | **BV micro**: hetzelfde rapport als BV klein, of een beknopte versie? Graag een voorbeeld | Rapportsjabloon micro | Het sjabloon van klein, met minder toelichtingen |
| D3 | P1 | **VOF**: hoe presenteren jullie de **winstverdeling en het kapitaal per vennoot**? Graag een voorbeeldrapport VOF | Rapportsjabloon VOF | Verloop ondernemingsvermogen per vennoot naast elkaar, plus een winstverdelingstabel |
| D5 | P2 | **Fiscale positie BV**: welke correcties komen standaard voor (niet-aftrekbare kosten, boetes, gemengde kosten, giften, KIA, verliesverrekening, fiscale eenheid)? | Omvang van de Vpb-module | Zoals in het voorbeeld, plus verliesverrekening, KIA en de fiscale eenheid-variant |
| D6 | P2 | **Fiscale winst EZ/VOF** in het rapport (ondernemersaftrek, MKB-winstvrijstelling, FOR), of alleen in de aangiftesoftware? | Omvang van de IB-module | Niet in het rapport, zoals in het voorbeeld |
| D7 | P2 | **Kengetallen en meerjarenoverzicht**: altijd dezelfde set als in het voorbeeld? Ook voor EZ/VOF? | Kengetallenbibliotheek | De set uit het voorbeeld voor BV; voor EZ/VOF uit, per dossier aan te zetten |
| D8 | P2 | **Word-export**: mag een in Word bewerkt rapport het definitieve rapport zijn? Dan moet de definitieve PDF terug in het dossier | Dossierintegriteit ([§7.6](07-technische-architectuur.md#76-rapportgeneratie-één-model-meerdere-uitvoerformaten)) | Definitief altijd uit de app (PDF). Word voor concepten en nabewerking, met een upload-verplichting als dat het definitieve rapport wordt |
| D9 | P2 | Kun je **huisstijl** (logo, kleuren, lettertype) en de **gescande handtekening** per tekenbevoegde accountant aanleveren? | Rapportopmaak | Ja. De handtekening wordt alleen geplaatst bij definitief uitbrengen door die accountant |
| D10 | P2 | **Specificaties**: standaard de grootboekrekeningen per rubriek, met aan te passen omschrijving, samenvoegen en verbergen. Akkoord? | Presentatie-editor | Ja |
| D11 | P3 | Is er een **Engelstalig rapport** nodig? | Vertaling | Nee |
| D12 | P3 | Hoe verloopt het **conceptproces richting klant**: concept-PDF of -Word mailen, en hoe komt de bevestiging (LOR) terug? | Afrondingsflow | Concept met watermerk; de ondertekende LOR uploaden als bijlage |

## E. Dossier, bijlagen en klantgegevens

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| E1 | P2 | Waar staan **klantgegevens en Wwft-dossiers** nu (CRM, het huidige pakket, Excel)? Is een eenmalige import van het klantenbestand nodig? | Klantbeheer en migratie | Eenmalige Excel-import van klanten en functionarissen |
| E2 | P2 | Waar staan **bijlagen** nu (SharePoint, fileserver)? Slaat de app ze zelf op, of verwijst hij naar een bestaande mappenstructuur? | Opslag en back-up | De app slaat zelf op |
| E3 | P2 | **Gesloten dossiers**: nooit heropenen (alleen kopiëren), of heropenen door een accountant met een reden? | Afsluitbeleid | Heropenen door een accountant, met reden en een gelogd addendum |
| E4 | P3 | **Dossiers in het huidige pakket**: alleen als PDF archiveren? | Migratie | PDF-archief per klant, plus saldibalansen van vorige jaren via het importsjabloon |

## F. Project

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| F1 | P1 | **Deadline**: wanneer moet de eerste versie bruikbaar zijn? | Planning en scope | Pilot in het seizoen 2027, parallel aan het huidige pakket; daarna overstap |
| F2 | P1 | **Wie bouwt** (intern met Claude Code, een freelance .NET-ontwikkelaar, een bureau), en wat is ongeveer het **budget**? | Planning, review en continuïteit | Eén vaste ontwikkelaar met AI-ondersteuning. De vaktechniek blijft intern |
| F3 | P1 | Is deze **GitHub-repository privé**? Wie krijgt er toegang toe? | Vertrouwelijkheid; testdata horen er nooit in | Privé. Testdata nooit in de repository |
| F4 | P3 | Wil je dit later ook aan **andere kantoren** aanbieden? | Dat verandert de eisen flink | Nee, alleen voor eigen gebruik |
