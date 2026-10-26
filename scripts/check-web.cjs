// Optional smoke test: npm install --prefix .tools --no-save playwright
// Build backend/inspection-server.exe first, then run: node scripts/check-web.cjs
const { chromium } = require('../.tools/node_modules/playwright');
const { spawn } = require('node:child_process');
const { once } = require('node:events');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');

const root = path.resolve(__dirname, '..');
const api = 'http://127.0.0.1:18080';
const database = path.join(root, '.tools', `smoke-${Date.now()}.db`);
let backend, web, browser;
const wait = ms => new Promise(resolve => setTimeout(resolve, ms));

async function ready(url) {
  for (let n = 0; n < 100; n++) {
    try { if ((await fetch(url)).ok) return; } catch {}
    await wait(200);
  }
  throw new Error(`Service not ready: ${url}`);
}

function startBackend() {
  backend = spawn(path.join(root, 'backend', 'inspection-server.exe'), [], {
    cwd: root, windowsHide: true,
    env: { ...process.env, ADDR: '127.0.0.1:18080', DB_PATH: database },
    stdio: 'ignore',
  });
  return ready(`${api}/api/health`);
}

async function stop(child) {
  if (!child || child.exitCode !== null) return;
  const exited = once(child, 'exit');
  child.kill();
  await exited;
}

(async () => {
  try {
    await startBackend();
    web = spawn(process.execPath, [path.join(root, 'web/node_modules/vite/bin/vite.js'), '--host', '127.0.0.1', '--port', '5173', '--strictPort'], {
      cwd: path.join(root, 'web'), windowsHide: true,
      env: { ...process.env, VITE_API_BASE_URL: api }, stdio: 'ignore',
    });
    await ready('http://127.0.0.1:5173');
    const executablePath = [
      'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe',
      'C:/Program Files/Google/Chrome/Application/chrome.exe',
    ].find(file => fs.existsSync(file));
    browser = await chromium.launch({ headless: true, ...(executablePath ? { executablePath } : {}) });
    const page = await browser.newPage({ viewport: { width: 1440, height: 960 } });
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    await page.goto('http://127.0.0.1:5173');
    await page.getByText('暂无问题', { exact: false }).waitFor();

    const response = await fetch(`${api}/api/issues`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title: '入口墙面破损', description: '浏览器联调样例：左侧墙体存在裂缝', priority: 'high', position: { x: 0, y: .03, z: 1.26 } }),
    });
    assert.equal(response.status, 201);
    const created = await response.json();
    await page.getByRole('button', { name: '刷新列表' }).click();
    await page.getByText(created.title, { exact: true }).waitFor();
    const row = page.getByRole('row').filter({ hasText: created.title });
    await row.getByRole('combobox').selectOption('in_progress');

    // A failed save must retain the previously saved state and the selected draft.
    await page.route('**/api/issues/*/status', route => route.fulfill({ status: 500, contentType: 'application/json', body: JSON.stringify({ error: { message: '测试保存失败' } }) }));
    await row.getByRole('button', { name: '保存', exact: true }).click();
    await page.getByRole('alert').filter({ hasText: '测试保存失败' }).waitFor();
    assert.equal(await row.locator('td').nth(2).textContent(), '待处理');
    assert.equal(await row.getByRole('combobox').inputValue(), 'in_progress');
    await page.unroute('**/api/issues/*/status');
    await row.getByRole('button', { name: '保存', exact: true }).click();
    await page.getByRole('status').filter({ hasText: '已更新为处理中' }).waitFor();
    const updated = (await (await fetch(`${api}/api/issues`)).json()).items[0];
    assert.equal(updated.status, 'in_progress');

    await stop(backend);
    await page.getByRole('button', { name: '刷新列表' }).click();
    await page.getByRole('alert').filter({ hasText: '无法连接后端' }).waitFor();
    assert.equal(await row.count(), 1);

    await startBackend();
    await page.getByRole('button', { name: '刷新列表' }).click();
    await row.getByText('处理中', { exact: true }).first().waitFor();
    assert.equal((await (await fetch(`${api}/api/issues`)).json()).items[0].status, 'in_progress');
    // Also verify that the page's first load fails visibly, without a white screen.
    await stop(backend);
    await page.reload();
    await page.getByRole('alert').filter({ hasText: '无法连接后端' }).waitFor();
    await page.getByRole('heading', { name: '问题管理' }).waitFor();
    await startBackend();
    await page.getByRole('button', { name: '刷新列表' }).click();
    await page.getByText(created.title, { exact: true }).waitFor();
    await page.screenshot({ path: path.join(root, 'doc', '管理端验证截图.png'), fullPage: true });
    assert.deepEqual(errors, []);
    console.log('PASS: empty list, create/refresh, failed save retained, status saved, offline error, process restart persistence, initial offline load, browser without page errors.');
  } finally {
    if (browser) await browser.close();
    await stop(web);
    await stop(backend);
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
