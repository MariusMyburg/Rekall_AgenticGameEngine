import { dotnet } from './_framework/dotnet.js';

const canvas = document.querySelector('#viewport');
const context = canvas.getContext('2d');
let started = performance.now();

function fitCanvas() {
    const scale = window.devicePixelRatio || 1;
    const bounds = canvas.getBoundingClientRect();
    canvas.width = Math.max(1, Math.floor(bounds.width * scale));
    canvas.height = Math.max(1, Math.floor(bounds.height * scale));
}

function draw(now) {
    const width = canvas.width;
    const height = canvas.height;
    const time = (now - started) / 1000;
    context.fillStyle = '#071012';
    context.fillRect(0, 0, width, height);
    context.strokeStyle = 'rgba(111, 255, 209, 0.09)';
    context.lineWidth = 1;
    const gap = Math.max(24, Math.floor(width / 26));
    for (let x = -gap; x < width + gap; x += gap) {
        context.beginPath();
        context.moveTo(x + (time * 11) % gap, 0);
        context.lineTo(x + (time * 11) % gap, height);
        context.stroke();
    }
    for (let y = -gap; y < height + gap; y += gap) {
        context.beginPath();
        context.moveTo(0, y);
        context.lineTo(width, y);
        context.stroke();
    }
    context.fillStyle = 'rgba(111, 255, 209, 0.72)';
    const radius = Math.max(5, width / 180);
    const x = width * (0.5 + Math.sin(time * 0.8) * 0.22);
    const y = height * (0.5 + Math.cos(time * 1.1) * 0.16);
    context.beginPath();
    context.arc(x, y, radius, 0, Math.PI * 2);
    context.fill();
    requestAnimationFrame(draw);
}

window.addEventListener('resize', fitCanvas);
fitCanvas();
requestAnimationFrame(draw);

const { setModuleImports, runMain } = await dotnet.create();
setModuleImports('main.js', {
    web: { hasWebGpu: () => 'gpu' in navigator },
    dom: {
        setText: (selector, value) => document.querySelector(selector).textContent = value,
        setReady: ready => document.body.dataset.device = ready ? 'ready' : 'compatibility'
    }
});
await runMain();
