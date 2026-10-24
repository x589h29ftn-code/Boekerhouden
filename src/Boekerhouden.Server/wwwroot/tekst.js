// Tekst invoegen op de plek van de cursor in een tekstvak (bijv. een variabele in een toelichting). Het input-event
// laat Blazor de nieuwe waarde oppakken.
window.boekerhouden = window.boekerhouden || {};
window.boekerhouden.voegIn = (veld, tekst) => {
    const begin = veld.selectionStart ?? veld.value.length;
    const eind = veld.selectionEnd ?? veld.value.length;
    veld.value = veld.value.slice(0, begin) + tekst + veld.value.slice(eind);
    veld.selectionStart = veld.selectionEnd = begin + tekst.length;
    veld.focus();
    veld.dispatchEvent(new Event('input', { bubbles: true }));
};
