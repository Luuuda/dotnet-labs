// Лаб. 07: перевірки «Перед здачею» (resilience, Redis і SQL Server вимкнено, бізнес-метрики) + скріни Aspire Dashboard.
// Передумова: платформа запущена (docs/lab05/scripts/start-platform.cmd), npm i puppeteer-core.
// reviews-api зупиняється й запускається кнопками на сторінці Resources Dashboard. Результати: docs/lab07/*.png|*.json|*.txt
const puppeteer = require('puppeteer-core');
const { execSync } = require('child_process');
const fs = require('fs'), path = require('path');
const root = process.env.LAB_ROOT || path.join(__dirname, '..', '..', '..');   // корінь репозиторію
const out = path.join(root, 'docs', 'lab07');
fs.mkdirSync(out, { recursive: true });
const gw = 'http://localhost:5000', dash = 'http://localhost:15500';
const svc = { booking: 'http://localhost:5180', catalog: 'http://localhost:5280', reviews: 'http://localhost:5380', aggregator: 'http://localhost:5400' };
const sleep = ms => new Promise(r => setTimeout(r, ms));
const ids = {}, report = [];
const note = s => { report.push(s); console.log(s); };
const t0 = Date.now(), ts = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6) + 's';

async function call(method, url, { body, cid, headers = {}, base = gw, quiet = false } = {}) {
  const t = performance.now();
  let r, text;
  try {
    r = await fetch(base + url, {
      method, body: body === undefined ? undefined : JSON.stringify(body),
      headers: { 'Content-Type': 'application/json', ...(cid ? { 'X-Correlation-Id': cid } : {}), ...headers }
    });
    text = await r.text();
  } catch (e) { note(`${ts()} ${method} ${url} -> ERR ${e.message}`); return { status: 0, json: null, ms: performance.now() - t }; }
  const ms = performance.now() - t;
  let json; try { json = JSON.parse(text); } catch { json = text; }
  if (cid) ids[cid] = r.headers.get('x-trace-id');
  const warn = json && Array.isArray(json.warnings) && json.warnings.length ? ` warnings=${JSON.stringify(json.warnings)}` : '';
  if (!quiet) note(`${ts()} ${method} ${base === gw ? '' : base}${url} -> ${r.status} in ${ms.toFixed(0)} ms${warn}${cid ? ' [' + cid + ']' : ''}`);
  if (r.status >= 400 && !quiet && base === gw) note('   body: ' + text.slice(0, 300));
  return { status: r.status, json, ms, headers: r.headers, text };
}
const docker = cmd => { try { return execSync('docker ' + cmd).toString().trim(); } catch (e) { return 'ERR ' + e.message; } };
const container = prefix => docker('ps -a --format "{{.Names}}"').split(/\r?\n/).find(n => n.startsWith(prefix));
const health = async (name, p) => {
  const r = await call('GET', p, { base: svc[name], quiet: true });
  const checks = r.json && r.json.checks ? r.json.checks.map(c => `${c.name}=${c.status}`).join(', ') : '';
  note(`${ts()} ${svc[name]}${p} -> HTTP ${r.status} ${r.json?.status ?? ''} (${r.ms.toFixed(0)} ms) ${checks}`);
  return r;
};
const save = (file, data) => fs.writeFileSync(path.join(out, file), typeof data === 'string' ? data : JSON.stringify(data, null, 2));

