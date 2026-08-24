import { dotnet } from './_framework/dotnet.js';
import { createWebGpuExecutor } from './webgpu-device.js';
import { publishWebGpuEvidence } from './webgpu-evidence.js';
import { createWebInputBridge, fullscreenEvent, resizeEvent, visibilityEvent } from './web-input.js';
import { createFrameLoop } from './web-player-loop.js';

const MAX_LIFECYCLE_EVENTS = 32;
const canvas = document.querySelector('#viewport');
const webgpu = createWebGpuExecutor();
const input = createWebInputBridge({ window, document, canvas, navigator });
const lifecycleEvents = [];

// Bridges the push-style frame loop (JS owns requestAnimationFrame timing) to the pull-style await .NET drives its
// simulate/present loop with -- .NET never needs a JS->.NET export, it just awaits "the next frame" like it awaits
// any other browser I/O. Only the latest pending tick is kept: presentation happens once per visual frame, so an
// unconsumed earlier tick would only ever be stale by the time it was read.
let resolveNextFrame = null;
let pendingElapsedSeconds = null;
const frameLoop = createFrameLoop({
    onTick: elapsedSeconds => {
        if (resolveNextFrame) {
            const resolve = resolveNextFrame;
            resolveNextFrame = null;
            resolve(elapsedSeconds);
        } else {
            pendingElapsedSeconds = elapsedSeconds;
        }
    }
});

function awaitNextFrame() {
    if (pendingElapsedSeconds !== null) {
        const value = pendingElapsedSeconds;
        pendingElapsedSeconds = null;
        return Promise.resolve(value);
    }

    return new Promise(resolve => { resolveNextFrame = resolve; });
}

function queueLifecycleEvent(event) {
    if (lifecycleEvents.length < MAX_LIFECYCLE_EVENTS) {
        lifecycleEvents.push(event);
    }
}

function fitCanvas() {
    const scale = window.devicePixelRatio || 1;
    const bounds = canvas.getBoundingClientRect();
    const previousWidth = canvas.width;
    const previousHeight = canvas.height;
    canvas.width = Math.max(1, Math.floor(bounds.width * scale));
    canvas.height = Math.max(1, Math.floor(bounds.height * scale));
    if (canvas.width !== previousWidth || canvas.height !== previousHeight) {
        queueLifecycleEvent(resizeEvent(canvas.width, canvas.height));
    }
}

window.addEventListener('resize', fitCanvas);
document.addEventListener('visibilitychange', () => queueLifecycleEvent(visibilityEvent(!document.hidden)));
document.addEventListener('fullscreenchange', () => queueLifecycleEvent(fullscreenEvent(Boolean(document.fullscreenElement))));
fitCanvas();

const { setModuleImports, runMain } = await dotnet.create();
setModuleImports('main.js', {
    web: { hasWebGpu: () => 'gpu' in navigator },
    webgpu: {
        initialize: async canvasSelector => JSON.stringify(await webgpu.initialize(canvasSelector)),
        execute: packet => JSON.stringify(webgpu.execute(packet)),
        flush: async () => JSON.stringify(await webgpu.flush()),
        drain: async () => JSON.stringify(await webgpu.drain()),
        readPixels: async () => JSON.stringify(await webgpu.readPixels()),
        canvasWidth: () => canvas.width,
        canvasHeight: () => canvas.height
    },
    input: {
        snapshot: () => JSON.stringify(input.snapshot()),
        pullLifecycleEvents: () => JSON.stringify(lifecycleEvents.splice(0))
    },
    frame: {
        start: () => frameLoop.start(),
        stop: () => frameLoop.stop(),
        pause: () => frameLoop.pause(),
        resume: () => frameLoop.resume(),
        awaitNext: () => awaitNextFrame()
    },
    dom: {
        baseUri: () => document.baseURI,
        setText: (selector, value) => document.querySelector(selector).textContent = value,
        setReady: ready => document.body.dataset.device = ready ? 'ready' : 'compatibility',
        publishEvidence: json => publishWebGpuEvidence(json, window),
        publishGameBootstrapEvidence: json => {
            const evidence = JSON.parse(json);
            document.querySelector('#rekall-web-bootstrap-evidence').textContent = json;
            window.__rekallAgeWebGameBootstrap = evidence;
            window.dispatchEvent(new CustomEvent('rekall-age-web-game-bootstrap', { detail: evidence }));
        }
    }
});

await runMain();
