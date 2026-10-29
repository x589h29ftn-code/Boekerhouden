// Sneltoetsen van het verwerkscherm. Ctrl+Enter = boeken, Alt+↑/↓ = vorige of volgende factuur, Alt+1…5 = naar
// factuurnummer, factuurdatum, omschrijving, rekening of bedrag (dan wijst een gebied op de factuur dat veld in).
const velden = ['#nummer', '#datum', '#omschrijving', 'input[aria-label="Rekening"]', 'input[aria-label="Bedrag exclusief btw"]'];

export function koppel(dotnet) {
    const opToets = (e) => {
        if (e.ctrlKey && !e.altKey && e.key === 'Enter') {
            e.preventDefault();
            // Eerst het veld verlaten, zodat een waarde die pas bij "change" doorkomt (zoals een datum) mee gaat.
            document.activeElement?.blur();
            dotnet.invokeMethodAsync('Sneltoets', 'boeken');
        } else if (e.altKey && !e.ctrlKey && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) {
            e.preventDefault();
            dotnet.invokeMethodAsync('Sneltoets', e.key === 'ArrowDown' ? 'volgende' : 'vorige');
        } else if (e.altKey && !e.ctrlKey && /^Digit[1-5]$/.test(e.code)) {
            const veld = document.querySelector(velden[Number(e.code.slice(5)) - 1]);
            if (veld) {
                e.preventDefault();
                veld.focus();
                veld.select?.();
            }
        }
    };
    document.addEventListener('keydown', opToets);
    return { los: () => document.removeEventListener('keydown', opToets) };
}
