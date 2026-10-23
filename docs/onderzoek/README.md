# Onderzoek: eigen samenstel-applicatie (à la Visionplanner Compilation)

*Stand van zaken: 23 oktober 2026, bijgewerkt met de besluiten van de opdrachtgever en een analyse van twee voorbeeldrapporten*

Dit onderzoek beantwoordt twee vragen:
1. **Hoe werkt Visionplanner Compilation**, nu *Nmbrs Reporting / Compilation*? Dat gaat van het inlezen van een administratie, via rubriceren en het samenstellingsdossier, tot het financieel rapport.
2. **Wat hebben we nodig om dit zelf te bouwen** als Windows-oplossing op eigen infrastructuur, zonder SBR/publicatie en zonder API-koppelingen?

## Besluiten tot nu toe

| Onderwerp | Besluit |
|---|---|
| Gebruikers | **Meerdere gebruikers**, elk met een eigen wachtwoord |
| Werkprogramma | **Eigen werkprogramma** in een sjabloon. Een **materieel** aangevinkte post krijgt zijn werkprogramma in de uitvoering, en stappen zijn toe te voegen en te verwijderen |
| Rechtsvormen | **BV micro, BV klein, eenmanszaak, VOF** |
| Uitvoer | **PDF en Word** |
| Rapportnorm | De aangeleverde voorbeeldrapporten (BV klein jaarrekening; eenmanszaak bijlage aangifte IB) |

## Inhoud

