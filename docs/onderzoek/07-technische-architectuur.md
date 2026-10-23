# 7. Technische architectuur

> **Bijgewerkt op 23-10-2026** na het besluit *"meerdere gebruikers met wachtwoord"*. De
> eerdere aanpak (één gebruiker, SQLite-bestand) is vervangen door een **kantoorserver-model**.
> Openstaande keuzes staan in de [vragenlijst](11-vragenlijst.md) (vooral: werken medewerkers ook
> offline of buiten kantoor?).

## 7.1 Uitgangspunten

1. **On-premises Windows-oplossing voor meerdere gebruikers.** Iedere gebruiker logt in met een eigen naam en wachtwoord. Er is geen cloud en zijn geen API-koppelingen (die vallen buiten de scope).
2. **Eén centrale database.** Samensteller en reviewer werken tegelijk in hetzelfde dossier.
3. **Bedragen zijn altijd exact.** In de code `decimal`, nooit `double`/`float`. In de database staan bedragen als gehele eurocenten of `numeric`.
4. **Grote auditfiles kunnen geen kwaad.** Een XAF-bestand kan honderden MB zijn. We lezen het *streaming* in (XmlReader), niet als DOM.
5. **Inhoud is data, geen code.** Rubriekenschema's, werkprogramma's, standaardteksten en rapportsjablonen staan als beheerbare data in de app, met versies.
6. **Ontwikkelen en testen kan ook buiten Windows.** Alles behalve de dunne Windows-schil moet op Linux en in CI te bouwen en te testen zijn.

## 7.2 Architectuurkeuze voor meerdere gebruikers

| Model | Hoe | Voordelen | Nadelen |
|---|---|---|---|
| **A. Kantoorserver met webtechniek** ✅ | ASP.NET Core + **Blazor Server** draait als Windows-service op een kantoorserver (of VM), met **PostgreSQL** ernaast. Medewerkers openen de app in een **dunne Windows-app** (WebView2-venster met eigen icoon, geïnstalleerd met Velopack) of gewoon in Edge/Chrome | Meerdere gebruikers en inloggen zitten er standaard in (ASP.NET Core Identity). **De database is niet bereikbaar vanaf de werkplekken**, dus rechten en audit trail zijn niet te omzeilen. Eén plek om te updaten. PDF en Word worden centraal gemaakt. De UI-componenten (Razor) zijn dezelfde als in het eerdere ontwerp | Vereist een server die altijd aan staat. Buiten kantoor alleen via VPN of een beveiligde publicatie. Niet offline |
| B. Desktop-app met centrale database | Blazor Hybrid/WPF op elke pc, die rechtstreeks verbindt met PostgreSQL op de server | Werkt als een "echte" desktop-app. PDF via WebView2 op de pc | **Elke pc heeft databasewachtwoorden**, dus een technisch onderlegde gebruiker kan de app-rechten en audit trail omzeilen. Afvangen kost veel werk (databaserollen per gebruiker). Updates op elke pc. Buiten kantoor ook VPN nodig |
| C. Desktop-app + eigen applicatieserver (API) | WPF/Blazor Hybrid-client praat met een ASP.NET Core Web API | Veilig en een volwaardige desktop-app | **Het meeste bouwwerk**: een volledige API-laag erbij. Offline vraagt nog steeds synchronisatie |

**Advies: model A.**
- Het voldoet het eenvoudigst en veiligst aan "meerdere gebruikers met wachtwoord".
- Voor de gebruiker voelt het als een Windows-applicatie (eigen venster en icoon), terwijl het technisch één centrale installatie is.
- Omdat de UI al in Blazor (Razor-componenten) was gepland, is het verschil met het eerdere ontwerp vooral het *hostingmodel*. Domeinlogica, import, rubricering en rapportage blijven gelijk.
- Wil het kantoor later naar de cloud (bijv. een Azure-VM of hosting), dan kan dit model daar ongewijzigd heen.

> **Wanneer toch model B of C?** Als medewerkers regelmatig **zonder verbinding** met kantoor
> moeten werken, bijv. bij klanten zonder internet of VPN. Dat staat als vraag in de [vragenlijst](11-vragenlijst.md).

