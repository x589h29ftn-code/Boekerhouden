# 8. Wat hebben we nodig? Besluiten, roadmap en risico's

> **Bijgewerkt op 23-10-2026** met de besluiten van de opdrachtgever en de analyse van de
> voorbeeldrapporten ([hoofdstuk 9](09-analyse-voorbeeldrapporten.md)). Openstaande vragen staan in de
> [vragenlijst](11-vragenlijst.md).

## 8.1 Besluitenlog

| # | Datum | Besluit | Gevolg |
|---|---|---|---|
| B1 | 23-10-2026 | **Meerdere gebruikers, met eigen wachtwoord** | Kantoorserver-model met centrale PostgreSQL-database en ASP.NET Core Identity ([§7.2](07-technische-architectuur.md#72-architectuurkeuze-voor-meerdere-gebruikers), [§7.8](07-technische-architectuur.md#78-inloggen-rechten-en-beveiliging)) |
| B2 | 23-10-2026 | **Eigen werkprogramma** in een sjabloon. Stappen per dossier toe te voegen en te verwijderen | Werkprogramma-engine ([hoofdstuk 10](10-werkprogramma-en-materialiteit.md)). Geen licentie op externe werkprogramma's nodig |
| B3 | 23-10-2026 | Rechtsvormen: **BV klein en eenmanszaak in de MVP**. **BV micro en VOF later** (er zijn nog geen voorbeelden) | Twee rubriekenschema's (BV en IB) en twee rapportsjablonen in de MVP. Het IB-schema houdt rekening met meerdere vennoten, zodat de VOF later eenvoudig volgt |
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
| – | 23-10-2026 | De rapportnorm zijn de **voorbeeldrapporten** (BV klein jaarrekening; EZ bijlage aangifte IB) | Kengetallen, meerjarenoverzicht, Vpb en verschillenanalyse in de MVP ([§9.6](09-analyse-voorbeeldrapporten.md#96-gevolgen-voor-scope-en-planning)) |

**Nog open:**
- **B4** (wie bouwt): zie vraag F2.
- **B6** (UI-technologie): Blazor Server ([§7.2](07-technische-architectuur.md#72-architectuurkeuze-voor-meerdere-gebruikers)).
- **B7** (migratie): bestaande dossiers als PDF-archief; vorige jaren via het importsjabloon.

## 8.2 Wat we nodig hebben om te bouwen

### Mensen

| Rol | Omvang (indicatie) | Taken |
|---|---|---|
| **Ontwikkelaar (.NET/C#)** | 1 fte tijdens de MVP | Architectuur, import, rubricering, werkprogramma, rapportgenerator (PDF en Word), UI, inloggen. AI-ondersteuning kan versnellen, maar alles wordt gereviewd en getest |
| **Vaktechnisch verantwoordelijke** | 0,2–0,4 fte | Productowner. Stelt rubriekenschema's, RGS→rubriek-tabel, werkprogramma, standaardteksten en rapportsjablonen op, en test met echte dossiers |
| **Interne beheerder** (sleutelgebruiker) | Ca. 1 uur per week | Back-upschijf wisselen, beheerpagina controleren, gebruikers beheren, updates starten. Er is geen ICT-partner, dus de software moet dit eenvoudig maken |
| **Testgebruikers** (2–3) | Pilotfase | Dossiers parallel draaien naast het huidige pakket |

### Middelen

| Wat | Kosten (indicatie) | Opmerking |
|---|---|---|
| **Server-pc** (mini-pc, 16 GB RAM, 2 SSD's), **UPS** en **2 externe back-upschijven** | Ca. € 900–1.600 eenmalig | Zie [§7.9](07-technische-architectuur.md#79-uitrol) |
| PostgreSQL | € 0 | Open source, wordt door de installer meegeïnstalleerd |
| Certificaat voor het interne netwerk | € 0 | Maakt de server zelf aan; werkplekken koppelen met een koppelcode |
| Code-signing-certificaat (client-installer) | ca. € 100–400 per jaar | Tegen SmartScreen-waarschuwingen |
| Ontwikkeltools (.NET 10 SDK, VS Code of Visual Studio, GitHub) | € 0 – laag | |
| Open-source bibliotheken | € 0 | MIT/Apache, zie [§7.4](07-technische-architectuur.md#74-bibliotheken-en-licenties) |

### Data en inhoud
1. **Testset**: 10–20 saldibalansen of XAF-bestanden van afgeronde dossiers, met het rapport uit het huidige pakket als referentie (vraag B2).
2. **RGS 3.8-werkboek**, en 3.9 zodra die definitief is.
3. **NBA-verklaringengenerator en voorbeeldrapporten** (MijnNBA) voor de teksten.
4. **Kantoorteksten**: grondslagen, opdrachtbevestiging, LOR, standaardantwoorden acceptatie, en de teksten uit de voorbeeldrapporten.
5. **Huisstijl**: logo, kleuren, lettertype en de handtekening per accountant (vraag D9).
6. **Historische saldibalansen** (4 jaar terug) per klant voor de 5-jaarskengetallen, in te lezen via het importsjabloon.

## 8.3 Risico's

| # | Risico | Kans/impact | Maatregel |
|---|---|---|---|
| R1 | **De inhoud wordt onderschat**: schema's, werkprogramma en teksten | Hoog/hoog | Vaktechnisch verantwoordelijke vanaf dag 1. Beginnen met BV klein, met de voorbeeldrapporten als norm |
| R2 | **Intellectueel eigendom** van gelicentieerde werkprogramma's, NBA-ledenmateriaal en teksten of vormgeving van bestaande pakketten | Middel/hoog | Eigen werkprogramma (B2) en eigen teksten. Bestaande pakketten alleen als functionele referentie (B12) |
| R3 | **Vaktechnische fouten in rapporten** | Middel/hoog | Golden-master-tests tegen de huidige rapporten, controles, parallelle pilot, review door de accountant |
| R4 | **Verschillen in import**: XAF per pakket, en saldibalansen met fouten (mintekens, ontbrekende regels) | Hoog/middel | Importsjabloon met harde controles (saldo nihil, uniek, geldig), XSD-validatie voor XAF, testset per pakket |
| R5 | **Server en beheer zonder ICT-partner**: uitval van de server-pc of een mislukte back-up treft alle gebruikers tegelijk | Middel/hoog | Zelfbeherende installatie, versleutelde back-up naar drie doelen met een banner bij problemen, herstelknop, UPS, beheerhandleiding van één A4. Optioneel een strippenkaart bij een lokale IT-dienstverlener voor hardwarestoringen |
| R6 | **Scope creep** | Hoog/middel | Strikte MVP. Alles buiten de lijst gaat naar fase 5 |
| R7 | **Regelgeving verandert** (NV COS/ISQM, RJk, RGS 3.9, Vpb-tarieven) | Zeker/middel | Versiebeheer op sjablonen en teksten, parameters per jaar |
| R8 | **Afhankelijkheid van één ontwikkelaar** | Middel/hoog | GitHub, tests, deze documentatie, gangbare technologie |
| R9 | **Beveiliging en AVG** | Middel/hoog | Wachtwoordbeleid, lockout, HTTPS, audit log, alleen bereikbaar binnen kantoor, database niet bereikbaar vanaf werkplekken, versleutelde back-ups, BitLocker |

*Deponeren valt bewust buiten de scope (B11). Voor BV's blijft dat voorlopig een apart proces.*

## 8.4 Roadmap (bijgewerkt)

> De doorlooptijden zijn **ruwe indicaties** voor 1 fte ontwikkeling plus 0,2–0,4 fte vaktechniek.
> Bijstellen na fase 0.

| Fase | Inhoud | Indicatie | Resultaat |
|---|---|---|---|
| **0. Verkenning en proefjes** | Openstaande P1-vragen beantwoorden. Proefjes: (1) **importsjabloon** met de controles en een XAF-parser op echte bestanden, (2) Blazor Server met inloggen en de **server-installer** op een test-pc, (3) HTML→PDF (Playwright en Paged.js) en DOCX met de opmaak uit de voorbeeldrapporten. Rubriekenschema BV en IB opzetten | 3–4 weken | Architectuur bevestigd, eerste schema's, één rapportpagina gelijk aan het voorbeeld |
| **1. Fundament** | Server-installer, database, back-up en herstel, inloggen en rollen, audit log. Klant- en dossierbeheer, **importsjabloon** (eerst), daarna XAF, RGS-beheer, rubriceringsengine en koppelscherm, proefbalans met correcties, controles | 8–10 weken | Van saldibalans naar een sluitende, gerubriceerde proefbalans, met meerdere gebruikers |
| **2. Dossier en werkprogramma** | Werkprogramma-engine (sjablonen, hoofdposten, stappen), **keuze materiële posten met doorklikken**, stappen toevoegen en verwijderen, aftekenen en goedkeuren, reviewpunten, bijlagen, cijferbeoordeling voor materiële posten | 7–9 weken | Volledig dossier |
| **3. Rapport** | Bouwstenen B01–B22, **presentatie-editor per post** (specificaties, samenvoegen, omschrijvingen, verloopoverzichten, opmaak), rapportsjablonen **Samenstellen jaarrekening BV klein** en **Winstaangifte eenmanszaak**, standaardteksten, kengetallen en meerjarenoverzicht (5 jaar), verschillenanalyse, Vpb-berekening, PDF en Word, voettekst per opdrachttype, handtekening, dossier sluiten en archiveren | 10–12 weken | Rapporten gelijkwaardig aan de voorbeeldrapporten |
| **4. Pilot** | 5–10 echte dossiers (BV klein en eenmanszaak) parallel aan het huidige pakket, verschillen analyseren, historische saldibalansen importeren, installatie op de server-pc, back-up en hersteltest, handleiding | 4–6 weken | Productierijpe versie 1.0 |
| **5. Uitbreidingen** | **BV micro** en **VOF** (sjablonen en rapporten, zodra er voorbeelden zijn), kolomtoewijzer voor andere Excel-exports, tweestapsverificatie/SSO (als niet in de MVP), digitale ondertekening, AI-voorstellen bij rubriceren, stichtingen, deponeren/SBR, Engelse rapporten | Doorlopend | |

**MVP (fasen 0–4): circa 7–9 maanden doorlooptijd.** Er is geen deadline, dus we leveren per fase op.
Daarnaast **jaarlijks onderhoud** (RGS, XAF, RJk, NV COS, Vpb-tarieven, grensbedragen, teksten): enkele weken per jaar.

### Volgorde van de inhoud (vaktechniek), parallel aan de bouw
1. Rubriekenschema BV en IB, met de **hoofd- en subposten**, en de RGS→rubriek-tabel (fase 0–1).
2. Koppelsjablonen voor de meest voorkomende rekeningschema's (fase 1).
3. Werkprogramma *Samenstellen jaarrekening* (BV) met de postprogramma's per hoofdpost, en *Winstaangifte opstellen* (eenmanszaak) (fase 2).
4. Standaardteksten en rapportsjablonen, op basis van de voorbeeldrapporten (fase 3).

## 8.5 Volgende stappen

1. **Openstaande P1-vragen** beantwoorden ([hoofdstuk 11](11-vragenlijst.md)): A1 (aantal gebruikers), A4 (rollen en review), B2 (testset) en F2 (wie bouwt, budget).
2. **Testset** aanleveren via een beveiligde map, niet via GitHub: saldibalansen of XAF-bestanden van BV's en eenmanszaken met de huidige rapporten, plus de huisstijlbestanden.
3. **Server-pc aanschaffen** (kan ook pas in fase 1): zie [§7.9](07-technische-architectuur.md#79-uitrol).
4. **Fase 0 starten**: de solution opzetten ([§7.3](07-technische-architectuur.md#73-oplossingsopbouw-projecten)), en de proefjes voor importsjabloon, server-installer met inloggen, en PDF/Word.
