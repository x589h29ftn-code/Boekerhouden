# 8. Wat hebben we nodig? Besluiten, roadmap en risico's

> **Bijgewerkt op 23-10-2026** met de besluiten van de opdrachtgever en de analyse van de
> voorbeeldrapporten ([hoofdstuk 9](09-analyse-voorbeeldrapporten.md)). Openstaande vragen staan in de
> [vragenlijst](11-vragenlijst.md).

## 8.1 Besluitenlog

| # | Datum | Besluit | Gevolg |
|---|---|---|---|
| B1 | 23-10-2026 | **Meerdere gebruikers, met eigen wachtwoord** | Kantoorserver-model met centrale PostgreSQL-database en ASP.NET Core Identity ([§7.2](07-technische-architectuur.md#72-architectuurkeuze-voor-meerdere-gebruikers), [§7.8](07-technische-architectuur.md#78-inloggen-rechten-en-beveiliging)). Definitief na vraag A2 (offline werken?) |
| B2 | 23-10-2026 | **Eigen werkprogramma** in een sjabloon. Een post die als **materieel** wordt aangevinkt, krijgt zijn werkprogramma in de uitvoering. Stappen zijn per dossier toe te voegen en te verwijderen | Werkprogramma-engine met materialiteit ([hoofdstuk 10](10-werkprogramma-en-materialiteit.md)). Geen SRA-licentie nodig |
| B3 | 23-10-2026 | Rechtsvormen: **BV micro, BV klein, eenmanszaak, VOF** | Twee rubriekenschema's (BV en IB) en drie rapportsjablonen (BV klein, BV micro, winstaangifte EZ/VOF) |
| B5 | 23-10-2026 | Export naar **PDF én Word** | De Word-renderer (OpenXML) zit in de MVP. Beleid voor definitieve versies: vraag D8 |
| – | 23-10-2026 | De rapportnorm is de **voorbeeldrapporten** (BV klein jaarrekening; EZ bijlage aangifte IB) | Kengetallen, meerjarenoverzicht, fiscale positie (Vpb) en verschillenanalyse horen in de MVP ([§9.6](09-analyse-voorbeeldrapporten.md#96-gevolgen-voor-scope-en-planning)) |

**Nog open:**
- **B4** (wie bouwt): zie vraag F2.
- **B6** (UI-technologie): Blazor, nu als Blazor Server ([§7.2](07-technische-architectuur.md#72-architectuurkeuze-voor-meerdere-gebruikers)).
- **B7** (migratie): zie vragen B4 en E4.

## 8.2 Wat we nodig hebben om te bouwen

### Mensen

| Rol | Omvang (indicatie) | Taken |
|---|---|---|
| **Ontwikkelaar (.NET/C#)** | 1 fte tijdens de MVP | Architectuur, import, rubriceringsengine, werkprogramma-engine, rapportgenerator (PDF en Word), UI, inloggen. Versnelling met AI-ondersteuning is mogelijk, maar alles wordt gereviewd en getest |
| **Vaktechnisch verantwoordelijke** | 0,2–0,4 fte | Productowner. Stelt rubriekenschema's, RGS→rubriek-tabel, werkprogramma, standaardteksten en rapportsjablonen op, en test met echte dossiers |
| **ICT-beheer / partner** | Beperkt | Server of VM, back-up, certificaten, VPN, installatie |
| **Testgebruikers** (2–3) | Pilotfase | Dossiers parallel draaien naast Visionplanner |

### Middelen

| Wat | Kosten (indicatie) | Opmerking |
|---|---|---|
| Kantoorserver of VM (Windows Server), 4 cores, 8–16 GB RAM | Bestaande hardware, of een VM bij de ICT-partner | Inclusief back-up en een kopie buiten de server |
| PostgreSQL | € 0 | Open source |
| Windows Server-licentie (als er nog geen is) | Afhankelijk van de ICT-partner | Linux kan ook; dat scheelt licentiekosten |
| TLS-certificaat (intern of extern) | € 0 – laag | Let's Encrypt of een interne CA |
| Code-signing-certificaat (client-installer) | ca. € 100–400 per jaar | Tegen SmartScreen-waarschuwingen |
| Ontwikkeltools (.NET 10 SDK, VS Code of Visual Studio, GitHub) | € 0 – laag | |
| Open-source bibliotheken | € 0 | MIT/Apache, zie [§7.4](07-technische-architectuur.md#74-bibliotheken-en-licenties) |

### Data en inhoud
1. **Testset**: 10–20 XAF-bestanden met de Visionplanner-rapporten (vraag B2).
2. **RGS 3.8-werkboek**, en 3.9 zodra die definitief is.
3. **NBA-verklaringengenerator en voorbeeldrapporten** (MijnNBA) voor de teksten.
4. **Kantoorteksten**: grondslagen, opdrachtbevestiging, LOR, standaardantwoorden acceptatie, en de bestaande teksten uit de voorbeeldrapporten.
5. **Huisstijl**: logo, kleuren, lettertype en de handtekening per accountant (vraag D9).
6. **Historische cijfers** voor 5-jaarskengetallen (vraag B4).

## 8.3 Risico's

| # | Risico | Kans/impact | Maatregel |
|---|---|---|---|
| R1 | **De inhoud wordt onderschat**: schema's, werkprogramma en teksten | Hoog/hoog | Vaktechnisch verantwoordelijke vanaf dag 1. Beginnen met BV klein, en de voorbeeldrapporten als norm |
| R2 | **Intellectueel eigendom**: SRA, NBA-ledenmateriaal, Visionplanner | Middel/hoog | Eigen werkprogramma (besluit B2) en eigen teksten. Visionplanner alleen functioneel nabouwen |
| R3 | **Vaktechnische fouten in rapporten** | Middel/hoog | Golden-master-tests tegen Visionplanner-uitkomsten, controles, parallelle pilot, review door de accountant |
| R4 | **XAF-variatie per pakket**, en XAF 4.0 per 1-1-2027 | Hoog/middel | Testset per pakket, XSD-validatie, 3.x en 4.0 vanaf het begin |
| R5 | **Server en beheer**: uitval of een slechte back-up treft alle gebruikers tegelijk | Middel/hoog | Een ICT-partner, back-up met hersteltest, monitoring, een eenvoudige installatie |
| R6 | **Scope creep** (Visionplanner heeft tien jaar functionaliteit) | Hoog/middel | Strikte MVP. Alles buiten de lijst gaat naar fase 5 |
| R7 | **Regelgeving verandert** (NV COS/ISQM, RJk, RGS 3.9, Vpb-tarieven) | Zeker/middel | Versiebeheer op sjablonen en teksten, en parameters per jaar |
| R8 | **Afhankelijkheid van één ontwikkelaar** | Middel/hoog | GitHub, tests, deze documentatie, gangbare technologie |
| R9 | **Beveiliging en AVG**: meerdere gebruikers en toegang buiten kantoor | Middel/hoog | Wachtwoordbeleid, lockout, tweestapsverificatie buiten kantoor, HTTPS, audit log, database niet bereikbaar vanaf werkplekken |
| R10 | **Deponering valt buiten de scope**: er ontstaat een procesgat bij BV's | Hoog/middel | Vraag D4: tijdelijk via de KvK-dienst of een bestaand pakket, SBR later |

## 8.4 Roadmap (bijgewerkt)

> De doorlooptijden zijn **ruwe indicaties** voor 1 fte ontwikkeling plus 0,2–0,4 fte vaktechniek.
> Ten opzichte van de eerste versie komen erbij: meerdere gebruikers en inloggen, Word-export,
> EZ/VOF in de MVP, en kengetallen, meerjarenoverzicht en Vpb in de MVP. Bijstellen na fase 0.

| Fase | Inhoud | Indicatie | Resultaat |
|---|---|---|---|
| **0. Verkenning en proefjes** | Vragenlijst P1 beantwoorden. Demo Nmbrs Reporting. Proefjes: (1) XAF-parser op echte bestanden, (2) Blazor Server en inloggen op een testserver, (3) HTML→PDF (Playwright en Paged.js) en DOCX met de opmaak uit de voorbeeldrapporten. Rubriekenschema BV en IB opzetten | 3–4 weken | Architectuur bevestigd, eerste schema's, één rapportpagina pixel-gelijk aan het voorbeeld |
| **1. Fundament** | Server, database, inloggen en rollen, audit log. Klant- en dossierbeheer, import XAF/Excel, RGS-beheer, rubriceringsengine en koppelscherm, proefbalans met correcties, controles | 8–10 weken | Van auditfile naar een sluitende, gerubriceerde proefbalans, met meerdere gebruikers |
| **2. Dossier en werkprogramma** | Werkprogramma-engine (sjablonen, posten, stappen), **materialiteit en materieel-vinkje**, stappen toevoegen en verwijderen, aftekenen en goedkeuren, reviewpunten, bijlagen, cijferbeoordeling, specificaties, verloopoverzichten | 8–10 weken | Volledig dossier zoals in de schermafbeeldingen |
| **3. Rapport** | Bouwstenen B01–B22, rapportsjablonen **BV klein, BV micro en winstaangifte EZ/VOF**, standaardteksten, kengetallen (5 jaar), meerjarenoverzicht, verschillenanalyse, **Vpb-berekening**, PDF en **Word**, voettekst per opdrachttype, handtekening, dossier sluiten en archiveren | 10–12 weken | Rapporten gelijkwaardig aan de voorbeeldrapporten |
| **4. Pilot** | 5–10 echte dossiers parallel aan Visionplanner, verschillen analyseren, historische cijfers importeren, installatie op de kantoorserver, back-up en hersteltest, handleiding | 4–6 weken | Productierijpe versie 1.0 |
| **5. Uitbreidingen** | Tweestapsverificatie/SSO (als niet in de MVP), digitale ondertekening, AI-voorstellen bij rubriceren, RGS Brugstaat, stichtingen, publicatiestukken/SBR, Engelse rapporten | Doorlopend | |

**MVP (fasen 0–4): circa 8–11 maanden doorlooptijd.**
Daarnaast **jaarlijks onderhoud** (RGS, XAF, RJk, NV COS, Vpb-tarieven, grensbedragen, teksten): enkele weken per jaar.

### Volgorde van de inhoud (vaktechniek), parallel aan de bouw
1. Rubriekenschema BV en IB, en de RGS→rubriek-tabel (fase 0–1).
2. Koppelsjablonen voor de meest voorkomende pakketten (fase 1).
3. Werkprogramma *Samenstellen BV* met de postprogramma's per post, en *Winstaangifte EZ/VOF* (fase 2).
4. Standaardteksten en rapportsjablonen, op basis van de voorbeeldrapporten (fase 3).

## 8.5 Volgende stappen

1. **Vragenlijst P1** beantwoorden ([hoofdstuk 11](11-vragenlijst.md)), vooral A2 (offline werken), A3 (server), C1–C4 (materialiteit), D1–D4 (opdrachttypen en rapporten) en F1–F3 (planning, bouwer, repository).
2. **Testset en voorbeelden** aanleveren via een beveiligde map, niet via GitHub: XAF-bestanden met Visionplanner-rapporten, een voorbeeldrapport **VOF** en **BV micro**, huisstijlbestanden.
3. **Demo Nmbrs Reporting** voor de open vragen uit [hoofdstuk 1](01-hoe-werkt-visionplanner-compilation.md#open-vragen).
4. **Fase 0 starten**: de solution opzetten ([§7.3](07-technische-architectuur.md#73-oplossingsopbouw-projecten)), en proefjes voor parser, server met inloggen, en PDF/Word.
