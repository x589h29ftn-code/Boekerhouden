// Lay-out bij verschuivingen: verspringt er iets tijdens het laden (layout shift), en blijft alles op zijn plek bij
// andere schermmaten en zoom (1024 tot 2560 breed), een smalle zijbalk, ruime weergave, het venster live kleiner en
// weer groter maken en de zijbalk in- en uitklappen? Per pagina: geen horizontale schuifbalk, niets rechts buiten het
// venster, geen elementen in de kop over elkaar, geen tekst buiten een tabelcel of knop, en de vaste knoppen (hulp,
// melding) niet over een knop of veld. Meldt per pagina en maat wat afwijkt; schermafbeeldingen van elke fout in <map>.
// Gebruik: scripts/ui/herstart.sh && node scripts/ui/verschuivingen.js <auditfile.xaf> <map>
// (de auditfile: zie README, Afdrukgegevens; met BOEKERHOUDEN_AFDRUKSCHAAL=15 voor grote bedragen)
const { chromium } = require('playwright');
const crypto = require('crypto');
const path = require('path');
const fs = require('fs');

function totp(sleutel) {
  const a = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'; let bits = '';
  for (const c of sleutel.replace(/\s/g, '').toUpperCase()) bits += a.indexOf(c).toString(2).padStart(5, '0');
  const b = []; for (let i = 0; i + 8 <= bits.length; i += 8) b.push(parseInt(bits.slice(i, i + 8), 2));
  const t = Buffer.alloc(8); t.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000)));
  const h = crypto.createHmac('sha1', Buffer.from(b)).update(t).digest(); const o = h[h.length - 1] & 0xf;
  return (((h[o] & 0x7f) << 24 | h[o + 1] << 16 | h[o + 2] << 8 | h[o + 3]) % 1000000).toString().padStart(6, '0');
}

