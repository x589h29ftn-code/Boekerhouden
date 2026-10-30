"""Maakt scan-kantoorvakhandel.png: een verzonnen inkoopfactuur als 'scan' (alleen pixels, geen tekstlaag), voor de
OCR-tests. Alle gegevens zijn verzonnen. Opnieuw maken: python3 maak-scan.py (met Pillow en het DejaVu-lettertype)."""
from PIL import Image, ImageDraw, ImageFont

BREEDTE, HOOGTE = 1240, 1754  # A4 op 150 dpi
LETTER = "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"
VET = "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf"

beeld = Image.new("L", (BREEDTE, HOOGTE), 255)
teken = ImageDraw.Draw(beeld)
gewoon = ImageFont.truetype(LETTER, 26)
groot = ImageFont.truetype(VET, 44)


def regel(x, y, tekst, letter=gewoon):
    teken.text((x, y), tekst, font=letter, fill=0)


regel(90, 90, "Kantoorvakhandel Pen B.V.", groot)
regel(90, 160, "Papierstraat 12")
regel(90, 195, "3511 AB Utrecht")
regel(90, 230, "IBAN NL20 INGB 0001 2345 67")
regel(90, 265, "BTW NL800000006B01")
regel(760, 160, "Test BV")
regel(760, 195, "Industrieweg 5")
regel(760, 230, "1234 AB Amersfoort")
regel(90, 380, "FACTUUR", groot)
regel(90, 460, "Factuurnummer:")
regel(330, 460, "KV-2026-0815")
regel(90, 500, "Factuurdatum:")
regel(330, 500, "12-05-2026")
regel(90, 600, "Omschrijving")
regel(980, 600, "Bedrag")
regel(90, 650, "Bureaustoel ergonomisch")
regel(980, 650, "350,00")
regel(90, 690, "Bureaulamp")
regel(980, 690, "50,00")
regel(660, 780, "Subtotaal")
regel(980, 780, "400,00")
regel(660, 820, "BTW 21%")
regel(980, 820, "84,00")
regel(660, 860, "Totaal")
regel(980, 860, "484,00")
regel(90, 1000, "Betalen binnen 30 dagen.")
beeld.save("scan-kantoorvakhandel.png", optimize=True)
