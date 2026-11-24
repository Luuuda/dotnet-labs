// Лаб. 05: демо-сценарій через Gateway + скріни Aspire Dashboard + часткова відповідь Aggregator.
// Передумова: платформа запущена (start-platform.cmd), npm i puppeteer-core. Результати: docs/lab05/*.png|*.json
const puppeteer = require('puppeteer-core');
const { execSync } = require('child_process');
const fs = require('fs'), path = require('path');
const root = path.join(__dirname, '..', '..', '..');   // корінь репозиторію
const out = path.join(root, 'docs', 'lab05');
fs.mkdirSync(out, { recursive: true });
const gw = 'http://localhost:5000', dash = 'http://localhost:15500';
const sleep = ms => new Promise(r => setTimeout(r, ms));
const ids = {};

async function call(method, url, body, cid, base = gw) {
  const t = Date.now();
  const r = await fetch(base + url, {
    method, headers: { 'Content-Type': 'application/json', ...(cid ? { 'X-Correlation-Id': cid } : {}) },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  const text = await r.text();
  let json; try { json = JSON.parse(text); } catch { json = text; }
  console.log(`${method} ${base}${url} -> ${r.status} in ${Date.now() - t} ms; X-Correlation-Id=${r.headers.get('x-correlation-id')}; X-Trace-Id=${r.headers.get('x-trace-id')}`);
  if (cid) ids[cid] = r.headers.get('x-trace-id');
  if (r.status >= 400) console.log('   body: ' + text.slice(0, 600));
  return { status: r.status, json };
}
function listenPort(exe) {
  const ps = `Get-NetTCPConnection -State Listen -OwningProcess (Get-Process -Name ${exe}).Id | Select-Object -First 1 -ExpandProperty LocalPort`;
  return execSync(`powershell -NoProfile -Command "${ps}"`).toString().trim();
}

(async () => {
  for (let i = 0; i < 300; i++) { try { if ((await fetch(gw + '/health')).ok) break; } catch { } await sleep(2000); }

  // ---------- 1. Бізнес-сценарій через Gateway ----------
  await call('GET', '/api/movies?pageSize=3', undefined, 'demo-catalog-list');
  const row = 5 + Math.floor(Math.random() * 5), seat = 1 + Math.floor(Math.random() * 10);   // «Зала 1»: 10 рядів × 12 місць
  const created = await call('POST', '/api/bookings', {
    customerId: '6f1c2a10-0000-4000-8000-000000000002', screeningId: 2,
    seats: [{ row, seat }, { row, seat: seat + 1 }], contactPhone: '+380501234567', channel: 'Web'
  }, 'demo-create-booking');
  const bookingId = created.json.id;
  await call('POST', `/api/bookings/${bookingId}/confirm`, {}, 'demo-confirm-booking');
  await call('POST', `/api/bookings/${bookingId}/pay`, { amount: created.json.totalAmount, provider: 'Card', externalTransactionId: 'demo-' + Date.now() }, 'demo-pay-booking');
  await call('GET', '/api/reviews?movieId=3&pageSize=2', undefined, 'demo-reviews-list');

  // ---------- 2. Агрегувальний запит ----------
  const agg = await call('GET', `/api/aggregator/bookings/${bookingId}/full`, undefined, 'demo-aggregate-booking');
  fs.writeFileSync(path.join(out, 'aggregate-booking.json'), JSON.stringify(agg.json, null, 2));
  const movie = await call('GET', '/api/aggregator/movies/3/full', undefined, 'demo-aggregate-movie');
  fs.writeFileSync(path.join(out, 'aggregate-movie.json'), JSON.stringify(movie.json, null, 2));

  // ---------- 3. Прямий виклик сервісу в обхід Gateway (для порівняння) ----------
  try {
    const port = listenPort('CatalogService.Api');
    await call('GET', '/api/movies/3', undefined, 'demo-direct-catalog', `http://localhost:${port}`);
  } catch (e) { console.log('direct call failed: ' + e.message); }

  await sleep(8000);   // OTLP експортує пакетами
  const browser = await puppeteer.launch({
    executablePath: 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    headless: true, args: ['--no-first-run', '--lang=uk-UA']
  });
  const page = await browser.newPage();
  await page.setViewport({ width: 1600, height: 900 });
  const shot = async (url, file, wait = 6000, full = false) => {
    await page.setViewport({ width: 1600, height: full ? 1400 : 900 });   // trace з усіма spans — вище вікно
    await page.goto(dash + url, { waitUntil: 'networkidle2', timeout: 60000 }).catch(() => {});
    await sleep(wait);
    await page.evaluate(() => {           // службові банери Dashboard (оновлення, dev-сертифікат) не стосуються роботи
      document.querySelectorAll('fluent-message-bar, .fluent-messagebar, [class*="message-bar"]').forEach(e => e.style.display = 'none');
    });
    await sleep(500);
    await page.screenshot({ path: path.join(out, file), fullPage: full });
    console.log('screenshot ' + file);
  };
  await shot('/', 'dashboard-resources.png');
  await shot(`/traces/detail/${ids['demo-aggregate-booking']}`, 'trace-aggregate-booking.png', 7000, true);
  const f = encodeURIComponent('CorrelationId:equals:demo-aggregate-booking');
  await shot(`/structuredlogs?filters=${f}`, 'logs-by-correlation-id.png', 7000);
  await shot('/traces', 'traces-list.png');
  await shot('/structuredlogs?logLevel=error', 'check-errors-before-stop.png');   // перевірка: помилок старту немає

  // ---------- 4. Часткова відмова: зупиняємо reviews-api ----------
  try { execSync('taskkill /IM Reviews.WebApi.exe /F'); console.log('reviews-api stopped'); } catch (e) { console.log('taskkill: ' + e.message); }
  await sleep(2000);
  const partial = await call('GET', `/api/aggregator/bookings/${bookingId}/full`, undefined, 'demo-partial-failure');
  fs.writeFileSync(path.join(out, 'aggregate-booking-partial.json'), JSON.stringify(partial.json, null, 2));
  const partialMovie = await call('GET', '/api/aggregator/movies/3/full', undefined, 'demo-partial-movie');
  fs.writeFileSync(path.join(out, 'aggregate-movie-partial.json'), JSON.stringify(partialMovie.json, null, 2));
  await sleep(8000);
  await shot(`/traces/detail/${ids['demo-partial-failure']}`, 'trace-partial-failure.png', 7000, true);
  await shot(`/structuredlogs?filters=${encodeURIComponent('CorrelationId:equals:demo-partial-failure')}`, 'logs-partial-failure.png', 7000);
  await shot('/', 'dashboard-resources-reviews-stopped.png');
  await browser.close();
  fs.writeFileSync(path.join(out, 'trace-ids.json'), JSON.stringify(ids, null, 2));
  console.log('OK');
})().catch(e => { console.error(e); process.exit(1); });
