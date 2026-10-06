const http = require('node:http');
const fs = require('node:fs');
const assert = require('node:assert/strict');
const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
const source = fs.readFileSync('plugin/Web/ede.js', 'utf8');
const firstId = '11111111111111111111111111111111';
const secondId = '22222222222222222222222222222222';
let sourceFails = false;
const danmakuRequests = [];
const server = http.createServer((request, response) => {
    response.setHeader('content-type', 'application/json');
    const url = new URL(request.url, 'http://localhost');
    if (url.pathname.includes('/api/v2/')) danmakuRequests.push({ path: url.pathname, token: request.headers.authorization });
    if (url.pathname === '/') {
        response.setHeader('content-type', 'text/html');
        response.end('<html><head><meta name="application-name" content="Jellyfin"></head><body><div id="reactRoot"><div class="skinHeader"></div></div></body></html>');
    } else if (url.pathname.endsWith('/PlaybackInfo')) {
        if (url.searchParams.has('fail')) response.statusCode = 500;
        response.end(JSON.stringify({ MediaSources: [{ Id: 'ffffffffffffffffffffffffffffffff' }] }));
    } else if (sourceFails) { response.statusCode = 429; response.end('{}'); }
    else if (url.pathname.includes('/search/episodes')) response.end(JSON.stringify({ animes: [{ animeId: 1, animeTitle: '测试', type: 'tvseries', episodes: [{ episodeId: 101, episodeTitle: '第1话' }, { episodeId: 102, episodeTitle: '第2话' }] }] }));
    else if (url.pathname.includes('/comment/')) response.end(JSON.stringify({ comments: Array.from({ length: 100 }, (_, i) => ({ p: i + ',1,16777215,[BiliBili]fixture', m: '测试弹幕' })) }));
    else if (url.pathname.includes('/related/')) response.end('{"relateds":[]}');
    else response.end('{}');
});
(async () => {
    await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
    const root = 'http://127.0.0.1:' + server.address().port;
    const browser = await chromium.launch({ headless: true, channel: 'chrome' });
    try {
        for (const transport of ['fetch', 'xhr', 'own']) {
            const page = await browser.newPage();
            const errors = [];
            page.on('pageerror', e => errors.push(e.message));
            await page.goto(root);
            await page.evaluate(({ root, firstId, secondId, transport }) => {
                window.JellyfinDanmakuConfig = { apiBaseUrl: root, corsProxyUrl: '', defaultEnabled: true };
                if (transport === 'own') {
                    window.JellyfinDanmakuConfig.serverApiPrefix = '/jellyfin/JellyfinDanmaku';
                    localStorage.setItem('customApiPrefix', 'https://unreachable.invalid');
                    localStorage.setItem('customCorsProxy', 'https://unreachable.invalid/cors/');
                }
                window.itemsRead = [];
                window.ApiClient = { accessToken: () => 'fixture-session', getCurrentUserId: () => 'user', getItem: async (_, id) => {
                    window.itemsRead.push(id);
                    if (![firstId, secondId].includes(id)) throw Error('Wrong item ID');
                    return { Id: id, SeasonId: 'season', SeriesName: '测试', IndexNumber: id === firstId ? 1 : 2, ParentIndexNumber: 1 };
                } };
            }, { root, firstId, secondId, transport });
            await page.addScriptTag({ content: source });
            await page.addScriptTag({ content: source });
            assert.equal(await page.evaluate(() => window.itemsRead.length), 0);
            await page.evaluate(() => {
                const player = document.createElement('div');
                player.className = 'videoPlayerContainer';
                player.innerHTML = '<video class="htmlvideoplayer"></video>';
                document.body.append(player);
                const osd = document.createElement('div');
                osd.dataset.type = 'video-osd';
                osd.style = 'height:500px;width:100%;';
                osd.innerHTML = '<div class="buttons"><div><button class="btnPause"><span class="material-icons pause"></span></button></div></div>';
                document.querySelector('#reactRoot').append(osd);
            });
            async function playback(id) {
                await page.evaluate(({ id, transport }) => {
                    const url = '/Items/' + id + '/PlaybackInfo?UserId=user';
                    return transport !== 'xhr' ? fetch(url) : new Promise(resolve => { const xhr = new XMLHttpRequest(); xhr.open('POST', url); xhr.onload = resolve; xhr.send(); });
                }, { id, transport });
            }
            await playback(firstId);
            await page.waitForFunction(() => window.ede.danmaku?.comments?.length > 0);
            assert.equal(await page.locator('#danmakuCtr').count(), 1);
            assert.equal(await page.locator('#danmakuWrapper').count(), 1);
            assert((await page.evaluate(() => window.itemsRead)).every(id => id === firstId));
            await playback(secondId);
            await page.waitForFunction(() => document.querySelector('#danmakuInfoTitle')?.textContent.includes('S1E2') && window.ede.danmaku?.comments?.length > 0);
            assert.equal(await page.locator('#danmakuCtr').count(), 1);
            await page.locator('#danmakuSettings').click();
            await page.waitForFunction(() => Math.abs(document.querySelector('#danmakuSidebar').getBoundingClientRect().right - innerWidth) < 2);
            assert.equal(await page.locator('#danmakuSidebar').count(), 1);
            if (transport === 'own') {
                assert(await page.locator('.controlTitle').filter({ hasText: '主站配置的弹弹play应用凭据' }).isVisible());
                assert.equal(await page.locator('#customApiPrefix').isVisible(), false);
            }
            await page.locator('.danmakuSidebarCancelButton').click();
            await page.evaluate(() => document.querySelector('.videoPlayerContainer').remove());
            await page.waitForFunction(() => !window.ede.danmaku && !document.querySelector('#danmakuWrapper'));
            assert.deepEqual(errors, []);
            await page.close();
            console.log('PASS ' + transport + ': query-string URL, correct ItemId, duplicate script guard, delayed player, same-element item change, rendering, sidebar and cleanup');
        }
        const ownRequests = danmakuRequests.filter(r => r.path.startsWith('/jellyfin/JellyfinDanmaku/api/v2/'));
        assert(ownRequests.length > 0 && ownRequests.every(r => r.token === 'MediaBrowser Token="fixture-session"'));
        assert(danmakuRequests.filter(r => !r.path.startsWith('/jellyfin/JellyfinDanmaku/')).every(r => !r.token));
        console.log('PASS own credentials mode: same-origin session headers, BaseUrl, stale overrides ignored, public requests exclude account token');
        const page = await browser.newPage();
        await page.goto(root);
        await page.evaluate(({ root, firstId }) => {
            window.JellyfinDanmakuConfig = { apiBaseUrl: root, corsProxyUrl: '' };
            window.ApiClient = { getCurrentUserId: () => 'user', getItem: async () => ({ Id: firstId, Name: '测试', IndexNumber: 1 }) };
            document.body.insertAdjacentHTML('beforeend', '<div class="videoPlayerContainer"><video></video></div><div data-type="video-osd" style="width:100%;height:500px"><div><button class="btnPause"><span class="material-icons pause"></span></button></div></div>');
        }, { root, firstId });
        sourceFails = true;
        await page.addScriptTag({ content: source });
        await page.evaluate(id => fetch('/Items/' + id + '/PlaybackInfo?x=1'), firstId);
        await page.waitForFunction(() => !window.ede.loading && document.querySelector('#displayDanmaku')?.title.includes('429'));
        sourceFails = false;
        await page.locator('#danmakuSettings').click();
        await page.locator('#opacity').waitFor({ state: 'attached' });
        await page.locator('.danmakuSidebarSaveButton').click();
        await page.waitForFunction(() => !!window.ede.danmaku);
        console.log('PASS source failure recovery through settings save');
    } finally { await browser.close(); await new Promise(resolve => server.close(resolve)); }
})().catch(e => { console.error(e.message.replace(/https?:\/\/\S+/g, '[url]')); process.exitCode = 1; server.close(); });
