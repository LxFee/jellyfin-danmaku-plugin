const fs = require('node:fs');
const assert = require('node:assert/strict');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const credentials = JSON.parse(fs.readFileSync(process.env.JELLYFIN_TEST_CREDENTIALS, 'utf8'));
const root = process.env.JELLYFIN_TEST_URL;
if (!root) throw new Error('Set JELLYFIN_TEST_URL to your Jellyfin test server');
const fixture = process.env.DANMAKU_FIXTURE === '1';
const errors = [];
(async () => {
    const browser = await chromium.launch({ headless: true, channel: 'chrome' });
    try {
        const page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, serviceWorkers: 'block' });
        page.setDefaultTimeout(30000);
        page.on('pageerror', error => { if (error.message !== 'CancelledError') errors.push(error.message); });
        if (fixture) await page.route(/api\.dandanplay\.net/, route => {
            const url = route.request().url();
            const body = url.includes('/search/episodes') ? { animes: [{ animeId: 123, animeTitle: '兼容性测试', type: 'tvseries', typeDescription: 'TV', episodes: Array.from({ length: 100 }, (_, i) => ({ episodeId: 123001 + i, episodeTitle: '第' + (i + 1) + '话' })) }] }
                : url.includes('/related/') ? { relateds: [] }
                : url.includes('/comment/') ? { comments: Array.from({ length: 1400 }, (_, i) => ({ cid: String(i), p: `${i},1,16777215,[BiliBili]fixture`, m: '弹幕兼容性测试 ' + i })) }
                : {};
            return route.fulfill({ json: body, headers: { 'access-control-allow-origin': '*' } });
        });
        await page.goto(root + '/web/index.html#/login', { waitUntil: 'commit' });
        await page.locator('.btnManual:visible, #txtManualName:visible').first().waitFor();
        if (await page.locator('.btnManual').isVisible()) await page.locator('.btnManual').click();
        await page.locator('#txtManualName').fill(credentials.Username);
        await page.locator('#txtManualPassword').fill(credentials.Password);
        await page.locator('form button.button-submit[type="submit"]:visible').click();
        await page.waitForURL(url => /^#\/(home|index)/.test(url.hash));
        assert.equal(await page.evaluate(() => !!window.JellyfinDanmakuLoaded), true);
        assert.equal(await page.locator('script[src*="/JellyfinEdge/edge.js"]').count(), 1);
        console.log('PASS actual 12.2 plugin script and Edge coexistence');
        await page.goto(root + '/web/index.html#/configurationpage?name=JellyfinDanmaku', { waitUntil: 'commit' });
        await page.locator('#danmakuConfigPage').waitFor({ state: 'visible' });
        await page.waitForFunction(() => document.querySelector('#danmakuApi').value.startsWith('https://'));
        await page.locator('#danmakuConfigForm button[type=submit]').click();
        await page.waitForFunction(() => document.querySelector('#danmakuConfigStatus').textContent.includes('已保存'));
        console.log('PASS native plugin configuration read/save');
        await page.goto(root + '/web/index.html#/details?id=' + credentials.ItemId, { waitUntil: 'commit' });
        await page.locator('.btnPlay:visible').first().click();
        await page.waitForFunction(() => document.querySelector('video')?.currentTime > 0, { timeout: 30000 });
        console.log('PASS real media advances in native player');
        await page.locator('#displayDanmaku').waitFor();
        try { await page.waitForFunction(() => !!window.ede?.danmaku); }
        catch (error) {
            console.log('Player diagnostic: ' + JSON.stringify(await page.evaluate(() => ({ control: !!document.querySelector('#displayDanmaku'),
                loading: window.ede?.loading, title: document.querySelector('#danmakuInfoTitle')?.textContent,
                logs: document.querySelector('#debugInfo')?.textContent.slice(-1200) }))));
            throw error;
        }
        const state = await page.evaluate(() => ({ loaded: !!window.ede.danmaku, playing: document.querySelector('video').currentTime > 0,
            comments: window.ede.danmaku.comments?.length, title: document.querySelector('#danmakuInfoTitle')?.textContent,
            canvas: !!document.querySelector('#danmakuWrapper canvas'), count: document.querySelectorAll('#danmakuCtr').length }));
        assert(state.loaded && state.playing && state.canvas && state.comments > 0 && state.count === 1);
        console.log('PASS actual video playback, comment fetch/match and canvas rendering: ' + JSON.stringify(state));
        await page.mouse.move(1000, 800);
        await page.locator('#displayDanmaku').click();
        assert.equal(await page.evaluate(() => window.ede.danmakuSwitch), 0);
        await page.locator('#displayDanmaku').click();
        assert.equal(await page.evaluate(() => window.ede.danmakuSwitch), 1);
        await page.locator('#danmakuSettings').click();
        await page.locator('#danmakuSidebar').waitFor({ state: 'visible' });
        await page.waitForFunction(() => Math.abs(document.querySelector('#danmakuSidebar').getBoundingClientRect().right - innerWidth) < 2);
        await page.screenshot({ path: process.env.DANMAKU_SCREENSHOT || 'artifacts/danmaku-live.png' });
        console.log('PASS toggle, settings button and sidebar');
        await page.locator('#opacity').waitFor({ state: 'attached' });
        await page.locator('.danmakuSidebarSaveButton').click();
        await page.waitForFunction(() => !window.ede.loading && !!window.ede.danmaku);
        // Jellyfin stops playback when the app navigates back from the player.
        await page.goBack();
        await page.waitForFunction(() => !document.querySelector('video'));
        await page.waitForFunction(() => !document.querySelector('#danmakuWrapper') && !window.ede.danmaku);
        await page.locator('.btnPlay:visible').first().click();
        await page.waitForFunction(() => document.querySelector('video')?.currentTime > 0 && !!window.ede?.danmaku);
        assert.equal(await page.locator('#danmakuCtr').count(), 1);
        console.log('PASS exit cleanup and replay without duplicate controls');
        assert.deepEqual(errors, []);
        console.log('PASS no browser exceptions; source=' + (fixture ? 'deterministic online fixtures' : 'real upstream online service'));
    } finally { await browser.close(); }
})().catch(error => {
    console.error(error.name + ': ' + error.message.replace(/https?:\/\/\S+/g, '[url]'));
    console.error('Browser exceptions: ' + JSON.stringify(errors).replace(/https?:\/\/\S+/g, '[url]'));
    process.exitCode = 1;
});
