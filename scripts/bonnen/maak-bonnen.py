#!/usr/bin/env python3
"""Maakt 25 verzonnen kassabonnen als 'telefoonfoto' voor de bonnen-stresstest: bouwmarkt, supermarkt, bakkerij,
tankstation, horeca, drogist en parkeren, met verschillende btw-tarieven (9%, 21%, gemengd, 0%), lettertypen en
gebreken (scheef, wazig, schaduw, vervaagde thermische inkt, ruis, donkere of lichte ondergrond).

    python3 scripts/bonnen/maak-bonnen.py <map>

Schrijft bon-01.jpg … bon-25.jpg en verwacht.json (wat er op elke bon staat). Alle winkels, adressen en nummers zijn
verzonnen; de opmaak lijkt op wat gangbare ketens afdrukken. Vast zaadje: elke keer dezelfde bonnen. Vereist Pillow.
De test: BOEKERHOUDEN_BONNENMAP=<map> dotnet test tests/Boekerhouden.Documenten.Tests --filter Bonnenstress"""
import json
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

LETTERS = {
    "mono": ("/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf"),
    "sans": ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"),
    "vrij": ("/usr/share/fonts/truetype/freefont/FreeMono.ttf", "/usr/share/fonts/truetype/freefont/FreeMonoBold.ttf"),
}


def geld(bedrag):
    return f"{bedrag:,.2f}".replace(",", "X").replace(".", ",").replace("X", ".")


class Bon:
    """Een bon als lijst regels: (tekst links, tekst rechts, stijl)."""

    def __init__(self, breedte=40):
        self.regels = []
        self.breedte = breedte

    def kop(self, tekst):
        self.regels.append((tekst, None, "kop"))

    def midden(self, tekst):
        self.regels.append((tekst, None, "midden"))

    def regel(self, links="", rechts=None, vet=False):
        self.regels.append((links, rechts, "vet" if vet else "gewoon"))

    def lijn(self, teken="-"):
        self.regels.append((teken * self.breedte, None, "gewoon"))

    def wit(self):
        self.regels.append(("", None, "gewoon"))


def btw_van(artikelen):
    """Per tarief (exclusief, btw, inclusief) uit artikelen (naam, bedrag inclusief, tarief)."""
    per = {}
    for _, bedrag, tarief in artikelen:
        per[tarief] = per.get(tarief, 0) + round(bedrag * 100)
    uit = {}
    for tarief, incl in sorted(per.items()):
        btw = round(incl * tarief / (100 + tarief))
        uit[tarief] = ((incl - btw) / 100, btw / 100, incl / 100)
    return uit


def verwacht(naam, datum, artikelen, nummer=None):
    btw = btw_van(artikelen)
    return {
        "naam": naam, "datum": datum, "nummer": nummer,
        "totaal": round(sum(round(a[1] * 100) for a in artikelen)) / 100,
        "btw": [{"tarief": t, "exclusief": e, "btw": b} for t, (e, b, _) in btw.items()],
    }


# ---- De soorten bonnen -------------------------------------------------------------------------------------------

def bouwmarkt(r, naam, plaats, tabel):
    art = r.sample([("Houtschroeven 4x40 200st", 8.95), ("Multiplex 18mm 122x61", 24.50), ("Kwast set 3-delig", 6.75), ("Muurverf wit 10L", 39.95),
                    ("Purschuim 750ml", 7.49), ("Schuurpapier K120 5st", 3.29), ("Stucloper 60cm", 12.99), ("Boor HSS 6mm", 4.59),
                    ("Tegellijm 5kg", 11.95), ("Afplaktape 50m", 4.25), ("Kabelgoot 2m", 6.10)], r.randint(2, 6))
    art = [(n, b, 21) for n, b in art]
    datum = f"{r.randint(1, 28):02d}-{r.randint(1, 12):02d}-2026"
    nummer = f"{r.randint(100000, 999999)}"
    v = verwacht(naam, datum, art, nummer)
    b = Bon()
    b.kop(naam.upper())
    b.midden(f"Industrieweg {r.randint(1, 80)}")
    b.midden(f"{r.randint(1000, 9999)} AB {plaats}")
    b.midden(f"Tel. 0{r.randint(10, 79)}-{r.randint(1000000, 9999999)}")
    b.wit()
    b.regel(f"Datum {datum}", f"Tijd {r.randint(8, 20):02d}:{r.randint(0, 59):02d}")
    b.regel(f"Kassa {r.randint(1, 9):02d}", f"Bon {nummer}")
    b.lijn()
    for n, bedrag, _ in art:
        if r.random() < 0.3:
            b.regel(f"{r.randint(1000000, 9999999)}")
        b.regel(n, geld(bedrag))
    b.lijn()
    b.regel("TOTAAL", geld(v["totaal"]), vet=True)
    b.lijn()
    b.regel("PIN", geld(v["totaal"]))
    b.wit()
    e, btw, incl = btw_van(art)[21]
    if tabel == "kolommen":
        b.regel("BTW%     Excl      BTW     Incl")
        b.regel(f"21%   {geld(e):>8} {geld(btw):>8} {geld(incl):>8}")
    elif tabel == "over":
        b.regel(f"BTW 21% over {geld(e)}", geld(btw))
    else:
        b.regel("Waarvan BTW 21%", geld(btw))
        b.regel("Totaal excl. BTW", geld(e))
    b.wit()
    b.regel(f"Terminal {r.randint(100000, 999999)}  Periode {r.randint(1000, 9999)}")
    b.regel(f"Transactie {r.randint(10000000, 99999999)}")
    b.regel("Kaart xxxxxxxxxxxx" + str(r.randint(1000, 9999)))
    b.wit()
    b.midden("Ruilen binnen 30 dagen met bon")
    return b, v


