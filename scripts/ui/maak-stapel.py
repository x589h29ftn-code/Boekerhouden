#!/usr/bin/env python3
"""Maakt een verzonnen stapel voor de browsercontrole: inkoopfacturen van één tot vijf pagina's achter elkaar in één
PDF, met een leeg blad en een pagina algemene voorwaarden (zoals tests/…/FictieveStapel.cs). Gebruik:
    python3 scripts/ui/maak-stapel.py /tmp/stapel.pdf
Vereist reportlab (alleen voor de browsercontrole, niet voor het pakket zelf)."""
import sys

from reportlab.lib.pagesizes import A4
from reportlab.pdfgen import canvas

FACTUREN = [
    ("Belnet B.V.", "NL91ABNA0417164300", "NL001234567B01", "BN-2026-0411", "01-04-2026", 1, 20.65, True, False),
    ("Groothandel De Linde B.V.", "NL18RABO0123459876", "NL002345678B01", "GL-88012", "03-04-2026", 3, 1250.00, True, False),
    ("Energie Noord N.V.", "NL69INGB0123456789", "NL003456789B01", "EN-2026-55120", "07-04-2026", 2, 310.40, False, False),
    ("Groothandel De Linde B.V.", "NL18RABO0123459876", "NL002345678B01", "GL-88047", "12-04-2026", 1, 96.00, True, False),
    ("Drukkerij Inkt V.O.F.", "NL44RABO0123456789", "NL004567890B01", "2026-0193", "15-04-2026", 5, 2480.00, True, False),
    ("Kantoorhal B.V.", "NL86INGB0002445588", "NL005678901B01", "KH-7741", "20-04-2026", 1, 54.90, False, True),
]
LEEG_NA = 1


def bedrag(waarde):
    return f"{waarde:,.2f}".replace(",", "X").replace(".", ",").replace("X", ".")


def maak(pad):
    c = canvas.Canvas(pad, pagesize=A4)
    for i, (naam, iban, btw, nummer, datum, paginas, exclusief, nummering, voorwaarden) in enumerate(FACTUREN):
        for p in range(1, paginas + 1):
            y = 800

            def regel(*delen, groot=False):
                nonlocal y
                c.setFont("Helvetica-Bold" if groot else "Helvetica", 16 if groot else 10)
                for x, tekst in delen:
                    c.drawString(x, y, tekst)
                y -= 22 if groot else 14

            regel((40, naam), groot=True)
            if p == 1:
                regel((40, "Dorpsstraat 1"), (350, "Voorbeeld Handel B.V."))
                regel((40, "1234 AB Ergens"), (350, "Laan 2"))
                regel((40, f"IBAN {iban}"))
                regel((40, f"BTW {btw}"))
                y -= 20
                regel((40, "Factuur"), groot=True)
                regel((40, "Factuurnummer:"), (160, nummer))
                regel((40, "Factuurdatum:"), (160, datum))
            elif nummering:
                regel((40, "Factuurnummer:"), (160, nummer))
            else:
                regel((40, "Specificatie van uw verbruik"))
            y -= 20
            regel((40, "Omschrijving"), (470, "Bedrag"))
            c.line(40, y + 10, 540, y + 10)
            for r in range(1, 19):
                regel((40, f"Levering {p}.{r}"), (470, bedrag(round(exclusief / paginas / 18, 2))))
            if p == paginas:
                y -= 10
                regel((300, "Subtotaal"), (470, bedrag(exclusief)))
                regel((300, "BTW 21%"), (470, bedrag(round(exclusief * 0.21, 2))))
                regel((300, "Totaal te betalen"), (470, bedrag(round(exclusief * 1.21, 2))))
            if nummering:
                c.setFont("Helvetica", 9)
                c.drawString(260, 40, f"Pagina {p} van {paginas}")
            c.showPage()
        if voorwaarden:
            y = 800
            c.setFont("Helvetica", 8)
            for tekst in ["Algemene voorwaarden"] + ["Op al onze leveringen zijn deze voorwaarden van toepassing. Betaling binnen de termijn op de voorzijde."] * 40:
                c.drawString(40, y, tekst)
                y -= 11
            c.showPage()
        if i == LEEG_NA:
            c.showPage()
    c.save()


if __name__ == "__main__":
    maak(sys.argv[1] if len(sys.argv) > 1 else "stapel.pdf")
