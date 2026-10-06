"""Adapt the pinned upstream ede.js; require explicit input and keep MIT attribution."""
from pathlib import Path
import argparse

p = argparse.ArgumentParser()
p.add_argument('source', type=Path)
a = p.parse_args()
s = a.source.read_text(encoding='utf-8')

def replace(old, new):
    global s
    assert s.count(old) == 1, f'upstream anchor changed: {old[:80]}'
    s = s.replace(old, new)

def span(start, end, new):
    global s
    left, right = s.index(start), s.index(end, s.index(start))
    s = s[:left] + new + '\n\n' + s[right:]

replace("    if (document.querySelector('meta[name=\"application-name\"]').content !== 'Jellyfin') {",
        "    if (window.JellyfinDanmakuLoaded || document.querySelector('meta[name=\"application-name\"]')?.content !== 'Jellyfin') {")
replace("    // ------ configs start------", "    window.JellyfinDanmakuLoaded = true;\n    const pluginDefaults = window.JellyfinDanmakuConfig || {};\n    // ------ configs start------")
replace("    const check_interval = 200;", '''    const check_interval = 200;
    const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[char]));''')
replace("    const corsProxy = 'https://ddplay-api.930524.xyz/cors/';", "    const corsProxy = pluginDefaults.corsProxyUrl ?? 'https://ddplay-api.930524.xyz/cors/';")
replace("    const apiPrefix = 'https://api.dandanplay.net';", "    const apiPrefix = pluginDefaults.apiBaseUrl || 'https://api.dandanplay.net';")
replace("    let ddplayStatus = JSON.parse(localStorage.getItem('ddplayStatus')) || { isLogin: false, token: '', tokenExpire: 0 };", "    let ddplayStatus;\n    try { ddplayStatus = JSON.parse(localStorage.getItem('ddplayStatus')); } catch (_) {}\n    ddplayStatus ||= { isLogin: false, token: '', tokenExpire: 0 };")
replace("    let isNewJellyfin = true;", "    let loadVersion = 0;\n    let observedMedia = null;\n    let observedItem = '';\n    let refreshTimer;")
span('    // Intercept XMLHttpRequest', '    const displayButtonOpts', r'''    // Jellyfin 12.2 SDK uses both fetch and XHR. Item ID is in the URL, not MediaSources[0].Id.
    function capturePlayback(url) {
        try {
            const parsed = new URL(url, location.href);
            if (parsed.origin !== location.origin) return;
            const match = parsed.pathname.match(/\/Items\/([a-f0-9-]{32,36})\/PlaybackInfo\/?$/i);
            if (!match || itemId === match[1]) return;
            itemId = match[1];
            loadVersion++;
            if (window.ede) {
                window.ede.loading = false;
                window.ede.episode_info = null;
                clearTimeout(refreshTimer);
                refreshTimer = setTimeout(() => {
                    if (document.querySelector(mediaQueryStr)) reloadDanmaku('refresh');
                }, 500);
            }
        } catch (_) { /* unrelated request */ }
    }
    const originalFetch = window.fetch;
    window.fetch = async function (input, options) {
        const response = await originalFetch.call(this, input, options);
        if (response.ok) capturePlayback(typeof input === 'string' || input instanceof URL ? String(input) : input.url);
        return response;
    };
    const originalOpen = XMLHttpRequest.prototype.open;
    XMLHttpRequest.prototype.open = function (_, url) {
        this.addEventListener('load', () => {
            if (this.status >= 200 && this.status < 300) capturePlayback(String(url));
        }, { once: true });
        return originalOpen.apply(this, arguments);
    };

    function cleanupPlayer() {
        window.ede?.obResize?.disconnect();
        window.ede?.obMutation?.disconnect();
        window.ede?.danmaku?.destroy();
        if (window.ede) {
            window.ede.danmaku = null;
            window.ede.episode_info = null;
            window.ede.loading = false;
        }
        ['danmakuWrapper', 'danmakuInfoTitle', 'danmakuCtr', 'debugInfo'].forEach(id => document.getElementById(id)?.remove());
    }''')
