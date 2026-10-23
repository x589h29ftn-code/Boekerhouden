# 7. Technische architectuur

> **Bijgewerkt op 23-10-2026** met de besluiten:
> - **meerdere gebruikers met wachtwoord**;
> - **alleen binnen kantoor werken** (A2);
> - er is **geen server en geen ICT-partner** (A3);
> - **maximaal 5 gebruikers** (A1), **review niet verplicht** (A4);
> - de opdrachtgever is zelf **beheerder** (A8) en **bouwt het zelf**, met AI-ondersteuning (F2).
>
> Daarom draait de applicatie op een eenvoudige **server-pc op kantoor**, die zichzelf zo veel
> mogelijk beheert: installatie, back-up, herstel, certificaat en updates.

## 7.1 Uitgangspunten

1. **Windows-oplossing op kantoor voor meerdere gebruikers.** Iedere gebruiker logt in met een eigen naam en wachtwoord. Alleen bereikbaar binnen het kantoornetwerk; geen cloud, geen toegang van buitenaf, geen API-koppelingen.
2. **Eén centrale database.** Samensteller en reviewer werken tegelijk in hetzelfde dossier.
3. **Bedragen zijn altijd exact.** In de code `decimal`, nooit `double`/`float`. In de database staan bedragen als gehele eurocenten of `numeric`.
4. **Grote auditfiles kunnen geen kwaad.** Een XAF-bestand kan honderden MB zijn. We lezen het *streaming* in (XmlReader), niet als DOM.
5. **Inhoud is data, geen code.** Rubriekenschema's, werkprogramma's, standaardteksten en rapportsjablonen staan als beheerbare data in de app, met versies.
6. **Ontwikkelen en testen kan ook buiten Windows.** Alles behalve de dunne Windows-schil moet op Linux en in CI te bouwen en te testen zijn.
7. **Zelfbeherend.** Er is geen ICT-partner. Installatie, back-up, herstel, certificaat en updates moeten werken zonder specialistische kennis, met duidelijke meldingen in de beheerpagina.

## 7.2 Architectuurkeuze voor meerdere gebruikers