def supermarkt(r, naam, plaats, bonus):
    laag = r.sample([("Halfvolle melk 1L", 1.19), ("Volkoren brood", 2.35), ("Bananen 1kg", 1.79), ("Jonge kaas plak", 3.49), ("Pasta penne", 0.99),
                     ("Koffie snelfilter", 5.99), ("Appelsap 1,5L", 1.65), ("Kipfilet 500g", 6.49), ("Yoghurt naturel", 1.29), ("Tomaten tros", 2.15)],
                    r.randint(3, 7))
    hoog = r.sample([("Afwasmiddel", 1.89), ("Toiletpapier 8rol", 4.49), ("Tandpasta", 2.29), ("Vaatdoekjes", 1.59), ("Batterijen AA 4st", 5.49)], r.randint(1, 3))
    art = [(n, b, 9) for n, b in laag] + [(n, b, 21) for n, b in hoog]
    r.shuffle(art)
    korting = None
    if bonus:
        doel = r.choice([a for a in art if a[2] == 9])
        korting = (f"BONUS {doel[0][:14]}", -round(doel[1] * 0.25, 2), 9)
        art.append(korting)
    datum = f"{r.randint(1, 28):02d}-{r.randint(1, 12):02d}-2026"
    v = verwacht(naam, datum, art, None)
    b = Bon()
    b.kop(naam)
    b.midden(f"Dorpsstraat {r.randint(1, 120)}")
    b.midden(f"{r.randint(1000, 9999)} CD {plaats}")
    b.wit()
    b.regel("AANTAL OMSCHRIJVING", "BEDRAG")
    b.lijn()
    for n, bedrag, t in art:
        if korting and (n, bedrag, t) == korting:
            continue
        b.regel(f" 1 {n}", geld(bedrag) + (" B" if t == 21 else ""))
    if korting:
        b.wit()
        b.regel(korting[0], geld(korting[1]))
    b.lijn()
    if korting:
        b.regel("UW VOORDEEL", geld(-korting[1]))
    b.regel("TOTAAL", geld(v["totaal"]), vet=True)
    b.wit()
    b.regel("PINNEN", geld(v["totaal"]))
    b.wit()
    b.regel("BTW       OVER       EUR")
    per = btw_van(art)
    for t, (e, btw, _) in per.items():
        b.regel(f"{t}%   {geld(e):>9} {geld(btw):>9}")
    b.regel(f"TOTAAL{geld(sum(x[0] for x in per.values())):>9} {geld(sum(x[1] for x in per.values())):>9}")
    b.wit()
    b.regel(f"{r.randint(1000, 9999)} {r.randint(10, 99)} {r.randint(100, 999)} {r.randint(10, 99)}", f"{datum} {r.randint(8, 21):02d}:{r.randint(0, 59):02d}")
    b.midden("Bedankt en tot ziens")
    return b, v


