// Weergave per browser: licht, donker of zoals het systeem, en de dichtheid (compact of ruim). Gewoon script in de
// kop (geen module), zodat het thema er staat vóór de pagina zichtbaar wordt; geen inline scripts (CSP).
(function () {
  var h = document.documentElement;
  var donkerSysteem = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

  function lees(naam, standaard) {
    try { return localStorage.getItem(naam) || standaard; } catch (e) { return standaard; }
  }

  function bewaar(naam, waarde) {
    try { localStorage.setItem(naam, waarde); } catch (e) { /* geen opslag */ }
  }

  function pas() {
    var thema = lees('thema', 'licht');
    var donker = thema === 'donker' || (thema === 'systeem' && donkerSysteem && donkerSysteem.matches);
    if (donker) { h.setAttribute('data-bs-theme', 'dark'); } else { h.removeAttribute('data-bs-theme'); }
    h.classList.toggle('ruim', lees('dichtheid', 'compact') === 'ruim');
    var keuzes = document.querySelectorAll('[data-thema], [data-dichtheid]');
    for (var i = 0; i < keuzes.length; i++) {
      var k = keuzes[i];
      var aan = k.hasAttribute('data-thema') ? k.getAttribute('data-thema') === thema : k.getAttribute('data-dichtheid') === lees('dichtheid', 'compact');
      k.classList.toggle('gekozen', aan);
      k.setAttribute('aria-pressed', aan ? 'true' : 'false');
    }
  }

  pas();
  if (donkerSysteem && donkerSysteem.addEventListener) { donkerSysteem.addEventListener('change', pas); }
  document.addEventListener('click', function (e) {
    var knop = e.target instanceof Element ? e.target.closest('[data-thema], [data-dichtheid]') : null;
    if (!knop) { return; }
    if (knop.hasAttribute('data-thema')) { bewaar('thema', knop.getAttribute('data-thema')); }
    else { bewaar('dichtheid', knop.getAttribute('data-dichtheid')); }
    pas();
  });
  // Het menu wordt door Blazor getekend (ook later): de gekozen knop bijwerken als hij verschijnt.
  document.addEventListener('toggle', function (e) { if (e.target instanceof Element && e.target.matches('.gebruikersmenu')) { pas(); } }, true);
})();
