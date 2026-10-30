// De kalender van de browser openen bij een (verborgen) datumveld.
export function kies(veld) {
    try {
        veld.showPicker();
    } catch {
        veld.focus();
    }
}