**.NET 10** (LTS, ondersteund tot november 2028). Bron: [.NET Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [Blazor (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid).

## 7.3 Oplossingsopbouw (projecten)

```
Boekerhouden.sln
├─ src/
│  ├─ Boekerhouden.Domain        // Entiteiten, rekenregels, aansluitcontroles (pure C#, geen I/O)
│  ├─ Boekerhouden.Import        // XAF 3.x/4.0-parser, Excel/CSV-proefbalans, RGS-referentietabel
│  ├─ Boekerhouden.Mapping       // Rubriceringsengine (handmatig → sjabloon → RGS → voorstel)
│  ├─ Boekerhouden.Workprogram   // Werkprogramma-engine, materialiteit, stappen (hfst. 10)
│  ├─ Boekerhouden.Reporting     // Rapportmodel, bouwstenen (hfst. 9), renderers HTML / PDF / DOCX
│  ├─ Boekerhouden.Data          // EF Core DbContext, migraties, PostgreSQL
│  ├─ Boekerhouden.UI            // Razor Class Library: alle schermen/componenten
│  ├─ Boekerhouden.Server        // ASP.NET Core: Blazor Server, Identity/inloggen, achtergrondtaken
│  └─ Boekerhouden.Client        // Dunne Windows-app: WebView2-venster naar de server, downloads/printen
└─ tests/
   ├─ Boekerhouden.Domain.Tests
   ├─ Boekerhouden.Import.Tests  // golden files: geanonimiseerde XAF's → verwachte proefbalans
   ├─ Boekerhouden.Mapping.Tests
   ├─ Boekerhouden.Workprogram.Tests
   ├─ Boekerhouden.Reporting.Tests // snapshot-tests van rapporten (Verify)
   └─ Boekerhouden.UI.Tests      // bUnit + Playwright end-to-end tegen de server
```

Alleen `Boekerhouden.Client` is Windows-specifiek. De server draait op Windows Server, maar kan ook op Linux. Alles bouwt en test in CI.

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
| Client-installer en updates | Velopack | MIT | Voor de dunne Windows-app ([docs](https://docs.velopack.io/getting-started/wpf)) |
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
- **Back-up**:
  - nachtelijke `pg_dump` plus de bijlagenmap, met rotatie (dag, week, maand) en een kopie buiten de server;
  - periodiek een **hersteltest**;
  - een melding in de beheerpagina als de laatste back-up te oud is.

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
- **Dossier sluiten**: alleen-lezen na de sluittermijn. Heropenen alleen met een reden, of zoals in Visionplanner alleen een kopie (zie de vragenlijst).
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
- **Optioneel tweestapsverificatie** met een authenticator-app (TOTP). Advies: verplicht bij toegang buiten kantoor.
- **Sessies** verlopen na een instelbare inactiviteit, bijv. 8 uur of 30 minuten.
- Inlogpogingen (geslaagd en mislukt) worden in de audit log vastgelegd.

**Rollen**

| Rol | Mag |
|---|---|
| Medewerker | Dossiers bewerken, stappen uitvoeren en toevoegen, rapporten (concept) genereren |
| Reviewer | Plus goedkeuren, reviewpunten, standaardstappen verwijderen |
| Accountant (tekenbevoegd) | Plus rapport definitief uitbrengen, verklaring ondertekenen (handtekeningafbeelding) |
| Beheerder | Gebruikers, sjablonen, rubriekenschema's, RGS, standaardteksten, huisstijl, parameters |

Een gebruiker kan meerdere rollen hebben. Of er toegang per klant of team nodig is, staat in de vragenlijst.

**Overige maatregelen**
- **HTTPS** ook op het interne netwerk, met een certificaat van een interne CA of Let's Encrypt via DNS.
- **AVG**:
  - BitLocker op de server;
  - minimale persoonsgegevens (geen BSN tenzij nodig);
  - het verwerkingsregister bijwerken;
  - logs zonder klantgegevens.
- **Toegang buiten kantoor**: alleen via VPN, of via een reverse proxy met verplichte tweestapsverificatie. Nooit de database open naar internet.

## 7.9 Uitrol

- **Server**:
  - één installatiepakket: de app als Windows-service, plus PostgreSQL;
  - migraties draaien automatisch bij een update;
  - een server met 4 cores en 8–16 GB RAM is ruim voldoende voor een klein kantoor (schatting).
- **Werkplekken**: de dunne Windows-app (Velopack, zonder adminrechten), of een snelkoppeling in Edge ("Als app installeren").
- **Code-signing-certificaat** voor de client-installer, tegen SmartScreen-waarschuwingen.
- **Updates**: de server wordt centraal bijgewerkt; de client werkt zichzelf bij via Velopack.

## 7.10 Ontwikkel- en teststrategie

- **Domeinregels eerst, met tests**: proefbalans, correcties, rubriceren, materialiteit, aansluitcontroles, afronding en Vpb-berekening.
- **Golden files**: 10–20 geanonimiseerde XAF-bestanden en de bijbehorende Visionplanner-rapporten. Onze uitkomst moet hetzelfde zijn, of een verklaarbaar verschil hebben.
- **Rapport-snapshots** (Verify): een gegenereerd rapport wordt vergeleken met de goedgekeurde versie.
- **End-to-end tests** (Playwright): inloggen, dossier aanmaken, importeren, koppelen, materieel aanvinken, stappen afronden, rapport genereren.
- **CI** (GitHub Actions): tests op Linux; client-installer bouwen en tekenen op Windows.
