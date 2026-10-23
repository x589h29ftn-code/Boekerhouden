# Boekerhouden – werkafspraken

Eigen applicatie voor het samenstellen van jaarrekeningen en het opstellen van winstaangiften
(BV klein en eenmanszaak; later BV micro en VOF). Ontwerp en besluiten: `docs/onderzoek/`
(start bij `docs/onderzoek/README.md`; roadmap in plakjes: §8.4).

## Bouwen en testen

```bash
dotnet build Boekerhouden.slnx
dotnet test Boekerhouden.slnx
dotnet format Boekerhouden.slnx --verify-no-changes   # opmaak/stijl
dotnet run --project src/Boekerhouden.Cli -- sjabloon uit.xlsx --boekjaar 2025
dotnet run --project src/Boekerhouden.Cli -- controleer tests/Fictief/bv-voorbeeld-bakkerij/saldibalans-2025.csv
```

Warnings zijn errors (`TreatWarningsAsErrors`). In cloudsessies installeert `.claude/hooks/session-start.sh`
de .NET 10 SDK.

## Structuur

- `src/Boekerhouden.Domain` – domein (saldibalans, bedragen); geen I/O.
- `src/Boekerhouden.Import` – importsjabloon (.xlsx/.csv) inlezen en controleren (I1–I6, I11), sjabloon genereren.
- `src/Boekerhouden.Cli` – opdrachtregel om sjablonen te maken en bestanden te controleren.
- `tests/` – xUnit-tests; `tests/Fictief/` – verzonnen testklanten (met `genereer.py`).
- Pakketversies centraal in `Directory.Packages.props`; build-instellingen in `Directory.Build.props`.

## Regels

- **Nooit klantgegevens in de repository**: geen echte saldibalansen, rapporten, namen, adressen of
  bedragen van klanten. Echte referentiebestanden blijven buiten git (`/testdata/` staat in `.gitignore`).
  Testdata is verzonnen (`tests/Fictief/`).
- **Geen verwijzingen naar bestaande samenstelpakketten of leveranciers** in code, documentatie of commitberichten.
- **Bedragen**: altijd `decimal` (nooit `double`/`float`). Tekenconventie: debet positief, credit negatief.
- **Taal**: domeinnamen, meldingen en documentatie in het Nederlands (bijv. `Saldibalans`, `Grootboeknummer`).
- **Elke wijziging met tests**; bestaande tests blijven groen. Werk in kleine plakjes (zie `docs/onderzoek` §8.4).
- **Licenties**: alleen open-source pakketten (MIT/Apache e.d.). `SixLabors.Fonts` niet ophogen naar 2.x of
  hoger (andere licentie); controleer de licentie van nieuwe pakketten vóór toevoegen.