def bakker(r, naam, plaats, website):
    art = r.sample([("Chocolade klassiek", 3.20), ("Aardbei punt", 3.25), ("Slagroom gebakje", 6.50), ("Toffee peer", 6.50), ("Mokka gebakje", 3.25),
                    ("Wit vloer gesneden", 2.95), ("Krentenbollen 6st", 3.60), ("Appelflap", 2.10), ("Roomboter croissant", 1.45)], r.randint(2, 6))
    art = [(n, b, 9) for n, b in art]
    datum = f"{r.randint(1, 28):02d}-{r.randint(1, 12):02d}-2026"
    nummer = f"{r.randint(10000000, 99999999)}"
    v = verwacht(naam, datum, art, nummer)
    e, btw, incl = btw_van(art)[9]
    b = Bon(38)
    b.kop(naam)
    b.regel(f"Marktplein {r.randint(1, 40)}")
    b.regel(f"{r.randint(1000, 9999)} HK {plaats}")
    b.regel(f"0{r.randint(100, 599)} {r.randint(100000, 999999)}")
    b.lijn("=")
    for n, bedrag, _ in art:
        b.regel(f"1    {n}", f"€   {geld(bedrag)}")
    b.lijn()
    b.regel("Totaal", f"€  {geld(v['totaal'])}", vet=True)
    b.lijn()
    b.regel("Pin betaald", f"€  {geld(v['totaal'])}")
    b.regel("        BTW       Over")
    b.regel(f"        9,0% €   {geld(e)} €   {geld(btw)}")
    b.regel(f"       Totaal €  {geld(e)} €   {geld(btw)}")
    b.lijn("=")
    b.midden("Kopie Kaarthouder")
    b.regel(f"Terminal: {r.randint(100000, 999999)}  Merchant: {r.randint(100000, 999999)}")
    b.regel(f"Totaal:   {geld(v['totaal'])} EUR")
    b.midden("AKKOORD")
    b.lijn()
    b.regel("Datum", f"{datum}")
    b.regel("Bonnummer", nummer)
    b.regel("Kassanummer", f"00{r.randint(1, 9)}")
    b.regel("Bedankt en graag tot ziens!")
    if website:
        b.regel("www." + "".join(c for c in naam.lower() if c.isalpha()) + ".nl")
    return b, v


def tankstation(r, naam, plaats, shop):
    liter = round(r.uniform(18, 62), 2)
    prijs = round(r.uniform(1.799, 2.199), 3)
    brandstof = r.choice(["Euro 95 E10", "Diesel B7", "Super 98 E5"])
    bedrag = round(liter * prijs, 2)
    art = [(brandstof, bedrag, 21)]
    if shop:
        art.append(("Broodje gezond", 4.75, 9))
        art.append(("Koffie groot", 2.95, 9))
    datum = f"{r.randint(1, 28):02d}-{r.randint(1, 12):02d}-2026"
    nummer = f"{r.randint(1000, 9999)}-{r.randint(100, 999)}"
    v = verwacht(naam, datum, art, nummer)
    b = Bon()
    b.kop(naam)
    b.midden(f"Rijksweg {r.randint(1, 300)}")
    b.midden(f"{r.randint(1000, 9999)} EF {plaats}")
    b.midden(f"BTW NL8{r.randint(10000000, 99999999)}B01")
    b.wit()
    b.regel(f"Bonnr: {nummer}", f"{datum} {r.randint(6, 23):02d}:{r.randint(0, 59):02d}")
    b.lijn()
    b.regel(f"Pomp {r.randint(1, 12)}  {brandstof}")
    b.regel(f"  {str(liter).replace('.', ',')} L x € {str(prijs).replace('.', ',')}/L", geld(bedrag))
    for n, x, _ in art[1:]:
        b.regel(n, geld(x))
    b.lijn()
    b.regel("Totaal incl. BTW  EUR", geld(v["totaal"]), vet=True)
    b.wit()
    for t, (e, btw, _) in btw_van(art).items():
        b.regel(f"BTW {t}% over {geld(e)}", geld(btw))
    b.wit()
    b.regel("Betaald met PIN", geld(v["totaal"]))
    b.regel(f"Kaart ************{r.randint(1000, 9999)}")
    b.regel(f"Autorisatie {r.randint(100000, 999999)}")
    b.wit()
    b.midden("Goede reis!")
    return b, v


