// Werken met het toetsenbord in lijsten met regels die data-toetsrij hebben (bijv. de bankmutaties):
// pijltje omhoog of omlaag gaat naar de vorige of volgende regel, Enter opent de regel (zoals klikken) en de spatie
// zet het vinkje van de regel aan of uit. Geen inline scripts (CSP): dit bestand wordt los geladen.
document.addEventListener('keydown', e => {
  const rij = e.target instanceof Element ? e.target.closest('tr[data-toetsrij]') : null;
  if (!rij || e.target !== rij || e.altKey || e.ctrlKey || e.metaKey) return;
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    let naar = e.key === 'ArrowDown' ? rij.nextElementSibling : rij.previousElementSibling;
    while (naar && !naar.matches('tr[data-toetsrij]')) naar = e.key === 'ArrowDown' ? naar.nextElementSibling : naar.previousElementSibling;
    if (naar) { e.preventDefault(); naar.focus(); naar.scrollIntoView({ block: 'nearest' }); }
  } else if (e.key === 'Enter') {
    e.preventDefault(); rij.click();
  } else if (e.key === ' ') {
    const vak = rij.querySelector('input[type=checkbox]');
    if (vak) { e.preventDefault(); vak.click(); }
  }
});

// Klikbare regels in de overzichten (inkoop, verkoop, journaal, relaties) doen ook mee: ze krijgen vanzelf een
// tabindex en data-toetsrij, ook als Blazor ze later tekent.
function koppel() {
  document.querySelectorAll('table tbody tr.klikbaar:not([data-toetsrij])').forEach(r => {
    r.tabIndex = 0;
    r.dataset.toetsrij = '';
  });
}

if (!window.toetsenGekoppeld) {
  window.toetsenGekoppeld = true;
  let gepland = false;
  new MutationObserver(() => {
    if (!gepland) { gepland = true; requestAnimationFrame(() => { gepland = false; koppel(); }); }
  }).observe(document.documentElement, { childList: true, subtree: true });
  koppel();
}

// Ctrl+K (of Cmd+K op de Mac) opent het zoekvenster: een klik op de zoekknop in de kopbalk.
document.addEventListener('keydown', e => {
  if ((e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && e.key.toLowerCase() === 'k') {
    const knop = document.getElementById('zoekknop');
    if (knop) { e.preventDefault(); knop.click(); }
  }
});

// De zijbalk van een administratie smal of breed; onthouden per browser.
const smal = 'zijbalk-smal';
try { if (localStorage.getItem(smal) === '1') document.documentElement.classList.add(smal); } catch { /* geen opslag */ }
document.addEventListener('click', e => {
  if (!(e.target instanceof Element) || !e.target.closest('[data-zijbalk]')) return;
  const aan = document.documentElement.classList.toggle(smal);
  try { localStorage.setItem(smal, aan ? '1' : '0'); } catch { /* geen opslag */ }
});
