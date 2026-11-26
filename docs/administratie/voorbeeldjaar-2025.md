# Voorbeeldjaar 2025 – om na te rekenen

Een verzonnen BV (Voorbeeld Rijwielen B.V., kwartaalaangifte btw) met een vast jaar aan boekingen. De uitkomsten
hieronder zijn met de hand uitgerekend; de test `Praktijk/VoorbeeldjaarTests` laat Sifer hetzelfde jaar verwerken
(langs dezelfde weg als op het scherm) en controleert elk bedrag tot op de cent. **Vraag aan de accountant:** reken de
uitkomsten (§3 t/m §6) na. Een afwijking is een fout in de berekening hieronder of in Sifer; in beide gevallen hoort de
test aangepast te worden.

Bedragen in euro's, exclusief btw tenzij anders vermeld. Tarieven 2025: btw 21% en 9%; vpb 19% tot € 200.000.

## 1. Beginbalans 1-1-2025

| Rekening | Debet | Credit |
|---|---:|---:|
| 1100 Bank | 30.000,00 | |
| 0500 Aandelenkapitaal | | 18.000,00 |
| 0510 Overige reserves | | 12.000,00 |

## 2. Het jaar

**Verkoop** (omzet op 8010 leveringen of 8000 diensten)

| | Datum | Klant | Regels | Btw | Incl. | Betaald |
|---|---|---|---|---:|---:|---|
| V1 | 15-01 | Fietsplan (NL) | 10 × 1.250,00 fietsen, 21% | 2.625,00 | 15.125,00 | 31-01 |
| V2 | 10-02 | Hotel (NL) | 20 × 45,00 onderhoud 9%; 1 × 1.500,00 fiets 21% | 81,00 + 315,00 | 2.796,00 | gecrediteerd |
| C1 | 15-05 | Hotel | creditfactuur bij V2 (alles) | −396,00 | −2.796,00 | verrekend met V2 |
| V3 | 05-04 | Radhaus Müller (DE) | 4 × 1.000,00, ICP (3b) | – | 4.000,00 | 20-05 |
| V4 | 12-05 | Fietsplan | 2 × 999,99 e-bike, 21% | 420,00 ¹ | 2.419,98 | 05-06 deel 1.000,00 |
| V5 | 15-08 | Velohaus (CH) | 3 × 1.800,00, uitvoer (3a) | – | 5.400,00 | 01-09 |
| V6 | 20-09 | Hotel | 30 × 45,00 onderhoud, 9% | 121,50 | 1.471,50 | open |
| V7 | 10-11 | Fietsplan | 5 × 1.250,00, 21% | 1.312,50 | 7.562,50 | open |

¹ 21% van 1.999,98 = 419,9958, afgerond 420,00.

**Inkoop**

| Nummer | Datum | Leverancier | Wat (rekening) | Btw | Incl. | Betaald |
|---|---|---|---|---:|---:|---|
| GH-2025-001 | 06-01 | Groothandel (NL) | fietsen 8.000,00 (7000), 21% | 1.680,00 | 9.680,00 | 20-01 |
| KC-118 | 03-03 | Koffie (NL) | koffie 120,00 (4900), 9% | 10,80 | 130,80 | open |
| WK-501 | 20-03 | Wolkkassa (IE) | software 600,00 (4310), dienst uit de EU (4b) | 126,00 verlegd | 600,00 | 28-03 |
| GH-2025-014 | 10-04 | Groothandel | werkbank 6.000,00 (0200), 21% | 1.260,00 | 7.260,00 | 30-04 |
| H-25-040 | 15-06 | Bouwbedrijf Hamer (NL) | verbouwing 2.000,00 (4900), verlegd (2a) | 420,00 verlegd | 2.000,00 | open |
| GH-2025-031 | 01-07 | Groothandel | fietsen 5.000,00 (7000), 21% | 1.050,00 | 6.050,00 | 31-07 |
| KC-170 | 15-12 | Koffie | koffie 240,00 (4900), 9% | 21,60 | 261,60 | open |

**Overig**

- Werkbank: aanschaf 10-04, € 6.000, geen restwaarde, 60 maanden lineair vanaf april: € 100 per maand, afgeschreven per
  kwartaal; 2025 = 9 maanden = € 900 (4800 / 0210).
- Loonjournaal 31-12: brutolonen 8.000,00 (4000), sociale lasten 1.500,00 (4010), loonheffing 2.000,00 (1520), nettolonen
  7.500,00 (1800), op 31-12 betaald.
- Lunch met relaties 20-06: € 300 per bank (4410 reis- en verblijfkosten, gemengde kosten), zonder btw-aftrek.
- Bankkosten: € 42,50 per kwartaal (9000).
- Btw: elke aangifte vastgelegd; 1e kwartaal betaald 25-04 (€ 1.330), teruggaaf 2e kwartaal ontvangen 20-07
  (€ 1.236), teruggaaf 3e kwartaal ontvangen 20-10 (€ 929); 4e kwartaal nog te betalen.

