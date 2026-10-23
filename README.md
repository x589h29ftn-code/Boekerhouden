# Boekerhouden

Een eigen applicatie voor het samenstellen van jaarrekeningen en financiële rapporten, voor het
MKB: eerst BV klein (samenstellen jaarrekening) en eenmanszaak (winstaangifte), later ook BV micro en VOF.
Het werkt zo:

1. Je importeert een saldibalans via het standaard importsjabloon of een XAF-auditfile.
2. Je rubriceert de grootboekrekeningen naar jaarrekeningposten (via sjabloon, RGS of handmatig).
3. Je werkt de materiële posten uit volgens het eigen kantoorwerkprogramma.
4. Je maakt het rapport in PDF en Word.

Het draait op een eigen server-pc op kantoor, met meerdere gebruikers.

## Status

Plakje 1 van 10 is gereed: het importsjabloon inlezen en controleren (onder meer saldo nihil), met
verzonnen testklanten. Ontwerp, besluiten en roadmap: [`docs/onderzoek`](docs/onderzoek/README.md).

## Snel proberen

Vereist de [.NET 10 SDK](https://dotnet.microsoft.com/).

```bash
dotnet test Boekerhouden.slnx
dotnet run --project src/Boekerhouden.Cli -- sjabloon importsjabloon.xlsx --boekjaar 2025
dotnet run --project src/Boekerhouden.Cli -- controleer tests/Fictief/bv-voorbeeld-bakkerij/saldibalans-2025.csv
dotnet run --project src/Boekerhouden.Cli -- controleer tests/Fictief/fouten/saldo-niet-nihil.csv
```

Werkafspraken voor ontwikkeling (ook voor AI-sessies): [`CLAUDE.md`](CLAUDE.md).
