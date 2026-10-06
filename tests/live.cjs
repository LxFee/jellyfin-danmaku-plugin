const fs = require('node:fs');
const assert = require('node:assert/strict');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const credentials = JSON.parse(fs.readFileSync(process.env.JELLYFIN_TEST_CREDENTIALS, 'utf8'));
const root = process.env.JELLYFIN_TEST_URL;
if (!root) throw new Error('Set JELLYFIN_TEST_URL to your Jellyfin test server');
const fixture = process.env.DANMAKU_FIXTURE === '1';
const ownMode = process.env.DANMAKU_TEST_OWN_MODE === '1';
if (ownMode && !fixture) throw new Error('Own-mode host validation requires fixture responses');
const errors = [];
(async () => {
    const browser = await chromium.launch({ headless: true, channel: 'chrome' });
    let page, restoreConfig;
    const sessionRequests = [];
    try {
        page = await browser.newPage({ viewport: { width: 1440, height: 1000 }, serviceWorkers: 'block' });
        page.setDefaultTimeout(30000);
        page.on('pageerror', error => { if (error.message !== 'CancelledError') errors.push(error.message); });
        if (fixture) await page.route(/api\.dandanplay\.net|\/JellyfinDanmaku\/api\/v2\//, route => {
            const url = route.request().url();
            if (url.includes('/JellyfinDanmaku/api/v2/')) sessionRequests.push({ sameOrigin: new URL(url).origin === new URL(root).origin,
                authenticated: route.request().headers().authorization?.startsWith('MediaBrowser Token="') === true });
            const body = url.includes('/search/episodes') ? { animes: [{ animeId: 123, animeTitle: '兼容性测试', type: 'tvseries', typeDescription: 'TV', episodes: Array.from({ length: 100 }, (_, i) => ({ episodeId: 123001 + i, episodeTitle: '第' + (i + 1) + '话' })) }] }
                : url.includes('/related/') ? { relateds: [] }
                : url.includes('/comment/') ? { comments: Array.from({ length: 1400 }, (_, i) => ({ cid: String(i), p: `${i},1,16777215,[BiliBili]fixture`, m: '弹幕兼容性测试 ' + i })) }
                : {};
            return route.fulfill({ json: body, headers: { 'access-control-allow-origin': '*' } });
        });
        await page.goto(root + '/web/index.html#/login', { waitUntil: 'commit' });
        await page.locator('.btnManual:visible, #txtManualName:visible').first().waitFor();
        if (!await page.locator('#txtManualName').isVisible()) {
            try { await page.locator('.btnManual:visible').click({ timeout: 5000 }); }
            catch (error) { if (!await page.locator('#txtManualName').isVisible()) throw error; }
        }
        await page.locator('#txtManualName').fill(credentials.Username);
        await page.locator('#txtManualPassword').fill(credentials.Password);
        await page.locator('form button.button-submit[type="submit"]:visible').click();
        await page.waitForURL(url => /^#\/(home|index)/.test(url.hash));
        assert.equal(await page.evaluate(() => !!window.JellyfinDanmakuLoaded), true);
        assert.equal(await page.locator('script[src*="/JellyfinEdge/edge.js"]').count(), 1);
        console.log('PASS actual 12.2 plugin script and Edge coexistence');
        const anonymous = await page.request.get(root + '/JellyfinDanmaku/api/v2/comment/1');
        assert.equal(anonymous.status(), 401);
        console.log('PASS real Jellyfin endpoint rejects anonymous access');
        if (ownMode) {
            restoreConfig = await page.evaluate(() => ApiClient.getPluginConfiguration('aab50987-0d10-4615-96e3-3a89b758dd28'));
            await page.evaluate(config => ApiClient.updatePluginConfiguration('aab50987-0d10-4615-96e3-3a89b758dd28',
                { ...config, UseOwnCredentials: true, AppId: 'fixture-app', AppSecret: 'not-a-real-secret' }), restoreConfig);
            const script = await (await page.request.get(root + '/JellyfinDanmaku/ede.js')).text();
            assert(script.includes('"serverApiPrefix":"/JellyfinDanmaku"') && !script.includes('fixture-app') && !script.includes('not-a-real-secret'));
            const session = await page.evaluate(() => ApiClient.accessToken());
            const backend = await page.request.get(root + '/JellyfinDanmaku/api/v2/unsupported', { headers: { Authorization: 'MediaBrowser Token="' + session + '"' } });
            assert.equal(backend.status(), 400); // Real controller authenticates before rejecting the route; no upstream request.
            console.log('PASS actual host authorization without response interception');
            // Jellyfin hash navigation keeps the already loaded script defaults. Reload after changing mode.
            await page.reload({ waitUntil: 'commit' });
            await page.waitForFunction(() => window.JellyfinDanmakuConfig?.serverApiPrefix === '/JellyfinDanmaku');
            console.log('PASS own configuration persistence and public script secret isolation');
        }
        await page.goto(root + '/web/index.html#/configurationpage?name=JellyfinDanmaku', { waitUntil: 'commit' });
        await page.locator('#danmakuConfigPage').waitFor({ state: 'visible' });
        await page.waitForFunction(() => document.querySelector('#danmakuApi').value.startsWith('https://'));
        assert.equal(await page.locator('#danmakuAppSecret').getAttribute('type'), 'password');
        assert.equal(await page.locator('#danmakuOwnCredentials').count(), 1);
        if (ownMode) assert.equal(await page.locator('#danmakuOwnCredentials').isChecked(), true);
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
        await page.waitForFunction(() => !!window.ede.danmaku && !window.ede.loading);
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
        if (ownMode) {
            assert(sessionRequests.length > 0 && sessionRequests.every(r => r.sameOrigin && r.authenticated));
            console.log('PASS real own-mode reads carry same-origin Jellyfin session');
        }
        console.log('PASS no browser exceptions; source=' + (fixture ? 'deterministic online fixtures' : 'real upstream online service'));
    } finally {
        try {
            if (restoreConfig && page) {
                await page.evaluate(config => ApiClient.updatePluginConfiguration('aab50987-0d10-4615-96e3-3a89b758dd28', config), restoreConfig);
                console.log('PASS original host credential configuration restored');
            }
        } finally { await browser.close(); }
    }
})().catch(error => {
    console.error(error.name + ': ' + error.message.replace(/https?:\/\/\S+/g, '[url]'));
    console.error('Browser exceptions: ' + JSON.stringify(errors).replace(/https?:\/\/\S+/g, '[url]'));
    process.exitCode = 1;
});