replace("            this.danmakuSwitch = danmakuSwitch ? parseInt(danmakuSwitch) : 1;", "            this.danmakuSwitch = danmakuSwitch !== null ? parseInt(danmakuSwitch) : (pluginDefaults.defaultEnabled === false ? 0 : 1);")
replace("            this.useXmlDanmaku = useXmlDanmaku ? parseInt(useXmlDanmaku) : 0;", "            this.useXmlDanmaku = useXmlDanmaku !== null ? parseInt(useXmlDanmaku) : (pluginDefaults.preferLocalXml ? 1 : 0);")
span('        let uiAnchor = document.getElementsByClassName(uiAnchorStr);', '        // 已初始化', '')
replace('        _container.appendChild(span);', '        if (!_container) { menubar.remove(); return; }\n        _container.appendChild(span);')
replace('        menubar.appendChild(createButton(displayButtonOpts));', '''        menubar.appendChild(createButton(displayButtonOpts));
        menubar.appendChild(createButton({ title: '弹幕设置', id: 'danmakuSettings', class: 'tune', onclick: createDanmakuSidebar }));''')
span('    // 添加弹幕设置到播放器设置菜单', '    function createButton(opt)', '')
span('    async function showDebugInfo(msg)', '    async function getEmbyItemInfo()', '''    function showDebugInfo(msg) {
        const span = document.getElementById('debugInfo');
        if (!span) return;
        const line = document.createElement('div');
        line.textContent = typeof msg === 'string' ? msg : JSON.stringify(msg);
        span.appendChild(line);
        while (span.childNodes.length > 100) span.firstChild.remove();
    }''')
span('    async function getEmbyItemInfo()', '    function makeGetRequest', '''    async function getEmbyItemInfo() {
        const version = loadVersion;
        for (let retry = 0; retry < 50; retry++) {
            if (version !== loadVersion || !document.querySelector(mediaQueryStr)) return null;
            if (itemId && window.ApiClient?.getCurrentUserId()) {
                const info = await ApiClient.getItem(ApiClient.getCurrentUserId(), itemId);
                if (version !== loadVersion) return null;
                showDebugInfo('获取Item信息成功: ' + (info.SeriesName || info.Name));
                return info;
            }
            await new Promise(resolve => setTimeout(resolve, 200));
        }
        throw new Error('未识别当前播放条目，请重新开始播放');
    }''')
span('    function makeGetRequest(url)', '    async function getEpisodeInfo(', '''    async function makeGetRequest(url) {
        const headers = { Accept: 'application/json' };
        const target = new URL(url, location.href);
        const server = pluginDefaults.serverApiPrefix ? new URL(pluginDefaults.serverApiPrefix, location.href) : null;
        if (server && target.origin === location.origin && server.origin === location.origin
            && target.pathname.startsWith(server.pathname.replace(/\\/$/, '') + '/api/v2/')) {
            const token = window.ApiClient?.accessToken();
            if (!token) throw new Error('请先登录 Jellyfin 再加载弹幕');
            headers.Authorization = 'MediaBrowser Token="' + token + '"';
        }
        const response = await fetch(url, { headers, signal: AbortSignal.timeout(server ? 20000 : 15000) });
        if (!response.ok) {
            let message = '弹幕接口返回 HTTP ' + response.status;
            if (server) {
                try { message = (await response.json()).errorMessage || message; } catch (_) {}
            }
            throw new Error(message);
        }
        return response;
    }''')
replace('    function getApiPrefix() {', '''    function getApiPrefix() {
        if (pluginDefaults.serverApiPrefix) return pluginDefaults.serverApiPrefix;''')
replace('            controlItems.push(customCorsProxy);', '''            if (pluginDefaults.serverApiPrefix) {
                customCorsProxy.querySelector('.controlTitle').textContent = '当前使用主站配置的弹弹play应用凭据';
                customCorsProxy.querySelectorAll('.custom-input-group').forEach(group => { group.style.display = 'none'; });
            }
            controlItems.push(customCorsProxy);''')
# Keep source failures visible instead of dereferencing null after failed search.
search_start, search_end = s.index('    async function getEpisodeInfo('), s.index('    async function getComments(')
search = s[search_start:search_end]
search = search.replace('return null;\n            });', 'throw error;\n            });').replace('return null;\n                    });', 'throw error;\n                    });')
s = s[:search_start] + search + s[search_end:]
comments_start, comments_end = s.index('    async function getComments('), s.index('    async function getCommentsByUrl(')
comments = s[comments_start:comments_end].replace('return null;', 'throw error;')
s = s[:comments_start] + comments + s[comments_end:]
for old, new in [
    ('${defaultValue}', '${escapeHtml(defaultValue)}'),
    ('${placeholder}', '${escapeHtml(placeholder)}'),
    ('${title}', '${escapeHtml(title)}'),
    ("${window.ede.customCorsProxy ?? ''}", '${escapeHtml(window.ede.customCorsProxy)}'),
    ("${window.ede.customApiPrefix ?? ''}", '${escapeHtml(window.ede.customApiPrefix)}'),
    ("window.ede.fontFamily?.replaceAll('\"', '&quot;') ?? defaultFontFamily", 'escapeHtml(window.ede.fontFamily || defaultFontFamily)'),
    ("window.ede.fontOptions?.replaceAll('\"', '&quot;') ?? ''", 'escapeHtml(window.ede.fontOptions)'),
]:
    replace(old, new)