def horeca(r, naam, plaats, alcohol):
    eten = r.sample([("Tosti ham kaas", 7.50), ("Uitsmijter", 9.75), ("Soep van de dag", 6.95), ("Cappuccino", 3.40), ("Verse muntthee", 3.60),
                     ("Appeltaart", 4.75), ("Friet met mayo", 4.50), ("Club sandwich", 12.50)], r.randint(2, 5))
    art = [(n, b, 9) for n, b in eten]
    if alcohol:
        art += [(n, b, 21) for n, b in r.sample([("Bier tap 25cl", 3.60), ("Witte wijn glas", 5.25), ("Rode wijn glas", 5.25)], r.randint(1, 2))]
    datum = f"{r.randint(1, 28):02d}/{r.randint(1, 12):02d}/2026"
    nummer = f"{r.randint(10000, 99999)}"
    v = verwacht(naam, datum.replace("/", "-"), art, nummer)
    b = Bon(36)
    b.kop(naam)
    b.midden(f"Havenkade {r.randint(1, 60)}, {plaats}")
    b.midden(f"KvK {r.randint(10000000, 99999999)}")
    b.wit()
    b.regel(f"Tafel {r.randint(1, 40)}", f"Bediening: {r.choice(['Sanne', 'Tim', 'Noor', 'Daan'])}")
    b.regel(f"Rekening {nummer}", datum)
    b.lijn()
    for n, x, _ in art:
        b.regel(f"1x {n}", geld(x))
    b.lijn()
    b.regel("Te betalen", f"€ {geld(v['totaal'])}", vet=True)
    b.wit()
    b.regel("Btw    Grondslag    Btw-bedrag")
    for t, (e, btw, _) in btw_van(art).items():
        b.regel(f"{t}%   {geld(e):>10} {geld(btw):>12}")
    b.wit()
    b.regel("Pin", geld(v["totaal"]))
    b.midden("Bedankt voor uw bezoek")
    return b, v


def drogist(r, naam, plaats):
    art = [(n, b, 21) for n, b in r.sample([("Shampoo 300ml", 3.99), ("Deodorant", 2.79), ("Zonnebrand SPF30", 9.99), ("Wattenschijfjes", 1.19)], 3)]
    art += [(n, b, 9) for n, b in r.sample([("Paracetamol 500mg", 1.49), ("Neusspray", 3.29), ("Vitamine C", 4.99)], 2)]
    datum = f"{r.randint(1, 28):02d}-{r.randint(1, 12):02d}-26"
    nummer = f"{r.randint(1000, 9999)}{r.randint(1000, 9999)}"
    v = verwacht(naam, datum[:6] + "2026", art, nummer)
    b = Bon()
    b.kop(naam)
    b.midden(f"Winkelcentrum {r.randint(1, 30)}  {plaats}")
    b.wit()
    for n, x, t in art:
        b.regel(n, geld(x))
    b.lijn()
    b.regel("Totaal", geld(v["totaal"]), vet=True)
    b.regel("Pinbetaling", geld(v["totaal"]))
    b.wit()
    for t, (e, btw, incl) in btw_van(art).items():
        b.regel(f"BTW {t}%", geld(btw))
    b.regel("Totaal exclusief BTW", geld(sum(x[0] for x in btw_van(art).values())))
    b.wit()
    b.regel(f"Bonnummer: {nummer}", datum)
    b.midden("Tot ziens!")
    return b, v


def parkeren(r, naam, plaats):
    bedrag = r.choice([3.50, 6.00, 8.40, 12.00, 17.50])
    art = [("Parkeren", bedrag, 21)]
    datum = f"{r.randint(1, 28):02d}.{r.randint(1, 12):02d}.2026"
    nummer = f"{r.randint(100000, 999999)}"
    v = verwacht(naam, datum.replace(".", "-"), art, nummer)
    e, btw, _ = btw_van(art)[21]
    b = Bon(32)
    b.kop(naam)
    b.midden(plaats)
    b.wit()
    b.regel(f"Ticket {nummer}")
    b.regel("In ", f"{datum} {r.randint(7, 12):02d}:{r.randint(0, 59):02d}")
    b.regel("Uit", f"{datum} {r.randint(13, 22):02d}:{r.randint(0, 59):02d}")
    b.wit()
    b.regel("Bedrag", f"EUR {geld(bedrag)}", vet=True)
    b.regel(f"Incl. 21% BTW", f"EUR {geld(btw)}")
    b.wit()
    b.regel("Betaald: contactloos")
    b.midden("Kwitantie - bewaar dit bewijs")
    return b, v


