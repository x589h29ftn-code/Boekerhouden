# Onderzoek en ontwerp: samenstelapplicatie *Boekerhouden*

*Stand van zaken: 23 oktober 2026*

*Boekerhouden* (werknaam) is een eigen applicatie voor het samenstellen van jaarrekeningen en
financiële rapporten. Je importeert een saldibalans, rubriceert de grootboekrekeningen naar
jaarrekeningposten, werkt het dossier uit volgens het kantoorwerkprogramma, en maakt het rapport
in PDF en Word. Het draait op eigen infrastructuur, met meerdere gebruikers. SBR/deponeren en
API-koppelingen vallen (voorlopig) buiten de scope.

## Besluiten tot nu toe

| Onderwerp | Besluit |
|---|---|
| Gebruikers | **Meerdere gebruikers**, elk met een eigen wachtwoord |
| Import | **Standaard importsjabloon**: boekjaar plus grootboeknummer, omschrijving en bedrag. Alleen importeren als het saldo nihil is. XAF-auditfile als tweede route |
| Werkprogramma | **Eigen werkprogramma** in een sjabloon. Stappen per dossier toe te voegen en te verwijderen |
| Materiële posten | **De gebruiker kiest**, per **hoofdpost**, met doorklikken naar de subposts. Alleen materiële posten krijgen werk. Niet-materiële posten zijn alleen zichtbaar in het rapport, met aanpasbare presentatie en opmaak |
| Opdrachttypen | **Samenstellen jaarrekening** (BV) en **Winstaangifte opstellen** (eenmanszaak) |
| Rechtsvormen | MVP: **BV klein** en **eenmanszaak**. **BV micro** en **VOF** later (nog geen voorbeelden) |
| Werkplek | **Alleen binnen kantoor**, **maximaal 5 gebruikers**. Er is geen server of ICT-partner, dus een eigen server-pc met zelfbeherende installatie en ingebouwde back-up. De opdrachtgever is beheerder (maximaal 4 uur per week) |
| Rollen | Medewerker, reviewer, accountant en beheerder; **review is niet verplicht** |
| Bouw | **De opdrachtgever bouwt het zelf**, met AI-ondersteuning, in kleine plakjes; geen budget, alleen open-source componenten |
| Testdata | Eerst **fictieve testdata**; een echte testset volgt vóór de pilot |
| Planning | **Geen deadline**; per fase opleveren |
| Uitvoer | **PDF en Word** |
| Rapportnorm | De aangeleverde voorbeeldrapporten (BV klein jaarrekening; eenmanszaak bijlage aangifte IB) |
| Deponeren | **Voorlopig negeren** |

## Inhoud