// In de pagina: wat er niet op zijn plek staat.
function meet() {
  const fouten = [];
  const zichtbaar = e => e.checkVisibility({ visibilityProperty: true, opacityProperty: true }) && e.getBoundingClientRect().width > 0;
  const naam = e => (e.getAttribute('aria-label') || e.title || e.innerText || e.value || e.id || e.tagName).trim().replace(/\s+/g, ' ').slice(0, 30);
  const knipt = (e, tot) => { for (let a = e.parentElement; a && a !== tot && a !== document.body; a = a.parentElement) { if (getComputedStyle(a).overflowX !== 'visible') return a; } return null; };
  const W = document.documentElement.clientWidth;
  if (document.documentElement.scrollWidth > W + 1) fouten.push(`pagina schuift horizontaal (${document.documentElement.scrollWidth - W}px)`);

  // Rechts buiten het venster, zonder een kader dat scrolt of knipt.
  const buiten = [...document.querySelectorAll('main *, header *, nav *')].filter(e => zichtbaar(e) && e.getBoundingClientRect().right > W + 1
    && getComputedStyle(e).position !== 'fixed' && !knipt(e, null) && !e.closest('[popover]:not(:popover-open)'));
  for (const e of buiten.filter(e => !buiten.includes(e.parentElement)).slice(0, 3)) fouten.push(`buiten het venster: ${e.tagName.toLowerCase()} "${naam(e)}" tot ${Math.round(e.getBoundingClientRect().right)}px (venster ${W})`);

  // De kop: titel, knoppen en keuzes niet over elkaar.
  const kop = document.querySelector('.paginakop')?.parentElement;
  if (kop) {
    const delen = [...kop.querySelectorAll('h1, button, a.btn, label.btn, summary, select, input:not([type=hidden])')].filter(zichtbaar)
      .filter(e => !e.closest('[popover], .dropdown-menu, .menu') && !delenBinnen(e));
    function delenBinnen(e) { return e.parentElement?.closest('button, a.btn, label.btn, summary'); }
    for (let i = 0; i < delen.length; i++) for (let j = i + 1; j < delen.length; j++) {
      const a = delen[i].getBoundingClientRect(), b = delen[j].getBoundingClientRect();
      if (delen[i].contains(delen[j]) || delen[j].contains(delen[i])) continue;
      // Een knop die bewust in een veld staat (de kalender in een datumveld) is geen overlap.
      if (delen[i].parentElement === delen[j].parentElement && [delen[i], delen[j]].some(e => getComputedStyle(e).position === 'absolute')) continue;
      const x = Math.min(a.right, b.right) - Math.max(a.left, b.left), y = Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top);
      if (x > 2 && y > 2) fouten.push(`in de kop over elkaar: "${naam(delen[i])}" en "${naam(delen[j])}"`);
    }
  }

  // Tekst buiten een knop of tabelcel (die niet zelf afkapt).
  for (const k of [...document.querySelectorAll('main .btn, header .btn')].filter(zichtbaar)) {
    const s = getComputedStyle(k);
    if (k.scrollWidth > k.clientWidth + 2 && s.overflowX === 'visible' && s.textOverflow !== 'ellipsis') fouten.push(`tekst buiten knop "${naam(k)}"`);
  }
  for (const t of [...document.querySelectorAll('main table')].filter(zichtbaar)) {
    const koppen = t.tHead ? [...t.tHead.rows[0].cells].map(c => c.innerText.trim()) : [];
    for (const r of [...t.rows].filter(r => r.offsetParent !== null).slice(0, 30)) for (const c of r.cells) {
      const s = getComputedStyle(c);
      if (s.display === 'none' || s.overflowX !== 'visible') continue;
      const cr = c.getBoundingClientRect();
      const loper = document.createTreeWalker(c, NodeFilter.SHOW_TEXT);
      let links = Infinity, rechts = -Infinity;
      for (let n = loper.nextNode(); n; n = loper.nextNode()) {
        if (!n.textContent.trim() || n.parentElement.closest('.kolomgreep')) continue;
        let afgekapt = false;
        for (let x = n.parentElement; x && x !== c; x = x.parentElement) if (getComputedStyle(x).overflowX !== 'visible') { afgekapt = true; break; }
        if (afgekapt) continue;
        const range = document.createRange(); range.selectNodeContents(n);
        for (const q of range.getClientRects()) { links = Math.min(links, q.left); rechts = Math.max(rechts, q.right); }
      }
      if (isFinite(links) && (rechts > cr.right + 1.5 || links < cr.left - 1.5)) {
        fouten.push(`tekst buiten cel: kolom "${koppen[c.cellIndex] ?? c.cellIndex}" "${c.innerText.trim().slice(0, 25)}" (${Math.round(rechts - links)}px in ${Math.round(cr.width)}px)`);
      }
    }
    const tb = t.getBoundingClientRect();
    const kader = knipt(t, null);
    const vlak = (t.closest('.card, .overzichtskaart') || document.querySelector('.administratie-inhoud') || document.querySelector('main')).getBoundingClientRect();
    if (!kader && tb.right > vlak.right + 1.5) fouten.push(`tabel ${Math.round(tb.right - vlak.right)}px buiten zijn kader`);
  }

  // Vaste knoppen (hulp rechtsonder, melding linksonder) niet over een knop of veld, ook niet onderaan de pagina.
  const vast = [...document.querySelectorAll('body *')].filter(e => zichtbaar(e) && getComputedStyle(e).position === 'fixed' && !e.closest('[popover]')
    && e.getBoundingClientRect().height < 200 && e.getBoundingClientRect().top > innerHeight / 2);
  const bedienbaar = [...document.querySelectorAll('main button, main a, main input, main select, main textarea, main label.btn')].filter(e => zichtbaar(e) && !vast.some(v => v.contains(e)));
  for (const v of vast) {
    const a = v.getBoundingClientRect();
    for (const e of bedienbaar) {
      const b = e.getBoundingClientRect();
      if (Math.min(a.right, b.right) - Math.max(a.left, b.left) > 4 && Math.min(a.bottom, b.bottom) - Math.max(a.top, b.top) > 4) {
        fouten.push(`vast element "${naam(v)}" over "${naam(e)}"`);
        break;
      }
    }
  }
  return [...new Set(fouten)];
}

// Vóór het laden: de verschuivingen bijhouden (layout shift), met de elementen die verschoven.
function volgVerschuivingen() {
  window.__verschuivingen = [];
  try {
    new PerformanceObserver(lijst => {
      for (const v of lijst.getEntries()) {
        if (v.hadRecentInput) continue;
        window.__verschuivingen.push({
          waarde: v.value, tijd: Math.round(v.startTime),
          bronnen: (v.sources || []).map(b => b.node && b.node.nodeType === 1
            ? `${b.node.tagName.toLowerCase()}${b.node.id ? '#' + b.node.id : ''}${b.node.classList?.length ? '.' + [...b.node.classList].slice(0, 2).join('.') : ''} ${Math.round(b.previousRect.top)}→${Math.round(b.currentRect.top)}`
            : 'tekst'),
        });
      }
    }).observe({ type: 'layout-shift', buffered: true });
  } catch { /* geen ondersteuning */ }
}

