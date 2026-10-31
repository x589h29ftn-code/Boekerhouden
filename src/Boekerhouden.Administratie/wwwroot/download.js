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

// Een document (HTML, zoals de PDF opgemaakt) afdrukken: in een verborgen frame laden en daar het afdrukvenster openen.
export function afdrukken(html) {
    const frame = document.createElement('iframe');
    frame.style.position = 'fixed';
    frame.style.width = '0';
    frame.style.height = '0';
    frame.style.border = '0';
    frame.onload = () => {
        frame.contentWindow.focus();
        frame.contentWindow.print();
        setTimeout(() => frame.remove(), 60000);
    };
    frame.srcdoc = html;
    document.body.append(frame);
}