def kiosk_nul(r, naam, plaats):
    """Een bon zonder btw (vrijgesteld): postzegels."""
    art = [("Postzegels NL 10st", 12.10, 0), ("Postzegel internationaal", 1.75, 0)]
    datum = f"{r.randint(1, 28):02d}-{r.randint(1, 12):02d}-2026"
    nummer = f"{r.randint(100000, 999999)}"
    v = verwacht(naam, datum, art, nummer)
    b = Bon()
    b.kop(naam)
    b.midden(f"Stationsplein {r.randint(1, 9)}, {plaats}")
    b.wit()
    for n, x, _ in art:
        b.regel(n, geld(x))
    b.lijn()
    b.regel("Totaal", geld(v["totaal"]), vet=True)
    b.regel("Vrijgesteld van BTW")
    b.regel("PIN", geld(v["totaal"]))
    b.wit()
    b.regel(f"Bon {nummer}", datum)
    return b, v


# ---- Tekenen en bederven ----------------------------------------------------------------------------------------

def teken(bon, letter, maat):
    gewoon, vet = (ImageFont.truetype(p, maat) for p in LETTERS[letter])
    kop = ImageFont.truetype(LETTERS[letter][1], int(maat * 1.5))
    proef = ImageDraw.Draw(Image.new("L", (10, 10)))
    breedte = int(max(proef.textlength("M" * bon.breedte, font=gewoon), 420)) + 80
    hoogte = 60 + sum(int(maat * (2.0 if s == "kop" else 1.45)) for _, _, s in bon.regels) + 60
    beeld = Image.new("L", (breedte, hoogte), 255)
    d = ImageDraw.Draw(beeld)
    y = 50
    for links, rechts, stijl in bon.regels:
        f = {"kop": kop, "vet": vet}.get(stijl, gewoon)
        if stijl in ("kop", "midden"):
            d.text(((breedte - d.textlength(links, font=f)) / 2, y), links, font=f, fill=0)
        else:
            d.text((40, y), links, font=f, fill=0)
            if rechts is not None:
                d.text((breedte - 40 - d.textlength(rechts, font=f), y), rechts, font=f, fill=0)
        y += int(maat * (2.0 if stijl == "kop" else 1.45))
    return beeld


def bederf(r, bon, gebrek):
    """Van de strakke bon een telefoonfoto: papierkleur, vervaagde inkt, schaduw, ondergrond, scheef, wazig, ruis."""
    b, h = bon.size
    papier = gebrek.get("papier", (244, 240, 230))
    inkt = gebrek.get("inkt", 55)
    kleur = Image.merge("RGB", [bon.point(lambda p, k=k: int(inkt + (p / 255) * (k - inkt))) for k in papier])
    if gebrek.get("schaduw"):
        masker = Image.new("L", (b, h), 255)
        m = ImageDraw.Draw(masker)
        richting = gebrek["schaduw"]
        for i in range(h if richting in ("onder", "boven") else b):
            deel = i / (h if richting in ("onder", "boven") else b)
            if richting in ("boven", "links"):
                deel = 1 - deel
            waarde = int(255 - 120 * max(0, (deel - 0.35) / 0.65))
            if richting in ("onder", "boven"):
                m.line([(0, i), (b, i)], fill=waarde)
            else:
                m.line([(i, 0), (i, h)], fill=waarde)
        kleur = Image.composite(kleur, Image.new("RGB", (b, h), (25, 22, 20)), masker.filter(ImageFilter.GaussianBlur(40)))
    ondergrond = gebrek.get("ondergrond", (62, 48, 40))
    rand = gebrek.get("rand", 110)
    hoek = gebrek.get("hoek", 0)
    gedraaid = kleur.rotate(hoek, expand=True, fillcolor=ondergrond, resample=Image.BICUBIC)
    foto = Image.new("RGB", (gedraaid.width + 2 * rand, gedraaid.height + 2 * rand), ondergrond)
    foto.paste(gedraaid, (rand, rand))
    if gebrek.get("wazig"):
        foto = foto.filter(ImageFilter.GaussianBlur(gebrek["wazig"]))
    if gebrek.get("ruis"):
        ruis = Image.effect_noise(foto.size, gebrek["ruis"]).convert("L")
        foto = Image.merge("RGB", [Image.blend(k, ruis, 0.12) for k in foto.split()])
    if gebrek.get("schaal"):
        foto = foto.resize((int(foto.width * gebrek["schaal"]), int(foto.height * gebrek["schaal"])), Image.LANCZOS)
    return foto


