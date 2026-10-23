# Onderzoek: eigen samenstel-applicatie (à la Visionplanner Compilation)

*Stand van zaken: 23 oktober 2026*

Dit onderzoek beantwoordt twee vragen:
1. **Hoe werkt Visionplanner Compilation**, nu *Nmbrs Reporting / Compilation*? Dat gaat van het inlezen van een administratie, via rubriceren en het samenstellingsdossier, tot het financieel rapport.
2. **Wat hebben we nodig om dit zelf te bouwen** als standalone Windows-applicatie, zonder SBR/publicatie en zonder API-koppelingen?

## Inhoud

| # | Hoofdstuk | Kern |
|---|---|---|
| 1 | [Hoe werkt Visionplanner Compilation?](01-hoe-werkt-visionplanner-compilation.md) | Architectuur, werkstroom, import, koppelen, rapport, klantenmonitor |
| 2 | [Vaktechnisch kader](02-vaktechnisch-kader.md) | Standaard 4410, VGBA/Wwft, dossierafsluiting, werkprogramma, jaarrekening per rechtsvorm en grootte, standaardteksten |
| 3 | [Gegevensimport: XAF, Excel en RGS](03-gegevensimport-xaf-excel-rgs.md) | Auditfile 3.x/4.0, proefbalans afleiden, Excel-profielen, RGS-structuur en -versies |
| 4 | [Rubricering: ontwerp](04-rubricering-ontwerp.md) | Rubriekenschema, koppelengine (handmatig > sjabloon > RGS > voorstel), omslag, jaarovergang |
| 5 | [Functioneel ontwerp](05-functioneel-ontwerp.md) | Modules, schermen (afgeleid van de schermafbeeldingen), werkstroom, controles |
| 6 | [Datamodel](06-datamodel.md) | Entiteiten op kantoor-, klant- en dossierniveau, ER-diagram, tekenconventies |
| 7 | [Technische architectuur](07-technische-architectuur.md) | .NET 10 + Blazor Hybrid in WPF, SQLite → PostgreSQL, HTML→PDF, bibliotheken en licenties |
| 8 | [Roadmap, risico's en beslispunten](08-roadmap-risico-s-en-beslispunten.md) | Mensen, middelen, fasering, risico's, beslissingen, volgende stappen |

## Samenvatting in tien punten

1. **Wat Visionplanner doet:** het combineert drie dingen.
   - **(a)** Op *klantniveau* een administratie (rekeningschema plus saldi per periode, uit XAF, Excel of API) met een **koppeling van elke grootboekrekening aan een rubriek**.
   - **(b)** Per *boekjaar en opdracht* een **dossier** dat een werkprogramma volgt: Profiel → Voorbereiding (acceptatie, planning) → Uitvoering per post → Rapport → Afronding, met goedkeuring per onderdeel.
   - **(c)** Een **rapportgenerator** die sjablonen, standaardteksten en de gerubriceerde cijfers samenvoegt tot een financieel rapport (PDF).
2. **Rubriceren gaat in deze volgorde:** handmatige keuze bij de klant > kantoorsjabloon > RGS-code > (AI-)voorstel. Een handmatige koppeling wordt nooit overschreven. Alleen aan het laagste niveau (rubriek) wordt gekoppeld. "Gekoppeld %" moet 100% zijn.
3. **RGS maakt automatisch rubriceren grotendeels mogelijk.** De meeste moderne pakketten leveren de RGS-code mee in de auditfile. RGS 3.8 is de actuele versie; 3.9 volgt in december 2026. Omdat RGS-codes hiërarchisch zijn (B → BIva → BIvaKoo → …), kun je rubriceren met *langste-voorvoegsel-match*.
4. **XAF 4.0 wordt per 1-1-2027 de norm** bij de Belastingdienst. De import moet 3.1, 3.2 én 4.0 aankunnen. Waar de RGS-code staat, verschilt per versie en per pakket. Vertrouw nooit op `leadCode` als rubricering.
5. **Vaktechnisch kader:**
   - Standaard 4410 (met HR 1136), VGBA, Wwft en de NVKS (vanaf 2027 SKM1) bepalen wat in het dossier moet staan. De kern: acceptatie en opdrachtbevestiging, kennis van de entiteit, significante aangelegenheden, aansluiting met de onderliggende administratie, de bevestiging van het management, een verklaring met verplichte elementen, en het dossier sluiten binnen twee maanden.
   - Visionplanner sluit automatisch na 60 dagen. Een gesloten dossier kan niet heropend worden, alleen gekopieerd.
6. **Jaarrekening:**
   - De MVP richt zich op de BV micro en klein (BW2 Titel 9, BMJ, RJk, eventueel fiscale grondslagen) en direct daarna op de IB-ondernemer (eenmanszaak/VOF, met kapitaalsvergelijking en winstverdeling).
   - Grootteclassificatie over twee jaar, met een waarschuwing bij controleplicht.
7. **De inhoud is de helft van het werk:** rubriekenschema's, de RGS→rubriek-tabel, koppelsjablonen, werkprogramma's, standaardteksten en rapportsjablonen. Daarvoor is een vaktechnisch verantwoordelijke nodig. SRA-werkprogramma's en NBA-ledenmateriaal vallen onder licenties; we bouwen de *functionaliteit* na, niet de inhoud of vormgeving van Visionplanner.
8. **Technisch advies:**
   - **.NET 10 (LTS) met Blazor Hybrid in een dunne WPF-schil.** De UI is in HTML/CSS (past bij de documentachtige schermen), met open-source rich-text (TipTap).
   - Eén rapportmodel voor preview, PDF (WebView2 + Paged.js) en later Word.
   - **SQLite** voor de MVP met bedragen in centen, en een voorbereide route naar **PostgreSQL/SQL Server** voor meerdere gebruikers.
   - Alleen MIT/Apache-bibliotheken; geen afhankelijkheid van commerciële component-suites.
9. **Inspanning (ruwe indicatie):** een MVP voor één gebruiker met BV klein in circa **6–8 maanden** met 1 fte ontwikkelaar en 0,2–0,4 fte vaktechniek. Daarna jaarlijks onderhoud voor regelgeving, RGS en XAF.
10. **Volgende stappen:**
    - beslispunten B1–B7 nemen;
    - een testset van echte XAF-bestanden met Visionplanner-uitkomsten verzamelen;
    - in een demo-account de open vragen beantwoorden;
    - fase 0 starten: proefjes voor parser, UI-schil en PDF.

## Over de bronnen

- **Hoe het onderzoek is gedaan:** de helpdesk-sites van Nmbrs/Visionplanner, de RGS-site en de NBA-site konden vanuit de onderzoeksomgeving niet rechtstreeks worden geopend. De informatie is daarom verzameld via zoekmachinesamenvattingen van die pagina's, aangevuld met vakkennis.
- **Hoe je de beweringen kunt beoordelen:** elk hoofdstuk vermeldt per bewering de bron. Wat afgeleid of onbevestigd is, is gemarkeerd. De open vragen staan per hoofdstuk en zijn samengevoegd in [§8.5](08-roadmap-risico-s-en-beslispunten.md#85-openstaande-vragen-geconsolideerd).
- **Wat je zelf moet nalopen:** controleer vóór het bouwen de vaktechnische inhoud (verklaringsteksten, wetsartikelen) tegen de primaire bron. Valideer de XAF-structuur tegen de officiële XSD's.
