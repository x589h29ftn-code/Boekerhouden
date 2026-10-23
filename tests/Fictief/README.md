# Fictieve testklanten

Alle namen en bedragen in deze map zijn **verzonnen**. Ze dienen als testdata zolang er geen
(buiten de repository bewaarde) echte testset is.

| Map | Klant | Rechtsvorm | Jaren | Bijzonderheden |
|---|---|---|---|---|
| `bv-voorbeeld-bakkerij` | Voorbeeld Bakkerij B.V. | BV klein | 2021–2025 | Duizendtalpunten, UTF-8 met BOM, vennootschapsbelasting, lening, jaarlijks dividend |
| `ez-schildersbedrijf` | Schildersbedrijf Voorbeeld | Eenmanszaak | 2021–2025 | Geen duizendtalpunten, UTF-8 zonder BOM, privéonttrekkingen/-stortingen, FOR |
| `fouten` | – | – | – | Bestanden die de import terecht weigert (saldo niet nihil, dubbel grootboeknummer) |

Opbouw van elke saldibalans (*voor afsluiting*):

- balansrekeningen tonen de eindstand; W&V-rekeningen het jaartotaal; het eigen vermogen de beginstand;
- jaren sluiten op elkaar aan (eigen vermogen, cumulatieve afschrijvingen, lening);
- de bankrekening is de sluitpost, zodat elk jaar exact op nihil sluit;
- `verwachting.csv` bevat per jaar het aantal rekeningen, het resultaat (winst positief) en het banksaldo.

Opnieuw genereren (vanuit de root van de repository): `python3 tests/Fictief/genereer.py`.
