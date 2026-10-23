"""Genereert fictieve saldibalansen (importsjabloon-formaat, CSV) voor tests.

Alle namen en bedragen zijn verzonnen. De saldibalansen zijn 'voor afsluiting':
balansrekeningen tonen de eindstand, W&V-rekeningen het jaartotaal en het eigen
vermogen de beginstand van het jaar. Debet = positief, credit = negatief.
De bankrekening is de sluitpost, zodat elk jaar exact op nihil sluit.

Gebruik: python3 tests/Fictief/genereer.py   (vanuit de root van de repository)
"""
from decimal import Decimal, ROUND_HALF_UP
from pathlib import Path
import random

JAREN = range(2021, 2026)
HIER = Path(__file__).parent


def c(x) -> Decimal:
    return Decimal(str(x)).quantize(Decimal("0.01"), rounding=ROUND_HALF_UP)


def nl(bedrag: Decimal, duizendtallen: bool) -> str:
    teken = "-" if bedrag < 0 else ""
    geheel, cent = f"{abs(bedrag):.2f}".split(".")
    if duizendtallen:
        geheel = f"{int(geheel):,}".replace(",", ".")
    return f"{teken}{geheel},{cent}"


def schrijf(map_: str, klant: str, jaar: int, regels, duizendtallen: bool, bom: bool):
    assert sum(b for _, _, b in regels) == 0, (klant, jaar)
    pad = HIER / map_ / f"saldibalans-{jaar}.csv"
    pad.parent.mkdir(parents=True, exist_ok=True)
    tekst = [f"Boekjaar;{jaar}", f"Klant;{klant}", "", "Grootboeknummer;Omschrijving;Bedrag"]
    tekst += [f"{nr};{oms};{nl(b, duizendtallen)}" for nr, oms, b in regels]
    pad.write_text("\n".join(tekst) + "\n", encoding="utf-8-sig" if bom else "utf-8")


def bv():
    r = random.Random(2021)
    klant = "Voorbeeld Bakkerij B.V."
    reserves = c(-30_000)
    inv_aanschaf, inv_cum = c(85_000), c(-30_000)
    auto_aanschaf, auto_cum = c(45_000), c(0)
    lening = c(-120_000)
    investeringen = {2022: 12_000, 2024: 20_000}
    dividend = {2022: 80_000, 2023: 90_000, 2024: 120_000, 2025: 130_000}
    verwachting = []
    for i, jaar in enumerate(JAREN):
        omzet = c(-1_150_000 * 1.06 ** i * r.uniform(0.97, 1.03))
        inkoop = c(-omzet * Decimal(str(r.uniform(0.42, 0.45))))
        lonen = c(310_000 * 1.05 ** i)
        inv_aanschaf += c(investeringen.get(jaar, 0))
        afschr_inv = c(inv_aanschaf * Decimal("0.10"))
        inv_cum -= afschr_inv
        afschr_auto = c(9_000)
        auto_cum -= afschr_auto
        rente = c(-lening * Decimal("0.04"))
        lening += c(20_000)
        wv = [
            ("4000", "Brutolonen", lonen),
            ("4010", "Sociale lasten", c(lonen * Decimal("0.21"))),
            ("4020", "Pensioenlasten", c(lonen * Decimal("0.06"))),
            ("4100", "Huur bedrijfsruimte", c(36_000 * 1.02 ** i)),
            ("4110", "Gas, water en elektra", c(r.uniform(14_000, 22_000))),
            ("4200", "Autokosten", c(r.uniform(16_000, 20_000))),
            ("4300", "Kantoorkosten", c(r.uniform(8_000, 10_000))),
            ("4400", "Verkoopkosten", c(r.uniform(10_000, 14_000))),
            ("4500", "Accountantskosten", c(r.uniform(7_500, 9_000))),
            ("4800", "Afschrijving inventaris", afschr_inv),
            ("4810", "Afschrijving vervoermiddelen", afschr_auto),
            ("7000", "Inkoop grondstoffen", inkoop),
            ("8000", "Omzet", omzet),
            ("9100", "Rentelasten lening", rente),
        ]
        voor_belasting = -sum(b for _, _, b in wv)
        vpb = c(max(Decimal(0), voor_belasting) * Decimal("0.19"))
        wv.append(("9500", "Vennootschapsbelasting", vpb))
        resultaat = -sum(b for _, _, b in wv)
        balans = [
            ("0200", "Inventaris aanschafwaarde", inv_aanschaf),
            ("0210", "Inventaris cumulatieve afschrijving", inv_cum),
            ("0300", "Vervoermiddelen aanschafwaarde", auto_aanschaf),
            ("0310", "Vervoermiddelen cumulatieve afschrijving", auto_cum),
            ("0500", "Geplaatst kapitaal", c(-18_000)),
            ("0550", "Overige reserves", reserves),
            ("0700", "Lening ING", lening),
            ("1000", "Kas", c(r.uniform(400, 1_500))),
            ("1110", "Rabobank spaarrekening", c(50_000 + 5_000 * i)),
            ("1300", "Debiteuren", c(-omzet * Decimal("0.09"))),
            ("1310", "Voorziening dubieuze debiteuren", c(omzet * Decimal("0.004"))),
            ("1400", "Vooruitbetaalde kosten", c(r.uniform(2_500, 4_000))),
            ("1500", "Crediteuren", c(inkoop * Decimal("-0.08"))),
            ("1600", "Te betalen omzetbelasting", c(omzet * Decimal("0.035"))),
            ("1610", "Te betalen loonheffing", c(lonen * Decimal("-0.09"))),
            ("1620", "Te betalen vennootschapsbelasting", -vpb),
            ("1800", "Nog te betalen kosten", c(-r.uniform(6_000, 9_000))),
            ("3000", "Voorraad grondstoffen", c(r.uniform(40_000, 55_000))),
        ]
        bank = -sum(b for _, _, b in balans + wv)
        balans.insert(8, ("1100", "Rabobank rekening-courant", bank))
        regels = sorted(balans + wv)
        schrijf("bv-voorbeeld-bakkerij", klant, jaar, regels, duizendtallen=True, bom=True)
        verwachting.append((jaar, len(regels), resultaat, bank))
        reserves = reserves - resultaat + c(dividend.get(jaar + 1, 0))
    return verwachting


