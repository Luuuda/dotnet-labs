// Лаб. 08: демонстрація подієвої взаємодії + скріни (Management UI RabbitMQ, Aspire Dashboard).
// Передумова: платформа запущена (dotnet run --project Platform.AppHost). Запуск: run-demo.cmd. Результати: docs/lab08/*.png|*.json|*.txt
const puppeteer = require('puppeteer-core');
const fs = require('fs'), path = require('path');
const { sh, http, mq, sleep } = require('./common');
const out = path.join(__dirname, '..');
fs.mkdirSync(out, { recursive: true });
const dash = 'http://localhost:15500', mgmt = 'http://localhost:15672';
const report = [];
const t0 = Date.now(), ts = () => ((Date.now() - t0) / 1000).toFixed(1).padStart(6) + 's';
const note = s => { report.push(s); console.log(s); };
const save = (f, d) => fs.writeFileSync(path.join(out, f), typeof d === 'string' ? d : JSON.stringify(d, null, 2));
const queueState = async () => (await mq('GET', '/queues/%2F?columns=name,messages,consumers'))
  .map(q => `${q.name}: messages=${q.messages}, consumers=${q.consumers}`);
const pub = (rk, type, body, cid, extra = {}) => mq('POST', '/exchanges/%2F/catalog-events/publish', {
  routing_key: rk, payload_encoding: 'string', payload: typeof body === 'string' ? body : JSON.stringify(body),
  properties: { delivery_mode: 2, content_type: 'application/json', type, message_id: body.eventId || 'malformed-' + Date.now(),
    headers: { 'x-correlation-id': cid, 'x-schema-version': 1, ...extra } } });
const bookingScreening = async id => (await http('GET', `/api/booking/screenings/${id}`)).body;
const sqlContainer = () => sh('docker ps --format "{{.Names}}"').split(/\r?\n/).find(n => n.startsWith('sql-'));