(async () => {
  const [auditfile, uit] = [process.argv[2], process.argv[3] || '.'];
  fs.mkdirSync(uit, { recursive: true });
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1366, height: 768 } });
  await context.addInitScript(volgVerschuivingen);
  const page = await context.newPage();
  const meldingen = []; let huidig = '';
  const meld = (t) => meldingen.push(`${huidig}: ${t}`);
  page.on('pageerror', e => meld('console: ' + e.message));
  page.on('console', m => { if (m.type() === 'error' && !/favicon|ERR_ABORTED/.test(m.text())) meld('console: ' + m.text().slice(0, 160)); });
  const basis = 'http://localhost:5090';

  await page.goto(basis + '/'); await page.waitForURL(/eerste-gebruiker/);
  await page.fill('#naam', 'Bea Beheer'); await page.fill('#initialen', 'BB'); await page.fill('#email', 'beheer@kantoor.test');
  await page.fill('#wachtwoord', 'Groene-Appel-Boom-42'); await page.fill('#herhaal', 'Groene-Appel-Boom-42');
  await page.click('button[type=submit]'); await page.waitForURL(/account\/beveiliging/);
  await page.fill('#code', totp(await page.textContent('code.fs-6'))); await page.click('button:has-text("Aanzetten")');
  await page.waitForSelector('text=Je herstelcodes');
  await page.goto(basis + '/'); await page.waitForTimeout(1500);
  await page.click('text=Nieuwe administratie'); await page.waitForSelector('#naam'); await page.waitForTimeout(800);
  await page.setInputFiles('#auditfile', auditfile);
  await page.waitForURL(/\/a\/[0-9a-f-]{36}/, { timeout: 120000 });
  const id = page.url().split('/a/')[1].split(/[/?#]/)[0];
  const j = 'van=2025-01-01&tot=2025-12-31';
  const a = p => `/a/${id}${p}`;
  const kort = p => p.replace(`/a/${id}`, '/a/…');
  const paginas = ['/', '/administraties/nieuw', '/hulp', '/beheer/gebruikers', '/beheer/btw', '/beheer/instellingen', '/beheer/wijzigingslog',
    a(''), a('/inkoop'), a('/verkoop'), a('/verkoop/nieuw'), a('/verkoop/terugkerend'), a('/facturen'), a('/factuurlayout'), a('/bank'), a('/bank?geboekt=1'), a('/bank/openstaand'),
    a('/memoriaal'), a(`/kolommenbalans?${j}`), a(`/kolommenbalans?${j}&weergave=maand`), a(`/grootboek?rekening=1100&${j}`), a(`/grootboek?${j}`),
    a(`/journaal?${j}`), a('/debiteuren'), a('/debiteuren?weergave=posten'), a('/crediteuren'), a('/btw?jaar=2025'), a('/btw/rondrekening'), a('/vpb?jaar=2025'), a('/activa'), a('/activa/staat'),
    a('/activa/nieuw'), a('/aansluitingen'), a(`/inkoopboek?${j}`), a(`/verkoopboek?${j}`), a('/relaties'), a('/regels'), a('/rekeningschema'), a('/kostenplaatsen'),
    a('/perioden'), a('/instellingen'), a('/instellingen/btw'), a('/instellingen/verwerken'), a('/beginbalans'), a('/balans?jaar=2025'), a('/proefbalans?jaar=2025')];
  const foto = async (achtervoegsel) => page.screenshot({ path: path.join(uit, (kort(huidig).replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '') || 'start') + achtervoegsel + '.png') });

  // 1. Verspringen tijdens het laden (op een laptop).
  for (const pad of paginas) {
    huidig = `${kort(pad)} laden`;
    await page.goto(basis + pad); await page.waitForTimeout(2500);
    const v = await page.evaluate(() => window.__verschuivingen || []);
    const som = v.reduce((s, x) => s + x.waarde, 0);
    if (som > 0.02) {
      const grootste = v.sort((x, y) => y.waarde - x.waarde).slice(0, 2).map(x => `${x.waarde.toFixed(3)} na ${x.tijd}ms (${x.bronnen.slice(0, 3).join(', ')})`);
      meld(`verspringt bij laden: ${som.toFixed(3)}; ${grootste.join('; ')}`);
    }
  }

  // 2. Elke pagina op andere maten en weergaven.
  const weergaven = [
    { naam: '1024x700 (laptop met zoom)', w: 1024, h: 700 },
    { naam: '1280x720', w: 1280, h: 720 },
    { naam: '1280 zijbalk smal', w: 1280, h: 720, smal: true },
    { naam: '1280 ruim', w: 1280, h: 720, ruim: true },
    { naam: '1536x864 (1920 met 125%)', w: 1536, h: 864 },
    { naam: '2560x1440', w: 2560, h: 1440 },
  ];
  for (const wg of weergaven) {
    await page.setViewportSize({ width: wg.w, height: wg.h });
    await page.goto(basis + '/'); await page.waitForTimeout(500);
    await page.evaluate(([smal, ruim]) => {
      localStorage.setItem('zijbalk-smal', smal ? '1' : '0');
      if (ruim) localStorage.setItem('dichtheid', 'ruim'); else localStorage.removeItem('dichtheid');
    }, [!!wg.smal, !!wg.ruim]);
    for (const pad of paginas) {
      huidig = `${kort(pad)} @${wg.naam}`;
      await page.goto(basis + pad); await page.waitForTimeout(1600);
      let f = await page.evaluate(meet);
      // Ook onderaan de pagina (de vaste knoppen over de laatste regels of de opslaanbalk).
      await page.evaluate(() => window.scrollTo(0, document.documentElement.scrollHeight)); await page.waitForTimeout(250);
      f = [...new Set([...f, ...(await page.evaluate(meet))])];
      if (f.length) { for (const x of f.slice(0, 6)) meld(x); await foto(`-${wg.w}${wg.smal ? '-smal' : ''}${wg.ruim ? '-ruim' : ''}`); }
    }
  }
  await page.evaluate(() => { localStorage.setItem('zijbalk-smal', '0'); localStorage.removeItem('dichtheid'); });

  // 3. Het venster live kleiner en weer groter, en de zijbalk in- en uitklappen, zonder opnieuw te laden.
  const breedTabel = async () => page.evaluate(() => [...document.querySelectorAll('main table')].filter(t => t.offsetParent !== null)
    .map(t => { const k = (t.closest('.card, .overzichtskaart, .te-breed') || t.parentElement).getBoundingClientRect(); return Math.round(k.right - t.getBoundingClientRect().right); }));
  for (const pad of paginas.filter(p => p.startsWith('/a/'))) {
    huidig = `${kort(pad)} live`;
    await page.setViewportSize({ width: 1920, height: 1000 });
    await page.goto(basis + pad); await page.waitForTimeout(1600);
    const ruimte = await breedTabel();
    await page.setViewportSize({ width: 1100, height: 800 }); await page.waitForTimeout(700);
    for (const x of (await page.evaluate(meet)).slice(0, 4)) meld(`na verkleinen tot 1100: ${x}`);
    await page.setViewportSize({ width: 1920, height: 1000 }); await page.waitForTimeout(700);
    for (const x of (await page.evaluate(meet)).slice(0, 4)) meld(`na weer vergroten: ${x}`);
    const terug = await breedTabel();
    terug.forEach((r, i) => { if (ruimte[i] !== undefined && r > ruimte[i] + 40) meld(`tabel ${i + 1} vult na weer vergroten de breedte niet meer (${r}px leeg, eerst ${ruimte[i]}px)`); });
    if (await page.locator('[data-zijbalk]').count()) {
      await page.locator('[data-zijbalk]').first().click(); await page.waitForTimeout(600);
      for (const x of (await page.evaluate(meet)).slice(0, 4)) meld(`zijbalk smal: ${x}`);
      const smal = await breedTabel();
      smal.forEach((r, i) => { if (ruimte[i] !== undefined && r > ruimte[i] + 40) meld(`tabel ${i + 1} groeit niet mee met de smalle zijbalk (${r}px leeg)`); });
      await page.locator('[data-zijbalk]').first().click(); await page.waitForTimeout(600);
      for (const x of (await page.evaluate(meet)).slice(0, 4)) meld(`zijbalk weer breed: ${x}`);
    }
    if (meldingen.some(m => m.startsWith(huidig))) await foto('-live');
  }

  for (const m of [...new Set(meldingen)]) console.log(m);
  console.log(meldingen.length === 0 ? 'Alles blijft op zijn plek.' : `${new Set(meldingen).size} meldingen.`);
  await browser.close();
  process.exit(meldingen.length === 0 ? 0 : 1);
})();
