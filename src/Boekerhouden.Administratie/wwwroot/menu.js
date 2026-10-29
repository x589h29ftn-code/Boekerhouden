// De stand van het zijmenu van een administratie (ingeklapt, dichte groepen), per browser onthouden.
const sleutel = 'boekerhouden.menu';

export function lees() {
    try {
        return localStorage.getItem(sleutel);
    } catch {
        return null;
    }
}

export function bewaar(waarde) {
    try {
        localStorage.setItem(sleutel, waarde);
    } catch {
        // Geen opslag: dan geldt de stand alleen in dit venster.
    }
}