def ez():
    r = random.Random(2025)
    klant = "Schildersbedrijf Voorbeeld"
    vermogen = c(-45_000)
    auto_aanschaf, auto_cum = c(32_000), c(-4_800)
    gereedschap, gereedschap_cum = c(12_000), c(-4_000)
    investeringen = {2023: 3_500}
    verwachting = []
    for i, jaar in enumerate(JAREN):
        omzet = c(-128_000 * 1.04 ** i * r.uniform(0.96, 1.04))
        gereedschap += c(investeringen.get(jaar, 0))
        afschr_auto = c(auto_aanschaf * Decimal("0.15"))
        auto_cum -= afschr_auto
        afschr_gereedschap = c(gereedschap * Decimal("0.10"))
        gereedschap_cum -= afschr_gereedschap
        onttrekkingen = c(r.uniform(64_000, 78_000))
        stortingen = c(-r.choice([0, 0, 1_500, 3_000]))
        wv = [
            ("4200", "Autokosten", c(r.uniform(6_000, 8_000))),
            ("4300", "Telefoon en internet", c(r.uniform(1_000, 1_400))),
            ("4400", "Verzekeringen", c(r.uniform(2_500, 3_000))),
            ("4500", "Accountantskosten", c(r.uniform(2_000, 2_500))),
            ("4600", "Kleine aanschaffingen", c(r.uniform(800, 2_000))),
            ("4800", "Afschrijving bedrijfsauto", afschr_auto),
            ("4810", "Afschrijving gereedschap", afschr_gereedschap),
            ("7000", "Inkoop verf en materialen", c(-omzet * Decimal(str(r.uniform(0.22, 0.26))))),
            ("7100", "Uitbesteed werk", c(-omzet * Decimal(str(r.uniform(0.04, 0.08))))),
            ("8000", "Omzet schilderwerk", omzet),
            ("9100", "Rente en bankkosten", c(r.uniform(250, 450))),
        ]
        resultaat = -sum(b for _, _, b in wv)
        balans = [
            ("0200", "Bedrijfsauto aanschafwaarde", auto_aanschaf),
            ("0210", "Bedrijfsauto cumulatieve afschrijving", auto_cum),
            ("0250", "Gereedschap aanschafwaarde", gereedschap),
            ("0260", "Gereedschap cumulatieve afschrijving", gereedschap_cum),
            ("0600", "Ondernemingsvermogen", vermogen),
            ("0650", "Fiscale oudedagsreserve", c(-8_500)),
            ("0700", "Privéonttrekkingen", onttrekkingen),
            ("0710", "Privéstortingen", stortingen),
            ("1000", "Kas", c(r.uniform(150, 600))),
            ("1300", "Debiteuren", c(-omzet * Decimal("0.08"))),
            ("1500", "Crediteuren", c(-r.uniform(2_500, 4_500))),
            ("1600", "Te betalen omzetbelasting", c(omzet * Decimal("0.04"))),
            ("1800", "Nog te betalen kosten", c(-r.uniform(1_500, 2_500))),
        ]
        bank = -sum(b for _, _, b in balans + wv)
        balans.insert(9, ("1100", "ING zakelijke rekening", bank))
        regels = sorted(balans + wv)
        schrijf("ez-schildersbedrijf", klant, jaar, regels, duizendtallen=False, bom=False)
        verwachting.append((jaar, len(regels), resultaat, bank))
        vermogen = vermogen - resultaat + onttrekkingen + stortingen
    return verwachting


def schrijf_verwachting(map_: str, rijen):
    regels = ["Boekjaar;AantalRekeningen;Resultaat;Bank"]
    regels += [f"{j};{n};{nl(res, False)};{nl(bank, False)}" for j, n, res, bank in rijen]
    (HIER / map_ / "verwachting.csv").write_text("\n".join(regels) + "\n", encoding="utf-8")


if __name__ == "__main__":
    schrijf_verwachting("bv-voorbeeld-bakkerij", bv())
    schrijf_verwachting("ez-schildersbedrijf", ez())
    for m in ("bv-voorbeeld-bakkerij", "ez-schildersbedrijf"):
        print(m); print((HIER / m / "verwachting.csv").read_text())
