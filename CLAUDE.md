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
dotnet run --project src/Boekerhouden.Cli -- controleer tests/Fictief/bv-voorbeeld-bakkerij/saldibalans-2025.csv
dotnet ef migrations add <Naam> --project src/Boekerhouden.Data --output-dir Migraties   # na een modelwijziging
```

Warnings zijn errors (`TreatWarningsAsErrors`). In cloudsessies installeert `.claude/hooks/session-start.sh`
de .NET 10 SDK en PostgreSQL, met rol en database `boekerhouden` (wachtwoord `boekerhouden-dev`).
`Boekerhouden.Server.Tests` maakt per testklasse een eigen database aan en ruimt die op; een andere
server kies je met `BOEKERHOUDEN_TEST_DB` (bijv. `Host=...;Username=...;Password=...`). De server past
migraties bij het starten zelf toe. Tests tegen het echte RGS-werkboek draaien alleen als
`BOEKERHOUDEN_RGS_WERKBOEK` naar het bestand wijst (het werkboek staat niet in git).

## Structuur

- `src/Boekerhouden.Domain` – domein (saldibalans, bedragen en `BedragParser`; `Rgs/`: RGS-codes, boom en selectie per
  rechtsvorm; `Rubricering/`: proefbalans per post met omslag, voorstellen; `Correcties/`: boekingscontrole (C08),
  effect op resultaat en eigen vermogen, gecorrigeerde saldi); geen I/O.
- `src/Boekerhouden.Import` – importsjabloon (.xlsx/.csv) inlezen en controleren (I1–I6, I11), sjabloon genereren,
  het officiële RGS-werkboek lezen (`RgsWerkboekLezer`).
- `src/Boekerhouden.Cli` – opdrachtregel om sjablonen te maken en bestanden te controleren.
- `src/Boekerhouden.Data` – EF Core + PostgreSQL: entiteiten, Identity (gebruikers, rollen), migraties (`Migraties/`).
- `src/Boekerhouden.Server` – Blazor Server-app: `Diensten/` (klanten, dossiers, import met I7/I9/I10, gebruikers,
  wijzigingslog, rubriceren, correcties; `DossierSaldi` = saldi van een dossier na correcties, met vorig jaar), `Identiteit/` (inloggen, wachtwoordbeleid), `Components/` (schermen; accountpagina's statisch,
  de rest interactief).
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
  klein of in `.bericht`; actieknoppen op regels pas bij aanwijzen; zelden gebruikte opties achter een schakelaar.
- **Taal**: domeinnamen, meldingen en documentatie in het Nederlands (bijv. `Saldibalans`, `Grootboeknummer`).
- **Wijzigingslog**: elke handeling die gegevens wijzigt, logt via `AuditDienst` (wie, wat, onderwerp).
- **Elke wijziging met tests**; bestaande tests blijven groen. Werk in kleine plakjes (zie `docs/onderzoek` §8.4).
- **Licenties**: alleen open-source pakketten (MIT/Apache e.d.). `SixLabors.Fonts` niet ophogen naar 2.x of
  hoger (andere licentie); controleer de licentie van nieuwe pakketten vóór toevoegen.