## 3. Btw-aangiften

Exact uit de boekingen; ingediend in hele euro's (omzet en btw naar beneden, voorbelasting naar boven).

| Rubriek | 1e kwartaal | 2e kwartaal | 3e kwartaal | 4e kwartaal |
|---|---|---|---|---|
| 1a (21%) | 14.000,00 / 2.940,00 | 499,98 / 105,00 ² | | 6.250,00 / 1.312,50 |
| 1b (9%) | 900,00 / 81,00 | −900,00 / −81,00 ² | 1.350,00 / 121,50 | |
| 2a (verlegd) | | 2.000,00 / 420,00 | | |
| 3a (uitvoer) | | | 5.400,00 | |
| 3b (ICP) | | 4.000,00 | | |
| 4b (diensten EU) | 600,00 / 126,00 | | | |
| 5a verschuldigd | 3.147,00 | 444,00 | 121,50 | 1.312,50 |
| 5b voorbelasting | 1.816,80 | 1.680,00 | 1.050,00 | 21,60 |
| Te betalen (exact) | 1.330,20 | −1.236,00 | −928,50 | 1.290,90 |
| Ingediend (hele euro's) | 1.330 | −1.236 | −929 | 1.290 |

² Inclusief de creditfactuur C1 (−1.500 / −315 in 1a en −900 / −81 in 1b): een creditfactuur telt in het kwartaal van
de creditfactuur, niet in dat van de oorspronkelijke factuur.

## 4. Winst-en-verliesrekening 2025

| Rekening | Bedrag |
|---|---:|
| 8010 Omzet leveringen | 30.149,98 |
| 8000 Omzet diensten | 1.350,00 |
| **Omzet** | **31.499,98** |
| 7000 Inkoopwaarde | 13.000,00 |
| 4000 Lonen | 8.000,00 |
| 4010 Sociale lasten | 1.500,00 |
| 4310 Telefoon en internet | 600,00 |
| 4410 Reis- en verblijfkosten | 300,00 |
| 4800 Afschrijving inventaris | 900,00 |
| 4900 Algemene kosten (120 + 2.000 + 240) | 2.360,00 |
| 9000 Rente en kosten bank | 170,00 |
| 4910 Afrondingsverschillen btw (0,20 + 0,50 + 0,90) | −1,60 |
| **Kosten** | **26.828,40** |
| **Resultaat voor belastingen** | **4.671,58** |

## 5. Balans 31-12-2025

| Activa | | Passiva | |
|---|---:|---|---:|
| 0200 Inventaris | 6.000,00 | 0500 Aandelenkapitaal | 18.000,00 |
| 0210 Afschrijving inventaris | −900,00 | 0510 Overige reserves | 12.000,00 |
| 1100 Bank | 24.800,00 | Resultaat 2025 | 4.671,58 |
| 1300 Debiteuren | 10.453,98 | 1600 Crediteuren | 2.392,40 |
| 1500 Te vorderen omzetbelasting | 4.568,40 | 1510 Te betalen omzetbelasting | 5.858,40 |
| | | 1520 Te betalen loonheffing | 2.000,00 |
| **Totaal** | **44.922,38** | **Totaal** | **44.922,38** |

Btw per saldo te betalen: 5.858,40 − 4.568,40 = 1.290,00 = het ingediende 4e kwartaal. Wat het afronden op hele euro's
scheelt (1e kwartaal 0,20, 3e 0,50, 4e 0,90) boekt Sifer bij het vastleggen van de aangifte naar 4910.

**Openstaande posten 31-12-2025**

- Debiteuren: V4 1.419,98, V6 1.471,50, V7 7.562,50 = 10.453,98.
- Crediteuren: KC-118 130,80, H-25-040 2.000,00, KC-170 261,60 = 2.392,40.

## 6. Vennootschapsbelasting 2025

| | |
|---|---:|
| Resultaat voor belastingen | 4.671,58 |
| Bij: gemengde kosten, niet aftrekbaar: 26,5% van 300 (lager dan de drempel van € 5.700) | 79,50 |
| Af: kleinschaligheidsinvesteringsaftrek, 28% van 6.000 | −1.680,00 |
| **Belastbaar bedrag** | **3.071,08** |
| Afgerond op € 5 naar beneden | 3.070 |
| Vennootschapsbelasting 19%, naar beneden op hele euro's (583,30) | **583** |
| Betaalde voorlopige aanslagen | 0 |
| Nog te betalen | 583 |

## Wat het voorbeeldjaar al opleverde

- Verlegde btw bij inkoop (2a en 4b) viel weg als het btw-veld 0 bevatte, zoals op de factuur van de leverancier. Nu
  berekent Sifer de verlegde btw dan toch (`Factuur.BtwVan`); test `FactuurTests.Verlegde_btw_met_nul_…`.
