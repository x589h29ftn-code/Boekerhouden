# 8. Wat hebben we nodig? Besluiten, roadmap en risico's

> **Bijgewerkt op 23-10-2026** met de besluiten van de opdrachtgever en de analyse van de
> voorbeeldrapporten ([hoofdstuk 9](09-analyse-voorbeeldrapporten.md)). Openstaande vragen staan in de
> [vragenlijst](11-vragenlijst.md).

## 8.1 Besluitenlog

| # | Datum | Besluit | Gevolg |
|---|---|---|---|
| B1 | 23-10-2026 | **Meerdere gebruikers, met eigen wachtwoord** | Kantoorserver-model met centrale PostgreSQL-database en ASP.NET Core Identity ([§7.2](07-technische-architectuur.md#72-architectuurkeuze-voor-meerdere-gebruikers), [§7.8](07-technische-architectuur.md#78-inloggen-rechten-en-beveiliging)) |
| B2 | 23-10-2026 | **Eigen werkprogramma** in een sjabloon. Stappen per dossier toe te voegen en te verwijderen | Werkprogramma-engine ([hoofdstuk 10](10-werkprogramma-en-materialiteit.md)). Geen licentie op externe werkprogramma's nodig |
| B3 | 23-10-2026 | Rechtsvormen: **BV klein en eenmanszaak in de MVP**. **BV micro en VOF later** (er zijn nog geen voorbeelden) | Twee RGS-filters (BV en eenmanszaak, B23) en twee rapportsjablonen in de MVP. RGS kent al codes voor tweede tot vijfde vennoot, zodat de VOF later eenvoudig volgt |
| B5 | 23-10-2026 | Export naar **PDF én Word** | Word-renderer (OpenXML) in de MVP. Beleid voor definitieve versies: vraag D8 |
| B8 | 23-10-2026 | **Standaard importsjabloon**: de gebruiker geeft het boekjaar op en een overzicht van grootboeknummer, omschrijving en bedrag. Er wordt alleen geïmporteerd als het saldo nihil is | Eerste importroute in de bouw ([§3.3](03-gegevensimport-xaf-excel-rgs.md#33-standaard-importsjabloon-excelcsv)). Ook bruikbaar voor vorige jaren en historie |
| B9 | 23-10-2026 | **De gebruiker kiest de materiële posten**, per **hoofdpost**, met doorklikken naar de subposts. Geen systeemberekening of voorstel | Planningsscherm met vinkjes per hoofdpost ([§10.2](10-werkprogramma-en-materialiteit.md#102-werking-in-het-dossier)) |
| B10 | 23-10-2026 | **Niet-materiële posten: geen vragen of werk**, alleen zichtbaar in het rapport, waar de presentatie en opmaak aan te passen zijn | Presentatie-editor per post ([§10.4](10-werkprogramma-en-materialiteit.md#104-rapport-presentatie-en-opmaak-per-post)) |
| B11 | 23-10-2026 | **Deponeren voorlopig negeren** | Buiten scope; geen publicatiestukken of SBR in de MVP |
| B12 | 23-10-2026 | **Algemene naamgeving**: een eigen samenstelapplicatie (werknaam *Boekerhouden*). **Geen verwijzingen naar bestaande pakketten** in de repository | Hoofdstuk 1 beschrijft de gangbare werkwijze zonder namen van leveranciers ([hoofdstuk 1](01-gangbare-werkwijze-samenstelsoftware.md)) |
| B13 | 23-10-2026 | **Alleen binnen kantoor werken** (A2) | Model A definitief; geen VPN, geen toegang van buitenaf, geen offline-variant |
| B14 | 23-10-2026 | **Geen server en geen ICT-partner** (A3) | Een eigen server-pc op kantoor, met een zelfbeherende installatie, ingebouwde versleutelde back-up, herstelknop, automatische certificaten en updates ([§7.9](07-technische-architectuur.md#79-uitrol)) |
| B15 | 23-10-2026 | Twee soorten opdrachten: **Samenstellen jaarrekening** en **Winstaangifte opstellen** (D1) | Twee werkprogramma- en rapportsjablonen |
| B16 | 23-10-2026 | **Geen deadline** (F1) | Gefaseerd bouwen; per fase opleveren en evalueren |
| B17 | 23-10-2026 | De repository **hoeft niet privé** (F3) | Daarom nooit klantgegevens, testbestanden of echte bedragen in de repository, en geen namen van bestaande pakketten |
| B18 | 23-10-2026 | **Maximaal 5 gebruikers** (A1) | Kleine server-pc volstaat; geen schaalbaarheidsmaatregelen nodig |
| B19 | 23-10-2026 | Rollen zoals voorgesteld (medewerker, reviewer, accountant, beheerder); **review is niet verplicht** (A4) | Een dossier kan zonder review worden afgerond. Een geplaatst reviewpunt moet wel eerst zijn opgelost |
| B20 | 23-10-2026 | **De opdrachtgever is de interne beheerder** (maximaal 4 uur per week) (A8) | Beheer moet in die tijd passen: zelfbeherende server, duidelijke beheerpagina |
| B21 | 23-10-2026 | **Testset met echte dossiers komt later** (B2) | Eerst bouwen en testen met fictieve testdata; echte golden files vóór de pilot |
| B22 | 23-10-2026 | **De opdrachtgever bouwt het zelf**, met AI-ondersteuning; geen budget (F2) | Bouwen in kleine plakjes; de hele applicatie moet in een Linux-/cloudomgeving te bouwen en te testen zijn; geen betaalde componenten |
| B23 | 23-10-2026 | **RGS is het rubriekenschema**: de RGS-indeling zijn in principe de rubrieken | Geen eigen rubriekenschema en geen RGS→rubriek-tabel. Koppelen aan een RGS-code op niveau 4; hoofdpost = niveau 2, subpost = niveau 3. Het officiële werkboek wordt in de app ingelezen (nu 3.8, in december 3.9) ([hoofdstuk 12](12-rgs-als-rubriekenschema.md)) |
| – | 23-10-2026 | De rapportnorm zijn de **voorbeeldrapporten** (BV klein jaarrekening; EZ bijlage aangifte IB) | Kengetallen, meerjarenoverzicht, Vpb en verschillenanalyse in de MVP ([§9.6](09-analyse-voorbeeldrapporten.md#96-gevolgen-voor-scope-en-planning)) |

**Verder vastgelegd:**
- **B4** (wie bouwt): de opdrachtgever zelf (B22).
- **B6** (UI-technologie): Blazor Server ([§7.2](07-technische-architectuur.md#72-architectuurkeuze-voor-meerdere-gebruikers)).
- **B7** (migratie): bestaande dossiers als PDF-archief; vorige jaren via het importsjabloon.

Voor plakje 3 zijn de vragen **B3** (rekeningschema van het kantoor) en **B7** (het RGS-werkboek aanleveren) nodig. De overige vragen ([hoofdstuk 11](11-vragenlijst.md)) komen per plakje aan de orde.

## 8.2 Wat we nodig hebben om te bouwen

### Mensen

| Rol | Omvang (indicatie) | Taken |
|---|---|---|
De opdrachtgever vervult drie rollen tegelijk. Dat is goed te doen, mits het werk in kleine stappen gaat en de AI het meeste programmeerwerk doet.

| Rol | Wie | Taken |
|---|---|---|
| **Bouwer** (met AI-ondersteuning) | Opdrachtgever | Per plakje: wens beschrijven, de AI laten bouwen en testen, uitkomsten controleren, pull request samenvoegen |
| **Vaktechnisch verantwoordelijke** | Opdrachtgever | RGS-filters en koppelsjabloon bevestigen, werkprogramma, standaardteksten en rapportsjablonen; controle van uitkomsten tegen de huidige rapporten |
| **Interne beheerder** | Opdrachtgever (maximaal 4 uur per week) | Back-upschijf wisselen, beheerpagina controleren, gebruikers beheren, updates starten |
| **Testgebruikers** (1–2 collega's) | Pilot | Dossiers parallel draaien naast het huidige pakket |

### Middelen

| Wat | Kosten (indicatie) | Opmerking |
|---|---|---|
| **Server-pc** (mini-pc, 16 GB RAM, 2 SSD's), **UPS** en **2 externe back-upschijven** | Ca. € 800–1.400 eenmalig | Pas nodig vanaf de pilot (plakje 9); tot die tijd draait alles op de ontwikkelomgeving. Zie [§7.9](07-technische-architectuur.md#79-uitrol) |
| PostgreSQL | € 0 | Open source, wordt door de installer meegeïnstalleerd |
| Certificaat voor het interne netwerk | € 0 | Maakt de server zelf aan; werkplekken vertrouwen het eenmalig |
| Code-signing-certificaat | Optioneel | Niet nodig voor intern gebruik |
| Ontwikkeltools (.NET 10 SDK, VS Code, GitHub) | € 0 | |
| AI-ontwikkelhulp (Claude Code) | Bestaand abonnement | Bouwen in de cloud of lokaal |
| Open-source bibliotheken | € 0 | MIT/Apache, zie [§7.4](07-technische-architectuur.md#74-bibliotheken-en-licenties) |

### Data en inhoud
1. **Fictieve testdata** (eerst): verzonnen BV's en eenmanszaken met saldibalansen over 5 jaar en een verwachte uitkomst. Mogen in de repository.
   **Echte testset** (later, vóór de pilot): saldibalansen van afgeronde dossiers met het huidige rapport als referentie. Die blijven buiten de repository.
2. **RGS 3.8-werkboek**, en 3.9 zodra die definitief is.
3. **NBA-verklaringengenerator en voorbeeldrapporten** (MijnNBA) voor de teksten.
4. **Kantoorteksten**: grondslagen, opdrachtbevestiging, LOR, standaardantwoorden acceptatie, en de teksten uit de voorbeeldrapporten.
5. **Huisstijl**: logo, kleuren, lettertype en de handtekening per accountant (vraag D9).
6. **Historische saldibalansen** (4 jaar terug) per klant voor de 5-jaarskengetallen, in te lezen via het importsjabloon.

## 8.3 Risico's

| # | Risico | Kans/impact | Maatregel |
|---|---|---|---|
| R1 | **De inhoud wordt onderschat**: werkprogramma, teksten en rapportpresentatie | Hoog/hoog | Inhoud per plakje maken, precies wanneer die nodig is. Beginnen met BV klein, met de voorbeeldrapporten als norm. Het rubriekenschema is geen eigen werk meer: dat is RGS (B23) |
| R2 | **Intellectueel eigendom** van gelicentieerde werkprogramma's, NBA-ledenmateriaal en teksten of vormgeving van bestaande pakketten | Middel/hoog | Eigen werkprogramma (B2) en eigen teksten. Bestaande pakketten alleen als functionele referentie (B12) |
| R3 | **Vaktechnische fouten in rapporten**, en de echte testset komt pas later | Middel/hoog | Eerst fictieve testdata met een bekende uitkomst. **Vóór de pilot** de echte testset (golden master tegen de huidige rapporten), daarna een parallelle pilot |
| R4 | **Verschillen in import**: XAF per pakket, en saldibalansen met fouten (mintekens, ontbrekende regels) | Hoog/middel | Importsjabloon met harde controles (saldo nihil, uniek, geldig), XSD-validatie voor XAF, testset per pakket |
| R5 | **Server en beheer zonder ICT-partner**: uitval van de server-pc of een mislukte back-up treft alle gebruikers tegelijk | Middel/hoog | Zelfbeherende installatie, versleutelde back-up naar drie doelen met een banner bij problemen, herstelknop, UPS, beheerhandleiding van één A4. Optioneel een strippenkaart bij een lokale IT-dienstverlener voor hardwarestoringen |
| R6 | **Scope creep** | Hoog/middel | Strikte MVP in plakjes. Alles daarbuiten gaat naar *later* |
| R7 | **Regelgeving verandert** (NV COS/ISQM, RJk, RGS 3.9, Vpb-tarieven) | Zeker/middel | Versiebeheer op sjablonen en teksten, parameters per jaar |
| R8 | **Eén persoon bouwt, beheert en doet de vaktechniek, naast de praktijk** (tijd en continuïteit) | Hoog/hoog | Geen deadline, kleine plakjes die elk afzonderlijk werken, AI doet het meeste programmeerwerk, alles in GitHub met tests en deze documentatie, gangbare technologie, zodat later een ontwikkelaar kan instappen. Tijdens het jaarrekeningseizoen eventueel een pauze inplannen |
| R9 | **Beveiliging en AVG** | Middel/hoog | Wachtwoordbeleid, lockout, HTTPS, audit log, alleen bereikbaar binnen kantoor, database niet bereikbaar vanaf werkplekken, versleutelde back-ups, BitLocker |

*Deponeren valt bewust buiten de scope (B11). Voor BV's blijft dat voorlopig een apart proces.*

## 8.4 Roadmap: bouwen in plakjes

Omdat de opdrachtgever het zelf bouwt met AI-ondersteuning en er geen deadline is, werken we in
**plakjes**. Elk plakje levert iets op dat werkt en getest is, en eindigt met een pull request in
deze repository. De volgorde is zo gekozen dat het risicovolste (import, rubricering, rapport)
vroeg zichtbaar is.

| # | Plakje | Resultaat | Inhoud (vaktechniek) die dan nodig is |
|---|---|---|---|
| 1 ✅ | **Fundament en importsjabloon**: solution, domeinmodel, inlezen sjabloon met alle controles (saldo nihil), proefbalans; fictieve testdata | Een saldibalans wordt ingelezen en gecontroleerd (via tests en de opdrachtregel). **Gereed 23-10-2026**; ook gangbare exports ("Rekening, Omschr, Saldo") worden herkend | Fictieve testklanten |
| 2 ✅ | **Eerste werkende app**: server (Blazor), database, inloggen en rollen, klanten en dossiers, import via scherm, audit log | Inloggen, klant en dossier aanmaken, sjabloon uploaden. **Gereed 23-10-2026**; met controles I7, I9 en I10, saldibalans met vorig jaar ernaast, gebruikersbeheer en wijzigingslog | – |
| 3 | **Rubriceren op RGS** ([§12.10](12-rgs-als-rubriekenschema.md#1210-gevolgen-voor-het-ontwerp-en-plakje-3)): RGS-werkboek inlezen, filter per rechtsvorm, koppelscherm, kantoor-koppelsjabloon, voorstellen, gekoppeld %, proefbalans per hoofd- en subpost met omslag, controles (o.a. I8) | 100% gekoppelde proefbalans per RGS-hoofdpost | Officieel RGS-werkboek 3.8 (download); bevestiging filters per rechtsvorm; het rekeningschema van het kantoor (B3) |
| 4 | **Proefbalans en correcties**: huidig, vorig, verschil, doorklikken, correctieboekingen, vergelijkende cijfers | Sluitende, gecorrigeerde proefbalans | – |
| 5 | **Dossier en werkprogramma**: sjabloonbeheer, keuze materiële posten, stappen (toevoegen en verwijderen), aftekenen, optionele reviewpunten, bijlagen, klantenoverzicht met signalen | Volledig uitwerkbaar dossier | Werkprogramma *Samenstellen jaarrekening* en *Winstaangifte opstellen* |
| 6 | **Rapport I**: rapportmodel, balans, W&V, specificaties, presentatie-editor per post, HTML-preview, PDF | Eerste rapport als PDF | Rapportsjabloon BV klein en eenmanszaak (structuur) |
| 7 | **Rapport II**: teksten (verklaring, algemeen, grondslagen, toelichtingen), kengetallen en meerjarenoverzicht (historie via sjabloon), verschillenanalyse, Vpb, voettekst per opdrachttype, handtekening, **Word** | Rapporten gelijkwaardig aan de voorbeeldrapporten | Standaardteksten, Vpb-parameters |
| 8 | **Afronding**: dossier afronden en sluiten, definitieve versie met hash, archief-export | Dossier van begin tot eind | – |
| 9 | **Installatie en pilot**: serverinstallatieprogramma, back-up en herstel, server-pc, echte testset, enkele echte dossiers parallel aan het huidige pakket | **Versie 1.0** in gebruik | Echte testset (B2) |
| 10 | **XAF-import** (3.x en 4.0), met automatische RGS-koppeling | Import rechtstreeks uit auditfiles | – |
| later | BV micro, VOF, kolomtoewijzer voor andere Excel-exports, eigen Windows-app, AI-voorstellen bij rubriceren, deponeren/SBR | | Voorbeelden micro en VOF |

### Inspanning

- **Traditioneel**: een ervaren ontwikkelaar zou voor deze MVP grofweg 1.100–1.500 uur nodig hebben, plus 250–400 uur inhoud (schema's, werkprogramma, teksten).
- **Met AI**: doet de AI het meeste programmeerwerk, dan verschuift de eigen tijd naar beschrijven, controleren, testen en de inhoud. Hoeveel dat scheelt, is vooraf niet betrouwbaar te zeggen.
- **Advies**: houd na plakje 1 en 2 bij hoeveel uur een plakje kostte, en reken de rest van de planning daarmee door.
- **Jaarlijks onderhoud** (RGS, XAF, RJk, NV COS, Vpb-tarieven, grensbedragen, teksten): enkele dagen tot weken per jaar.

## 8.5 Volgende stappen

1. ~~Plakje 1 en 2~~: gereed. De app draait: inloggen, klanten en dossiers, saldibalans inlezen.
2. **Plakje 3 (rubriceren op RGS)**: het rubriekenschema is RGS (B23). Nodig is het officiële werkboek RGS 3.8 (en in december 3.9); het onderzoek staat in [hoofdstuk 12](12-rgs-als-rubriekenschema.md).
3. **Later**: de server-pc aanschaffen (vóór plakje 9) en de echte testset verzamelen (vóór de pilot).
