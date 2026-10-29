# Boekerhouden – werkafspraken

Eigen applicatie voor het samenstellen van jaarrekeningen en het opstellen van winstaangiften
(BV klein en eenmanszaak; later BV micro en VOF). Ontwerp en besluiten: `docs/onderzoek/`
(start bij `docs/onderzoek/README.md`; roadmap in plakjes: §8.4).

## Bouwen en testen

```bash
dotnet build Boekerhouden.slnx
dotnet test Boekerhouden.slnx
dotnet format Boekerhouden.slnx --verify-no-changes   # opmaak/stijl
dotnet run --project src/Boekerhouden.Server           # webapp op http://localhost:5080
dotnet run --project src/Boekerhouden.Server -- --demo   # idem, gevuld met demo-account, RGS en klanten (README)
dotnet run --project src/Boekerhouden.Administratie    # administratiepakket op http://localhost:5090
dotnet run --project src/Boekerhouden.Cli -- controleer tests/Fictief/bv-voorbeeld-bakkerij/saldibalans-2025.csv
dotnet run --project src/Boekerhouden.Cli -- herken testdata/factuur.pdf --leer 4500 I21   # inkoopfactuur herkennen
dotnet ef migrations add <Naam> --project src/Boekerhouden.Data --output-dir Migraties   # na een modelwijziging
dotnet ef migrations add <Naam> --project src/Boekerhouden.Administratie --context AdministratieDbContext --output-dir Data/Migraties   # idem, administratiepakket
dotnet ef migrations add <Naam> --project src/Boekerhouden.Administratie --context SqliteAdministratieDbContext --output-dir Data/MigratiesSqlite   # en voor SQLite (Windows-app)
BOEKERHOUDEN_TEST_SQLITE=1 dotnet test tests/Boekerhouden.Administratie.Tests   # administratiepakket op SQLite
```

Warnings zijn errors (`TreatWarningsAsErrors`). In cloudsessies installeert `.claude/hooks/session-start.sh`
de .NET 10 SDK en PostgreSQL, met rol en database `boekerhouden` (wachtwoord `boekerhouden-dev`).
`Boekerhouden.Server.Tests` en `Boekerhouden.Administratie.Tests` maken per testklasse een eigen database aan en ruimen die op; een andere
server kies je met `BOEKERHOUDEN_TEST_DB` (bijv. `Host=...;Username=...;Password=...`). De server past
migraties bij het starten zelf toe. Tests tegen het echte RGS-werkboek draaien alleen als
`BOEKERHOUDEN_RGS_WERKBOEK` naar het bestand wijst (het werkboek staat niet in git).

## Structuur

- `src/Boekerhouden.Domain` – domein (saldibalans, bedragen en `BedragParser`; `Rgs/`: RGS-codes, boom en selectie per
  rechtsvorm; `Rubricering/`: proefbalans per post met omslag en saldering per eigen post, voorstellen; `Correcties/`: boekingscontrole (C08),
  effect op resultaat en eigen vermogen, gecorrigeerde saldi; `Doorlezen/`: signalen bij het doorlezen; `Rapport/`: rapportmodel en `RapportOpbouw` voor balans, W&V
  en specificaties, `RapportVariabelen` voor `{boekjaar}` e.d. in teksten, `Samenstellingsverklaring` voor de BV); geen I/O.
- `src/Boekerhouden.Boekhouding` – domein van het **administratiepakket** (ontwerp: `docs/administratie/README.md`), geen I/O:
  `Grootboek/` (rekeningschema met RGS-code en functie, dagboeken, journaal, grootboekkaart, proefbalans, balans en W&V),
  `Btw/` (btw-codes met aangifterubriek), `Relaties/` (IBAN, btw-nummer, namen vergelijken), `Facturen/` (facturen boeken,
  openstaande posten), `Bank/` (MT940, CAMT.053, bankboekingen), `Afletteren/`, `Regels/` (verwerkingsregels, leergeheugen,
  advies) en `Herkenning/` (factuurtekst, UBL, leverancier herkennen, inkoopvoorstel).