(async () => {
  for (let i = 0; i < 300; i++) { try { if ((await fetch(gw + '/health')).ok) break; } catch { } await sleep(2000); }
  await sleep(5000);
  const browser = await puppeteer.launch({ executablePath: 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', headless: true, args: ['--no-first-run'], protocolTimeout: 120000 });
  const page = await browser.newPage();
  const ctl = await browser.newPage();
  await ctl.setViewport({ width: 1600, height: 900 });

  const shot = async (url, file, height = 900, wait = 6000, p = page) => {
    await p.bringToFront();                 // фонова вкладка headless-браузера не малюється → screenshot зависає
    await p.setViewport({ width: 1600, height });
    await p.goto(url.startsWith('http') ? url : dash + url, { waitUntil: 'networkidle2', timeout: 60000 }).catch(() => {});
    if (!url.startsWith('http'))
      await p.waitForFunction(() => document.querySelectorAll('[role=row], fluent-data-grid-row, canvas, svg.plot, .plot-container').length > 2,
        { timeout: 25000 }).catch(() => console.log('   (таблиця не з\'явилася за 25 с)'));
    await sleep(wait);
    await p.evaluate(() => document.querySelectorAll('fluent-message-bar, .fluent-messagebar, [class*="message-bar"]').forEach(e => e.style.display = 'none'));
    await sleep(300);
    await p.screenshot({ path: path.join(out, file) });
    console.log('screenshot ' + file);
  };
  // Кнопки «Остановить ресурс» / «Запустить ресурс» у рядку ресурсу на сторінці Resources — так само, як руками з Dashboard
  const resourceAction = async (name, verbs) => {
    await ctl.bringToFront();
    await ctl.goto(dash + '/', { waitUntil: 'networkidle2', timeout: 60000 }).catch(() => {});
    await sleep(3000);
    const res = await ctl.evaluate((name, verbs) => {
      const rows = [...document.querySelectorAll('fluent-data-grid-row, [role=row]')];
      const row = rows.find(r => r.innerText.trim().startsWith(name + ' ') || r.innerText.trim().startsWith(name + '\n') || r.innerText.trim().split(/\s/)[0] === name);
      if (!row) return 'row not found';
      const buttons = [...row.querySelectorAll('fluent-button, button')];
      const labels = buttons.map(b => (b.getAttribute('title') || b.getAttribute('aria-label') || '').trim());
      const i = labels.findIndex(l => verbs.some(v => l.includes(v)));
      if (i < 0) return 'button not found: ' + labels.join(' | ');
      buttons[i].click();
      return 'clicked ' + labels[i];
    }, name, verbs);
    note(`${ts()} Dashboard ${name}: ${res}`);
    return res;
  };
  // JSON health endpoint → HTML-сторінка з адресою, HTTP-статусом і відформатованим тілом (як у браузері)
  const jsonShot = async (url, file, height) => {
    const r = await call('GET', url.replace(/^http:\/\/localhost:\d+/, ''), { base: url.match(/^http:\/\/localhost:\d+/)[0], quiet: true });
    const esc = t => t.replace(/&/g, '&amp;').replace(/</g, '&lt;');
    const color = r.status === 200 ? (r.json?.status === 'Degraded' ? '#e3b341' : '#3fb950') : '#f85149';
    await page.bringToFront();
    await page.setViewport({ width: 1100, height });
    await page.setContent(`<html><body style="background:#0d1117;color:#c9d1d9;font:15px Consolas,monospace;margin:20px">
      <div style="font-size:17px;margin-bottom:12px">GET ${esc(url)} → <b style="color:${color}">HTTP ${r.status} · ${esc(r.json?.status ?? '')}</b>
      <span style="color:#8b949e">(${new Date().toLocaleTimeString('uk-UA')})</span></div>
      <pre style="white-space:pre-wrap">${esc(r.text ?? '')}</pre></body></html>`);
    await page.screenshot({ path: path.join(out, file) });
    console.log('screenshot ' + file);
    return r;
  };
  const waitFor = async (url, okWanted, seconds) => {
    for (let i = 0; i < seconds; i++) {
      let ok = false; try { ok = (await fetch(url)).ok; } catch { }
      if (ok === okWanted) return true;
      await sleep(1000);
    }
    return false;
  };
  const logs = cid => `/structuredlogs?filters=${encodeURIComponent('CorrelationId:equals:' + cid)}`;

  // Відновлення після можливого попереднього запуску: усі контейнери й reviews-api запущені
  for (const p of ['redis-', 'sql-', 'mongo-']) { const c = container(p); if (c) docker('start ' + c); }
  if (!(await waitFor(svc.reviews + '/health/live', true, 3))) {
    await resourceAction('reviews-api', ['Запуск', 'Start']);
    note(`${ts()} reviews-api ${await waitFor(svc.reviews + '/health/ready', true, 90) ? 'запущено' : 'НЕ запустився'}`);
  }
  for (const n of Object.keys(svc)) await waitFor(svc[n] + '/health/ready', true, 90);
  await sleep(17000);                       // ланцюги попереднього запуску: BreakDuration минає
  for (let i = 0; i < 3; i++) {             // пробні запити (Half-Open → Closed) і прогрів з'єднань
    await call('GET', '/api/aggregator/movies/3/full?transport=http', { headers: { 'Cache-Control': 'no-cache' }, quiet: true });
    await call('GET', '/api/aggregator/movies/3/full', { headers: { 'Cache-Control': 'no-cache' }, quiet: true });
    await sleep(500);
  }

  // ------------------------------------------------------------------------------------------------------------
  note('### 0. Стан health endpoints (усе запущено)');
  for (const n of Object.keys(svc)) await health(n, '/health/ready');
  await health('booking', '/health/live');
  save('health-ready-aggregator-healthy.json', (await call('GET', '/health/ready', { base: svc.aggregator, quiet: true })).json);

  // ------------------------------------------------------------------------------------------------------------
  note('\n### 1. Бізнес-метрики: кілька бронювань (створення, підтвердження, оплата, скасування, невдалі операції)');
  const channels = ['Web', 'Mobile', 'BoxOffice', 'Web', 'Mobile', 'Web'];
  const providers = ['Card', 'ApplePay', 'GooglePay'];
  const created = [];
  for (let i = 0; i < channels.length; i++) {
    for (let attempt = 0; attempt < 6; attempt++) {
      const row = 1 + Math.floor(Math.random() * 10), seat = 1 + Math.floor(Math.random() * 10);
      const r = await call('POST', '/api/bookings', { cid: `lab07-booking-create-${i}`, body: {
        customerId: '6f1c2a10-0000-4000-8000-000000000002', screeningId: 2 + (i % 2),
        seats: [{ row, seat }, { row, seat: seat + 1 }], contactPhone: '+380501234567', channel: channels[i] } });
      if (r.status === 201) { created.push(r.json); break; }
    }
  }
  for (const [i, b] of created.entries()) {
    if (i < 4) await call('POST', `/api/bookings/${b.id}/confirm`, { body: {}, cid: `lab07-booking-confirm-${i}` });
    if (i < 3) await call('POST', `/api/bookings/${b.id}/pay`, { cid: `lab07-booking-pay-${i}`,
      body: { amount: b.totalAmount, provider: providers[i % 3], externalTransactionId: `lab07-${Date.now()}-${i}` } });
    if (i === 5) await call('POST', `/api/bookings/${b.id}/cancel`, { body: {}, cid: 'lab07-booking-cancel' });
  }
  if (created[0]) await call('POST', `/api/bookings/${created[0].id}/confirm`, { body: {}, cid: 'lab07-booking-confirm-again' });  // 409 → failure у гістограмі
  await call('POST', '/api/bookings/999999/cancel', { body: {}, cid: 'lab07-booking-cancel-missing' });                            // 404 → failure
  for (let i = 0; i < 5; i++) await call('GET', `/api/aggregator/movies/${1 + i}/full`, { cid: `lab07-aggregate-${i}`, quiet: true });

  // ------------------------------------------------------------------------------------------------------------
  note('\n### 2. reviews-api зупинено з Dashboard на ~8 с: повтори, швидка часткова відповідь, розмикання ланцюга');
  await call('GET', '/api/aggregator/movies/3/full?transport=http', { cid: 'lab07-before-stop', headers: { 'Cache-Control': 'no-cache' } });
  await resourceAction('reviews-api', ['Остановить', 'Stop', 'Зупинити']);
  note(`${ts()} reviews-api ${await waitFor(svc.reviews + '/health/live', false, 30) ? 'більше не відповідає' : 'ДОСІ відповідає'}`);
  const down = [];
  for (let i = 0; i < 8; i++) {
    const http = await call('GET', '/api/aggregator/movies/3/full?transport=http', { cid: `lab07-reviews-down-http-${i}`, headers: { 'Cache-Control': 'no-cache' } });
    const grpc = await call('GET', '/api/aggregator/movies/3/full', { cid: `lab07-reviews-down-grpc-${i}`, headers: { 'Cache-Control': 'no-cache' } });
    down.push({ i, http: { status: http.status, ms: +http.ms.toFixed(0), warnings: http.json?.warnings }, grpc: { status: grpc.status, ms: +grpc.ms.toFixed(0), warnings: grpc.json?.warnings } });
    await sleep(700);
  }
  const lastFailAt = Date.now();
  await health('aggregator', '/health/ready');
  save('health-ready-aggregator-reviews-down.json', (await call('GET', '/health/ready', { base: svc.aggregator, quiet: true })).json);
  await resourceAction('reviews-api', ['Запуск', 'Start']);
  note(`${ts()} reviews-api ${await waitFor(svc.reviews + '/health/ready', true, 90) ? 'знову відповідає' : 'НЕ запустився'}`);
  await sleep(Math.max(0, 16000 - (Date.now() - lastFailAt)));       // BreakDuration reviews = 15 с
  note('   після BreakDuration: пробний запит (Half-Open) → успіх → Closed');
  const closed = await call('GET', '/api/aggregator/movies/3/full?transport=http', { cid: 'lab07-circuit-closed-http', headers: { 'Cache-Control': 'no-cache' } });
  await sleep(1000);
  const closedGrpc = await call('GET', '/api/aggregator/movies/3/full', { cid: 'lab07-circuit-closed-grpc', headers: { 'Cache-Control': 'no-cache' } });
  await sleep(1000);
  const full = await call('GET', '/api/aggregator/movies/3/full?transport=http', { cid: 'lab07-after-recovery-http', headers: { 'Cache-Control': 'no-cache' } });
  const fullGrpc = await call('GET', '/api/aggregator/movies/3/full', { cid: 'lab07-after-recovery-grpc', headers: { 'Cache-Control': 'no-cache' } });
  down.push({ i: 'after-recovery', http: { status: full.status, ms: +full.ms.toFixed(0), warnings: full.json?.warnings }, grpc: { status: fullGrpc.status, ms: +fullGrpc.ms.toFixed(0), warnings: fullGrpc.json?.warnings } });
  down.push({ i: 'after-break', http: { status: closed.status, ms: +closed.ms.toFixed(0), warnings: closed.json?.warnings }, grpc: { status: closedGrpc.status, ms: +closedGrpc.ms.toFixed(0), warnings: closedGrpc.json?.warnings } });
  save('reviews-down-timeline.json', down);

  // ------------------------------------------------------------------------------------------------------------
  note('\n### 3. Redis зупинено: /health/ready → Degraded (200), сервіси відповідають з БД');
  const redis = container('redis-');
  note(`${ts()} docker stop ${redis}: ${docker('stop ' + redis)}`);
  await sleep(1500);
  await call('GET', '/api/movies/2', { cid: 'lab07-redis-down-movie' });
  await call('GET', '/api/aggregator/bookings/1/full', { cid: 'lab07-redis-down-aggregate' });
  for (const n of Object.keys(svc)) await health(n, '/health/ready');
  save('health-ready-catalog-redis-degraded.json', (await jsonShot(svc.catalog + '/health/ready', 'health-ready-redis-degraded.png', 900)).json);
  await call('GET', '/api/movies/4', { cid: 'lab07-redis-down-movie-2' });
  await sleep(6000);
  await shot(logs('lab07-redis-down-aggregate'), 'logs-redis-down-fallback.png', 800);
  note(`${ts()} docker start ${redis}: ${docker('start ' + redis)}`);
  await sleep(5000);
  await health('catalog', '/health/ready');

  // ------------------------------------------------------------------------------------------------------------
  note('\n### 4. SQL Server зупинено: /health/live → 200, /health/ready → 503');
  const sql = container('sql-');
  note(`${ts()} docker stop ${sql}: ${docker('stop ' + sql)}`);
  await sleep(2000);
  const live = await health('booking', '/health/live');
  const ready = await health('booking', '/health/ready');
  await health('catalog', '/health/ready');
  await health('aggregator', '/health/ready');
  save('health-sql-down-booking.json', { live: { status: live.status, body: live.json }, ready: { status: ready.status, body: ready.json } });
  await jsonShot(svc.booking + '/health/ready', 'health-ready-sql-down.png', 900);
  await jsonShot(svc.booking + '/health/live', 'health-live-sql-down.png', 400);
  await sleep(12000);                       // Aspire опитує /health кожні кілька секунд
  await shot('/', 'dashboard-resources-sql-down.png', 800, 6000);
  note(`${ts()} docker start ${sql}: ${docker('start ' + sql)}`);
  await waitFor(svc.booking + '/health/ready', true, 120);
  await health('booking', '/health/ready');
  await waitFor(svc.aggregator + '/health/ready', true, 60);
  await health('aggregator', '/health/ready');

  // ------------------------------------------------------------------------------------------------------------
  note('\n### 5. Скріни Dashboard: Resources, метрики Polly і бізнес-метрики, логи й traces повторів');
  await sleep(15000);
  await shot('/', 'dashboard-resources.png', 800, 6000);
  const m = (res, meter, inst, file, h = 1000) => shot(`/metrics/resource/${res}?meter=${encodeURIComponent(meter)}&instrument=${encodeURIComponent(inst)}&duration=15`, file, h, 9000);
  await m('aggregator', 'Polly', 'resilience.polly.strategy.events', 'metrics-polly-strategy-events.png', 1200);
  // фільтр event.name: лише потрібні події (чекбокси панелі «Фільтри» під графіком)
  const eventFilter = async (wanted, file) => {
    await page.bringToFront();
    await page.goto(`${dash}/metrics/resource/aggregator?meter=Polly&instrument=resilience.polly.strategy.events&duration=15`,
      { waitUntil: 'networkidle2', timeout: 60000 }).catch(() => {});
    await sleep(8000);
    const res = await page.evaluate(wanted => {
      const names = ['OnCircuitClosed', 'OnCircuitHalfOpened', 'OnCircuitOpened', 'OnRetry', 'OnTimeout',
                     'PipelineExecuting', 'PipelineExecuted', 'ExecutionAttempt', 'OnHedging', 'OnFallback'];
      const label = c => (c.getAttribute('aria-label') || c.getAttribute('title') || c.innerText || c.textContent || '').trim();
      const isChecked = c => c.checked === true || c.getAttribute('aria-checked') === 'true';
      const log = [];
      const boxes = [...document.querySelectorAll('fluent-checkbox')].filter(c => names.includes(label(c)));
      boxes.forEach(c => { const want = wanted.includes(label(c)); if (isChecked(c) !== want) { c.click(); log.push((want ? '+' : '-') + label(c)); } });
      return `${boxes.length} boxes: ${log.join(' ')}`;
    }, wanted);
    console.log('   filter event.name: ' + res);
    await sleep(4000);
    await page.evaluate(() => document.querySelectorAll('fluent-message-bar, .fluent-messagebar, [class*="message-bar"]').forEach(e => e.style.display = 'none'));
    await page.screenshot({ path: path.join(out, file) });
    console.log('screenshot ' + file);
  };
  await eventFilter(['OnRetry'], 'metrics-polly-onretry.png');
  await eventFilter(['OnCircuitOpened'], 'metrics-polly-oncircuitopened.png');
  await eventFilter(['OnRetry', 'OnCircuitOpened', 'OnCircuitHalfOpened', 'OnCircuitClosed'], 'metrics-polly-retry-and-circuit.png');
  await m('aggregator', 'Polly', 'resilience.polly.pipeline.duration', 'metrics-polly-pipeline-duration.png');
  await m('aggregator', 'Polly', 'resilience.polly.strategy.attempt.duration', 'metrics-polly-attempt-duration.png');
  await m('booking-api', 'BookingService.Api', 'bookings.created', 'metrics-bookings-created.png');
  await m('booking-api', 'BookingService.Api', 'bookings.operation.duration', 'metrics-bookings-operation-duration.png');
  await m('booking-api', 'BookingService.Api', 'bookings.current', 'metrics-bookings-current-gauge.png');
  await m('aggregator', 'Platform.Aggregator', 'aggregator.responses', 'metrics-aggregator-responses.png');
  await m('catalog-api', 'CatalogService.Api', 'catalog.screenings.upcoming', 'metrics-catalog-screenings-upcoming.png');
  await shot(logs('lab07-reviews-down-http-0'), 'logs-retry-http.png', 800);
  await shot(logs('lab07-reviews-down-grpc-0'), 'logs-retry-grpc.png', 800);
  await shot(logs('lab07-reviews-down-http-1'), 'logs-circuit-opened-http.png', 1000);
  await shot(logs('lab07-reviews-down-grpc-1'), 'logs-circuit-opened-grpc.png', 1000);
  await shot(logs('lab07-reviews-down-http-2'), 'logs-circuit-open-fast-fail.png', 700);
  await shot(`/traces/detail/${ids['lab07-reviews-down-http-0']}`, 'trace-retry-http.png', 900);
  await shot(`/traces/detail/${ids['lab07-reviews-down-http-2']}`, 'trace-circuit-open-fast-fail.png', 900);
  await shot(`/traces/detail/${ids['lab07-reviews-down-grpc-0']}`, 'trace-retry-grpc.png', 900);
  await shot(logs('lab07-circuit-closed-http'), 'logs-circuit-closed.png', 700);
  await browser.close();
  save('demo-output.txt', report.join('\n') + '\n');
  console.log('OK');
})().catch(e => { console.error(e); fs.writeFileSync(path.join(out, 'demo-output.txt'), report.join('\n')); process.exit(1); });
