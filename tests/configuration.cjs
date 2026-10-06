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
        if (process.env.DANMAKU_CONFIG_PREVIEW === '1') {
            const html = fs.readFileSync('plugin/Configuration/config.html', 'utf8');
            await page.route(url => url.pathname.endsWith('/configurationpage') && url.searchParams.get('name') === 'JellyfinDanmaku',
                route => route.fulfill({ contentType: 'text/html', body: html }));
        }
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
        const fixture = { ...original, UseOwnCredentials: false, AppId: 'fixture-app', AppSecret: 'not-a-real-secret',
            ApiBaseUrl: 'https://api.dandanplay.net', CorsProxyUrl: 'https://ddplay-api.930524.xyz/cors/' };
        await page.evaluate(({ id, config }) => ApiClient.updatePluginConfiguration(id, config), { id: pluginId, config: fixture });
        await page.goto(configUrl, { waitUntil: 'commit' });
        const mode = page.locator('#danmakuSourceMode');
        const publicFields = page.locator('#JellyfinDanmakuPublicFields');
        const credentialFields = page.locator('#JellyfinDanmakuCredentialFields');
        const appId = page.locator('#danmakuAppId');
        const secret = page.locator('#danmakuAppSecret');
        const api = page.locator('#danmakuApi');
        const submit = page.locator('#JellyfinDanmakuConfigForm button[type=submit]');
        const saved = async () => page.waitForFunction(() => document.querySelector('#danmakuConfigStatus').textContent.includes('已保存'));
        const read = () => page.evaluate(id => ApiClient.getPluginConfiguration(id), pluginId);
        const screenshot = async name => page.screenshot({ path: (process.env.DANMAKU_CONFIG_SCREENSHOT_PREFIX || 'artifacts/configuration') + '-' + name + '.png', fullPage: true });
        await page.waitForFunction(() => document.querySelector('#danmakuApi')?.value === 'https://api.dandanplay.net');
        assert.equal(await mode.inputValue(), 'public');
        assert(await publicFields.isVisible() && !await credentialFields.isVisible());
        assert(await secret.isDisabled() && !await api.isDisabled());
        assert(await mode.evaluate(el => el.classList.contains('emby-select')));
        await screenshot('public');
        console.log('PASS public mode shows only native API/CORS controls');

        await mode.selectOption('credentials');
        assert(await credentialFields.isVisible() && !await publicFields.isVisible());
        assert(!await secret.isDisabled() && await api.isDisabled());
        assert.equal(await secret.getAttribute('type'), 'password');
        const appearance = await page.evaluate(() => Array.from(document.querySelectorAll('#JellyfinDanmakuConfigForm .emby-input')).map(el => {
            const style = getComputedStyle(el);
            return { labels: el.labels.length, styles: ['backgroundColor', 'backgroundImage', 'border', 'borderRadius', 'fontSize', 'padding', 'color'].map(key => style[key]) };
        }));
        assert(appearance.every(input => input.labels === 1));
        assert(appearance.every(input => JSON.stringify(input.styles) === JSON.stringify(appearance[0].styles)));
        await appId.fill('');
        assert.equal(await page.locator('#JellyfinDanmakuConfigForm').evaluate(el => el.checkValidity()), false);
        await appId.fill('ui-test-app');
        await secret.fill('ui-test-secret');
        // Inactive required fields do not block saving or overwrite their saved values.
        await api.evaluate(el => { el.value = ''; });
        assert.equal(await page.locator('#JellyfinDanmakuConfigForm').evaluate(el => el.checkValidity()), true);
        await submit.click();
        await saved();
        let value = await read();
        assert(value.UseOwnCredentials && value.AppId === 'ui-test-app' && value.AppSecret === 'ui-test-secret');
        assert.equal(value.ApiBaseUrl, fixture.ApiBaseUrl);
        assert.equal(value.CorsProxyUrl, fixture.CorsProxyUrl);
        await page.reload({ waitUntil: 'commit' });
        await page.waitForFunction(() => document.querySelector('#danmakuAppId')?.value === 'ui-test-app');
        assert.equal(await mode.inputValue(), 'credentials');
        assert(await credentialFields.isVisible() && !await publicFields.isVisible());
        await screenshot('credentials');
        console.log('PASS credential mode, masked secret, matching styles and save/reload persistence');

        await mode.selectOption('public');
        await secret.evaluate(el => { el.value = ''; });
        assert.equal(await page.locator('#JellyfinDanmakuConfigForm').evaluate(el => el.checkValidity()), true);
        await submit.click();
        await saved();
        value = await read();
        assert(!value.UseOwnCredentials && value.AppId === 'ui-test-app' && value.AppSecret === 'ui-test-secret');
        assert.equal(value.ApiBaseUrl, fixture.ApiBaseUrl);
        await page.reload({ waitUntil: 'commit' });
        await page.waitForFunction(() => document.querySelector('#danmakuApi')?.value === 'https://api.dandanplay.net');
        assert.equal(await mode.inputValue(), 'public');
        assert(await publicFields.isVisible() && !await credentialFields.isVisible());
        assert.deepEqual(errors, []);
        console.log('PASS switching back preserves saved credentials; no browser exceptions');
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