replace("        const response = await fetch(url);", "        const response = await fetch(url, { headers: { Authorization: 'MediaBrowser Token=\"' + ApiClient.accessToken() + '\"' } });")
replace("                const p = comment.getAttribute('p').split(',').map(Number);", "                const p = (comment.getAttribute('p') || '').split(',');\n                if (p.length < 8 || !Number.isFinite(Number(p[0]))) continue;")
span('        if (!window.obVideo) {', '        if (!comments) {', '')
replace('    async function createDanmaku(comments) {', '    async function createDanmaku(comments) {\n        const version = loadVersion;')
replace('        await waitForMediaContainer();', '        await waitForMediaContainer();\n        if (version !== loadVersion || !document.querySelector(mediaQueryStr)) return;')
replace("            while (!document.querySelector(mediaContainerQueryStr)?.children.length) {", "            for (let retry = 0; retry < 50 && !document.querySelector(mediaContainerQueryStr)?.children.length; retry++) {")
replace("            document.querySelector('div.skinHeader').appendChild(infoContainer);", "            const header = document.querySelector('.skinHeader');\n            if (!header) return;\n            header.appendChild(infoContainer);")
span('    function reloadDanmaku(', '    function preProcessDanmaku(', '''    async function reloadDanmaku(type = 'check') {
        if (window.ede.loading || !document.querySelector(mediaQueryStr)) return;
        const version = loadVersion;
        window.ede.loading = true;
        try {
            let comments;
            if (window.ede.useXmlDanmaku === 1) {
                const id = await getItemId();
                if (id) comments = await getCommentsByPluginApi(id);
            }
            if (!comments?.length) {
                const info = await getEpisodeInfo(type !== 'search');
                if (!info || version !== loadVersion) return;
                if (type !== 'search' && type !== 'reload' && window.ede.danmaku && window.ede.episode_info?.episodeId === info.episodeId) return;
                window.ede.episode_info = info;
                displayDanmakuInfo(info);
                comments = await getComments(info.episodeId);
            }
            if (version !== loadVersion || !document.querySelector(mediaQueryStr)) return;
            if (!comments?.length) throw new Error('弹幕源未返回数据，请检查接口或手动匹配');
            await createDanmaku(comments);
            const button = document.getElementById('displayDanmaku');
            if (button) button.title = '弹幕开关';
        } catch (error) {
            const message = error?.message || '弹幕加载失败，可在弹幕设置中重新匹配';
            showDebugInfo(message);
            const button = document.getElementById('displayDanmaku');
            if (button) button.title = message;
        } finally {
            if (version === loadVersion) {
                window.ede.loading = false;
                const control = document.getElementById('danmakuCtr');
                if (control) control.style.opacity = 1;
            }
        }
    }''')
span('    const waitForElement = (selector)', '    function styleSettingItemForContent(item)', '''    // Do not permanently abort initialization when media/ApiClient is not ready yet.
    if (window.ede) return; // Existing userscript: avoid duplicate controls and renderers.
    window.ede = new EDE();
    const materialIcon = document.querySelector('.material-icons');
    if (materialIcon && window.getComputedStyle(materialIcon).fontFamily === '"Font Awesome 6 Pro"') {
        danmaku_icons = ['fa-comment-slash', 'fa-comment'];
        sendDanmakuOpts.class = 'fa-paper-plane';
    }
    setInterval(() => {
        const media = document.querySelector(mediaQueryStr);
        if (!media) {
            if (observedMedia) {
                loadVersion++;
                cleanupPlayer();
                clearTimeout(refreshTimer);
                observedMedia = null;
                observedItem = '';
            }
            return;
        }
        if (observedMedia !== media || observedItem !== itemId) {
            if (observedMedia) cleanupPlayer();
            observedMedia = media;
            observedItem = itemId;
        }
        initUI();
        initListener();
    }, check_interval);''')

target = Path(__file__).resolve().parents[1] / 'plugin/Web/ede.js'
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text('// Adapted from Izumiko/jellyfin-danmaku f6447c1a (1.61), MIT. See docs/upstream.md.\n' + s, encoding='utf-8', newline='\n')
