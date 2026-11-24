// Лаб. 06: перевірки «Перед здачею» через Gateway + скріни Aspire Dashboard + порівняння HTTP vs gRPC.
// Передумова: платформа запущена (docs/lab05/scripts/start-platform.cmd), npm i puppeteer-core. Результати: docs/lab06/*.png|*.json|*.txt
const puppeteer = require('puppeteer-core');
const { execSync } = require('child_process');
const fs = require('fs'), path = require('path');
const root = process.env.LAB_ROOT || path.join(__dirname, '..', '..', '..');   // корінь репозиторію
const out = path.join(root, 'docs', 'lab06');
fs.mkdirSync(out, { recursive: true });
const gw = 'http://localhost:5000', dash = 'http://localhost:15500';
const sleep = ms => new Promise(r => setTimeout(r, ms));
const ids = {}, report = [];
const note = s => { report.push(s); console.log(s); };

async function call(method, url, { body, cid, headers = {} } = {}) {
  const t = performance.now();
  const r = await fetch(gw + url, {
    method, body: body === undefined ? undefined : JSON.stringify(body),
    headers: { 'Content-Type': 'application/json', ...(cid ? { 'X-Correlation-Id': cid } : {}), ...headers }
  });
  const text = await r.text();
  const ms = performance.now() - t;
  let json; try { json = JSON.parse(text); } catch { json = text; }
  if (cid) ids[cid] = r.headers.get('x-trace-id');
  note(`${method} ${url} -> ${r.status} in ${ms.toFixed(1)} ms; X-Cache=${r.headers.get('x-cache') ?? '-'}; X-Correlation-Id=${r.headers.get('x-correlation-id')}; X-Trace-Id=${r.headers.get('x-trace-id')}`);
  if (r.status >= 400) note('   body: ' + text.slice(0, 400));
  return { status: r.status, json, ms, headers: r.headers };
}
const docker = cmd => { try { return execSync('docker ' + cmd).toString().trim(); } catch (e) { return 'ERR ' + e.message; } };
const redisContainer = () => docker('ps -a --format "{{.Names}}"').split(/\r?\n/).find(n => n.startsWith('redis-'));
const stats = arr => {
  const s = [...arr].sort((a, b) => a - b), q = p => s[Math.min(s.length - 1, Math.floor(p * s.length))];
  return { n: s.length, mean: +(s.reduce((a, b) => a + b, 0) / s.length).toFixed(1), median: +q(0.5).toFixed(1), p95: +q(0.95).toFixed(1), min: +s[0].toFixed(1) };
};

