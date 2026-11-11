"""Maakt foto-bon-bouwmarkt.jpg: een verzonnen kassabon als 'telefoonfoto' voor de tests: vergeeld papier op een
donkere tafel, een schaduw over de onderste helft en een beetje scheef. Alle gegevens zijn verzonnen.
Opnieuw maken: python3 maak-bon.py (met Pillow en het DejaVu-lettertype)."""
from PIL import Image, ImageDraw, ImageFilter, ImageFont

MONO = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono.ttf"
VET = "/usr/share/fonts/truetype/dejavu/DejaVuSansMono-Bold.ttf"
BREEDTE, HOOGTE = 760, 1500

bon = Image.new("RGB", (BREEDTE, HOOGTE), (236, 226, 200))
teken = ImageDraw.Draw(bon)
gewoon = ImageFont.truetype(MONO, 30)
groot = ImageFont.truetype(VET, 44)
inkt = (70, 66, 74)
y = 50


def regel(tekst, letter=gewoon, midden=False):
    global y
    x = (BREEDTE - teken.textlength(tekst, font=letter)) / 2 if midden else 40
    teken.text((x, y), tekst, font=letter, fill=inkt)
    y += 62 if letter is groot else 44


regel("BOUWMARKT DE HAMER", groot, True)
regel("Industrieweg 14", midden=True)
regel("3811 AB Amersfoort", midden=True)
regel("BTW NL805512342B01", midden=True)
y += 30
regel("Datum 14-03-2026    Tijd 16:42")
regel("Kassa 03   Bon 0048213")
y += 20
regel("Houtschroeven 4x40 200st    8,95")
regel("Multiplex 18mm 122x61      24,50")
regel("Kwast set 3-delig           6,75")
regel("Muurverf wit 10L           39,95")
y += 10
regel("--------------------------------")
regel("TOTAAL             80,15", groot)
regel("--------------------------------")
regel("PIN                        80,15")
y += 20
regel("BTW%     Excl      BTW     Incl")
regel("21%     66,24    13,91    80,15")
y += 30
regel("Terminal 7K21B4  Periode 3074")
regel("Transactie 02213564")
regel("Kaart xxxxxxxxxxxx4411")
y += 30
regel("Bedankt en tot ziens!", midden=True)

# Schaduw over de onderste helft en een donkere hoek, zoals bij een foto onder een lamp.
schaduw = Image.new("L", (BREEDTE, HOOGTE), 255)
st = ImageDraw.Draw(schaduw)
for rij in range(HOOGTE):
    st.line([(0, rij), (BREEDTE, rij)], fill=int(255 - 110 * max(0, (rij - 500) / (HOOGTE - 500))))
st.ellipse([BREEDTE - 320, -200, BREEDTE + 260, 330], fill=150)
schaduw = schaduw.filter(ImageFilter.GaussianBlur(70))
bon = Image.composite(bon, Image.new("RGB", bon.size, (20, 18, 16)), schaduw)

tafel = Image.new("RGB", (1000, 1750), (58, 44, 36))
gedraaid = bon.rotate(2.5, expand=True, fillcolor=(58, 44, 36), resample=Image.BICUBIC)
tafel.paste(gedraaid, ((1000 - gedraaid.width) // 2, (1750 - gedraaid.height) // 2))
tafel = tafel.filter(ImageFilter.GaussianBlur(0.8))
tafel.save("foto-bon-bouwmarkt.jpg", quality=80)
