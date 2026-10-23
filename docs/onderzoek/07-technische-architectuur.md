# 7. Technische architectuur (Windows, standalone)

> Dit hoofdstuk beschrijft hoe we de applicatie technisch opzetten. Het is een advies;
> de knopen die jij moet doorhakken staan in [hoofdstuk 8](08-roadmap-risico-s-en-beslispunten.md).

## 7.1 Uitgangspunten

1. **Windows 10/11-desktop, werkt offline.** Er is geen cloud nodig en er zijn geen API-koppelingen (die vallen buiten de scope).
2. **Bedragen zijn altijd exact.** In de code gebruiken we `decimal` en nooit `double`/`float`. In de database staan bedragen als gehele eurocenten.
3. **Grote auditfiles kunnen geen kwaad.** Een XAF-bestand kan honderden MB groot zijn. We lezen het daarom *streaming* in (XmlReader) en laden het niet als DOM in het geheugen.
4. **Inhoud is data, geen code.** Rubriekenschema's, werkprogramma's, standaardteksten en rapportsjablonen staan als beheerbare data in de applicatie (met versies). Zo kan een vaktechnisch beheerder ze aanpassen zonder programmeur.
5. **We kunnen later groeien.** Van één gebruiker naar een kantoorserver met meerdere gebruikers, en eventueel naar een webversie, zonder het domein opnieuw te schrijven.
6. **Ontwikkelen en testen kan ook buiten Windows.** Dat is belangrijk als je (deels) met Claude Code in een cloudomgeving werkt, want die draait op Linux. Alles behalve de dunne Windows-schil moet daar te bouwen en te testen zijn.

## 7.2 Keuze UI-technologie

| Optie | Voordelen | Nadelen |
|---|---|---|
| **.NET 10 + WPF (klassiek XAML)** | Volwassen en Windows-native, veel kennis beschikbaar, goede datagrids. | Een rich-text-editor en rapportweergave (zoals in de screenshots) zijn zonder dure commerciële componenten lastig. Rapportpreview en PDF worden twee aparte werelden. UI alleen te testen op Windows. |
| **.NET 10 + WPF-schil met Blazor Hybrid (`BlazorWebView`/WebView2)** ✅ | De UI is HTML/CSS, en dat past bij de documentachtige schermen van Visionplanner (boom, tabellen, tekstblokken, rapportweergave). Bewezen open-source editors (TipTap/ProseMirror) zijn bruikbaar. Preview en PDF gebruiken dezelfde rendering. Domein en UI blijven in C#. Componenten zijn later herbruikbaar in een webversie. UI is ook in een browser te testen (Playwright). | Vereist de WebView2-runtime (standaard aanwezig op Windows 11, meestal ook op 10). Er komt een klein beetje JavaScript bij (editor, paginering). |
| WinUI 3 | Moderne Fluent-look | Dunner ecosysteem voor data-intensieve LOB-apps en Windows App SDK-afhankelijkheid. Rich-text-probleem blijft. |
| Avalonia | Cross-platform en lijkt op WPF | Cross-platform is niet nodig. Rich-text-probleem blijft. |
| Electron/Tauri + TypeScript | Groot web-ecosysteem | JavaScript kent geen exacte decimale getallen (aparte library nodig), zwaarder, geen .NET-bibliotheken voor XML/Excel/Word. |

