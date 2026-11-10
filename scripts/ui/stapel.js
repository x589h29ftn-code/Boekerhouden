// Browsercontrole van Stapel splitsen, met een verzonnen stapel (scripts/ui/maak-stapel.py, vereist reportlab).
// Gebruik: scripts/ui/herstart.sh && node scripts/ui/stapel.js <map> [voorvoegsel]   (THEMA=donker ervoor kan)
// Maakt de eerste gebruiker en een administratie, levert bij inkoop en bij verkoop een stapel aan, knipt en splitst.
const { chromium } = require('playwright');
const crypto = require('crypto');
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
  const uit = process.argv[2] || '.'; const pre = process.argv[3] || 'scherm';
  const fictief = path.resolve(__dirname, '../../tests/Fictief/administratie');
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1600, height: 1000 } });
  // THEMA=donker of DICHTHEID=ruim: dezelfde schermen in die weergave (zoals gekozen in het gebruikersmenu).
  if (process.env.THEMA || process.env.DICHTHEID) await page.addInitScript(([t, d]) => { if (t) localStorage.setItem('thema', t); if (d) localStorage.setItem('dichtheid', d); }, [process.env.THEMA, process.env.DICHTHEID]);
  const log = []; page.on('pageerror', e => log.push(e.message)); page.on('console', m => { if (m.type() === 'error') log.push(m.text()); else if (process.env.DEBUG) console.log('console:', m.text()); });
  const basis = 'http://localhost:5090';

  // Eerste gebruiker met tweestapsverificatie, dan een administratie.
  await page.goto(basis + '/'); await page.waitForURL(/eerste-gebruiker/);
  await page.fill('#naam', 'Bea Beheer'); await page.fill('#initialen', 'BB'); await page.fill('#email', 'beheer@kantoor.test');
  await page.fill('#wachtwoord', 'Groene-Appel-Boom-42'); await page.fill('#herhaal', 'Groene-Appel-Boom-42');
  await page.click('button[type=submit]'); await page.waitForURL(/account\/beveiliging/);
  await page.fill('#code', totp(await page.textContent('code.fs-6'))); await page.click('button:has-text("Aanzetten")');
  await page.waitForSelector('text=Je herstelcodes');
  await page.goto(basis + '/'); await page.waitForTimeout(1500);
  await page.click('text=Nieuwe administratie'); await page.waitForSelector('#naam'); await page.waitForTimeout(800);
  await page.fill('#naam', 'Voorbeeld Handel B.V.'); await page.click('text=Aanmaken');
  await page.waitForURL(/\/a\/[0-9a-f-]+$/); const id = page.url().split('/a/')[1];

  // Stapel splitsen: één PDF met zes facturen (15 pagina's) aanleveren, het voorstel bekijken, een pagina groot
  // openen, een knip zetten en weer weghalen, en splitsen: de facturen staan daarna los bij inkoop.
  try {
    const stapelpdf = path.join(uit, 'stapel-inkoop.pdf');
    require('child_process').execFileSync('python3', [path.join(__dirname, 'maak-stapel.py'), stapelpdf]);
    await page.goto(`${basis}/a/${id}/inkoop`); await page.waitForSelector('label:has-text("Stapel splitsen")'); await page.waitForTimeout(800);
    const voorStapel = await page.locator('table tbody tr').count();
    await page.locator('label:has-text("Stapel splitsen") input[type=file]').setInputFiles(stapelpdf);
    await page.waitForURL(/\/stapel\/[0-9a-f-]+$/, { timeout: 30000 }); await page.waitForSelector('.stapelblad canvas.klaar', { timeout: 30000 }); await page.waitForTimeout(1500);
    const bladen = await page.locator('.stapelblad').count();
    if (bladen !== 15) log.push(`stapel: ${bladen} pagina's in plaats van 15`);
    if (!(await page.locator('.stapelkop').innerText()).includes('6 facturen')) log.push('stapel: voorstel is niet 6 facturen: ' + await page.locator('.stapelkop').innerText());
    await page.screenshot({ path: `${uit}/${pre}-stapel-voorstel.png`, fullPage: true });
    await page.locator('[data-blad="3"] .bladbeeld').click(); await page.waitForTimeout(1200);
    await page.keyboard.press('ArrowRight'); await page.waitForTimeout(600);
    await page.keyboard.press('k'); await page.waitForTimeout(600);
    if (!(await page.locator('.stapelkop').innerText()).includes('7 facturen')) log.push('stapel: knip met K geeft geen 7 facturen');
    await page.screenshot({ path: `${uit}/${pre}-stapel-groot.png` });
    await page.keyboard.press('k'); await page.waitForTimeout(600);
    await page.keyboard.press('Escape'); await page.waitForTimeout(400);
    await page.click('button:has-text("Splitsen in 6 facturen")'); await page.waitForURL(/\/inkoop$/, { timeout: 60000 }); await page.waitForTimeout(1500);
    const naStapel = await page.locator('table tbody tr').count();
    if (naStapel - voorStapel !== 6) log.push(`stapel: geen facturen bij inkoop erbij (${voorStapel} -> ${naStapel})`);
    await page.screenshot({ path: `${uit}/${pre}-stapel-ingelezen.png` });
  } catch (fout) { log.push('stapel: ' + String(fout).split('\n')[0]); }


  // Verkoop: dezelfde stapel als verkoopfacturen.
  try {
    await page.goto(`${basis}/a/${id}/verkoop`); await page.waitForSelector('label:has-text("Stapel splitsen")'); await page.waitForTimeout(800);
    await page.locator('label:has-text("Stapel splitsen") input[type=file]').setInputFiles(path.join(uit, 'stapel-inkoop.pdf'));
    await page.waitForURL(/\/stapel\/[0-9a-f-]+$/, { timeout: 30000 }); await page.waitForSelector('.stapelblad canvas.klaar', { timeout: 30000 }); await page.waitForTimeout(1200);
    await page.locator('[data-blad="5"] .bladvoet').hover(); await page.locator('[data-blad="5"] .bladactie').click(); await page.waitForTimeout(600);
    await page.screenshot({ path: `${uit}/${pre}-stapel-verkoop.png`, fullPage: true });
    await page.click('button:has-text("Splitsen in")'); await page.waitForURL(/\/verkoop$/, { timeout: 60000 }); await page.waitForTimeout(1500);
    await page.screenshot({ path: `${uit}/${pre}-stapel-verkoop-ingelezen.png` });
  } catch (fout) { log.push('stapel verkoop: ' + String(fout).split('\n')[0]); }

  console.log(log.length ? log : 'geen fouten in de browser');
  await browser.close();
})();