| Model | Hoe | Voordelen | Nadelen |
|---|---|---|---|
| **A. Kantoorserver met webtechniek** ✅ | ASP.NET Core + **Blazor Server** draait als Windows-service op een **server-pc op kantoor** ([§7.9](#79-uitrol)), met **PostgreSQL** ernaast. Medewerkers openen de app in **Edge, geïnstalleerd als app** (eigen venster en icoon). Later eventueel een eigen dunne Windows-app | Meerdere gebruikers en inloggen zitten er standaard in (ASP.NET Core Identity). **De database is niet bereikbaar vanaf de werkplekken**, dus rechten en audit trail zijn niet te omzeilen. Eén plek om te updaten. PDF en Word worden centraal gemaakt. De UI-componenten (Razor) zijn dezelfde als in het eerdere ontwerp | De server-pc moet tijdens kantooruren aan staan. Niet buiten kantoor en niet offline (niet nodig, besluit A2) |
| B. Desktop-app met centrale database | Blazor Hybrid/WPF op elke pc, die rechtstreeks verbindt met PostgreSQL op de server | Werkt als een "echte" desktop-app. PDF via WebView2 op de pc | **Elke pc heeft databasewachtwoorden**, dus een technisch onderlegde gebruiker kan de app-rechten en audit trail omzeilen. Afvangen kost veel werk (databaserollen per gebruiker). Updates op elke pc |
| C. Desktop-app + eigen applicatieserver (API) | WPF/Blazor Hybrid-client praat met een ASP.NET Core Web API | Veilig en een volwaardige desktop-app | **Het meeste bouwwerk**: een volledige API-laag erbij. Offline vraagt nog steeds synchronisatie |

**Advies: model A.**
- Het voldoet het eenvoudigst en veiligst aan "meerdere gebruikers met wachtwoord".
- Voor de gebruiker voelt het als een Windows-applicatie (eigen venster en icoon), terwijl het technisch één centrale installatie is.
- Omdat de UI al in Blazor (Razor-componenten) was gepland, is het verschil met het eerdere ontwerp vooral het *hostingmodel*. Domeinlogica, import, rubricering en rapportage blijven gelijk.
- Wil het kantoor later naar de cloud (bijv. een Azure-VM of hosting), dan kan dit model daar ongewijzigd heen.

> **Besloten (A2):** er wordt alleen binnen kantoor gewerkt. Offline werken is niet nodig, dus
> **model A is definitief**.

**.NET 10** (LTS, ondersteund tot november 2028). Bron: [.NET Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [Blazor (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid).

## 7.3 Oplossingsopbouw (projecten)

```
Boekerhouden.sln
├─ src/
│  ├─ Boekerhouden.Domain        // Entiteiten, rekenregels, aansluitcontroles (pure C#, geen I/O)
│  ├─ Boekerhouden.Import        // Standaard importsjabloon, XAF 3.x/4.0-parser, RGS-referentietabel
│  ├─ Boekerhouden.Mapping       // Rubriceringsengine (handmatig → sjabloon → RGS → voorstel)
│  ├─ Boekerhouden.Workprogram   // Werkprogramma-engine, materiële posten, stappen (hfst. 10)
│  ├─ Boekerhouden.Reporting     // Rapportmodel, bouwstenen (hfst. 9), renderers HTML / PDF / DOCX
│  ├─ Boekerhouden.Data          // EF Core DbContext, migraties, PostgreSQL
│  ├─ Boekerhouden.UI            // Razor Class Library: alle schermen/componenten
│  ├─ Boekerhouden.Server        // ASP.NET Core: Blazor Server, Identity/inloggen, achtergrondtaken
│  └─ Boekerhouden.Client        // (later, optioneel) dunne Windows-app: WebView2-venster naar de server
└─ tests/
   ├─ Boekerhouden.Domain.Tests
   ├─ Boekerhouden.Import.Tests  // golden files: geanonimiseerde XAF's → verwachte proefbalans
   ├─ Boekerhouden.Mapping.Tests
   ├─ Boekerhouden.Workprogram.Tests
   ├─ Boekerhouden.Reporting.Tests // snapshot-tests van rapporten (Verify)
   └─ Boekerhouden.UI.Tests      // bUnit + Playwright end-to-end tegen de server
```

*Stand na plakje 2:* gebouwd zijn `Domain`, `Import`, `Data` en `Server`, plus `Boekerhouden.Cli` (opdrachtregel voor sjablonen en controles). De schermen staan voorlopig in `Boekerhouden.Server/Components`; een aparte `UI`-bibliotheek komt pas als dat iets oplevert. `Boekerhouden.Server.Tests` test de diensten en het inloggen via echte HTTP-verzoeken, tegen een eigen PostgreSQL-testdatabase per testklasse.

Alleen de optionele `Boekerhouden.Client` is Windows-specifiek. De server draait op Windows, maar ook op Linux. Daardoor is **de hele applicatie te bouwen en te testen in een Linux-omgeving**, bijvoorbeeld een Claude Code-cloudsessie of CI.

## 7.4 Bibliotheken en licenties

| Doel | Bibliotheek | Licentie | Opmerking |
|---|---|---|---|
| Web/UI-host | ASP.NET Core + Blazor Server | MIT | |
| **Inloggen en gebruikersbeheer** | **ASP.NET Core Identity** | MIT | Wachtwoordhashing (PBKDF2), lockout, wachtwoordbeleid, optioneel tweestapsverificatie (TOTP-app) |
| Database | EF Core + **Npgsql** (PostgreSQL) | MIT / PostgreSQL-licentie | Alternatief: SQL Server Express (gratis, maximaal 10 GB per database) |
| Excel lezen/schrijven (.xlsx) | ClosedXML | MIT | Import proefbalans, export specificaties |
| Oude Excel (.xls) | ExcelDataReader | MIT | Alleen lezen |
| CSV | CsvHelper | MS-PL / Apache-2.0 | |
| XML Auditfile | System.Xml (`XmlReader` + XSD-validatie) | MIT | Streaming |
| Tekstsjablonen met variabelen | Fluid (Liquid) of Scriban | MIT / BSD-2 | `{{ klant.naam }}`, `{% if klant.rechtsvorm == "BV" %}` |
| Rich-text-editor | TipTap (kern) / ProseMirror | MIT | **CKEditor 5 vermijden**: GPL of commercieel, met een verplichte licentiesleutel sinds v44 ([docs](https://ckeditor.com/docs/ckeditor5/latest/support/licensing/license-key-and-activation.html)) |
| Paginering HTML-rapport | Paged.js | MIT | Kop- en voetteksten, paginanummers, inhoudsopgave met paginanummers, keep-with-next |
| **HTML → PDF (server)** | **Playwright for .NET** (headless Chromium) | Apache-2.0 | Dezelfde Chromium-rendering als de preview; `@page`-margeboxen sinds Chromium 131 ([Chrome-blog](https://developer.chrome.com/blog/print-margins)) |
| PDF (alternatief) | QuestPDF | Community License (source-available, sinds 2026 geen MIT) | Gratis onder USD 1 mln omzet ([licentie](https://questpdf.com/license/community.html)) |
| **Word-export (.docx)** | DocumentFormat.OpenXml | MIT | Rechtstreeks uit het rapportmodel opgebouwd, niet via HTML, zodat de huisstijl en tabellen netjes blijven |
| Client-installer en updates | Velopack | MIT | Alleen als de optionele dunne Windows-app er komt ([docs](https://docs.velopack.io/getting-started/wpf)) |
| Logging | Serilog | Apache-2.0 | Nooit klantgegevens in logs |
| Tests | xUnit, bUnit, Verify, Playwright | Apache-2.0 / MIT | |

**Commerciële component-suites** (Syncfusion, DevExpress, Telerik): niet op bouwen. De gratis Syncfusion-licentie geldt alleen bij minder dan USD 1 mln omzet, maximaal 5 ontwikkelaars en maximaal 10 medewerkers.

## 7.5 Gegevensopslag

- **PostgreSQL** op de kantoorserver vanaf de eerste versie. Een SQLite-bestand is ongeschikt voor meerdere gelijktijdige gebruikers ([SQLite-forum](https://sqlite.org/forum/forumpost/6423b047ae1c8779f705d15a744a63a50ad8457ca205978ebee9353cf48fcac6?t=c)).
- **Bedragen** als `numeric(18,2)` of `bigint`-centen, in het domein altijd `decimal`.
- **Gelijktijdig werken**:
  - optimistische concurrency (row version) per dossieronderdeel;
  - een indicatie "Jan werkt nu in *Vorderingen*";
  - live verversen via Blazor Server/SignalR wanneer een collega iets afrondt of goedkeurt.
- **Bijlagen** staan in een *content-addressed* map op de server (bestandsnaam = SHA-256-hash), met de metadata in de database. Alternatief: in PostgreSQL zelf, zodat één back-up alles bevat. Dat is af te wegen op volume.
- **Back-up (ingebouwd, want er is geen ICT-partner):**
  - **automatisch elke nacht**: een databasedump plus de bijlagenmap, **versleuteld** (AES-256, met een sleutel die het kantoor bewaart), met rotatie per dag (14), week (8) en maand (12);
  - **doel 1**: een tweede schijf in de server-pc;
  - **doel 2**: twee externe USB-schijven die om de week wisselen. Eén ligt altijd buiten kantoor, tegen brand en diefstal;
  - **doel 3** (optioneel): dezelfde versleutelde back-up naar een cloudmap van het kantoor (bijv. OneDrive). Door de versleuteling zijn de gegevens daar niet leesbaar;
  - **beheerpagina**: status van de laatste back-up per doel. Is er langer dan 2 dagen geen geslaagde back-up, dan krijgt elke gebruiker een **banner**;
  - **herstellen** met een knop in de beheerpagina ("terugzetten naar de back-up van …"), plus een **hersteltest** van een paar minuten, twee keer per jaar, via een checklist.

## 7.6 Rapportgeneratie (één model, meerdere uitvoerformaten)

```
Rapportsjabloon (bouwstenen B01–B22, hfst. 9) ─┐
Dossier: klant, proefbalans, correcties,       ├─► Rapportmodel (C#) ─┬─► HTML  → preview in app
specificaties, verloopoverzichten, teksten,    │   hoofdstukken,      ├─► PDF   via Playwright/Chromium + Paged.js
historie (5 jaar), fiscale berekening          ┘   tabellen, teksten  └─► DOCX  via OpenXML (huisstijl-sjabloon .dotx)
```

- **Eén rapportmodel** is de bron voor preview, PDF en Word.
- **Afronding** op hele euro's in het rapportmodel, met verdeling van afrondingsverschillen zodat de optellingen kloppen. Ook verdelingen zoals de resultaatbestemming volgen een vaste afrondingsregel (zie [§9.5](09-analyse-voorbeeldrapporten.md#95-wat-de-voorbeelden-leren-over-kwaliteit-en-waar-wij-het-beter-doen)).
- **Voettekst per opdrachttype**: "Samenstellingsverklaring afgegeven", "Geen accountantscontrole toegepast" plus datum, of "CONCEPT".
- **Definitieve versie**: bij uitbrengen slaan we de PDF met hash en tijdstempel op in het dossier.
- **Word-export** is bedoeld voor nabewerking. Een in Word aangepaste versie is daarna *niet meer gelijk* aan het dossier. Daarom: wie Word als definitief gebruikt, uploadt de definitieve PDF terug in het dossier. Dit beleid staat in de [vragenlijst](11-vragenlijst.md).

## 7.7 Audit trail, integriteit en dossierafsluiting

- **Wijzigingslog** (append-only): wie, wanneer, wat, oude en nieuwe waarde. De database is alleen voor de server bereikbaar, dus de log is niet te omzeilen vanaf een werkplek.
- **Aftekeningen**: uitgevoerd door/op en goedgekeurd door/op per onderdeel en per stap. Een wijziging na goedkeuring zet de goedkeuring terug.
- **Dossier sluiten**: alleen-lezen na de sluittermijn. Heropenen alleen met een reden, of alleen een kopie maken (zie de vragenlijst).
- **Bewaartermijn** minimaal 7 jaar, met een archiefexport (zip met PDF's, bijlagen en JSON).

## 7.8 Inloggen, rechten en beveiliging

**Accounts**
- **De beheerder maakt de accounts aan**; er is geen zelfregistratie.
- Bij de eerste keer inloggen moet de gebruiker het wachtwoord wijzigen.
- Een wachtwoord resetten doet de beheerder. Per e-mail kan alleen als er een SMTP-server beschikbaar is.

**Wachtwoorden en sessies**
- **Wachtwoordbeleid**: minimaal 12 tekens, met een controle tegen veelgebruikte wachtwoorden.
- **Lockout** na 5 mislukte pogingen (15 minuten).
- **Hashing**: PBKDF2 via ASP.NET Core Identity. Wachtwoorden worden nooit leesbaar opgeslagen.
- **Optioneel tweestapsverificatie** met een authenticator-app (TOTP). Niet nodig in de MVP, omdat er alleen binnen kantoor gewerkt wordt.
- **Sessies** verlopen na een instelbare inactiviteit, bijv. 8 uur of 30 minuten.
- Inlogpogingen (geslaagd en mislukt) worden in de audit log vastgelegd.

*Gebouwd in plakje 2:*
- **Eerste gebruiker**: bij een lege database maakt de eerste bezoeker een account met alle rollen aan. Doe dit dus direct na de installatie.
- **Tijdelijk wachtwoord**: de beheerder maakt accounts aan en reset wachtwoorden. De app toont een tijdelijk wachtwoord (4 × 4 tekens) één keer; de gebruiker moet het bij de eerste keer inloggen wijzigen.
- **Beleid**: minstens 12 tekens, met een hoofdletter, een kleine letter en een cijfer, plus een lijst met verboden woorden (onder meer *wachtwoord*, *welkom*, seizoenen, en de eigen naam of het e-mailadres).
- **Lockout**: na 5 mislukte pogingen 15 minuten.
- **Sessie**: een cookie die vervalt na 10 uur zonder activiteit, en bij het sluiten van de browser.
- **Deactiveren**: gebruikers worden niet verwijderd maar gedeactiveerd. Na deactiveren, een rolwijziging of een wachtwoordreset is een open scherm binnen een minuut uitgelogd. De laatste actieve beheerder kan niet worden gedeactiveerd en niemand kan zijn eigen beheerdersrol intrekken.
- **Wijzigingslog**: alleen toevoegen, zichtbaar voor de beheerder. Het legt inloggen en uitloggen, gebruikersbeheer, klanten, dossiers en imports vast (met de SHA-256 van het bestand).

**Rollen**

| Rol | Mag |
|---|---|
| Medewerker | Dossiers bewerken, stappen uitvoeren en toevoegen, rapporten (concept) genereren |
| Reviewer | Plus reviewpunten plaatsen en posten als *gereviewd* markeren |
| Accountant (tekenbevoegd) | Plus dossier afronden, rapport definitief uitbrengen, verklaring ondertekenen (handtekeningafbeelding) |
| Beheerder | Gebruikers, sjablonen, rubriekenschema's, RGS, standaardteksten, huisstijl, parameters |

Een gebruiker kan meerdere rollen hebben. **Review is niet verplicht** (besluit A4): een dossier kan zonder review worden afgerond. Is er wel een reviewpunt geplaatst, dan moet dat eerst zijn opgelost. Of er toegang per klant nodig is, staat in de vragenlijst.

**Overige maatregelen**
- **HTTPS** ook op het interne netwerk, **zonder certificaatbeheer**:
  - de server-pc maakt bij de installatie zelf een certificaat aan;
  - de Windows-app op een werkplek **koppelt één keer** met de server (servernaam plus een koppelcode uit de beheerpagina) en vertrouwt daarna alleen dat certificaat.
- **AVG**:
  - BitLocker op de server;
  - minimale persoonsgegevens (geen BSN tenzij nodig);
  - het verwerkingsregister bijwerken;
  - logs zonder klantgegevens.
- **Geen toegang van buitenaf**: de server luistert alleen op het kantoornetwerk (firewallregel *lokaal subnet*). De database luistert alleen op de server-pc zelf.

## 7.9 Uitrol

**Server-pc** (er is geen server, dus die schaft het kantoor aan):

| Onderdeel | Advies (indicatie) |
|---|---|
| Hardware | Zakelijke mini-pc: recente quad-core, **16 GB RAM**, **2 SSD's** (1 TB voor data, 1 TB voor de lokale back-up). Ruim voldoende voor maximaal 5 gebruikers |
| Besturingssysteem | Windows 11 Pro, met BitLocker aan en Windows Update buiten kantoortijd. Controleer de licentievoorwaarden voor gebruik als server; alternatief Windows Server of Linux |
| Noodstroom (UPS) | Ja: voorkomt databaseschade bij stroomuitval en sluit netjes af |
| Back-upschijven | 2 externe USB-schijven (wisselen) |
| Plaats | Een afsluitbare ruimte, bekabeld aan het netwerk, altijd aan tijdens kantooruren (of continu) |
| Kosten | Ca. € 800–1.400 eenmalig, voor pc, UPS en schijven |

- **Eén installatieprogramma "Boekerhouden Server"** met een wizard, zonder ICT-kennis:
  - installeert PostgreSQL en de applicatie als Windows-service;
  - maakt de firewallregel en het certificaat aan;
  - stelt de back-up in en maakt de eerste beheerder aan.
- **Werkplekken (maximaal 5)**:
  - de server biedt een pagina `https://<server>/installeren` met een klein script dat het servercertificaat **eenmalig** vertrouwt op die werkplek;
  - daarna de app in Edge openen en *Als app installeren* kiezen (eigen venster en icoon in de taakbalk);
  - er is geen client-software nodig. Een eigen dunne Windows-app met koppelcode kan later nog.
- **Updates**:
  - de beheerder klikt op *Bijwerken* in de beheerpagina, of de update gebeurt automatisch 's nachts;
  - de app maakt vóór elke update een back-up, en migraties draaien automatisch;
  - werkplekken hoeven niet bijgewerkt te worden.
- **Code-signing-certificaat**: optioneel. Het serverinstallatieprogramma draait alleen op de eigen server-pc, dus een eenmalige SmartScreen-waarschuwing is acceptabel.
- **Beheerhandleiding van één A4**: aanzetten, back-upschijf wisselen, gebruiker toevoegen, wachtwoord resetten, wat te doen bij een melding.
- **Niet aanbevolen**: een gewone werkplek-pc als server gebruiken. Die moet dan altijd aan staan, en het werk van de gebruiker op die pc beïnvloedt de snelheid en de veiligheid.

## 7.10 Ontwikkel- en teststrategie

- **Bouwen met AI-ondersteuning** (Claude Code), door de opdrachtgever zelf:
  - in **kleine, werkende stappen** (verticale plakjes, zie [§8.4](08-roadmap-risico-s-en-beslispunten.md#84-roadmap-bouwen-in-plakjes));
  - elke stap met tests, en een pull request per stap;
  - conventies vastgelegd in een `CLAUDE.md` in de repository, zodat elke sessie dezelfde regels volgt;
  - de opdrachtgever beoordeelt vooral *uitkomsten* (klopt de proefbalans, klopt het rapport) en de vaktechniek. De code-kwaliteit bewaken tests en reviews.
- **Domeinregels eerst, met tests**: importsjabloon (saldo nihil), proefbalans, correcties, rubriceren, materiële posten, aansluitcontroles, afronding en Vpb-berekening.
- **Fictieve testdata eerst**: een eigen set verzonnen klanten (BV klein en eenmanszaak) met saldibalansen over 5 jaar en een verwachte uitkomst. Die mag ook in de repository staan.
- **Golden files later** (vóór de pilot): echte saldibalansen of XAF-bestanden met de huidige rapporten. Die staan buiten de repository. Onze uitkomst moet hetzelfde zijn, of een verklaarbaar verschil hebben.
- **Rapport-snapshots** (Verify): een gegenereerd rapport wordt vergeleken met de goedgekeurde versie.
- **End-to-end tests** (Playwright): inloggen, dossier aanmaken, importeren, koppelen, materieel aanvinken, stappen afronden, rapport genereren.
- **CI** (GitHub Actions): alle tests op Linux; het serverinstallatieprogramma bouwen op Windows.
