// Een bestand uit .NET (bijv. een PDF) opslaan via de browser; werkt ook in de Windows-app (geen webserver nodig).
export async function bewaar(naam, type, stroom) {
    const data = await stroom.arrayBuffer();
    const url = URL.createObjectURL(new Blob([data], { type }));
    const a = document.createElement('a');
    a.href = url;
    a.download = naam;
    document.body.append(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
}
