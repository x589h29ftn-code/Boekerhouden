// Het rekeningveld in één keer aanpassen: de cursor erin en de gekozen rekening geselecteerd, zodat typen hem vervangt.
export function selecteer(veld) {
    if (!veld) return;
    veld.focus();
    veld.select();
}