(async () => {
  for (let i = 0; i < 300; i++) { try { if ((await fetch(gw + '/health')).ok) break; } catch { } await sleep(2000); }
  await sleep(5000);

  note('### 1. Повторне читання: перший запит — БД, другий — кеш (немає SQL-span)');
  await call('GET', '/api/movies/3', { cid: 'lab06-movie-first' });
  await call('GET', '/api/movies/3', { cid: 'lab06-movie-repeat' });

  note('\n### 2. PUT інвалідує кеш: наступний GET повертає нові дані');
  const before = (await call('GET', '/api/movies/3')).json;
  const upd = d => ({ title: before.title, originalTitle: before.originalTitle, releaseYear: before.releaseYear,
    durationMinutes: before.durationMinutes, ageRating: before.ageRating, description: d, director: before.details?.director,
    country: before.details?.country, language: before.details?.language, genres: before.genres.map(g => g.slug) });
  const marker = 'Пол Атрід об\'єднується з фрименами (оновлено ' + new Date().toISOString().slice(11, 19) + ').';
  const put = await call('PUT', '/api/movies/3', { cid: 'lab06-movie-put', body: { ...upd(marker), rowVersion: before.rowVersion } });
  const after = await call('GET', '/api/movies/3', { cid: 'lab06-movie-after-put' });
  note(`   опис після PUT: «${after.json.details?.description}» — ${after.json.details?.description === marker ? 'НОВІ дані' : 'СТАРІ дані!'}`);
  await call('PUT', '/api/movies/3', { body: { ...upd(before.details.description), rowVersion: after.json.rowVersion } });   // повертаємо як було

  note('\n### 3. Aggregator через gRPC (Cache-Control: no-cache — справжня агрегація), потім кеш агрегованої відповіді в Redis');
  await call('GET', '/api/aggregator/bookings/1/full', { cid: 'lab06-aggregate-grpc', headers: { 'Cache-Control': 'no-cache' } });
  await call('GET', '/api/aggregator/bookings/1/full?transport=http', { cid: 'lab06-aggregate-http' });
  await call('GET', '/api/aggregator/movies/9999/full', { cid: 'lab06-grpc-notfound' });

  note('\n### 4. Порівняння затримок HTTP (Лаб. 05) і gRPC (Лаб. 06), без кешу агрегатора');
  const bench = {};
  for (const url of ['/api/aggregator/bookings/1/full', '/api/aggregator/movies/3/full']) {
    for (const t of ['grpc', 'http']) for (let i = 0; i < 5; i++)          // прогрів з'єднань
      await fetch(`${gw}${url}?transport=${t}`, { headers: { 'Cache-Control': 'no-cache' } });
    const res = { grpc: [], http: [] };
    for (let i = 0; i < 40; i++) for (const t of (i % 2 ? ['grpc', 'http'] : ['http', 'grpc'])) {
      const t0 = performance.now();
      const r = await fetch(`${gw}${url}?transport=${t}`, { headers: { 'Cache-Control': 'no-cache' } });
      await r.text();
      res[t].push(performance.now() - t0);
    }
    bench[url] = { grpc: stats(res.grpc), http: stats(res.http) };
    note(`   ${url}: gRPC ${JSON.stringify(bench[url].grpc)} | HTTP ${JSON.stringify(bench[url].http)}`);
  }
  note('   афіша з 7 фільмів: gRPC — 2 пакетні виклики (GetMoviesByIds + GetRatingsForMovies), REST — 14 запитів');
  {
    const url = '/api/aggregator/movies/board?ids=1,2,3,4,5,6,7';
    for (const t of ['grpc', 'http']) for (let i = 0; i < 5; i++) await (await fetch(`${gw}${url}&transport=${t}`, { headers: { 'Cache-Control': 'no-cache' } })).text();
    const res = { grpc: [], http: [] };
    for (let i = 0; i < 40; i++) for (const t of (i % 2 ? ['grpc', 'http'] : ['http', 'grpc'])) {
      const t0 = performance.now();
      await (await fetch(`${gw}${url}&transport=${t}`, { headers: { 'Cache-Control': 'no-cache' } })).text();
      res[t].push(performance.now() - t0);
    }
    bench[url] = { grpc: stats(res.grpc), http: stats(res.http) };
    note(`   ${url}: gRPC ${JSON.stringify(bench[url].grpc)} | HTTP ${JSON.stringify(bench[url].http)}`);
  }
  note('   паралельне навантаження: 16 одночасних клієнтів, 320 запитів картки бронювання на транспорт');
  for (const t of ['http', 'grpc']) {
    const lat = []; let next = 0; const t0 = performance.now();
    await Promise.all(Array.from({ length: 16 }, async () => {
      while (next++ < 320) {
        const s = performance.now();
        const r = await fetch(`${gw}/api/aggregator/bookings/1/full?transport=${t}`, { headers: { 'Cache-Control': 'no-cache' } });
        await r.text(); lat.push(performance.now() - s);
      }
    }));
    const total = performance.now() - t0;
    bench['parallel-16:' + t] = { ...stats(lat), totalMs: +total.toFixed(0), rps: +(lat.length / total * 1000).toFixed(1) };
    note(`   ${t}: ${JSON.stringify(bench['parallel-16:' + t])}`);
  }
  fs.writeFileSync(path.join(out, 'latency-http-vs-grpc.json'), JSON.stringify(bench, null, 2));
  await sleep(20000);   // експортери OTLP скидають накопичені після навантаження spans і логи — інакше наступні trace неповні
  await call('GET', '/api/aggregator/movies/board?ids=1,2,3,4,5,6,7&transport=http', { cid: 'lab06-board-http', headers: { 'Cache-Control': 'no-cache' } });
  await call('GET', '/api/aggregator/movies/board?ids=1,2,3,4,5,6,7', { cid: 'lab06-board-grpc', headers: { 'Cache-Control': 'no-cache' } });
  await call('GET', '/api/aggregator/bookings/1/full?transport=http', { cid: 'lab06-bench-http', headers: { 'Cache-Control': 'no-cache' } });
  await call('GET', '/api/aggregator/bookings/1/full', { cid: 'lab06-bench-grpc', headers: { 'Cache-Control': 'no-cache' } });
  note('\n   кеш агрегованої відповіді в Redis: перший запит збирає (MISS), повторний — з Redis (HIT)');
  await call('GET', '/api/aggregator/movies/3/full', { cid: 'lab06-aggregate-movie-1' });
  await call('GET', '/api/aggregator/movies/3/full', { cid: 'lab06-aggregate-movie-2' });

  await sleep(9000);
  const browser = await puppeteer.launch({ executablePath: 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', headless: true, args: ['--no-first-run'] });
  const page = await browser.newPage();
  const shot = async (url, file, height = 900, wait = 6000) => {
    await page.setViewport({ width: 1600, height });
    await page.goto(dash + url, { waitUntil: 'networkidle2', timeout: 60000 }).catch(() => {});
    await page.waitForFunction(() => document.querySelectorAll('[role=row], fluent-data-grid-row, .fluent-data-grid-row, canvas, svg.plot').length > 2,
      { timeout: 25000 }).catch(() => console.log('   (таблиця не з\'явилася за 25 с)'));
    await sleep(wait);
    await page.evaluate(() => document.querySelectorAll('fluent-message-bar, .fluent-messagebar, [class*="message-bar"]').forEach(e => e.style.display = 'none'));
    await sleep(500);
    await page.screenshot({ path: path.join(out, file) });
    console.log('screenshot ' + file);
  };
  const logs = cid => `/structuredlogs?filters=${encodeURIComponent('CorrelationId:equals:' + cid)}`;
  await shot('/', 'dashboard-resources.png');
  await shot(`/traces/detail/${ids['lab06-movie-first']}`, 'trace-movie-first-sql.png', 700);
  await shot(`/traces/detail/${ids['lab06-movie-repeat']}`, 'trace-movie-repeat-no-sql.png', 700);
  await shot(`/traces/detail/${ids['lab06-bench-grpc']}`, 'trace-aggregate-grpc.png', 1500);
  await shot(`/traces/detail/${ids['lab06-bench-http']}`, 'trace-aggregate-http.png', 1500);
  await shot(`/traces/detail/${ids['lab06-aggregate-grpc']}`, 'trace-aggregate-grpc-cold.png', 1500);
  await shot(`/traces/detail/${ids['lab06-board-grpc']}`, 'trace-board-grpc.png', 700);
  await shot(`/traces/detail/${ids['lab06-board-http']}`, 'trace-board-http.png', 1500);
  await shot(`/traces/detail/${ids['lab06-aggregate-movie-2']}`, 'trace-aggregate-cache-hit.png', 600);
  await shot(logs('lab06-bench-grpc'), 'logs-grpc-correlation-id.png');
  await shot(logs('lab06-movie-put'), 'logs-cache-invalidation.png', 700);
  await shot(logs('lab06-grpc-notfound'), 'logs-grpc-notfound.png', 700);
  await shot('/metrics/resource/catalog-api?meter=Cinema.Catalog.Cache&instrument=cache.hits&duration=5', 'metrics-cache-hits.png', 900, 8000);
  await shot('/metrics/resource/catalog-api?meter=Cinema.Catalog.Cache&instrument=cache.misses&duration=5', 'metrics-cache-misses.png', 900, 8000);

  note('\n### 5. Redis зупинено — сервіси відповідають з БД (повільніше), а не падають');
  const redis = redisContainer();
  note('   docker stop ' + redis + ': ' + docker('stop ' + redis));
  await sleep(2000);
  await call('GET', '/api/movies/2', { cid: 'lab06-redis-down-1' });
  await call('GET', '/api/movies/5', { cid: 'lab06-redis-down-2' });
  await call('GET', '/api/aggregator/bookings/2/full', { cid: 'lab06-redis-down-aggregate' });
  await sleep(8000);
  await shot(logs('lab06-redis-down-1'), 'logs-redis-down.png', 700);
  note('   docker start ' + redis + ': ' + docker('start ' + redis));

  note('\n### 6. catalog-api зупинено — часткова відповідь Aggregator (gRPC Unavailable → Warnings, не 500)');
  try { execSync('taskkill /IM CatalogService.Api.exe /F'); note('   catalog-api stopped'); } catch (e) { note('   taskkill: ' + e.message); }
  await sleep(2000);
  const partial = await call('GET', '/api/aggregator/bookings/1/full', { cid: 'lab06-partial-catalog', headers: { 'Cache-Control': 'no-cache' } });
  fs.writeFileSync(path.join(out, 'aggregate-partial-catalog-down.json'), JSON.stringify(partial.json, null, 2));
  await sleep(8000);
  await shot(`/traces/detail/${ids['lab06-partial-catalog']}`, 'trace-partial-catalog-down.png', 1100);
  await shot(logs('lab06-partial-catalog'), 'logs-partial-catalog-down.png');
  await browser.close();
  fs.writeFileSync(path.join(out, 'demo-output.txt'), report.join('\n') + '\n');
  console.log('OK');
})().catch(e => { console.error(e); fs.writeFileSync(path.join(out, 'demo-output.txt'), report.join('\n')); process.exit(1); });