| # | Hoofdstuk | Kern |
|---|---|---|
| 1 | [Hoe werkt Visionplanner Compilation?](01-hoe-werkt-visionplanner-compilation.md) | Architectuur, werkstroom, import, koppelen, rapport, klantenmonitor |
| 2 | [Vaktechnisch kader](02-vaktechnisch-kader.md) | Standaard 4410, VGBA/Wwft, dossierafsluiting, werkprogramma, jaarrekening per rechtsvorm en grootte, standaardteksten |
| 3 | [Gegevensimport: XAF, Excel en RGS](03-gegevensimport-xaf-excel-rgs.md) | Auditfile 3.x/4.0, proefbalans afleiden, Excel-profielen, RGS-structuur en -versies |
| 4 | [Rubricering: ontwerp](04-rubricering-ontwerp.md) | Rubriekenschema, koppelengine (handmatig > sjabloon > RGS > voorstel), omslag, jaarovergang |
| 5 | [Functioneel ontwerp](05-functioneel-ontwerp.md) | Modules, schermen, werkstroom, controles |
| 6 | [Datamodel](06-datamodel.md) | Entiteiten op kantoor-, klant- en dossierniveau, ER-diagram, tekenconventies |
| 7 | [Technische architectuur](07-technische-architectuur.md) | Kantoorserver-model: .NET 10, Blazor Server, PostgreSQL, Identity, PDF (Chromium) en Word (OpenXML) |
| 8 | [Besluiten, roadmap en risico's](08-roadmap-risico-s-en-beslispunten.md) | Besluitenlog, mensen en middelen, risico's, fasering, volgende stappen |
| 9 | [Analyse voorbeeldrapporten](09-analyse-voorbeeldrapporten.md) | Opbouw van beide rapporten, catalogus van 22 rapportbouwstenen, opmaakregels, benodigde gegevens, kwaliteitslessen |
| 10 | [Werkprogramma en materialiteit](10-werkprogramma-en-materialiteit.md) | Materieel-vinkje → postprogramma, stappen toevoegen en verwijderen, sjabloonbeheer, datamodel |
| 11 | [**Vragenlijst**](11-vragenlijst.md) | De vragen die het plan strakker maken, met prioriteit en een voorstel per vraag |

## Samenvatting

1. **Wat Visionplanner doet:** het combineert drie dingen.
   - **(a)** Op klantniveau een administratie met een **koppeling van elke grootboekrekening aan een rubriek**.
   - **(b)** Per boekjaar en opdracht een **dossier** dat een werkprogramma volgt: Profiel → Voorbereiding → Uitvoering per post → Rapport → Afronding, met goedkeuring per onderdeel.
   - **(c)** Een **rapportgenerator** die sjablonen, standaardteksten en gerubriceerde cijfers samenvoegt.
2. **Rubriceren gaat in deze volgorde:** handmatig > kantoorsjabloon > RGS-code > voorstel. Een handmatige koppeling wordt nooit overschreven. Het doel is "Gekoppeld 100%".
3. **RGS en XAF:**
   - RGS 3.8 is actueel; 3.9 volgt in december 2026. De meeste pakketten leveren RGS-codes mee, en rubriceren kan met *langste-voorvoegsel-match*.
   - **XAF 4.0 wordt per 1-1-2027 de norm**, dus de import ondersteunt 3.1, 3.2 en 4.0.
4. **Vaktechniek:** Standaard 4410 (HR 1136), VGBA, Wwft en de NVKS (vanaf 2027 SKM1) bepalen de dossiereisen. Het dossier sluit binnen twee maanden na de verklaring; Visionplanner doet dat na 60 dagen.
5. **Rapporten:**
   - De voorbeeldrapporten leggen de lat. BV klein bevat accountantsrapport, resultatenanalyse, **kengetallen en meerjarenoverzicht (5 jaar)**, **fiscale positie (Vpb)**, de jaarrekening, en de W&V-toelichting op grootboekniveau.
   - EZ: bijlage bij de aangifte IB, zonder verklaring, met voettekst "Geen accountantscontrole toegepast".
   - Dit vertaalt zich in **22 herbruikbare bouwstenen**.
6. **Werkprogramma:**
   - In de planning stel je de materialiteit vast, met een systeemvoorstel, en vink je per post *materieel* aan.
   - Daarmee komt het **postprogramma** uit het sjabloon in de uitvoering, als kopie.
   - Stappen zijn toe te voegen en te verwijderen. Verwijderen gebeurt met een reden en is zichtbaar voor de reviewer.
7. **Architectuur na het besluit "meerdere gebruikers":**
   - Een **kantoorserver** met ASP.NET Core en Blazor Server, plus PostgreSQL. Inloggen via ASP.NET Core Identity (wachtwoordbeleid, lockout, optioneel tweestapsverificatie).
   - Werkplekken gebruiken een dunne Windows-app (WebView2) of de browser. De database is niet bereikbaar vanaf de werkplekken.
   - PDF via headless Chromium, Word via OpenXML, uit één rapportmodel.
   - Moet er **offline** gewerkt kunnen worden, dan wordt het een desktopvariant (vraag A2).
8. **De inhoud is de helft van het werk:** rubriekenschema's, de RGS→rubriek-tabel, koppelsjablonen, het werkprogramma, standaardteksten en rapportsjablonen. Daarvoor is een vaktechnisch verantwoordelijke nodig.
9. **Inspanning (ruwe indicatie):** een MVP met meerdere gebruikers, BV micro/klein en EZ/VOF, PDF en Word, kengetallen en Vpb, in circa **8–11 maanden**, met 1 fte ontwikkeling en 0,2–0,4 fte vaktechniek. Daarna jaarlijks onderhoud.
10. **Volgende stap:** de **P1-vragen** uit de [vragenlijst](11-vragenlijst.md) beantwoorden, de testset en een voorbeeld **VOF** en **BV micro** aanleveren, en fase 0 starten.

## Over de bronnen

- **Hoe het onderzoek is gedaan:** de helpdesk-sites van Nmbrs/Visionplanner, de RGS-site en de NBA-site konden vanuit de onderzoeksomgeving niet rechtstreeks worden geopend. De informatie komt daarom uit zoekmachinesamenvattingen van die pagina's, aangevuld met vakkennis.
- **Hoe je de beweringen kunt beoordelen:** elk hoofdstuk vermeldt de bronnen, en wat afgeleid of onbevestigd is, is gemarkeerd.
- **Voorbeeldrapporten:** de twee aangeleverde rapporten zijn geanalyseerd, maar staan **niet** in deze repository (klantgegevens).