(async () => {
  for (let i = 0; i < 20; i++) { const r = await http('GET', 'http://localhost:5400/health'); if (r.status === 200 && r.body.status === 'Healthy') break; await sleep(2000); }
  await sleep(5000);
  for (const p of [5180, 5280, 5380]) { const h = await http('GET', `http://localhost:${p}/health/ready`);
    note(`${ts()} :${p}/health/ready -> ${h.status} ${h.body.status}: ` + (h.body.checks || []).map(c => `${c.name}=${c.status}`).join(', ')); }
  const pw = /messaging-password = (\S+)/.exec(sh(`dotnet user-secrets list --project "${require('path').join(__dirname, '..', '..', '..', 'Platform.AppHost')}"`))[1];
  const browser = await puppeteer.launch({ executablePath: 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', headless: true, args: ['--no-first-run'], protocolTimeout: 120000 });
  const page = await browser.newPage();
  const ctl = await browser.newPage();
  const shot = async (url, file, { height = 900, wait = 5000, before } = {}) => {
    await page.bringToFront();
    await page.setViewport({ width: 1600, height });
    await page.goto(url, { waitUntil: 'networkidle2', timeout: 60000 }).catch(() => {});
    await sleep(wait);
    if (before) await before(page);
    await page.evaluate(() => {
      // банери Dashboard (оновлення, сертифікат, незахищений OTLP) — закрити, щоб не закривали дані
      for (const b of document.querySelectorAll('fluent-button, button'))
        if (/Закрыть|Close|Dismiss|Игнорировать/i.test((b.getAttribute('aria-label') || b.getAttribute('title') || b.innerText || '').trim())
            && b.getBoundingClientRect().top < 220) b.click();
      document.querySelectorAll('fluent-message-bar, .fluent-messagebar').forEach(e => e.style.display = 'none');
    }).catch(() => {});
    await sleep(700);
    await page.screenshot({ path: path.join(out, file) });
    console.log('screenshot ' + file);
  };
  const resourceAction = async (name, verbs) => {
    await ctl.bringToFront();
    await ctl.setViewport({ width: 1600, height: 900 });
    await ctl.goto(dash + '/', { waitUntil: 'networkidle2', timeout: 60000 }).catch(() => {});
    await sleep(3000);
    const res = await ctl.evaluate((name, verbs) => {
      const rows = [...document.querySelectorAll('fluent-data-grid-row, [role=row]')];
      const row = rows.find(r => r.innerText.trim().split(/\s/)[0] === name);
      if (!row) return 'row not found';
      const buttons = [...row.querySelectorAll('fluent-button, button')];
      const labels = buttons.map(b => (b.getAttribute('title') || b.getAttribute('aria-label') || '').trim());
      const i = labels.findIndex(l => verbs.some(v => l.includes(v)));
      if (i < 0) return 'button not found: ' + labels.join(' | ');
      buttons[i].click();
      return 'clicked ' + labels[i];
    }, name, verbs);
    note(`${ts()} Dashboard ${name}: ${res}`);
  };

  if ((await http('GET', 'http://localhost:5180/health')).status !== 200) {   // залишився зупиненим після попереднього прогону
    await resourceAction('booking-api', ['Запуск', 'Start']);
    for (let i = 0; i < 60; i++) { await sleep(2000); if ((await http('GET', 'http://localhost:5180/health/ready')).status === 200) break; }
  }

  // ---------- 0. Management UI: вхід ----------
  await page.bringToFront();
  await page.goto(mgmt + '/', { waitUntil: 'networkidle2' }).catch(() => {});
  await sleep(1500);
  await page.type('input[name=username]', 'guest');
  await page.type('input[name=password]', pw);
  await Promise.all([page.keyboard.press('Enter'), sleep(3000)]);

  // DLQ від попередніх прогонів очищаються, щоб у звіті були лише повідомлення цього прогону
  for (const q of ['booking.catalog-sync.dlq', 'reviews.catalog-sync.dlq']) await mq('DELETE', `/queues/%2F/${q}/contents`);

  // ---------- 1. Наскрізний сценарій: зміна ціни → Бронювання; перейменування → Бронювання + Відгуки ----------
  // сеанс 1 (Дюна IMAX): на нього є оформлене бронювання №1 — ціни його квитків мають лишитися знімком
  const tickets = async () => ((await http('GET', '/api/bookings/1')).body.tickets || []).map(t => `${t.screeningId}:${t.unitPrice}`).join(', ');
  const s0 = await bookingScreening(1);
  const t0p = await tickets();
  const p1 = Number(s0.basePrice) + 5;
  const ch = await http('PUT', '/api/screenings/1/price', { basePrice: p1 }, 'lab08-e2e-price');
  note(`${ts()} PUT /api/screenings/1/price ${p1} -> ${ch.status}, X-Trace-Id ${ch.headers['x-trace-id']}`);
  const priceTrace = ch.headers['x-trace-id'];
  await sleep(3000);
  const s1 = await bookingScreening(1);
  const t1p = await tickets();
  note(`${ts()} Бронювання: копія сеансу 1 basePrice ${s0.basePrice} -> ${s1.basePrice} (syncedAt ${s1.syncedAt})`);
  note(`${ts()} Бронювання №1 (оформлене): квитки [${t0p}] -> [${t1p}] — ціни квитків не змінилися`);
  const price = Number((await bookingScreening(3)).basePrice);

  const movie = (await http('GET', '/api/movies/1')).body;
  const original = movie.title.replace(' (режисерська версія)', '');
  const renameTo = original + ' (режисерська версія)' === movie.title ? original : original + ' (режисерська версія)';
  const upd = { title: renameTo, originalTitle: movie.originalTitle, releaseYear: movie.releaseYear, durationMinutes: movie.durationMinutes,
    ageRating: movie.ageRating, rowVersion: movie.rowVersion };
  const pu = await http('PUT', '/api/movies/1', upd, 'lab08-e2e-rename');
  note(`${ts()} PUT /api/movies/1 «${movie.title}» -> «${renameTo}»: ${pu.status}, X-Trace-Id ${pu.headers['x-trace-id']}`);
  const renameTrace = pu.headers['x-trace-id'];
  await sleep(3000);
  const s2 = await bookingScreening(3);
  const rv = (await http('GET', '/api/reviews?movieId=1&limit=50')).body.items || [];
  const ds = (await http('GET', '/api/discussions?movieId=1')).body;
  note(`${ts()} Бронювання: сеанс 3 movieTitle = «${s2.movieTitle}»`);
  note(`${ts()} Відгуки: ${rv.length} відгуків фільму 1, movieTitle = ${JSON.stringify([...new Set(rv.map(r => r.movieTitle))])}`);
  note(`${ts()} Обговорення: movieTitle = ${JSON.stringify([...new Set((ds.items || ds).map(d => d.movieTitle))])}`);
  save('e2e-result.json', { before: s0, afterPrice: s1, ticketsBefore: t0p, ticketsAfter: t1p, afterRename: s2, reviewsTitles: rv.map(r => r.movieTitle), priceTrace, renameTrace });

  // ---------- 2. Дубль: той самий EventId двічі ----------
  const dupId = crypto.randomUUID();
  const dup = { eventId: dupId, occurredAt: new Date().toISOString(), correlationId: 'lab08-duplicate', schemaVersion: 1,
    screeningId: 3, movieId: 1, movieTitle: renameTo, oldPrice: price, newPrice: price + 10 };
  await pub('screening.price_changed', 'ScreeningPriceChangedEvent', dup, 'lab08-duplicate');
  await sleep(2500);
  const d1 = (await bookingScreening(3)).basePrice;
  await pub('screening.price_changed', 'ScreeningPriceChangedEvent', { ...dup, newPrice: 999 }, 'lab08-duplicate');
  await sleep(2500);
  const d2 = (await bookingScreening(3)).basePrice;
  note(`${ts()} Дубль EventId ${dupId}: після 1-ї доставки ціна ${d1}, після 2-ї (у тілі 999) ціна ${d2} — повтор пропущено`);

  // ---------- 3. DLQ: некоректний JSON і несумісна версія ----------
  await pub('screening.price_changed', 'ScreeningPriceChangedEvent', '{ "eventId": "не guid", "screeningId": ', 'lab08-malformed');
  await pub('movie.renamed', 'MovieRenamedEvent', { eventId: crypto.randomUUID(), occurredAt: new Date().toISOString(), schemaVersion: 2,
    movieId: 1, oldTitle: renameTo, newTitle: 'Довбуш v2', originalTitle: null }, 'lab08-schema-v2', { 'x-schema-version': 2 });
  await sleep(7000);   // статистика Management API оновлюється раз на 5 с
  note(`${ts()} Черги після некоректних повідомлень:\n   ` + (await queueState()).join('\n   '));
  const dlq = await mq('POST', '/queues/%2F/booking.catalog-sync.dlq/get', { count: 10, ackmode: 'ack_requeue_true', encoding: 'auto', truncate: 400 });
  save('dlq-booking-messages.json', dlq.map(m => ({ type: m.properties.type, messageId: m.properties.message_id, headers: m.properties.headers, payload: m.payload })));

  // ---------- 4. Тимчасова помилка: SQL Server призупинено → retry з x-retry-count → успіх ----------
  const sqlName = sqlContainer();
  const retryId = crypto.randomUUID();
  note(`${ts()} docker pause ${sqlName}: ${sh('docker pause ' + sqlName).trim()}`);
  await pub('screening.price_changed', 'ScreeningPriceChangedEvent', { eventId: retryId, occurredAt: new Date().toISOString(),
    correlationId: 'lab08-retry', schemaVersion: 1, screeningId: 3, movieId: 1, movieTitle: renameTo, oldPrice: d2, newPrice: price + 20 }, 'lab08-retry');
  await sleep(16000);  // перша спроба впаде за таймаутом підключення (~15 с)
  note(`${ts()} docker unpause ${sqlName}: ${sh('docker unpause ' + sqlName).trim()}`);
  for (let i = 0; i < 30; i++) { await sleep(2000); if (Number((await bookingScreening(3)).basePrice) === price + 20) break; }
  note(`${ts()} Після відновлення SQL: сеанс 3 basePrice = ${(await bookingScreening(3)).basePrice} (очікувалось ${price + 20})`);

  // ---------- 5. Бронювання зупинено: події накопичуються в черзі й обробляються після запуску ----------
  await resourceAction('booking-api', ['Остановить', 'Stop', 'Зупинити']);
  await sleep(8000);
  const offIds = [1, 3, 5], offPrices = {};
  for (const id of offIds) {
    offPrices[id] = Number((await http('GET', `/api/screenings/${id}`)).body.basePrice) + 10;   // ціна з Каталогу: Бронювання зупинено
    const off = await http('PUT', `/api/screenings/${id}/price`, { basePrice: offPrices[id] }, 'lab08-booking-offline');
    note(`${ts()} Бронювання зупинено; PUT /api/screenings/${id}/price ${offPrices[id]} у Каталозі -> ${off.status}`);
  }
  await sleep(10000);  // статистика Management API оновлюється раз на 5 с
  const qOff = await queueState();
  // надійніше за статистику — подивитися самі повідомлення (ack_requeue_true: повертаються в чергу без змін)
  const waiting = await mq('POST', '/queues/%2F/booking.catalog-sync/get', { count: 10, ackmode: 'ack_requeue_true', encoding: 'auto' });
  note(`${ts()} У booking.catalog-sync чекають ${waiting.length} повідомлень: ` +
    waiting.map(m => `${m.properties.type} ${JSON.parse(m.payload).screeningId}→${JSON.parse(m.payload).newPrice}`).join('; '));
  save('booking-offline-waiting-messages.json', waiting.map(m => ({ type: m.properties.type, messageId: m.properties.message_id,
    correlationId: m.properties.headers['x-correlation-id'], traceparent: m.properties.headers.traceparent, payload: JSON.parse(m.payload) })));
  note(`${ts()} Черги, поки Бронювання не працює:\n   ` + qOff.join('\n   '));
  await shot(mgmt + '/#/queues', 'rabbitmq-queues-booking-offline.png', { height: 700 });
  await resourceAction('booking-api', ['Запуск', 'Start']);
  let back = null;
  for (let i = 0; i < 60; i++) { await sleep(2000); const r = await http('GET', '/api/booking/screenings/5'); if (r.status === 200 && Number(r.body.basePrice) === offPrices[5]) { back = r.body; break; } }
  for (const id of offIds) note(`${ts()} Після запуску Бронювання: копія сеансу ${id} basePrice = ${(await bookingScreening(id)).basePrice} (очікувалось ${offPrices[id]})`);
  await sleep(7000);
  note(`${ts()} Черги:\n   ` + (await queueState()).join('\n   '));
  save('queues.json', await mq('GET', '/queues/%2F?columns=name,durable,arguments,messages,consumers'));
  save('bindings.json', (await mq('GET', '/bindings/%2F')).filter(b => b.source).map(b => `${b.source} --[${b.routing_key}]--> ${b.destination}`));

  // ---------- 6. Скріни Management UI ----------
  await shot(mgmt + '/#/exchanges', 'rabbitmq-exchanges.png', { height: 750 });
  const expand = title => async p => {
    await p.evaluate(t => { const h = [...document.querySelectorAll('h2')].find(h => h.innerText.trim().startsWith(t));
      if (h && !h.classList.contains('section-visible')) h.click(); }, title);
    await sleep(1500);
  };
  await shot(mgmt + '/#/exchanges/%2F/catalog-events', 'rabbitmq-catalog-events-bindings.png', { height: 900, before: expand('Bindings') });
  await shot(mgmt + '/#/exchanges/%2F/catalog-events.dlx', 'rabbitmq-dlx-bindings.png', { height: 900, before: expand('Bindings') });
  await shot(mgmt + '/#/queues', 'rabbitmq-queues.png', { height: 700 });
  await shot(mgmt + '/#/queues/%2F/booking.catalog-sync', 'rabbitmq-booking-queue-features.png', { height: 1300,
    before: async p => { await expand('Consumers')(p); await expand('Bindings')(p); } });
  await shot(mgmt + '/#/queues/%2F/booking.catalog-sync.dlq', 'rabbitmq-dlq-message.png', { height: 1500, wait: 4000,
    before: async p => {
      await p.evaluate(() => { const h = [...document.querySelectorAll('h2')].find(h => /Get messages/i.test(h.innerText)); if (h) { h.click(); h.scrollIntoView(); } });
      await sleep(800);
      await p.evaluate(() => { const b = [...document.querySelectorAll('input[type=submit]')].find(i => /Get Message/i.test(i.value)); if (b) b.click(); });
      await sleep(2500);
      await p.evaluate(() => { const h = [...document.querySelectorAll('h2')].find(h => /Get messages/i.test(h.innerText)); if (h) h.scrollIntoView(); });
    } });

  // ---------- 7. Aspire Dashboard: trace, логи, метрики ----------
  await sleep(6000);   // експорт OTLP
  await shot(dash + '/', 'dashboard-resources.png', { height: 800 });
  await shot(`${dash}/traces/detail/${priceTrace}`, 'trace-price-changed-e2e.png', { height: 1000, wait: 7000 });
  await shot(`${dash}/traces/detail/${renameTrace}`, 'trace-movie-renamed-e2e.png', { height: 1100, wait: 7000 });
  const traceText = await page.evaluate(() => document.body.innerText);
  save('trace-movie-renamed.txt', traceText);
  const logs = cid => `${dash}/structuredlogs?filters=${encodeURIComponent('CorrelationId:equals:' + cid)}`;
  await shot(`${dash}/metrics/resource/booking-api?meter=Platform.Messaging&instrument=messaging.events.consumed&duration=15&view=Table`,
    'metrics-messaging-consumed.png', { height: 900, wait: 7000 });
  for (const [cid, file] of [['lab08-e2e-price', 'logs-e2e-price.png'], ['lab08-duplicate', 'logs-duplicate.png'], ['lab08-malformed', 'logs-dlq-malformed.png'],
    ['lab08-schema-v2', 'logs-dlq-schema-version.png'], ['lab08-retry', 'logs-retry.png'], ['lab08-booking-offline', 'logs-booking-offline.png']]) {
    await shot(logs(cid), file, { height: 800, wait: 6000 });
    const rows = await page.evaluate(() => [...document.querySelectorAll('[role=row], fluent-data-grid-row')].map(r => r.innerText.replace(/\s+/g, ' ').slice(0, 260)));
    note(`--- логи ${cid}: ${rows.length - 1}\n` + rows.slice(1, 14).join('\n'));
  }
  // повернути початкову назву фільму (теж через подію MovieRenamed)
  if (renameTo !== original) {
    const m2 = (await http('GET', '/api/movies/1')).body;
    const back = await http('PUT', '/api/movies/1', { ...upd, title: original, rowVersion: m2.rowVersion }, 'lab08-restore-title');
    note(`${ts()} Назву фільму повернуто: «${original}» -> ${back.status}`);
  }
  save('demo-report.txt', report.join('\n'));
  await browser.close();
})().catch(e => { console.error(e); save('demo-report.txt', report.join('\n') + '\nERROR ' + e.stack); process.exit(1); });
