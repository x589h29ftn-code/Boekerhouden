// Kolommen smaller en breder slepen op alle pagina's met versleepbare tabellen (kolommen.js), met verzonnen gegevens
// met grote bedragen en lange namen, en meten of tekst buiten zijn cel of de tabel buiten zijn kader valt (dan loopt een
// bedrag over de kolom ernaast of schuift de hele pagina). Gebruik:
//   scripts/ui/herstart.sh && node scripts/ui/kolombreedte.js <map> [schermbreedte]
// Schrijft <map>/kolombreedte.xaf (de gegevens), schermafbeeldingen van elke fout en twee vaste voorbeelden, en stopt
// met exitcode 1 als er iets buiten valt.
const { chromium } = require('playwright');
const fs = require('fs');
const crypto = require('crypto');
function totp(s) {
  const a = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'; let bits = '';
  for (const c of s.replace(/\s/g, '').toUpperCase()) bits += a.indexOf(c).toString(2).padStart(5, '0');
  const b = []; for (let i = 0; i + 8 <= bits.length; i += 8) b.push(parseInt(bits.slice(i, i + 8), 2));
  const t = Buffer.alloc(8); t.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000)));
  const h = crypto.createHmac('sha1', Buffer.from(b)).update(t).digest(); const o = h[h.length - 1] & 0xf;
  return (((h[o] & 0x7f) << 24 | h[o + 1] << 16 | h[o + 2] << 8 | h[o + 3]) % 1000000).toString().padStart(6, '0');
}
// Een verzonnen auditfile (XAF 3.2): één boekjaar met verkoop, inkoop, memoriaal en bank, bedragen tot over het miljard.
function auditfile() {
  let zaad = 7;
  const toeval = (a, b) => { zaad = (zaad * 16807) % 2147483647; return a + (zaad % (b - a + 1)); };
  const jaar = 2026, e = (t, v) => `<${t}>${v}</${t}>`, bedrag = c => (c / 100).toFixed(2);
  const rek = [['0100', 'Bedrijfsgebouwen en terreinen met een lange omschrijving', 'B', 'BMvaBegBeg'], ['0500', 'Overige reserves', 'B', 'BEivOvrOvr'],
    ['1100', 'Bank zakelijke rekening', 'B', 'BLimBanRba'], ['1300', 'Debiteuren', 'B', 'BVorDebHad'], ['1500', 'Te vorderen omzetbelasting', 'B', 'BVorVbkTvo'],
    ['1600', 'Crediteuren', 'B', 'BSchCreHac'], ['1700', 'Af te dragen omzetbelasting', 'B', 'BSchBepBtw'],
    ['4000', 'Lonen en salarissen inclusief vakantiegeld en eindejaarsuitkering', 'P'], ['4300', 'Huisvestingskosten', 'P'],
    ['4500', 'Autokosten leasewagens directie en buitendienst', 'P'], ['8000', 'Omzet hoog tarief', 'P']];
  const rel = ['Groothandel Van der Veen en Zonen Holding B.V.', 'Bakkerij De Korenbloem', 'Transportbedrijf Noord-Nederland B.V.',
    'Installatietechniek Zuidwest V.O.F.', 'Advocatenkantoor Mr. A.B. Jansen-de Vries'].map((n, i) => [`R00${i + 1}`, n]);
  const dagboeken = { VRK: [], INK: [], MEM: [], BNK: [] };
  let n = 0;
  for (let maand = 1; maand <= 12; maand++) {
    for (let k = 0; k < 6; k++) {
      n++;
      const dt = `${jaar}-${String(maand).padStart(2, '0')}-${String(1 + k * 4).padStart(2, '0')}`;
      const b = [toeval(100, 99999), toeval(100000, 9999999), toeval(10000000, 1234567890)][toeval(0, 2)], btw = Math.round(b * 0.21);
      const [code, naam] = rel[n % rel.length], fn = `F${jaar}-${String(n).padStart(5, '0')}`, nr = `${jaar}-${String(n).padStart(4, '0')}`;
      if (k % 3 === 0) dagboeken.VRK.push([nr, dt, `Verkoop ${naam} ${fn}`, [['1300', b + btw, code, fn], ['8000', -b], ['1700', -btw]]]);
      else if (k % 3 === 1) dagboeken.INK.push([nr, dt, `Inkoop ${naam} ${fn} met een lange omschrijving`, [[k % 2 ? '4500' : '4300', b], ['1500', btw], ['1600', -(b + btw), code, fn]]]);
      else dagboeken.MEM.push([nr, dt, `Lonen en sociale lasten ${maand}`, [['4000', b], ['1100', -b]]]);
    }
    dagboeken.BNK.push([`${jaar}-B${String(maand).padStart(2, '0')}`, `${jaar}-${String(maand).padStart(2, '0')}-28`, 'Ontvangst klant', [['1100', 98765432112], ['1300', -98765432112, rel[0][0]]]]);
  }
  const regels = Object.values(dagboeken).flat().flatMap(t => t[3]);
  const som = teken => bedrag(regels.filter(r => teken * r[1] > 0).reduce((a, r) => a + Math.abs(r[1]), 0));
  const soort = { VRK: ['S', 'Verkoop'], INK: ['P', 'Inkoop'], MEM: ['Z', 'Memoriaal'], BNK: ['B', 'Bank'] };
  return `<?xml version="1.0" encoding="utf-8"?><auditfile xmlns="http://www.auditfiles.nl/XAF/3.2"><header>${e('fiscalYear', jaar)}${e('startDate', `${jaar}-01-01`)}${e('endDate', `${jaar}-12-31`)}${e('curCode', 'EUR')}${e('dateCreated', `${jaar}-11-25`)}${e('softwareDesc', 'Testgegevens')}${e('softwareVersion', '1')}</header>`
    + `<company>${e('companyIdent', '12345678')}${e('companyName', 'Grote Getallen Holding B.V.')}${e('taxRegistrationCountry', 'NL')}${e('taxRegIdent', 'NL001234567B01')}`
    + `<customersSuppliers>${rel.map(([c, nm]) => `<customerSupplier>${e('custSupID', c)}${e('custSupName', nm)}${e('custSupTp', 'B')}</customerSupplier>`).join('')}</customersSuppliers>`
    + `<generalLedger>${rek.map(([nr, nm, tp, rgs]) => `<ledgerAccount>${e('accID', nr)}${e('accDesc', nm)}${e('accTp', tp)}${rgs ? e('leadReference', rgs) : ''}</ledgerAccount>`).join('')}</generalLedger>`
    + `<vatCodes><vatCode>${e('vatID', 'V21')}${e('vatDesc', 'Verkoop 21%')}${e('vatToPayAccID', '1700')}</vatCode><vatCode>${e('vatID', 'I21')}${e('vatDesc', 'Inkoop 21%')}${e('vatToClaimAccID', '1500')}</vatCode></vatCodes>`
    + `<periods>${[...Array(12)].map((_, i) => `<period>${e('periodNumber', i + 1)}${e('periodDesc', `maand ${i + 1}`)}${e('startDatePeriod', `${jaar}-${String(i + 1).padStart(2, '0')}-01`)}${e('endDatePeriod', `${jaar}-${String(i + 1).padStart(2, '0')}-28`)}</period>`).join('')}</periods>`
    + `<transactions>${e('linesCount', regels.length)}${e('totalDebit', som(1))}${e('totalCredit', som(-1))}`
    + Object.entries(dagboeken).map(([d, lijst]) => `<journal>${e('jrnID', d)}${e('desc', soort[d][1])}${e('jrnTp', soort[d][0])}${d === 'BNK' ? e('offsetAccID', '1100') : ''}`
      + lijst.map(([nr, dt, oms, rr]) => `<transaction>${e('nr', nr)}${e('desc', oms)}${e('periodNumber', Number(dt.slice(5, 7)))}${e('trDt', dt)}${e('amnt', bedrag(rr.filter(r => r[1] > 0).reduce((a, r) => a + r[1], 0)))}${e('amntTp', 'D')}`
        + rr.map(([acc, b, cs, inv], i) => `<trLine>${e('nr', i + 1)}${e('accID', acc)}${e('docRef', inv || nr)}${e('effDate', dt)}${e('desc', oms)}${e('amnt', bedrag(Math.abs(b)))}${e('amntTp', b < 0 ? 'C' : 'D')}${cs ? e('custSupID', cs) : ''}${inv ? e('invRef', inv) : ''}</trLine>`).join('')
        + '</transaction>').join('') + '</journal>').join('')
    + '</transactions></company></auditfile>';
}