BONNEN = [
    # (soort, naam, plaats, extra, letter, maat, gebrek)
    (bouwmarkt, "Klusmarkt Paxo", "Zwolle", "kolommen", "mono", 26, dict(hoek=2.5, schaduw="onder")),
    (bouwmarkt, "Klusmarkt Paxo", "Zwolle", "over", "mono", 24, dict(hoek=-4, wazig=1.0)),
    (bouwmarkt, "Bouwhal Gamar", "Assen", "waarvan", "sans", 26, dict(hoek=1, inkt=120, papier=(238, 232, 215))),
    (bouwmarkt, "Bouwhal Gamar", "Assen", "kolommen", "vrij", 28, dict(hoek=-6, ondergrond=(235, 235, 235), rand=60)),
    (bouwmarkt, "IJzerhandel De Moer", "Sneek", "over", "mono", 22, dict(hoek=3.5, wazig=1.4, ruis=40)),
    (supermarkt, "Albertus Hein Supermarkt", "Leeuwarden", False, "mono", 24, dict(hoek=-2)),
    (supermarkt, "Albertus Hein Supermarkt", "Leeuwarden", True, "mono", 24, dict(hoek=4.5, schaduw="links")),
    (supermarkt, "Supermarkt De Jumper", "Heerenveen", True, "sans", 24, dict(hoek=0.5, wazig=1.2)),
    (supermarkt, "Supermarkt De Jumper", "Heerenveen", False, "vrij", 26, dict(hoek=-3, inkt=110)),
    (supermarkt, "Buurtsuper Het Hoekje", "Bolsward", True, "mono", 22, dict(hoek=5.5, ruis=50, schaduw="boven")),
    (bakker, "Bakkerij Zoet", "Sneek", True, "sans", 24, dict(hoek=1.5)),
    (bakker, "Bakkerij Zoet", "Sneek", True, "sans", 22, dict(hoek=-5, wazig=1.5)),
    (bakker, "Banketbakker Roomhart", "Joure", False, "mono", 24, dict(hoek=3, schaduw="onder", inkt=100)),
    (bakker, "Warme Bakker Korst", "Lemmer", True, "vrij", 26, dict(hoek=-1.5, ondergrond=(240, 240, 238), rand=40)),
    (tankstation, "Tankstation De Brug", "Drachten", False, "mono", 24, dict(hoek=2)),
    (tankstation, "Tankstation De Brug", "Drachten", True, "mono", 24, dict(hoek=-4.5, wazig=1.1, schaduw="links")),
    (tankstation, "Brandstof Noord", "Franeker", False, "sans", 24, dict(hoek=6, inkt=125)),
    (tankstation, "Brandstof Noord", "Franeker", True, "vrij", 26, dict(hoek=-2.5, ruis=45)),
    (horeca, "Lunchroom De Kade", "Harlingen", False, "sans", 24, dict(hoek=3.5)),
    (horeca, "Lunchroom De Kade", "Harlingen", True, "mono", 24, dict(hoek=-3.5, wazig=1.3)),
    (horeca, "Eetcafe Het Anker", "Stavoren", True, "mono", 22, dict(hoek=1, schaduw="onder", ruis=35)),
    (drogist, "Drogisterij Kruidhof", "Dokkum", None, "mono", 24, dict(hoek=-5.5)),
    (drogist, "Drogisterij Kruidhof", "Dokkum", None, "sans", 24, dict(hoek=2.5, wazig=1.2, inkt=105)),
    (parkeren, "Parkeergarage Centrum", "Groningen", None, "mono", 28, dict(hoek=-1, schaal=0.7)),
    (kiosk_nul, "Kiosk Perron 1", "Zwolle", None, "sans", 24, dict(hoek=4, schaduw="onder")),
]


def main(map_):
    uit = Path(map_)
    uit.mkdir(parents=True, exist_ok=True)
    verwachtingen = []
    for i, (soort, naam, plaats, extra, letter, maat, gebrek) in enumerate(BONNEN, 1):
        r = random.Random(1000 + i)
        random.seed(2000 + i)
        bon, v = soort(r, naam, plaats, extra) if extra is not None else soort(r, naam, plaats)
        foto = bederf(r, teken(bon, letter, maat), gebrek)
        bestand = f"bon-{i:02d}.jpg"
        foto.save(uit / bestand, quality=r.choice([60, 72, 85]))
        v.update(bestand=bestand, soort=soort.__name__, gebrek=", ".join(f"{k}={w}" for k, w in gebrek.items()))
        verwachtingen.append(v)
    (uit / "verwacht.json").write_text(json.dumps(verwachtingen, indent=1, ensure_ascii=False), encoding="utf-8")
    print(f"{len(verwachtingen)} bonnen in {uit}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "bonnen")
