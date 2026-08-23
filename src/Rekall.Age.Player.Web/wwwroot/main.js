import { dotnet } from './_framework/dotnet.js';
import { createWebGpuExecutor } from './webgpu-device.js';
import { publishWebGpuEvidence } from './webgpu-evidence.js';

const canvas = document.querySelector('#viewport');
const webgpu = createWebGpuExecutor();

function fitCanvas() {
    const scale = window.devicePixelRatio || 1;
    const bounds = canvas.getBoundingClientRect();
    canvas.width = Math.max(1, Math.floor(bounds.width * scale));
    canvas.height = Math.max(1, Math.floor(bounds.height * scale));
}

window.addEventListener('resize', fitCanvas);
fitCanvas();

const { setModuleImports, runMain } = await dotnet.create();
setModuleImports('main.js', {
    web: { hasWebGpu: () => 'gpu' in navigator },
    webgpu: {
        initialize: async canvasSelector => JSON.stringify(await webgpu.initialize(canvasSelector)),
        execute: packet => JSON.stringify(webgpu.execute(packet)),
        flush: async () => JSON.stringify(await webgpu.flush()),
        readPixels: async () => JSON.stringify(await webgpu.readPixels()),
        canvasWidth: () => canvas.width,
        canvasHeight: () => canvas.height
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
