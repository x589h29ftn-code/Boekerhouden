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

Plakje 6 van 10 is gereed: een werkende webapp. Je logt in, beheert gebruikers, maakt klanten en
dossiers aan en leest saldibalansen in (met controles, onder meer saldo nihil), met het vorige jaar
ernaast. Daarna rubriceer je op **RGS** (Referentie GrootboekSchema): grootboekrekeningen slepen
naar een post, eigen posten toevoegen en posten hernoemen voor de jaarrekening, met voorstellen en
een kantoorsjabloon dat dezelfde rekeningen bij de volgende klant automatisch koppelt. In het
dossier boek je correcties (altijd in evenwicht, te exporteren naar Excel en PDF) en bekijk je de
proefbalans per post met administratie, correcties, vorig jaar en verschil, tot op de boeking. Het
werkprogramma per dossier volgt het kantoorsjabloon: materiële posten kiezen, stappen aftekenen,
reviewpunten en bijlagen; het klantenoverzicht toont per klant de voortgang en wat aandacht vraagt.
Het rapport (jaarrekening voor de BV, bijlage bij de aangifte IB voor de eenmanszaak) controleer en
bewerk je in een viewer in de app: posten in- en uitklappen, namen en toelichtingen aanpassen, rekeningen
uitsplitsen met een controletelling. Bij een BV kies je commerciële of fiscale grondslagen; de algemene toelichting
en de passende grondslagen staan dan standaard bij de posten met saldo. Een eigen post zoals de omzetbelasting kan
per jaar worden gesaldeerd, zodat een te vorderen saldo vanzelf onder de vorderingen staat. De concept-PDF in de
huisstijl van het kantoor is precies wat je daar ziet.
Alles wordt vastgelegd in een wijzigingslog. Ontwerp, besluiten en roadmap: [`docs/onderzoek`](docs/onderzoek/README.md).

## Snel proberen

Vereist de [.NET 10 SDK](https://dotnet.microsoft.com/) en PostgreSQL 16 met een rol `boekerhouden`
(wachtwoord `boekerhouden-dev`, mag databases aanmaken) en een database `boekerhouden`. In
Claude Code-cloudsessies regelt `.claude/hooks/session-start.sh` dat.

```bash
dotnet test Boekerhouden.slnx                          # alle tests (servertests maken eigen testdatabases)
dotnet run --project src/Boekerhouden.Server           # http://localhost:5080
```

Bij de eerste keer openen vraagt de app om het eerste account; dat krijgt alle rollen. Lees daarna
via *RGS* het officiële RGS-werkboek in (download: Kennisbank op referentiegrootboekschema.nl; het
werkboek staat niet in deze repository). Verzonnen saldibalansen om in te lezen staan in `tests/Fictief/`.

De opdrachtregel werkt zonder database:

```bash
dotnet run --project src/Boekerhouden.Cli -- sjabloon importsjabloon.xlsx --boekjaar 2025
dotnet run --project src/Boekerhouden.Cli -- controleer tests/Fictief/bv-voorbeeld-bakkerij/saldibalans-2025.csv
dotnet run --project src/Boekerhouden.Cli -- rgs "RGS 3.8-def.xlsx"      # controleert een RGS-werkboek
```

Werkafspraken voor ontwikkeling (ook voor AI-sessies): [`CLAUDE.md`](CLAUDE.md).