**Advies: .NET 10 (LTS, ondersteund tot november 2028) met een dunne WPF-schil die Blazor Hybrid host.**
.NET 8 en 9 lopen in november 2026 uit ondersteuning, dus begin je nu met .NET 10.
Bron: [.NET Support Policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core), [Blazor Hybrid (Microsoft Learn)](https://learn.microsoft.com/en-us/aspnet/core/blazor/hybrid).

## 7.3 Oplossingsopbouw (projecten)

```
Boekerhouden.sln
├─ src/
│  ├─ Boekerhouden.Domain        // Entiteiten, rekenregels, aansluitcontroles (pure C#, geen I/O)
│  ├─ Boekerhouden.Import        // XAF 3.x-parser, Excel/CSV-proefbalans, RGS-referentietabel
│  ├─ Boekerhouden.Mapping       // Rubriceringsengine (sjabloon → RGS → handmatig → voorstellen)
│  ├─ Boekerhouden.Reporting     // Rapportmodel, sjablonen (Liquid), renderers HTML / PDF / DOCX
│  ├─ Boekerhouden.Data          // EF Core DbContext, migraties, SQLite (later PostgreSQL)
│  ├─ Boekerhouden.UI            // Razor Class Library: alle schermen/componenten
│  ├─ Boekerhouden.Desktop       // WPF-host: venster, BlazorWebView, bestandsdialogen, PDF-print
│  └─ Boekerhouden.DevHost       // (optioneel) ASP.NET Core-host om de UI in een browser te draaien
└─ tests/
   ├─ Boekerhouden.Domain.Tests
   ├─ Boekerhouden.Import.Tests  // "golden files": geanonimiseerde XAF's → verwachte proefbalans
   ├─ Boekerhouden.Mapping.Tests
   ├─ Boekerhouden.Reporting.Tests // snapshot-tests van gegenereerde rapporten (Verify)
   └─ Boekerhouden.UI.Tests      // bUnit + Playwright tegen DevHost
```

Alleen `Boekerhouden.Desktop` is Windows-specifiek. De rest bouwt en test op Windows, Linux en CI.

## 7.4 Bibliotheken en licenties

| Doel | Bibliotheek | Licentie | Opmerking |
|---|---|---|---|
| Database-toegang | EF Core + Microsoft.Data.Sqlite | MIT | Later Npgsql (PostgreSQL-licentie) of SQL Server-provider |
| Excel lezen/schrijven (.xlsx) | ClosedXML | MIT | Import proefbalans en export specificaties |
| Oude Excel (.xls) | ExcelDataReader | MIT | Alleen lezen |
| CSV | CsvHelper | MS-PL / Apache-2.0 | Scheidingsteken- en encodingdetectie |
| XML Auditfile | System.Xml (`XmlReader` + XSD-validatie) | .NET (MIT) | Streaming, valideren tegen de officiële XSD |
| Tekstsjablonen met variabelen | Fluid (Liquid-syntax) of Scriban | MIT / BSD-2 | Veilig (sandboxed), door beheerder te bewerken: `{{ klant.naam }}`, `{% if klant.rechtsvorm == "BV" %}` |
| Rich-text-editor | TipTap (kern) / ProseMirror | MIT | TipTap Pro-extensies zijn commercieel, die hebben we niet nodig. **CKEditor 5 vermijden**: dat is GPL of commercieel, en voor GPL-gebruik is sinds v44 ook een licentiesleutel verplicht ([CKEditor docs](https://ckeditor.com/docs/ckeditor5/latest/support/licensing/license-key-and-activation.html)). |
| Paginering HTML-rapport | Paged.js | MIT | Kop- en voetteksten, paginanummers, inhoudsopgave met paginanummers |
| HTML → PDF | WebView2 `PrintToPdfAsync` | Onderdeel van Windows/WebView2 | Geen extra licentie. Chromium ondersteunt sinds versie 131 `@page`-margeboxen met `counter(page)`/`counter(pages)` ([Chrome-blog](https://developer.chrome.com/blog/print-margins)). |
| PDF (alternatief, code-gedreven) | QuestPDF | **Community License, source-available, sinds 2026 geen MIT meer** | Gratis onder USD 1 mln jaaromzet, anders betaald ([QuestPDF licentie](https://questpdf.com/license/community.html)) |
| Word-export (.docx) | DocumentFormat.OpenXml (+ evt. HtmlToOpenXml) | MIT | Voor kantoren die een rapport nog in Word willen nabewerken |
| Installer en updates | Velopack | MIT | Per-gebruiker installeren zonder adminrechten, delta-updates ([docs](https://docs.velopack.io/getting-started/wpf)) |
| Logging | Serilog | Apache-2.0 | Nooit klantgegevens in logs |
| Tests | xUnit, bUnit, Verify, Playwright | Apache-2.0 / MIT | |

**Commerciële component-suites** (Syncfusion, DevExpress, Telerik) bieden kant-en-klare grids, rich-text en Word/PDF-componenten. Syncfusion heeft een gratis Community License, maar alleen bij minder dan USD 1 mln omzet, maximaal 5 ontwikkelaars en maximaal 10 medewerkers, en Syncfusion beoordeelt dat zelf ([bron](https://syncfusion.com/sales/products/winui)). Advies: niet op bouwen. Een kantoor groeit snel boven die grens uit, en dan zit je vast aan een jaarlicentie per ontwikkelaar.

## 7.5 Gegevensopslag

### Fase 1 – één gebruiker per database
- **SQLite**: één databasebestand plus een map `bijlagen/`, met EF Core-migraties.
- **Niet** met meerdere gebruikers tegelijk via een netwerkshare gebruiken. SQLite raadt dat zelf af, omdat file-locking over SMB tot corruptie kan leiden ([SQLite-forum](https://sqlite.org/forum/forumpost/6423b047ae1c8779f705d15a744a63a50ad8457ca205978ebee9353cf48fcac6?t=c)).
- **Bedragen als `long` (eurocenten)** via een EF Core value converter. SQLite kent geen decimal, en EF Core kan op `decimal`-kolommen in SQLite niet server-side sorteren, vergelijken of optellen ([EF Core SQLite limitations](https://learn.microsoft.com/en-us/ef/core/providers/sqlite/limitations)).
  Voor percentages en rentes gebruiken we een aparte decimal-kolom, als tekst opgeslagen.

### Fase 2 – kantoor met meerdere gebruikers (samensteller én reviewer)
- **PostgreSQL** (gratis) of **SQL Server Express** (gratis, maximaal 10 GB per database) op een kantoorserver of NAS-VM, met hetzelfde EF Core-model.
- Vermijd daarom nu al SQLite-specifieke SQL. Alle queries lopen via EF Core/LINQ en worden met beide providers getest.
- Optimistische concurrency (row version) op dossieronderdelen, plus "dossier in gebruik door …"-indicatie.

### Bijlagen
- Bestanden staan in een *content-addressed* map (bestandsnaam = SHA-256-hash) en de metadata staat in de database (originele naam, type, gekoppeld aan welk dossieronderdeel, geüpload door/op).
- Zo kan de hash aantonen dat een bijlage in een gesloten dossier niet meer gewijzigd is.

## 7.6 Rapportgeneratie (één model, meerdere uitvoerformaten)

```
Rapportsjabloon (data, versie)  ─┐
Dossier: klant, proefbalans,     ├─►  Rapportmodel (C#)  ─┬─► HTML  → preview in app
correcties, specificaties,       │    - hoofdstukken       │          → PDF via WebView2 + Paged.js
teksten, instellingen           ─┘    - tabellen (afgerond)└─► DOCX  via OpenXML
                                      - teksten (variabelen ingevuld)
```

- **Eén rapportmodel** is de bron voor preview, PDF en Word, zodat de uitvoerformaten niet uit elkaar lopen.
- **Afronden** op hele euro's gebeurt in het rapportmodel, met verdeling van afrondingsverschillen ("largest remainder") zodat optellingen in het rapport kloppen. De onderliggende administratie blijft in centen.
- **Watermerk "CONCEPT"** staat op elk rapport totdat het dossieronderdeel *Uitbrengen rapport* is goedgekeurd.
- **Definitieve versie**: bij uitbrengen slaan we de PDF met hash en tijdstempel op in het dossier, als bewijs van wat er is uitgebracht.

## 7.7 Audit trail, integriteit en dossierafsluiting

- **Wijzigingslog** (append-only): wie, wanneer, welk object, oude en nieuwe waarde. Dit is nodig voor reviews en kwaliteitstoetsing.
- **Aftekeningen**: *uitgevoerd door/op* en *goedgekeurd door/op* per onderdeel. Een inhoudelijke wijziging na goedkeuring zet de goedkeuring terug (onderdeel wordt weer "open"). Zo werkt Visionplanners "Goedgekeurd door … op …" ook (zie [hoofdstuk 1](01-hoe-werkt-visionplanner-compilation.md)).
- **Dossier sluiten**: na sluiten is alles alleen-lezen. Heropenen kan alleen met een reden en wordt gelogd. Het sluiten moet binnen de termijn uit de beroepsregels gebeuren (zie [hoofdstuk 2](02-vaktechnisch-kader.md)).
- **Bewaartermijn**: dossiers worden minimaal 7 jaar bewaard. Er komt een archieffunctie: een gesloten dossier exporteren als één bestand (zip met PDF's, bijlagen en een JSON-export).

## 7.8 Beveiliging en AVG

- **Gebruikers**: in fase 1 koppelen we de Windows-gebruikersnaam aan een gebruiker in de app (rollen: medewerker, reviewer/accountant, beheerder). We slaan zelf geen wachtwoorden op.
- **Versleuteling**: BitLocker op alle laptops en desktops (organisatorische maatregel). Databaseversleuteling (SQLCipher) is optioneel; check vooraf de licentie van de gebruikte build.
- **Dataminimalisatie**: geen BSN's opslaan tenzij echt nodig. Relatiegegevens (debiteuren en crediteuren) uit de XAF alleen importeren als ze voor specificaties nodig zijn.
- **Back-ups**: automatische back-up (`VACUUM INTO` / SQLite online backup) bij afsluiten, met rotatie, plus een melding als de laatste back-up te oud is.
- **Logs**: alleen technische informatie, geen bedragen, namen of omschrijvingen.

## 7.9 Uitrol

- **Velopack** voor installatie en automatische updates vanuit een kantoorshare of webserver.
- **Code-signing-certificaat** (bijvoorbeeld Azure Artifact/Trusted Signing of een OV-certificaat), zodat Windows SmartScreen geen waarschuwing geeft.
- **WebView2 Evergreen Runtime** controleren bij installatie (bootstrapper meeleveren voor oudere Windows 10-machines).
- **Data buiten de installatiemap**: database en instellingen staan in `%LocalAppData%\Boekerhouden\` of in een door de gebruiker gekozen map, nooit in de installatiemap. Velopack vervangt die map bij een update.

## 7.10 Ontwikkel- en teststrategie

- **Domeinregels eerst, met tests**: proefbalans opbouwen, correcties, rubriceren, aansluitcontroles en afronding zijn allemaal pure functies met unit-tests.
- **Golden files**: verzamel 10 à 20 *geanonimiseerde* XAF-bestanden en Excel-proefbalansen uit verschillende pakketten (Exact, Twinfield, SnelStart, AFAS, e-Boekhouden, Moneybird) met de verwachte proefbalans en rubricering. Elke wijziging wordt hiertegen getest.
- **Rapport-snapshots**: een gegenereerd rapport (HTML) voor een vaste testklant wordt vergeleken met een goedgekeurde versie (Verify). Zo zie je onbedoelde verschillen direct.
- **CI** (GitHub Actions): tests op `ubuntu-latest`, installer bouwen en tekenen op `windows-latest`.