| # | Hoofdstuk | Kern |
|---|---|---|
| 1 | [Marktreferentie](01-gangbare-werkwijze-samenstelsoftware.md) | Achtergrond: hoe werkt bestaande samenstelsoftware? (onderzocht aan de hand van een veelgebruikt pakket) |
| 2 | [Vaktechnisch kader](02-vaktechnisch-kader.md) | Standaard 4410, VGBA/Wwft, dossierafsluiting, jaarrekening per rechtsvorm en grootte, standaardteksten |
| 3 | [Gegevensimport](03-gegevensimport-xaf-excel-rgs.md) | **Standaard importsjabloon** met controles, XAF 3.x/4.0, RGS-structuur en -versies |
| 4 | [Rubricering](04-rubricering-ontwerp.md) | Rubriekenschema, koppelengine (handmatig > sjabloon > RGS > voorstel), omslag, jaarovergang |
| 5 | [Functioneel ontwerp](05-functioneel-ontwerp.md) | Modules, schermen, werkstroom, controles |
| 6 | [Datamodel](06-datamodel.md) | Entiteiten op kantoor-, klant- en dossierniveau, tekenconventies |
| 7 | [Technische architectuur](07-technische-architectuur.md) | Kantoorserver: .NET 10, Blazor Server, PostgreSQL, Identity, PDF (Chromium) en Word (OpenXML) |
| 8 | [Besluiten, roadmap en risico's](08-roadmap-risico-s-en-beslispunten.md) | Besluitenlog, mensen en middelen, risico's, fasering |
| 9 | [Analyse voorbeeldrapporten](09-analyse-voorbeeldrapporten.md) | Opbouw van de rapporten, 22 rapportbouwstenen, opmaakregels, benodigde gegevens |
| 10 | [Werkprogramma en materiële posten](10-werkprogramma-en-materialiteit.md) | Keuze materiële posten, stappen, presentatie per post in het rapport |
| 11 | [**Vragenlijst**](11-vragenlijst.md) | Beantwoorde en openstaande vragen, met prioriteit en een voorstel per vraag |

## Samenvatting

1. **Werkwijze in vijf stappen:**
   1. Saldibalans importeren (importsjabloon of XAF, met controle op saldo nihil).
   2. Rubriceren naar jaarrekeningposten (handmatig > kantoorsjabloon > RGS-code).
   3. In de planning de materiële hoofdposten kiezen.
   4. Alleen die posten uitwerken met het werkprogramma.
   5. Het rapport opmaken voor alle posten, en het dossier afronden.
2. **Importsjabloon:**
   - drie kolommen (grootboeknummer, omschrijving, bedrag; debet +, credit −) en het boekjaar;
   - harde controles: verplichte velden, unieke nummers, geldige bedragen en **som = € 0,00**;
   - hetzelfde sjabloon werkt voor vergelijkende cijfers en historische jaren (5-jaarskengetallen).
3. **Rubriceren:**
   - de koppeling is een klant-eigenschap die doorrolt over de jaren, met als doel 100% gekoppeld;
   - RGS-codes (uit XAF of een optionele kolom in het sjabloon) maken automatisch koppelen grotendeels mogelijk;
   - RGS 3.8 is actueel, en XAF 4.0 wordt per 1-1-2027 de norm.
4. **Werkprogramma:**
   - een eigen kantoorsjabloon met hoofdposten en stappen;
   - een materieel aangevinkte hoofdpost krijgt zijn stappen (als kopie) in de uitvoering, met doorklikken naar subposts en grootboekrekeningen;
   - niet-materiële posten kosten geen werk;
   - stappen toevoegen en verwijderen kan, waarbij verwijderen met een reden gebeurt die zichtbaar is voor de reviewer.
5. **Rapporten:**
   - de voorbeeldrapporten leggen de lat. BV klein bevat accountantsrapport, resultatenanalyse, kengetallen en meerjarenoverzicht (5 jaar), fiscale positie (Vpb), de jaarrekening en de W&V-toelichting op grootboekniveau; EZ is een bijlage bij de aangifte IB zonder verklaring;
   - de rapporten bestaan uit 22 bouwstenen, en per post zijn de presentatie en opmaak aan te passen.
6. **Vaktechniek:** Standaard 4410, VGBA, Wwft en de NVKS (vanaf 2027 SKM1) bepalen de dossiereisen. Het dossier sluit binnen twee maanden na de verklaring.
7. **Architectuur:**
   - een eigen **server-pc op kantoor** (ASP.NET Core en Blazor Server) met PostgreSQL, alleen bereikbaar binnen kantoor;
   - een zelfbeherende installatie, en een ingebouwde, versleutelde back-up met herstelknop, want er is geen ICT-partner;
   - inloggen met eigen wachtwoord (ASP.NET Core Identity), en de database is niet bereikbaar vanaf de werkplekken;
   - PDF en Word uit één rapportmodel.
8. **De inhoud is de helft van het werk:** rubriekenschema's, de RGS→rubriek-tabel, het werkprogramma, standaardteksten en rapportsjablonen. Daarvoor is een vaktechnisch verantwoordelijke nodig.
9. **Aanpak en inspanning:**
   - bouwen in **10 plakjes** ([§8.4](08-roadmap-risico-s-en-beslispunten.md#84-roadmap-bouwen-in-plakjes)), elk werkend en getest, zonder deadline;
   - traditioneel zou de MVP grofweg 1.100–1.500 ontwikkeluren plus 250–400 uur inhoud kosten. Met AI-ondersteuning verschuift de eigen tijd naar beschrijven, controleren en inhoud;
   - de planning wordt na plakje 1 en 2 bijgesteld op basis van de werkelijke uren.
10. **Volgende stap:** er staan geen blokkerende vragen meer open. **Plakje 1**: de solution, het importsjabloon met de controles, en fictieve testdata. Parallel het rubriekenschema voorbereiden.

## Over de bronnen

- **Hoe het onderzoek is gedaan:** externe bronnen (helpdesks van softwareleveranciers, RGS, NBA) konden niet rechtstreeks worden geopend. De informatie komt uit zoekmachinesamenvattingen, aangevuld met vakkennis.
- **Hoe je de beweringen kunt beoordelen:** elk hoofdstuk vermeldt de bronnen, en wat afgeleid of onbevestigd is, is gemarkeerd.
- **Voorbeeldrapporten:** de aangeleverde rapporten zijn geanalyseerd, maar staan **niet** in deze repository (klantgegevens).
