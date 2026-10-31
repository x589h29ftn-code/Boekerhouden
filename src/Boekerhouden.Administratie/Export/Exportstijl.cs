namespace Boekerhouden.Administratie.Export;

/// <summary>De stijl van de PDF's (A4 liggend): het lettertype en de kleuren van het pakket, voor alle exports gelijk.</summary>
internal static class Exportstijl
{
    public const string Css = """
        @page { size: A4 landscape; margin: 14mm 12mm; }
        body { font-family: system-ui, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif; font-size: 9pt; color: #212529; }
        h1 { font-size: 14pt; font-weight: 600; margin: 0 0 8pt; color: #212529; }
        .kop { display: flex; gap: 3rem; padding: 7pt 10pt; margin-bottom: 14pt; background: #f4f6f8; border: 1px solid #e3e7eb; border-radius: 4pt; }
        .kop span { color: #6c757d; margin-right: .5rem; }
        h2 { font-size: 10.5pt; font-weight: 600; margin: 14pt 0 4pt; }
        h2 .code { display: inline-block; min-width: 6rem; color: #6c757d; }
        table { width: 100%; border-collapse: collapse; table-layout: fixed; border: 1px solid #e3e7eb; }
        th { font-size: 7pt; font-weight: 600; letter-spacing: .05em; text-transform: uppercase; color: #6c757d; text-align: left;
             background: #f4f6f8; border-bottom: 1px solid #1d5c63; padding: 4pt 5pt; vertical-align: bottom; }
        td { padding: 2.5pt 5pt; border-bottom: 1px solid #eef0f2; }
        .r { text-align: right; white-space: nowrap; font-variant-numeric: tabular-nums; }
        .datum { white-space: nowrap; }
        .zacht { color: #6c757d; }
        tr.totaal td { font-weight: 600; background: #f4f6f8; border-bottom: 0; }
        table.generaal { margin-top: 12pt; }
        table.generaal td { font-weight: 600; font-size: 10pt; background: #f4f6f8; border-top: 1px solid #1d5c63; border-bottom: 0; }
        .relatie { break-inside: avoid; }
        .voet { margin-top: 10pt; font-size: 7.5pt; color: #6c757d; }
        """;
}
