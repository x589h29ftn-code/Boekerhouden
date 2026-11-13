#!/usr/bin/env python3
"""Maakt verzonnen kassabonnen zoals ze in een ordner gefotografeerd worden: een smalle thermische bon (lichte, grijze
inkt) op een ander vel met drukwerk (een factuur) dat rechts naast de bon uitsteekt, de ringen van de ordner over de
bon heen, de rug van de ordner links, een beetje onscherp. Dat is de lastigste foto voor de bonherkenning: de tekst
ernaast hoort niet bij de bon, de ringen zijn grote donkere vlekken en de inkt is licht.

    python3 scripts/bonnen/maak-ordnerbonnen.py <map>

Schrijft ordner-01.jpg … en verwacht.json in dezelfde vorm als maak-bonnen.py (dezelfde verzonnen winkels). Vereist
Pillow. De test: BOEKERHOUDEN_BONNENMAP=<map> dotnet test tests/Boekerhouden.Documenten.Tests --filter Bonnenstress"""
import importlib.util
import json
import random
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

_spec = importlib.util.spec_from_file_location("bonnen", Path(__file__).with_name("maak-bonnen.py"))
bonnen = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(bonnen)

# Welke bonnen uit maak-bonnen.py (op volgnummer), en hoe licht de inkt is (0 = zwart, 1 = onzichtbaar).
KEUZE = [(1, 0.35), (3, 0.5), (5, 0.45), (8, 0.55), (12, 0.4), (14, 0.5), (17, 0.45), (20, 0.55), (22, 0.4), (25, 0.5)]


def ordnerfoto(r, bon, licht):
    breedte, hoogte = 1800, 2400
    foto = Image.new("L", (breedte, hoogte), 236)
    d = ImageDraw.Draw(foto)
    # De rug van de ordner links, en rechts het vel erachter met drukwerk dat niet bij de bon hoort.
    d.rectangle([0, 0, 150, hoogte], fill=150)
    vet = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf", 44)
    klein = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", 30)
    d.text((1330, 150), "FACTUUR", font=vet, fill=40)
    for i, regel in enumerate(["Factuurnummer 88121", "Debiteurnummer 4410", "Factuurdatum 03-02-2026", "Vermeld altijd uw", "debiteurnummer"]):
        d.text((1330, 260 + i * 46), regel, font=klein, fill=70)
    for i, regel in enumerate(["H. de Boer", "Heideweg 2", "9200 AA Ergens"]):
        d.text((1330, 600 + i * 46), regel, font=klein, fill=60)
    d.rectangle([1320, 980, breedte, 1030], fill=170)
    d.text((1340, 1100), "1 BLISTER   12,50", font=klein, fill=60)
    d.text((1340, 1160), "1 Stuk(s)    0,95", font=klein, fill=60)

    # De bon: smal, lichte inkt, met een dunne schaduwrand; iets scheef.
    schaal = min(1050 / bon.width, 2250 / bon.height)
    strook = bon.convert("L").resize((int(bon.width * schaal), int(bon.height * schaal)), Image.LANCZOS)
    strook = strook.point(lambda v: int(250 - (250 - v) * (1 - licht)))
    kader = Image.new("L", (strook.width + 8, strook.height + 8), 190)
    kader.paste(strook, (4, 4))
    kader = kader.rotate(r.uniform(-1.5, 1.5), expand=True, fillcolor=236, resample=Image.BICUBIC)
    links = 230 + r.randint(-20, 30)
    foto.paste(kader, (links, max(20, (hoogte - kader.height) // 2)))

    # De ringen: dikke donkere bogen die vanaf de rug over de bon heen komen.
    d = ImageDraw.Draw(foto)
    for midden in (hoogte * 0.38, hoogte * 0.78):
        y = int(midden + r.randint(-40, 40))
        d.arc([60, y - 110, links + 300, y + 110], start=180, end=20, fill=35, width=22)
        d.arc([66, y - 104, links + 294, y + 104], start=200, end=350, fill=120, width=6)
    foto = foto.filter(ImageFilter.GaussianBlur(1.1))
    ruis = Image.effect_noise((breedte, hoogte), 7).point(lambda v: v - 128 + 128)
    foto = Image.blend(foto, ruis, 0.06)
    return foto.convert("RGB")


def main(map_):
    uit = Path(map_)
    uit.mkdir(parents=True, exist_ok=True)
    verwachtingen = []
    for volg, (nummer, licht) in enumerate(KEUZE, 1):
        soort, naam, plaats, extra, letter, maat, _ = bonnen.BONNEN[nummer - 1]
        r = random.Random(1000 + nummer)
        random.seed(2000 + nummer)
        bon, v = soort(r, naam, plaats, extra) if extra is not None else soort(r, naam, plaats)
        bestand = f"ordner-{volg:02d}.jpg"
        ordnerfoto(random.Random(3000 + nummer), bonnen.teken(bon, letter, maat), licht).save(uit / bestand, quality=80)
        v.update(bestand=bestand, soort=soort.__name__, gebrek=f"ordner, inkt {int(licht * 100)}% lichter")
        verwachtingen.append(v)
    (uit / "verwacht.json").write_text(json.dumps(verwachtingen, indent=1, ensure_ascii=False), encoding="utf-8")
    print(f"{len(verwachtingen)} ordnerbonnen in {uit}")


if __name__ == "__main__":
    main(sys.argv[1] if len(sys.argv) > 1 else "ordnerbonnen")