// In de pagina: wat er buiten valt in tabel t.
function meet(t) {
  const fouten = [];
  const koppen = [...t.tHead.rows[0].cells].map(c => c.innerText.trim());
  const rijen = [...t.rows].filter(r => r.offsetParent !== null).slice(0, 40);
  for (const r of rijen) for (const c of r.cells) {
    const s = getComputedStyle(c);
    if (s.display === 'none') continue;
    const cr = c.getBoundingClientRect();
    // Tekst die buiten de cel steekt (en niet wordt afgekapt).
    // Alleen de tekst (zonder de sleepgreep): de uiterste randen van alle tekstknopen in de cel.
    const loper = document.createTreeWalker(c, NodeFilter.SHOW_TEXT);
    let links = Infinity, rechts = -Infinity, knipt = false;
    for (let n = loper.nextNode(); n; n = loper.nextNode()) {
      if (!n.textContent.trim() || n.parentElement.closest('.kolomgreep')) continue;
      // Een element binnen de cel dat zelf afkapt (bijv. .text-truncate) telt als afgekapt.
      for (let e = n.parentElement; e && e !== c; e = e.parentElement) { const es = getComputedStyle(e); if (es.overflowX !== 'visible') { knipt = true; break; } }
      const range = document.createRange(); range.selectNodeContents(n);
      for (const r of range.getClientRects()) { links = Math.min(links, r.left); rechts = Math.max(rechts, r.right); }
    }
    const tr = { left: links, right: rechts, width: rechts - links };
    const knip = knipt || s.overflow !== 'visible' || s.overflowX !== 'visible';
    if (!knip && isFinite(tr.width) && tr.width > 0 && (tr.right > cr.right + 1.5 || tr.left < cr.left - 1.5)) {
      fouten.push(`cel buiten: kolom "${koppen[c.cellIndex] ?? c.cellIndex}" "${c.innerText.trim().slice(0, 30)}" (${Math.round(tr.width)}px tekst in ${Math.round(cr.width)}px)`);
    }
  }
  // De tabel buiten zijn kader: het eerste voorouder dat knipt of scrollt, of anders het inhoudsvlak.
  const tb = t.getBoundingClientRect();
  let k = t.parentElement, kader = null;
  while (k && k !== document.body) { const s = getComputedStyle(k); if (s.overflowX !== 'visible') { kader = k; break; } k = k.parentElement; }
  const vlak = document.querySelector('.administratie-inhoud') || document.querySelector('main');
  if (!kader || kader.contains(vlak)) {
    const v = (t.closest('.card, .overzichtskaart') || vlak).getBoundingClientRect();
    if (tb.right > v.right + 1.5) fouten.push(`tabel ${Math.round(tb.right - v.right)}px buiten zijn kader`);
  }
  if (document.documentElement.scrollWidth > document.documentElement.clientWidth + 1) fouten.push('pagina schuift horizontaal');
  return [...new Set(fouten)];
}
(async () => {
  const uit = process.argv[2] || '.', breed = Number(process.argv[3] || 1600);
  fs.mkdirSync(uit, { recursive: true });
  const xaf = `${uit}/kolombreedte.xaf`;
  fs.writeFileSync(xaf, auditfile());
  const basis = 'http://localhost:5090';
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: breed, height: 1000 } });
  const log = []; page.on('pageerror', e => log.push(e.message));
  await page.goto(basis + '/'); await page.waitForURL(/eerste-gebruiker/);
  await page.fill('#naam', 'Bea Beheer'); await page.fill('#initialen', 'BB'); await page.fill('#email', 'beheer@kantoor.test');
  await page.fill('#wachtwoord', 'Groene-Appel-Boom-42'); await page.fill('#herhaal', 'Groene-Appel-Boom-42');
  await page.click('button[type=submit]'); await page.waitForURL(/account\/beveiliging/);
  await page.fill('#code', totp(await page.textContent('code.fs-6'))); await page.click('button:has-text("Aanzetten")');
  await page.waitForSelector('text=Je herstelcodes');
  await page.goto(basis + '/administraties/nieuw'); await page.waitForSelector('#auditfile', { state: 'attached' }); await page.waitForTimeout(800);
  await page.setInputFiles('#auditfile', xaf); await page.waitForURL(/\/a\/[0-9a-f-]+$/, { timeout: 30000 }); await page.waitForTimeout(1000);
  const id = page.url().split('/a/')[1];
  const p = `/a/${id}`;
  const paginas = [
    '/', `${p}/kolommenbalans`, `${p}/kolommenbalans?weergave=maand`, `${p}/grootboek?rekening=8000`, `${p}/grootboek?rekening=1100`, `${p}/dagboeken`,
    `${p}/journaal`, `${p}/debiteuren`, `${p}/crediteuren`, `${p}/proefbalans`, `${p}/balans`, `${p}/relaties`, `${p}/rekeningschema`,
    `${p}/aansluitingen`, `${p}/inkoopboek`, `${p}/verkoopboek`, `${p}/memoriaal`, `${p}/regels`, '/beheer/wijzigingslog'];
  const alle = [], breedtes = [];
  for (const pad of paginas) {
    await page.goto(basis + pad); await page.waitForTimeout(1800);
    const sleutels = await page.$$eval('table[data-kolommen]', ts => ts.filter(t => t.offsetParent !== null).map(t => t.dataset.kolommen));
    for (const sleutel of sleutels) {
      const t = page.locator(`table[data-kolommen="${sleutel}"]`).first();
      const aantal = await t.locator('thead tr').first().locator('th').count();
      const begin = await t.evaluate(meet);
      if (begin.length) alle.push(`${pad.replace(/\/a\/[0-9a-f-]+/, '')} [${sleutel}] zonder slepen: ${begin.slice(0, 4).join('; ')}`);
      for (let i = 0; i < aantal; i++) {
        for (const dx of [-700, 600]) {
          const greep = t.locator('thead tr').first().locator('th').nth(i).locator('.kolomgreep');
          if (!(await greep.count())) continue;
          const b = await greep.boundingBox(); if (!b) continue;
          await page.mouse.move(b.x + b.width / 2, b.y + b.height / 2); await page.mouse.down();
          await page.mouse.move(b.x + dx / 2, b.y + b.height / 2, { steps: 4 }); await page.mouse.move(b.x + dx, b.y + b.height / 2, { steps: 4 }); await page.mouse.up();
          await page.waitForTimeout(150);
          const f = await t.evaluate(meet);
          if (dx < 0) { const w = await t.evaluate((t, i) => Math.round(t.tHead.rows[0].cells[i].getBoundingClientRect().width), i); breedtes.push(`${pad.replace(/\/a\/[0-9a-f-]+/, '')} [${sleutel}] ${i + 1}:${w}`); }
          if (f.length) {
            alle.push(`${pad.replace(/\/a\/[0-9a-f-]+/, '')} [${sleutel}] kolom ${i + 1} ${dx < 0 ? 'smaller' : 'breder'}: ${f.slice(0, 3).join('; ')}${f.length > 3 ? ` (+${f.length - 3})` : ''}`);
            await page.screenshot({ path: `${uit}/${sleutel.replace(/\W/g, '_')}-${i + 1}-${dx < 0 ? 'smal' : 'breed'}.png` });
          }
          await greep.dispatchEvent('dblclick'); await page.waitForTimeout(120);
        }
      }
    }
  }
  // Twee vaste voorbeelden: een bedragkolom zo smal mogelijk en de laatste kolom van het journaal veel breder.
  for (const [pad, sleutel, kolom, dx, naam] of [[`${p}/kolommenbalans`, 'kolommenbalans', 3, -700, 'kolommenbalans-smal'], [`${p}/journaal`, 'journaal', 5, 600, 'journaal-breed']]) {
    await page.goto(basis + pad); await page.waitForTimeout(1800);
    const b = await page.locator(`table[data-kolommen="${sleutel}"] thead th`).nth(kolom).locator('.kolomgreep').boundingBox();
    await page.mouse.move(b.x + 2, b.y + b.height / 2); await page.mouse.down();
    await page.mouse.move(b.x + dx, b.y + b.height / 2, { steps: 8 }); await page.mouse.up(); await page.waitForTimeout(300);
    await page.screenshot({ path: `${uit}/${naam}.png` });
  }
  console.log(alle.join('\n') || 'geen problemen');
  fs.writeFileSync(`${uit}/smalste.txt`, breedtes.join('\n'));
  console.log(log.join('\n')); await browser.close();
  process.exit(alle.length || log.length ? 1 : 0);
})();
