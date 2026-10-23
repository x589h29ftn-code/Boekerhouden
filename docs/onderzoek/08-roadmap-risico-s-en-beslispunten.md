# 8. Wat hebben we nodig? Roadmap, risico's en beslispunten

## 8.1 Wat we nodig hebben om te bouwen

### Mensen

| Rol | Omvang (indicatie) | Taken |
|---|---|---|
| **Ontwikkelaar (.NET/C#)** | 1 fte gedurende de MVP | Architectuur, import, rubriceringsengine, werkprogramma-engine, rapportgenerator, UI. Kan versneld worden met AI-ondersteuning (Claude Code), maar elke regel moet gereviewd en getest worden |
| **Vaktechnisch verantwoordelijke (accountant)** | 0,2–0,4 fte | Productowner. Stelt rubriekenschema, RGS→rubriek-tabel, werkprogramma, standaardteksten en rapportsjablonen op; test met echte dossiers. **Dit is de kritieke rol: de inhoud is minstens de helft van het product** |
| **Testgebruikers** (2–3 medewerkers) | Pilotfase | Echte dossiers parallel draaien naast Visionplanner |
| **Beheer/ICT** | Beperkt | Installatie, back-up, code-signing, later de databaseserver |

### Middelen en licenties

| Wat | Kosten (indicatie) | Opmerking |
|---|---|---|
| .NET 10 SDK, Visual Studio Community/Code, GitHub | € 0 – laag | Visual Studio Community is gratis voor kleine organisaties; anders VS Professional |
| Open-source bibliotheken (zie [§7.4](07-technische-architectuur.md#74-bibliotheken-en-licenties)) | € 0 | MIT/Apache-licenties |
| Code-signing-certificaat | ca. € 100–400 per jaar | Tegen Windows SmartScreen-waarschuwingen |
| (Optioneel) QuestPDF | € 0 zolang de omzet < USD 1 mln, anders een betaalde licentie | Alleen als we niet voor HTML→PDF kiezen |
| (Optioneel) licentie SRA/NOAB-werkprogramma | Op aanvraag | Alleen als je hun inhoud wilt gebruiken (zie beslispunt B2) |
| (Later) databaseserver | € 0 (PostgreSQL / SQL Server Express) + hardware/VM | Voor multi-user |

### Data en inhoud

1. **Testbestanden**: 10–20 XAF-bestanden (3.1/3.2 én 4.0) en Excel-proefbalansen uit de pakketten die het kantoor ziet, plus het bijbehorende eindrapport uit Visionplanner als referentie (*golden master*). AVG: geanonimiseerd of met een verwerkingsgrondslag, alleen op beveiligde apparaten.
2. **RGS 3.8-werkboek** (download van referentiegrootboekschema.nl), en straks 3.9.
3. **NBA-voorbeeldrapporten 2025** (MijnNBA) en de **NBA-verklaringengenerator**, als basis voor rapportsjablonen en verklaringsteksten.
4. **Eigen bestaande teksten** van het kantoor: grondslagen, opdrachtbevestiging, LOR, standaardantwoorden voor de acceptatie (zoals in je schermafbeelding *Scope opdracht*).
5. **Een export van bestaande Visionplanner-dossiers** (PDF's van rapporten en dossiers) als archief én als referentie voor de inhoudsopbouw.

## 8.2 Roadmap

> De doorlooptijden zijn **ruwe indicaties** voor 1 fte ontwikkeling plus 0,2–0,4 fte vaktechniek.
> Ze hangen sterk af van ervaring, AI-ondersteuning en hoe snel de inhoud klaar is. Na fase 0
> stellen we ze bij.

| Fase | Inhoud | Indicatie | Resultaat |
|---|---|---|---|
| **0. Verkenning en proefjes** | Demo-account Nmbrs Reporting bekijken om de open vragen te beantwoorden. Proefjes met (1) XAF-parser op echte bestanden, (2) Blazor Hybrid-schil, (3) HTML→PDF met paginering en inhoudsopgave. Rubriekenschema en RGS-tabel opzetten | 2–4 weken | Technische keuzes bevestigd, eerste schema |
| **1. Fundament** | Datamodel en database, klant- en dossierbeheer, import XAF/Excel, RGS-beheer, rubriceringsengine en koppelscherm, proefbalans met correcties, controles C01–C08 | 6–8 weken | Van auditfile naar sluitende, gerubriceerde proefbalans |
| **2. Dossier** | Werkprogramma-engine (sjablonen, voorwaarden, verplicht), dossierboom, aftekenen en goedkeuren, reviewpunten, bijlagen, cijferbeoordeling (planning/huidig/vorig/delta), specificaties, verloopoverzichten | 6–8 weken | Volledig dossier zoals in de schermafbeeldingen |
| **3. Rapport** | Rapportsjablonen, standaardteksten met variabelen, balans/W&V/toelichting/specificaties, samenstellingsverklaring, PDF (concept/definitief), dossier sluiten en archiveren, audit trail | 6–8 weken | Eerste complete financieel rapport BV klein |
| **4. Pilot** | 5–10 echte dossiers parallel aan Visionplanner, verschillen analyseren, installer en updates, back-up, gebruikershandleiding | 4–6 weken | Productierijpe versie 1.0 voor één gebruiker |
| **5. Uitbreidingen** | Multi-user (PostgreSQL/SQL Server), IB-ondernemer/VOF-rapport (kapitaalsvergelijking, winstverdeling), kengetallen, fiscale positie/Vpb, Word-export, stichtingen, AI-voorstellen bij rubriceren, Visionplanner-Excelprofiel, RGS Brugstaat | doorlopend | |

**MVP (fasen 0–4): circa 6–8 maanden doorlooptijd.**
Daarnaast **jaarlijks onderhoud**: RGS-versie, XAF-wijzigingen, RJk-jaareditie, NV COS-aanpassingen, grensbedragen en teksten. Reken op enkele weken per jaar (ontwikkeling en vaktechniek samen).

### Volgorde van de inhoud (vaktechniek), parallel aan de ontwikkeling

1. Rubriekenschema BV (fase 0–1).
2. RGS→rubriek-tabel en koppelsjablonen voor de 2–3 meest voorkomende pakketten (fase 1).
3. Werkprogramma samenstellen BV klein, plus acceptatie en afronding (fase 2).
4. Standaardteksten (verklaring, grondslagen, toelichting) en rapportsjabloon BV klein en micro (fase 3).
5. IB-schema, rapportsjabloon en werkprogramma aangifte-plus (fase 5).

## 8.3 Risico's

| # | Risico | Kans/impact | Maatregel |
|---|---|---|---|
| R1 | **De inhoud wordt onderschat.** Schema's, werkprogramma's en teksten zijn veel werk en vragen vakkennis | Hoog/hoog | Vaktechnisch verantwoordelijke vanaf dag 1. Beginnen met één variant (BV klein); NBA-voorbeeldrapporten als basis |
| R2 | **Intellectueel eigendom.** SRA-werkprogramma, NBA-ledenmateriaal en Visionplanner-teksten mogen niet zomaar worden overgenomen | Middel/hoog | Eigen teksten schrijven of een licentie regelen. Visionplanner alleen *functioneel* nabouwen, niet de vormgeving of teksten kopiëren |
| R3 | **Vaktechnische fouten in de rapporten** (verkeerde rubricering, afronding, verkeerde verklaringsvariant) | Middel/hoog | Golden-master-tests tegen Visionplanner-uitkomsten, controles C01–C15, pilot met parallelle dossiers, review door de accountant |
| R4 | **Verschillen in XAF per pakket**, en de overgang naar XAF 4.0 per 1-1-2027 | Hoog/middel | Testset per pakket, XSD-validatie, 3.x en 4.0 vanaf het begin ondersteunen, duidelijke meldingen |
| R5 | **Multi-user blijkt eerder nodig** (samensteller én reviewer tegelijk) | Middel/middel | EF Core zonder SQLite-specifieke code, optimistische concurrency, PostgreSQL-route voorbereid (beslispunt B1) |
| R6 | **Scope creep.** Visionplanner heeft tien jaar functionaliteit | Hoog/middel | Strikte MVP-lijst ([§5.1](05-functioneel-ontwerp.md#51-modules-in-één-oogopslag)), alles daarbuiten naar fase 5 |
| R7 | **Regelgeving verandert** (NV COS/ISQM 2026–2027, RJk, RGS 3.9) | Zeker/middel | Versiebeheer op sjablonen en teksten, en een jaarlijkse onderhoudscyclus |
| R8 | **Afhankelijkheid van één ontwikkelaar** | Middel/hoog | Code in GitHub, tests, architectuurdocumentatie (deze map), conventionele technologie (.NET) |
| R9 | **AVG en beveiliging** (klantgegevens op laptops) | Middel/hoog | BitLocker, rollen, audit log, back-ups, dataminimalisatie, verwerkingsregister bijwerken |
| R10 | **Licentiewijzigingen** van bibliotheken (zoals QuestPDF en CKEditor eerder) | Middel/laag | Waar mogelijk MIT/Apache; alternatieven in de architectuur (HTML→PDF) |

## 8.4 Beslispunten voor jou

| # | Beslissing | Opties | Advies |
|---|---|---|---|
| **B1** | Eén gebruiker of meerdere gebruikers? | (a) Standalone, één gebruiker per database (SQLite). (b) Vanaf het begin een databaseserver op kantoor | **(a) voor de MVP en pilot, (b) vóór de uitrol op kantoor.** De architectuur houdt (b) open. Wil je vanaf het begin met reviewer en samensteller tegelijk werken, kies dan direct (b) |
| **B2** | Werkprogramma-inhoud | (a) Eigen kantoorwerkprogramma. (b) Licentie op het SRA/NOAB-werkprogramma (lidmaatschap en toestemming van de licentiegever nodig) | **(a)**, op basis van [§2.4](02-vaktechnisch-kader.md#24-werkprogramma-samenstellen). De engine kan later (b) laden als dat juridisch kan |
| **B3** | Rechtsvormen in de MVP | BV micro/klein, eenmanszaak/VOF, stichting | **BV micro en klein** in de MVP, **IB-ondernemer** direct daarna. Kijk in de klantenportefeuille welke het meest voorkomt |
| **B4** | Wie bouwt het? | Zelf met Claude Code, een freelance .NET-ontwikkelaar, of een softwarebureau | Een ervaren .NET-ontwikkelaar (eventueel met AI-ondersteuning). De vaktechniek blijft altijd in eigen huis |
| **B5** | Rapportexport | Alleen PDF, of ook Word | **PDF in de MVP**, Word in fase 5. Word-nabewerking ondermijnt de dossierintegriteit, want het uitgebrachte rapport moet overeenkomen met het dossier |
| **B6** | UI-technologie | Blazor Hybrid in WPF, of klassiek WPF | **Blazor Hybrid** ([§7.2](07-technische-architectuur.md#72-keuze-ui-technologie)). Bevestigen na het proefje in fase 0 |
| **B7** | Migratie vanuit Visionplanner | Geen; alleen vorig-jaarcijfers; of volledige dossiers | **Archief als PDF bewaren** en vorig-jaarcijfers per klant via XAF of Excel ("Cijfers vorig") importeren. Een volledige dossiermigratie is niet zinvol |

## 8.5 Openstaande vragen (geconsolideerd)

**Voor een demo- of proefaccount Nmbrs Reporting** (zie [hoofdstuk 1](01-hoe-werkt-visionplanner-compilation.md#open-vragen)):
- het goedkeuringsmechaniek en de rollen;
- correctieboekingen;
- splitsen van rekeningen;
- doorrollen van vorig jaar;
- drill-down naar transacties;
- drempels van de alerts;
- nummering van het rapport;
- Word-export.

**Vaktechnisch** (zie [hoofdstuk 2](02-vaktechnisch-kader.md#open-vragen-vaktechnisch)):
- letterlijke NBA-verklaringsteksten;
- Bijlage 1 van HR 1136;
- afsluit- en bewaartermijn onder SKM1;
- of klein vrijgesteld is van 2:368;
- de huidige modellen uit het BMJ;
- licenties van de werkprogramma's.

**Technisch** (zie [hoofdstuk 3](03-gegevensimport-xaf-excel-rgs.md#open-vragen)):
- RGS-licentie;
- de officiële XSD van XAF 4.0;
- `accTp = M`;
- de eigenaardigheden per pakket;
- het formaat van de RGS Brugstaat.

## 8.6 Concrete volgende stappen

1. **Beslispunten B1–B7** doorlopen (een uur overleg is genoeg).
2. **Testset verzamelen**: XAF-bestanden en Visionplanner-rapporten van 10–20 representatieve klanten.
3. **Demo-account Nmbrs Reporting** aanvragen, of een collega met toegang een uur laten meekijken, om de open vragen uit hoofdstuk 1 te beantwoorden.
4. **Fase 0 starten**: de solution opzetten (structuur uit [§7.3](07-technische-architectuur.md#73-oplossingsopbouw-projecten)), XAF-parser met tests op de testset, en proefjes voor de UI-schil en PDF.
5. **Vaktechniek start parallel** met het rubriekenschema BV en de RGS→rubriek-tabel.
