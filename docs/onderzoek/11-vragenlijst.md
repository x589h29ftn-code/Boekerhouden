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
| A2 | Ook buiten kantoor of offline werken? | **Nee, alleen binnen kantoor** → kantoorserver-model definitief |
| A3 | Is er een server of ICT-partner? | **Nee** → eigen server-pc met zelfbeherende installatie en ingebouwde back-up ([§7.9](07-technische-architectuur.md#79-uitrol)) |
| B4 | Hoe komen historische cijfers binnen? | Via het **standaard importsjabloon** met het betreffende boekjaar |
| C1 | Hoe wordt de materialiteit bepaald? | **De gebruiker kiest de materiële posten zelf**; geen berekening |
| C2 | Op welk niveau wordt materieel aangevinkt? | **Per hoofdpost**, met doorklikken naar de subposts |
| C3 | Wat gebeurt er bij niet-materiële posten? | **Geen vragen en geen werk**: alleen zichtbaar in het rapport, waar de presentatie en opmaak aan te passen zijn |
| C4 | Doet het systeem een voorstel? | **Nee** |
| D1 | Welke opdrachttypen? | **Samenstellen jaarrekening** en **Winstaangifte opstellen** |
| D2 | Rapport BV micro? | **Later**; er is nog geen voorbeeld |
| D3 | Rapport VOF? | **Later**; er is nog geen voorbeeld |
| D4 | Deponeren? | **Voorlopig negeren** |
| F1 | Deadline? | **Geen deadline** |
| F3 | Repository privé? | **Hoeft niet**, maar er mag niet naar bestaande pakketten worden verwezen. Daarom staan er ook nooit klantgegevens of testbestanden in |
| – | Import | **Standaard importsjabloon**: boekjaar plus grootboeknummer, omschrijving en bedrag; alleen importeren bij saldo nihil |
| – | Naamgeving | **Algemeen** houden (werknaam *Boekerhouden*) |

## A. Organisatie en gebruik

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| A1 | P1 | Hoeveel gebruikers krijgt het systeem in totaal, en hoeveel werken er tegelijk? | Formaat van de server-pc | 5–15 gebruikers, maximaal 10 tegelijk |
| A4 | P1 | Welke **rollen** zijn er, en wie mag wat? Is **review door een tweede persoon** altijd verplicht, of alleen bij samenstellen jaarrekening? | Rechtenmodel en goedkeuringsflow | Rollen medewerker, reviewer, accountant en beheerder. Review verplicht bij *Samenstellen jaarrekening*, optioneel bij *Winstaangifte opstellen* |
| A5 | P2 | Moeten medewerkers **alle klanten** zien, of alleen hun eigen portefeuille? | Autorisatie per klant | Iedereen ziet alles, met een filter "mijn klanten" |
| A6 | P2 | Is inloggen met **gebruikersnaam en wachtwoord** genoeg? | Beveiliging, gebruiksgemak | Ja. Er wordt alleen binnen kantoor gewerkt, dus geen tweestapsverificatie in de MVP |
| A7 | P2 | Hoeveel dossiers per jaar, ongeveer verdeeld over **BV en eenmanszaak** (en later micro en VOF)? | Omvang van de pilot, prioriteiten | Graag de aantallen |
| A8 | P2 | **Wie wordt de interne beheerder** (back-upschijf wisselen, gebruikers beheren, updates starten, ca. 1 uur per week)? | Zonder ICT-partner moet iemand dit doen | Eén sleutelgebruiker, plus een vervanger |

## B. Administraties en import

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| B1 | P2 | **Welke boekhoudpakketten** gebruiken je klanten (top 5)? Voert het kantoor zelf administraties? | Testbestanden, koppelsjablonen. De import loopt eerst via het sjabloon, XAF volgt | Per pakket 2–3 voorbeeldbestanden |
| B2 | P1 | Kun je **10–20 saldibalansen of XAF-bestanden** van afgeronde dossiers (BV en eenmanszaak) aanleveren, met het bijbehorende **huidige rapport**? | Golden-master-tests | Ja, via een beveiligde map, nooit via GitHub |
| B3 | P2 | Heeft het kantoor een **eigen standaard-rekeningschema**? Hebben de rekeningen RGS-codes? | Wordt het eerste koppelsjabloon | Als het bestaat, is dat koppelsjabloon nr. 1 |
| B5 | P2 | Hoe worden bij de **eenmanszaak** privéonttrekkingen en -stortingen geboekt (aparte privérekeningen)? | Verloop ondernemingsvermogen | Privérekeningen herkennen via de rubrieken *Privéonttrekkingen/-stortingen* |
| B6 | P2 | **Importsjabloon**: een optionele kolom **RGS-code** houden, of alleen de drie vaste kolommen? | Gebruiksgemak tegenover automatisch rubriceren | Optionele vierde kolom; leeg mag |

## C. Werkprogramma en materiële posten

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| C5 | P2 | **Wie mag standaardstappen verwijderen**: iedereen, of alleen reviewer of accountant? Is een **reden** verplicht? | Dossierkwaliteit en review | Iedereen, met verplichte reden. De reviewer ziet een overzicht |
| C6 | P2 | Hoe wordt **afgetekend**: per stap en per post? Twee niveaus of drie? | Goedkeuringsflow | Per stap "uitgevoerd", per post "gereviewd", het dossier "goedgekeurd door accountant" |
| C7 | P2 | Is er een **bestaand werkprogramma** (Word, Excel) waarmee we beginnen? Wie stelt het op, en hoeveel uur heeft die persoon? | De inhoud is minstens de helft van het werk ([§8.3 R1](08-roadmap-risico-s-en-beslispunten.md#83-risicos)) | Eén vaktechnisch verantwoordelijke, 1–2 dagen per week tijdens de bouw |
| C8 | P2 | Mogen eigen toegevoegde stappen worden **voorgesteld voor het kantoorsjabloon**? | Sjabloonbeheer | Ja, de beheerder beslist |
| C9 | P2 | **Doorklikken**: het vinkje *materieel* staat alleen per hoofdpost. Doorklikken toont de cijfers per subpost en grootboekrekening, en een stap kan optioneel bij een subpost horen. Klopt dat? | Planningsscherm en sjabloon | Zoals beschreven |

## D. Rapporten

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| D5 | P2 | **Fiscale positie BV**: welke correcties komen standaard voor (niet-aftrekbare kosten, boetes, gemengde kosten, giften, KIA, verliesverrekening, fiscale eenheid)? | Omvang van de Vpb-module | Zoals in het voorbeeld, plus verliesverrekening, KIA en de fiscale eenheid-variant |
| D6 | P2 | **Winstaangifte eenmanszaak**: een fiscale winstberekening in het rapport (ondernemersaftrek, MKB-winstvrijstelling, FOR), of alleen in de aangiftesoftware? | Omvang van de IB-module | Niet in het rapport, zoals in het voorbeeld |
| D7 | P2 | **Kengetallen en meerjarenoverzicht**: altijd de set uit het voorbeeld? Ook bij de winstaangifte? | Kengetallenbibliotheek | De set uit het voorbeeld bij samenstellen; bij winstaangifte uit, per dossier aan te zetten |
| D8 | P2 | **Word-export**: mag een in Word bewerkt rapport het definitieve rapport zijn? | Dossierintegriteit ([§7.6](07-technische-architectuur.md#76-rapportgeneratie-één-model-meerdere-uitvoerformaten)) | Definitief altijd als PDF uit de app. Word voor concepten en nabewerking, met een upload-verplichting als dat het definitieve rapport wordt |
| D9 | P2 | Kun je **huisstijl** (logo, kleuren, lettertype) en de **gescande handtekening** per tekenbevoegde accountant aanleveren? | Rapportopmaak | Ja. De handtekening wordt alleen geplaatst bij definitief uitbrengen door die accountant |
| D10 | P2 | **Specificaties**: standaard de grootboekrekeningen per rubriek, met aan te passen omschrijving, samenvoegen en verbergen. Akkoord? | Presentatie-editor | Ja |
| D11 | P3 | Is er een **Engelstalig rapport** nodig? | Vertaling | Nee |
| D12 | P3 | Hoe verloopt het **conceptproces richting klant**? | Afrondingsflow | Concept met watermerk; de ondertekende LOR uploaden als bijlage |

## E. Dossier, bijlagen en klantgegevens

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| E1 | P2 | Waar staan **klantgegevens en Wwft-dossiers** nu? Is een eenmalige import van het klantenbestand nodig? | Klantbeheer en migratie | Eenmalige Excel-import van klanten en functionarissen |
| E2 | P2 | Waar staan **bijlagen** nu? Slaat de app ze zelf op? | Opslag en back-up | De app slaat zelf op (mee in de back-up) |
| E3 | P2 | **Gesloten dossiers**: nooit heropenen (alleen kopiëren), of heropenen door een accountant met een reden? | Afsluitbeleid | Heropenen door een accountant, met reden en een gelogd addendum |
| E4 | P3 | **Bestaande dossiers**: alleen als PDF archiveren? | Migratie | PDF-archief per klant, plus saldibalansen van vorige jaren via het importsjabloon |

## F. Project

| # | P | Vraag | Waarom | Voorstel |
|---|---|---|---|---|
| F2 | P1 | **Wie bouwt** (intern met Claude Code, een freelance .NET-ontwikkelaar, een bureau), en wat is ongeveer het **budget**? | Planning, review en continuïteit | Eén vaste ontwikkelaar met AI-ondersteuning. De vaktechniek blijft intern |
| F4 | P3 | Wil je dit later ook aan **andere kantoren** aanbieden? | Dat verandert de eisen flink | Nee, alleen voor eigen gebruik |
