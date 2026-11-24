const { execSync } = require('child_process');
const sh = c => { try { return execSync(c, { encoding: 'utf8' }); } catch (e) { return 'ERR ' + e.message + (e.stdout || ''); } };
const G = 'http://localhost:5000';
async function http(method, url, body, cid) {
  const headers = { 'Content-Type': 'application/json' };
  if (cid) headers['X-Correlation-Id'] = cid;
  try {
    const r = await fetch(url.startsWith('http') ? url : G + url, { method, headers, body: body ? JSON.stringify(body) : undefined, signal: AbortSignal.timeout(20000) });
    const t = await r.text(); let j; try { j = JSON.parse(t); } catch { j = t; }
    return { status: r.status, body: j, headers: Object.fromEntries(r.headers) };
  } catch (e) { return { status: 'ERR', body: e.message }; }
}
const pw = () => /messaging-password = (\S+)/.exec(sh(`dotnet user-secrets list --project "${require('path').join(__dirname, '..', '..', '..', 'Platform.AppHost')}"`))[1];
async function mq(method, path, body, attempt = 0) {
  let r; try { r = await fetch('http://localhost:15672/api' + path, { method, body: body ? JSON.stringify(body) : undefined,
    headers: { 'Content-Type': 'application/json', Authorization: 'Basic ' + Buffer.from('guest:' + pw()).toString('base64') }, signal: AbortSignal.timeout(15000) }); } catch (e) { if (attempt < 2) { await sleep(500); return mq(method, path, body, attempt + 1); } throw e; }
  const t = await r.text(); try { return JSON.parse(t); } catch { return t; }
}
const queues = async () => (await mq('GET', '/queues/%2F?columns=name,messages,messages_ready,messages_unacknowledged,consumers'))
  .map(q => `${q.name}: messages=${q.messages} consumers=${q.consumers}`).join('\n');
const sleep = ms => new Promise(r => setTimeout(r, ms));
module.exports = { sh, http, mq, queues, sleep, G };
