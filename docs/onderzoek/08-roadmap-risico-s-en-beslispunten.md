# 8. Wat hebben we nodig? Besluiten, roadmap en risico's

> **Bijgewerkt op 23-10-2026** met de besluiten van de opdrachtgever en de analyse van de
> voorbeeldrapporten ([hoofdstuk 9](09-analyse-voorbeeldrapporten.md)). Openstaande vragen staan in de
> [vragenlijst](11-vragenlijst.md).

## 8.1 Besluitenlog

| # | Datum | Besluit | Gevolg |
|---|---|---|---|
| B1 | 23-10-2026 | **Meerdere gebruikers, met eigen wachtwoord** | Kantoorserver-model met centrale PostgreSQL-database en ASP.NET Core Identity ([§7.2](07-technische-architectuur.md#72-architectuurkeuze-voor-meerdere-gebruikers), [§7.8](07-technische-architectuur.md#78-inloggen-rechten-en-beveiliging)). Definitief na vraag A2 (offline werken?) |
| B2 | 23-10-2026 | **Eigen werkprogramma** in een sjabloon. Stappen per dossier toe te voegen en te verwijderen | Werkprogramma-engine ([hoofdstuk 10](10-werkprogramma-en-materialiteit.md)). Geen licentie op externe werkprogramma's nodig |
| B3 | 23-10-2026 | Rechtsvormen: **BV micro, BV klein, eenmanszaak, VOF** | Twee rubriekenschema's (BV en IB) en drie rapportsjablonen (BV klein, BV micro, winstaangifte EZ/VOF) |
| B5 | 23-10-2026 | Export naar **PDF én Word** | Word-renderer (OpenXML) in de MVP. Beleid voor definitieve versies: vraag D8 |
| B8 | 23-10-2026 | **Standaard importsjabloon**: de gebruiker geeft het boekjaar op en een overzicht van grootboeknummer, omschrijving en bedrag. Er wordt alleen geïmporteerd als het saldo nihil is | Eerste importroute in de bouw ([§3.3](03-gegevensimport-xaf-excel-rgs.md#33-standaard-importsjabloon-excelcsv)). Ook bruikbaar voor vorige jaren en historie |
| B9 | 23-10-2026 | **De gebruiker kiest de materiële posten**, per **hoofdpost**, met doorklikken naar de subposts. Geen systeemberekening of voorstel | Planningsscherm met vinkjes per hoofdpost ([§10.2](10-werkprogramma-en-materialiteit.md#102-werking-in-het-dossier)) |
| B10 | 23-10-2026 | **Niet-materiële posten: geen vragen of werk**, alleen zichtbaar in het rapport, waar de presentatie en opmaak aan te passen zijn | Presentatie-editor per post ([§10.4](10-werkprogramma-en-materialiteit.md#104-rapport-presentatie-en-opmaak-per-post)) |
| B11 | 23-10-2026 | **Deponeren voorlopig negeren** | Buiten scope; geen publicatiestukken of SBR in de MVP |
| B12 | 23-10-2026 | **Algemene naamgeving**: het project is een eigen samenstelapplicatie (werknaam *Boekerhouden*), geen afgeleide van een bestaand pakket | Bestaande pakketten zijn alleen marktreferentie ([hoofdstuk 1](01-marktreferentie-bestaande-samenstelsoftware.md)) |
| – | 23-10-2026 | De rapportnorm zijn de **voorbeeldrapporten** (BV klein jaarrekening; EZ bijlage aangifte IB) | Kengetallen, meerjarenoverzicht, Vpb en verschillenanalyse in de MVP ([§9.6](09-analyse-voorbeeldrapporten.md#96-gevolgen-voor-scope-en-planning)) |

**Nog open:**
- **B4** (wie bouwt): zie vraag F2.
- **B6** (UI-technologie): Blazor Server ([§7.2](07-technische-architectuur.md#72-architectuurkeuze-voor-meerdere-gebruikers)).
- **B7** (migratie): oude dossiers als PDF-archief; vorige jaren via het importsjabloon.

## 8.2 Wat we nodig hebben om te bouwen

### Mensen

| Rol | Omvang (indicatie) | Taken |
|---|---|---|
| **Ontwikkelaar (.NET/C#)** | 1 fte tijdens de MVP | Architectuur, import, rubricering, werkprogramma, rapportgenerator (PDF en Word), UI, inloggen. AI-ondersteuning kan versnellen, maar alles wordt gereviewd en getest |
| **Vaktechnisch verantwoordelijke** | 0,2–0,4 fte | Productowner. Stelt rubriekenschema's, RGS→rubriek-tabel, werkprogramma, standaardteksten en rapportsjablonen op, en test met echte dossiers |
| **ICT-beheer / partner** | Beperkt | Server of VM, back-up, certificaten, VPN, installatie |
| **Testgebruikers** (2–3) | Pilotfase | Dossiers parallel draaien naast het huidige pakket |

### Middelen

| Wat | Kosten (indicatie) | Opmerking |
|---|---|---|
| Kantoorserver of VM (4 cores, 8–16 GB RAM) | Bestaande hardware, of een VM bij de ICT-partner | Inclusief back-up en een kopie buiten de server |
| PostgreSQL | € 0 | Open source |
| Windows Server-licentie (als er nog geen is) | Afhankelijk van de ICT-partner | Linux kan ook |
| TLS-certificaat | € 0 – laag | Let's Encrypt of een interne CA |
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
| R5 | **Server en beheer**: uitval of een slechte back-up treft alle gebruikers tegelijk | Middel/hoog | ICT-partner, back-up met hersteltest, monitoring, eenvoudige installatie |
| R6 | **Scope creep** | Hoog/middel | Strikte MVP. Alles buiten de lijst gaat naar fase 5 |
| R7 | **Regelgeving verandert** (NV COS/ISQM, RJk, RGS 3.9, Vpb-tarieven) | Zeker/middel | Versiebeheer op sjablonen en teksten, parameters per jaar |
| R8 | **Afhankelijkheid van één ontwikkelaar** | Middel/hoog | GitHub, tests, deze documentatie, gangbare technologie |
| R9 | **Beveiliging en AVG** | Middel/hoog | Wachtwoordbeleid, lockout, tweestapsverificatie buiten kantoor, HTTPS, audit log, database niet bereikbaar vanaf werkplekken |

*Deponeren valt bewust buiten de scope (B11). Voor BV's blijft dat voorlopig een apart proces.*

## 8.4 Roadmap (bijgewerkt)

> De doorlooptijden zijn **ruwe indicaties** voor 1 fte ontwikkeling plus 0,2–0,4 fte vaktechniek.
> Bijstellen na fase 0.

| Fase | Inhoud | Indicatie | Resultaat |
|---|---|---|---|
| **0. Verkenning en proefjes** | Vragenlijst P1 beantwoorden. Proefjes: (1) **importsjabloon** met de controles en een XAF-parser op echte bestanden, (2) Blazor Server met inloggen op een testserver, (3) HTML→PDF (Playwright en Paged.js) en DOCX met de opmaak uit de voorbeeldrapporten. Rubriekenschema BV en IB opzetten | 3–4 weken | Architectuur bevestigd, eerste schema's, één rapportpagina gelijk aan het voorbeeld |
| **1. Fundament** | Server, database, inloggen en rollen, audit log. Klant- en dossierbeheer, **importsjabloon** (eerst), daarna XAF, RGS-beheer, rubriceringsengine en koppelscherm, proefbalans met correcties, controles | 8–10 weken | Van saldibalans naar een sluitende, gerubriceerde proefbalans, met meerdere gebruikers |
| **2. Dossier en werkprogramma** | Werkprogramma-engine (sjablonen, hoofdposten, stappen), **keuze materiële posten met doorklikken**, stappen toevoegen en verwijderen, aftekenen en goedkeuren, reviewpunten, bijlagen, cijferbeoordeling voor materiële posten | 7–9 weken | Volledig dossier |
| **3. Rapport** | Bouwstenen B01–B22, **presentatie-editor per post** (specificaties, samenvoegen, omschrijvingen, verloopoverzichten, opmaak), rapportsjablonen BV klein, BV micro en winstaangifte EZ/VOF, standaardteksten, kengetallen en meerjarenoverzicht (5 jaar), verschillenanalyse, Vpb-berekening, PDF en Word, voettekst per opdrachttype, handtekening, dossier sluiten en archiveren | 10–12 weken | Rapporten gelijkwaardig aan de voorbeeldrapporten |
| **4. Pilot** | 5–10 echte dossiers parallel aan het huidige pakket, verschillen analyseren, historische saldibalansen importeren, installatie op de kantoorserver, back-up en hersteltest, handleiding | 4–6 weken | Productierijpe versie 1.0 |
| **5. Uitbreidingen** | Kolomtoewijzer voor andere Excel-exports, tweestapsverificatie/SSO (als niet in de MVP), digitale ondertekening, AI-voorstellen bij rubriceren, stichtingen, deponeren/SBR, Engelse rapporten | Doorlopend | |

**MVP (fasen 0–4): circa 8–10 maanden doorlooptijd.**
Daarnaast **jaarlijks onderhoud** (RGS, XAF, RJk, NV COS, Vpb-tarieven, grensbedragen, teksten): enkele weken per jaar.

### Volgorde van de inhoud (vaktechniek), parallel aan de bouw
1. Rubriekenschema BV en IB, met de **hoofd- en subposten**, en de RGS→rubriek-tabel (fase 0–1).
2. Koppelsjablonen voor de meest voorkomende rekeningschema's (fase 1).
3. Werkprogramma *Samenstellen BV* met de postprogramma's per hoofdpost, en *Winstaangifte EZ/VOF* (fase 2).
4. Standaardteksten en rapportsjablonen, op basis van de voorbeeldrapporten (fase 3).

## 8.5 Volgende stappen

1. **Openstaande P1-vragen** beantwoorden ([hoofdstuk 11](11-vragenlijst.md)), vooral A2 (offline werken), A3 (server), D1–D3 (opdrachttypen en rapporten) en F1–F3 (planning, bouwer, repository).
2. **Testset en voorbeelden** aanleveren via een beveiligde map, niet via GitHub: saldibalansen of XAF-bestanden met de huidige rapporten, een voorbeeldrapport **VOF** en **BV micro**, en de huisstijlbestanden.
3. **Fase 0 starten**: de solution opzetten ([§7.3](07-technische-architectuur.md#73-oplossingsopbouw-projecten)), en de proefjes voor importsjabloon, server met inloggen, en PDF/Word.
