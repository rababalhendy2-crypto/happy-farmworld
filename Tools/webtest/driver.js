// Keeps one browser page open on the web build and executes command files dropped in /tmp/cmd.
// Each command file: JSON array of steps: {click:[x,y]} | {wait:ms} | {shot:"name"} | {drag:[x1,y1,x2,y2]} | {reload:true}
const { chromium } = require('playwright');
const fs = require('fs');
(async () => {
  const browser = await chromium.launch({ args: ['--use-gl=angle','--use-angle=swiftshader','--enable-unsafe-swiftshader','--ignore-gpu-blocklist'] });
  const page = await browser.newPage({ viewport: { width: 1280, height: 640 } });
  const log = [];
  page.on('console', m => { const t = m.text(); if (/error|Exception|ModelLibrary|Game|warn/i.test(t)) log.push(t.slice(0,300)); });
  page.on('pageerror', e => log.push('pageerror: ' + e.message.slice(0,300)));
  if (process.env.MOCK) {
    const mock = require('/tmp/mock.js');
    const cors = { 'Access-Control-Allow-Origin': '*', 'Access-Control-Allow-Headers': '*', 'Access-Control-Allow-Methods': 'POST, GET, OPTIONS' };
    await page.route(/supabase\.co/, async route => {
      const req = route.request();
      if (fs.existsSync('/tmp/offline')) return route.abort('internetdisconnected');
      if (req.method() == 'OPTIONS') return route.fulfill({ status: 204, headers: cors });
      let body = {}; try { body = JSON.parse(req.postData() || '{}'); } catch (e) {}
      const res = mock.handle(new URL(req.url()).pathname, body);
      fs.appendFileSync('/tmp/mock.log', new URL(req.url()).pathname + ' ' + (req.postData() || '').slice(0, 150) + '\n');
      return route.fulfill({ status: 200, headers: Object.assign({ 'Content-Type': 'application/json' }, cors), body: JSON.stringify(res) });
    });
  }
  await page.goto(process.argv[2]);
  fs.mkdirSync('/tmp/cmd', { recursive: true });
  fs.writeFileSync('/tmp/cmd/ready', 'loaded');
  while (true) {
    const files = fs.readdirSync('/tmp/cmd').filter(f => f.endsWith('.json')).sort();
    for (const f of files) {
      const steps = JSON.parse(fs.readFileSync('/tmp/cmd/' + f));
      fs.unlinkSync('/tmp/cmd/' + f);
      for (const s of steps) {
        if (s.click) { await page.mouse.move(s.click[0], s.click[1]); await page.mouse.down(); await page.waitForTimeout(s.hold||300); await page.mouse.up(); }
        if (s.drag) { await page.mouse.move(s.drag[0], s.drag[1]); await page.mouse.down(); for (let i=1;i<=10;i++){ await page.mouse.move(s.drag[0]+(s.drag[2]-s.drag[0])*i/10, s.drag[1]+(s.drag[3]-s.drag[1])*i/10); await page.waitForTimeout(30);} await page.mouse.up(); }
        if (s.wheel) { await page.mouse.move(s.wheel[0], s.wheel[1]); await page.mouse.wheel(0, s.wheel[2]); }
        if (s.type) await page.keyboard.type(s.type, { delay: 120 });
        if (s.wait) await page.waitForTimeout(s.wait);
        if (s.reload) await page.reload();
        if (s.shot) await page.screenshot({ path: '/tmp/cmd/' + s.shot + '.png' });
      }
      fs.writeFileSync('/tmp/cmd/' + f.replace('.json', '.done'), log.splice(0).join('\n'));
    }
    await page.waitForTimeout(300);
  }
})();
