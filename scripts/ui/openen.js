// Bestanden openen vanuit Windows (dubbelklik, Openen met): de Windows-app geeft ze met window.siferOpen aan de pagina.
// Hier hetzelfde script (uit Vensterstijl.cs) in de webapp: een auditfile op de startpagina wordt een nieuwe
// administratie, een document op een pagina zonder eigen sleepvlak gaat via "overal inlezen", op een pagina met een
// eigen sleepvlak eerst naar het overzicht, en zonder administratie wacht het met een melding. F5 en Ctrl+R laden niet opnieuw.
// Gebruik: scripts/ui/herstart.sh && node scripts/ui/openen.js <auditfile.xaf>
const { chromium } = require('playwright');
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

function totp(sleutel) {
  const a = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'; let bits = '';
  for (const c of sleutel.replace(/\s/g, '').toUpperCase()) bits += a.indexOf(c).toString(2).padStart(5, '0');
  const b = []; for (let i = 0; i + 8 <= bits.length; i += 8) b.push(parseInt(bits.slice(i, i + 8), 2));
  const t = Buffer.alloc(8); t.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000)));
  const h = crypto.createHmac('sha1', Buffer.from(b)).update(t).digest(); const o = h[h.length - 1] & 0xf;
  return (((h[o] & 0x7f) << 24 | h[o + 1] << 16 | h[o + 2] << 8 | h[o + 3]) % 1000000).toString().padStart(6, '0');
}

(async () => {
  const auditfile = process.argv[2];
  const browser = await chromium.launch();
  const context = await browser.newContext({ viewport: { width: 1400, height: 900 } });
  const page = await context.newPage();
  const fouten = []; const meld = t => { fouten.push(t); console.log('FOUT: ' + t); };
  page.on('pageerror', e => meld('console: ' + e.message));
  const basis = 'http://localhost:5090';
  const cs = fs.readFileSync(path.resolve(__dirname, '../../src/Boekerhouden.Administratie.Windows/Vensterstijl.cs'), 'utf8');
  await context.addInitScript(cs.split('private const string Script = """')[1].split('""";')[0]);
  const fictief = path.resolve(__dirname, '../../tests/Fictief/administratie');
  const open = (naam, pad) => page.evaluate(([n, i]) => window.siferOpen([{ naam: n, inhoud: i }]), [naam, fs.readFileSync(pad).toString('base64')]);
  async function melding() {
    for (let t = 0; t < 20; t++) { await page.waitForTimeout(500); const m = (await page.locator('.melding-kort').allInnerTexts()).join(' | '); if (m) return m; }
    return '';
  }

  await page.goto(basis + '/'); await page.waitForURL(/eerste-gebruiker/);
  await page.fill('#naam', 'Bea Beheer'); await page.fill('#initialen', 'BB'); await page.fill('#email', 'beheer@kantoor.test');
  await page.fill('#wachtwoord', 'Groene-Appel-Boom-42'); await page.fill('#herhaal', 'Groene-Appel-Boom-42');
  await page.click('button[type=submit]'); await page.waitForURL(/account\/beveiliging/);
  await page.fill('#code', totp(await page.textContent('code.fs-6'))); await page.click('button:has-text("Aanzetten")');
  await page.waitForSelector('text=Je herstelcodes');

  // 1. Een auditfile op de startpagina: een nieuwe administratie.
  await page.goto(basis + '/'); await page.waitForTimeout(1500);
  await open('voorbeeld.xaf', auditfile);
  await page.waitForURL(/\/a\/[0-9a-f-]{36}/, { timeout: 120000 }).catch(() => meld('de auditfile werd geen administratie'));
  const id = page.url().split('/a/')[1]?.split(/[/?#]/)[0];
  console.log('1. auditfile: ' + page.url().replace(basis, ''));

  // 2. Een afschrift op de kolommenbalans: overal inlezen (deze administratie heeft die rekening niet: dezelfde melding als bij slepen).
  await page.goto(`${basis}/a/${id}/kolommenbalans`); await page.waitForTimeout(1500);
  await open('ing-2026-03.mt940', path.join(fictief, 'ing-2026-03.mt940'));
  const m2 = await melding();
  console.log('2. afschrift: ' + m2);
  if (!m2.includes('ing-2026-03.mt940')) meld('het afschrift kwam niet bij overal inlezen');

  // 3. Een e-factuur op de inkooppagina (eigen sleepvlak): via het overzicht naar inkoop.
  await page.goto(`${basis}/a/${id}/inkoop`); await page.waitForTimeout(1500);
  await open('belnet-2026-0411.xml', path.join(fictief, 'belnet-2026-0411.xml'));
  await page.waitForURL(/\/inkoop/, { timeout: 30000 }); await page.waitForTimeout(3000);
  const m3 = await melding();
  console.log('3. e-factuur: ' + page.url().replace(basis, '') + ' ' + m3);
  if (!/inkoopfactuur naar inkoop/.test(m3)) meld('de e-factuur kwam niet in inkoop');

  // 4. Zonder administratie: wachten met een melding, en inlezen zodra er een administratie open is.
  await page.goto(basis + '/'); await page.waitForTimeout(1500);
  await open('abn-2026-03.sta', path.join(fictief, 'abn-2026-03.sta'));
  const m4 = await melding();
  console.log('4. startpagina: ' + m4);
  if (!m4.includes('klaar om in te lezen')) meld('geen melding op de startpagina');
  await page.click(`a[href="/a/${id}"]`);
  const m4b = await melding();
  console.log('   na openen: ' + m4b);
  if (!m4b.includes('abn-2026-03.sta') || m4b.includes('klaar om in te lezen')) meld('het wachtende bestand werd niet ingelezen');

  // 5. F5 en Ctrl+R laden de app niet opnieuw.
  await page.evaluate(() => { window.__merk = 1; });
  await page.keyboard.press('F5'); await page.keyboard.press('Control+r'); await page.waitForTimeout(1500);
  if ((await page.evaluate(() => window.__merk)) !== 1) meld('F5 of Ctrl+R laadt de pagina opnieuw');

  console.log(fouten.length === 0 ? 'Alles goed.' : `${fouten.length} fouten.`);
  await browser.close();
  process.exit(fouten.length === 0 ? 0 : 1);
})();
