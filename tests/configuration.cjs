const fs = require('node:fs');
const assert = require('node:assert/strict');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const credentials = JSON.parse(fs.readFileSync(process.env.JELLYFIN_TEST_CREDENTIALS, 'utf8'));
const root = process.env.JELLYFIN_TEST_URL;
if (!root) throw new Error('Set JELLYFIN_TEST_URL to your Jellyfin test server');
const pluginId = 'aab50987-0d10-4615-96e3-3a89b758dd28';
const configUrl = root + '/web/index.html#/configurationpage?name=JellyfinDanmaku';
const errors = [];

(async () => {
    const browser = await chromium.launch({ headless: true, channel: 'chrome' });
    let page, original;
    try {
        page = await browser.newPage({ viewport: { width: 1440, height: 1100 }, serviceWorkers: 'block' });
        page.on('pageerror', error => { if (error.message !== 'CancelledError') errors.push(error.message); });
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
        original = await page.evaluate(id => ApiClient.getPluginConfiguration(id), pluginId);
        assert(!['UseOwnCredentials', 'AppId', 'AppSecret'].some(key => key in original));
        await page.goto(configUrl, { waitUntil: 'commit' });
        const api = page.locator('#danmakuApi');
        const cors = page.locator('#danmakuCors');
        const form = page.locator('#JellyfinDanmakuConfigForm');
        await page.waitForFunction(() => !!document.querySelector('#danmakuApi')?.value);
        assert.equal(await page.locator('#danmakuSourceMode, #danmakuAppId, #danmakuAppSecret').count(), 0);
        assert(await api.isVisible() && await cors.isVisible() && !await api.isDisabled() && !await cors.isDisabled());
        const appearance = await page.evaluate(() => Array.from(document.querySelectorAll('#JellyfinDanmakuConfigForm .emby-input')).map(el => {
            const style = getComputedStyle(el);
            return { labels: el.labels.length, styles: ['backgroundColor', 'border', 'borderRadius', 'fontSize', 'padding', 'color'].map(key => style[key]) };
        }));
        assert.equal(appearance.length, 2);
        assert(appearance.every(input => input.labels === 1 && JSON.stringify(input.styles) === JSON.stringify(appearance[0].styles)));
        await api.fill('');
        assert.equal(await form.evaluate(el => el.checkValidity()), false);
        const customApi = 'https://danmaku.example.test/edge';
        await api.fill(customApi);
        await cors.fill('');
        assert.equal(await form.evaluate(el => el.checkValidity()), true);
        await form.locator('button[type=submit]').click();
        await page.waitForFunction(() => document.querySelector('#danmakuConfigStatus').textContent.includes('已保存'));
        const value = await page.evaluate(id => ApiClient.getPluginConfiguration(id), pluginId);
        assert.equal(value.ApiBaseUrl, customApi);
        assert.equal(value.CorsProxyUrl, '');
        await page.reload({ waitUntil: 'commit' });
        await page.waitForFunction(() => document.querySelector('#danmakuApi')?.value === 'https://danmaku.example.test/edge');
        assert.equal(await cors.inputValue(), '');
        await page.screenshot({ path: process.env.DANMAKU_CONFIG_SCREENSHOT || 'artifacts/configuration-public.png', fullPage: true });
        assert.deepEqual(errors, []);
        console.log('PASS native API/CORS controls, obsolete credentials removed, required API, empty CORS, save/reload and consistent styles');
    } finally {
        try {
            if (original && page) {
                await page.evaluate(({ id, config }) => ApiClient.updatePluginConfiguration(id, config), { id: pluginId, config: original });
                const restored = await page.evaluate(id => ApiClient.getPluginConfiguration(id), pluginId);
                assert.deepEqual(restored, original);
                console.log('PASS original administrator configuration restored');
            }
        } finally { await browser.close(); }
    }
})().catch(error => {
    console.error(error.name + ': ' + error.message.replace(/https?:\/\/\S+/g, '[url]'));
    process.exitCode = 1;
});