- `src/Boekerhouden.Documenten` – documenten van buiten lezen voor de herkenning: tekst uit PDF (PdfPig), UBL als los
  bestand of als bijlage; begrensd (grootte, pagina's), XML altijd zonder DTD; vooraf een virusscan met ClamAV (clamd),
  *fail closed*. Beveiliging en besluiten B1–B5: `docs/administratie/beveiliging.md`.
- `src/Boekerhouden.Administratie` – Blazor Server-app van het **administratiepakket** (eigen database
  `boekerhouden_administratie`): `Data/` (entiteiten, `AdministratieDbContext` met query filter per administratie,
  migraties met triggers die boekingen en het log onwijzigbaar maken), `Diensten/` (`Toegang` = de enige weg naar de
  gegevens van een administratie, met rechten per lidmaatschap; administraties, grootboek en boeken met hash-keten,
  gebruikers, tweestapsverificatie en passkeys, wijzigingslog; `InkoopDienst` = inlezen met virusscan, voorstel en
  (automatisch) boeken en de kopie in de documentmap; `BankDienst` = afschriften inlezen (saldo, aansluiting, dubbel), afletteren en op rekening boeken; `InrichtingDienst` = relaties, regels en instellingen van een
  administratie; `KantoorDienst` = kantoorinstellingen, alleen beheerder; `Werkomgeving` = webapp of lokaal),
  `Lokaal/` (`LokaleApp` = samenstelling voor de Windows-app: SQLite, lokale gebruiker zonder inloggen), `Identiteit/` (inloggen, beveiligingsheaders,
  tweestap verplicht voor de beheerder), `Components/` (schermen; accountpagina's statisch, de rest interactief).
  Negatieve toegangstests horen bij elke dienst. Geen inline scripts (CSP): JavaScript als los bestand in `wwwroot/`
  (`factuurviewer.js` = de PDF-viewer met gebied aanwijzen; pdf.js staat in `wwwroot/lib/pdfjs`, Apache-2.0).
- `src/Boekerhouden.Administratie.Windows` – de Windows-app voor de tests: WinForms met BlazorWebView (dezelfde
  schermen, geen webserver), SQLite in `%LOCALAPPDATA%`, geen inloggen. Bouwt op Linux (`EnableWindowsTargeting`),
  draait alleen op Windows. `scripts/maak-windows-app.ps1` / `.sh` maken de zip; de CI ook, en start hem op Windows.
- De database bestaat in twee smaken: PostgreSQL (`Data/Migraties`) en SQLite (`SqliteAdministratieDbContext`,
  `Data/MigratiesSqlite`). Na een modelwijziging in het administratiepakket beide migraties maken.
- `src/Boekerhouden.Huisstijl` – gedeelde huisstijl van beide apps (Bootstrap, `app.css`, logo) onder
  `_content/Boekerhouden.Huisstijl/`.
- `src/Boekerhouden.Import` – importsjabloon (.xlsx/.csv) inlezen en controleren (I1–I6, I11), sjabloon genereren,
  het officiële RGS-werkboek lezen (`RgsWerkboekLezer`).
- `src/Boekerhouden.Cli` – opdrachtregel om sjablonen te maken en bestanden te controleren.
- `src/Boekerhouden.Data` – EF Core + PostgreSQL: entiteiten, Identity (gebruikers, rollen), migraties (`Migraties/`).
- `src/Boekerhouden.Server` – Blazor Server-app: `Diensten/` (klanten, dossiers, import met I7/I9/I10, gebruikers,
  wijzigingslog, rubriceren, correcties, werkprogramma en review, doorlezen en bespreekpunten, bijlagen, signalen, kantoor en huisstijl, rapport en
  presentatie per klant, grondslagen; `DossierSaldi` = saldi van een dossier na correcties, met vorig jaar;
  `Jaarversies` = instellingen die gelden vanaf een boekjaar, nooit met terugwerkende kracht), `Werkprogramma/` (het meegeleverde standaardwerkprogramma), `Grondslagen/` (de
  meegeleverde bibliotheek: algemene toelichting en grondslagen, commercieel en fiscaal, met `{naam}` e.d.), `Identiteit/` (inloggen, wachtwoordbeleid), `Components/` (schermen; `Layout/HoofdLayout` = kopbalk met Klanten, Beheer en het gebruikersmenu, `BeheerLayout` = menu links bij beheer; accountpagina's statisch,
  de rest interactief; `Rapportweergave/` = het rapport als bewerkbare viewer, dezelfde component rendert de PDF),
  `wwwroot/navigatie.js` (werkprogramma en rapport openen op dezelfde post),
  `Demo/` (`--demo`: vult account, RGS-werkboek en klanten uit `tests/Fictief/` en `testdata/demo/`),
  `Export/` (correcties naar Excel en PDF; `BespreekverslagHtml` = bespreekverslag als PDF; `RapportHtml` = stijl en A4-document van het rapport; `PdfDienst` maakt
  PDF's met Edge/Chromium zonder venster, instellingen in sectie `Pdf`).
- `tests/` – xUnit-tests; `tests/Fictief/` – verzonnen testklanten (met `genereer.py`); `tests/Gedeeld/` – gedeelde
  testhulp, o.a. een klein uittreksel uit RGS 3.8 (`RgsTestWerkboek`).
- Pakketversies centraal in `Directory.Packages.props`; build-instellingen in `Directory.Build.props`.

## Regels

- **RGS is het rubriekenschema** (docs §12): koppel aan RGS-codes, wijzig of verzin geen codes; eigen indeling via
  eigen posten en namen. Het officiële werkboek en de taxonomie komen niet in de repository.
- **Nooit klantgegevens in de repository**: geen echte saldibalansen, rapporten, namen, adressen of
  bedragen van klanten. Echte referentiebestanden blijven buiten git (`/testdata/` staat in `.gitignore`).
  Testdata is verzonnen (`tests/Fictief/`).
- **Geen verwijzingen naar bestaande samenstelpakketten of leveranciers** in code, documentatie of commitberichten.
- **Bedragen**: altijd `decimal` (nooit `double`/`float`). Tekenconventie: debet positief, credit negatief.
  Opslaan en loggen exact; tonen in hele euro's (`Bedragen.Heel`), en sommen afronden met `Afronding.OpHeleEuros`
  zodat totalen blijven sluiten.
- **Rustige uitstraling** (docs §5.2): één regel per item, details (codes, bron, pad) in de tooltip, geen gekleurde
  badges of rode bedragen; status als `.status` (grijs; `.fout`/`.let-op` alleen als het nodig is), bevestigingen
  klein of in `.bericht`; actieknoppen op regels pas bij aanwijzen; zelden gebruikte opties achter een schakelaar. Geen uitlegteksten op de schermen: uitleg hoort in de documentatie
  (`Components/Pages/Hulp.razor`, `/hulp`), bereikbaar via het vraagteken rechtsonder dat naar het deel van de pagina wijst.
- **Taal**: domeinnamen, meldingen en documentatie in het Nederlands (bijv. `Saldibalans`, `Grootboeknummer`).
- **Wijzigingslog**: elke handeling die gegevens wijzigt, logt via `AuditDienst` (wie, wat, onderwerp).
- **Elke wijziging met tests**; bestaande tests blijven groen. Werk in kleine plakjes (zie `docs/onderzoek` §8.4).
- **Licenties**: alleen open-source pakketten (MIT/Apache e.d.). `SixLabors.Fonts` niet ophogen naar 2.x of
  hoger (andere licentie); controleer de licentie van nieuwe pakketten vóór toevoegen.
