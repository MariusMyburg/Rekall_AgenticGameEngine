import assert from 'node:assert/strict';
import test from 'node:test';
import { createWebGpuExecutor } from '../wwwroot/webgpu-device.js';

const deviceId = '11111111-1111-1111-1111-111111111111';
const handle = (kind, slot) => ({ deviceId, kind, slot, generation: 1 });
const vertexBuffer = handle('buffer', 1);
const indirectBuffer = handle('buffer', 2);
const vertexShader = handle('shaderModule', 3);
const fragmentShader = handle('shaderModule', 4);
const pipeline = handle('renderPipeline', 5);
const outputTexture = handle('texture', 6);
const outputTarget = handle('renderTarget', 7);

test('reads the submitted canvas texture through a 256-byte aligned GPU copy and proves four real samples', async () => {
    const fake = createProofWebGpu({ format: 'rgba8unorm' });
    const executor = createWebGpuExecutor(fake.environment);
    const initialized = await executor.initialize();
    assert.equal(initialized.succeeded, true);
    executeProof(executor, 'rgba8Unorm');

    assert.equal((await executor.flush()).succeeded, true);
    const readback = await executor.readPixels();

    assert.equal(readback.succeeded, true);
    assert.equal(readback.pixelProof.passed, true);
    assert.equal(readback.pixelProof.bytesPerRow, 256);
    assert.deepEqual(fake.configurations[0], { device: fake.device, format: 'rgba8unorm', alphaMode: 'opaque', usage: 17 });
    assert.equal(fake.copyCalls.length, 1);
    assert.deepEqual(fake.copyCalls[0].layout, { offset: 0, bytesPerRow: 256, rowsPerImage: 64 });
    assert.ok(readback.pixelProof.samples.background.r < 40);
    assert.ok(readback.pixelProof.samples.cyan.g > 150 && readback.pixelProof.samples.cyan.b > 170);
    assert.ok(readback.pixelProof.samples.blue.b > 190);
    assert.ok(readback.pixelProof.samples.magenta.r > 150 && readback.pixelProof.samples.magenta.b > 160);
});

test('pixel proof rejects a copied canvas whose renderer produced no triangle', async () => {
    const fake = createProofWebGpu({ format: 'bgra8unorm', rasterize: false });
    const executor = createWebGpuExecutor(fake.environment);
    await executor.initialize();
    executeProof(executor, 'bgra8Unorm');
    await executor.flush();

    const readback = await executor.readPixels();

    assert.equal(readback.succeeded, false);
    assert.equal(readback.pixelProof.passed, false);
    assert.equal(readback.diagnostics[0].code, 'REKALL_WEBGPU_PIXEL_PROOF_FAILED');
});

function executeProof(executor, format) {
    const execute = packet => {
        const result = executor.execute(JSON.stringify({ version: 1, ...packet }));
        assert.equal(result.succeeded, true, JSON.stringify(result.diagnostics));
    };
    execute({ operation: 'importCanvasOutput', texture: outputTexture, renderTarget: outputTarget, width: 64, height: 64, format });
    execute({ operation: 'create', resourceType: 'buffer', handle: vertexBuffer, descriptor: { sizeBytes: 24, usage: 'vertex, transferDestination', memoryAccess: 'deviceLocal', label: 'vertices' } });
    execute({ operation: 'writeBuffer', handle: vertexBuffer, offset: 0, dataBase64: uint32Base64([0x7878784c, 0x78787844, 0x78787852, 0x78787844, 0x78787843, 0x0a787855]) });
    execute({ operation: 'create', resourceType: 'buffer', handle: indirectBuffer, descriptor: { sizeBytes: 16, usage: 'indirect, transferDestination', memoryAccess: 'deviceLocal', label: 'draw.arguments' } });
    execute({ operation: 'writeBuffer', handle: indirectBuffer, offset: 0, dataBase64: uint32Base64([3, 1, 0, 0]) });
    execute({ operation: 'create', resourceType: 'shaderModule', handle: vertexShader, descriptor: { stage: 'vertex', language: 'wgsl', source: 'runtime-authored vertex WGSL', entryPoint: 'main', label: 'proof.vertex' } });
    execute({ operation: 'create', resourceType: 'shaderModule', handle: fragmentShader, descriptor: { stage: 'fragment', language: 'wgsl', source: 'runtime-authored fragment WGSL', entryPoint: 'main', label: 'proof.fragment' } });
    execute({ operation: 'create', resourceType: 'renderPipeline', handle: pipeline, descriptor: {
        vertexShader, fragmentShader, bindingLayouts: [],
        colorTargets: [{ format, blendEnabled: false, writeMask: 15 }],
        depthStencil: null, topology: 'triangleList', cullMode: 'none', frontFace: 'counterClockwise',
        vertexBuffers: [{ strideBytes: 8, stepMode: 'vertex', attributes: [{ name: 'Code', location: 0, format: 'uint32x2', offsetBytes: 0 }] }]
    } });
    execute({ operation: 'submit', label: 'proof.webgpu.asset-independent', commands: [
        { kind: 'beginRenderPass', data: { descriptor: { renderTarget: outputTarget, colorClearValues: [{ red: .015, green: .025, blue: .04, alpha: 1 }], label: 'proof.webgpu.render' } } },
        { kind: 'setRenderPipeline', data: { pipeline } },
        { kind: 'setVertexBuffer', data: { slot: 0, buffer: vertexBuffer, offset: 0, sizeBytes: 24 } },
        { kind: 'drawIndirect', data: { buffer: indirectBuffer, offset: 0, drawCount: 1, strideBytes: 16 } },
        { kind: 'endRenderPass', data: {} }
    ] });
}

function uint32Base64(values) {
    const bytes = new Uint8Array(values.length * 4);
    values.forEach((value, index) => new DataView(bytes.buffer).setUint32(index * 4, value, true));
    return Buffer.from(bytes).toString('base64');
}

function createProofWebGpu({ format, rasterize = true }) {
    const width = 64; const height = 64;
    const configurations = []; const copyCalls = [];
    const canvasTexture = makeTexture(width, height, format);
    const queue = {
        writeBuffer(buffer, offset, source) { buffer.bytes.set(new Uint8Array(source.buffer, source.byteOffset, source.byteLength), offset); },
        submit(commandBuffers) { commandBuffers.forEach(commandBuffer => commandBuffer.execute()); },
        async onSubmittedWorkDone() {}
    };
    const device = {
        limits: {}, features: new Set(), lost: new Promise(() => {}), queue,
        pushErrorScope() {}, async popErrorScope() { return null; }, addEventListener() {},
        createBuffer(descriptor) { return makeBuffer(descriptor); },
        createShaderModule(descriptor) { return { descriptor, async getCompilationInfo() { return { messages: [] }; } }; },
        createPipelineLayout(descriptor) { return descriptor; },
        createRenderPipeline(descriptor) { return { descriptor }; },
        createCommandEncoder() { return makeEncoder(canvasTexture, rasterize, copyCalls); }
    };
    const context = {
        configure(descriptor) { configurations.push(descriptor); },
        getCurrentTexture() { return canvasTexture; }
    };
    return {
        device, configurations, copyCalls,
        environment: {
            navigator: { gpu: { async requestAdapter() { return { limits: {}, async requestDevice() { return device; } }; }, getPreferredCanvasFormat() { return format; } } },
            canvas: { width, height, getContext() { return context; } }
        }
    };
}

function makeBuffer(descriptor) {
    return {
        descriptor,
        bytes: new Uint8Array(descriptor.size),
        async mapAsync() {},
        getMappedRange() { return this.bytes.buffer; },
        unmap() {}, destroy() {}
    };
}

function makeTexture(width, height, format) {
    const texture = { width, height, format, bytes: new Uint8Array(width * height * 4) };
    texture.createView = () => ({ texture });
    return texture;
}

function makeEncoder(canvasTexture, rasterize, copyCalls) {
    const operations = [];
    return {
        beginRenderPass(descriptor) {
            const state = { descriptor, vertexBuffer: null };
            operations.push(() => clearTexture(descriptor.colorAttachments[0].view.texture, descriptor.colorAttachments[0].clearValue));
            return {
                setPipeline() {},
                setVertexBuffer(_slot, buffer) { state.vertexBuffer = buffer; },
                drawIndirect(buffer, offset) { operations.push(() => { if (rasterize) drawTriangle(state.descriptor.colorAttachments[0].view.texture, state.vertexBuffer, buffer, offset); }); },
                end() {}
            };
        },
        copyTextureToBuffer(source, destination, size) {
            copyCalls.push({ source, destination, size, layout: { ...destination, buffer: undefined } });
            copyCalls.at(-1).layout = { offset: destination.offset ?? 0, bytesPerRow: destination.bytesPerRow, rowsPerImage: destination.rowsPerImage };
            operations.push(() => copyTexture(source.texture, destination.buffer, destination.bytesPerRow, size));
        },
        finish() { return { execute() { operations.forEach(operation => operation()); } }; }
    };
}

function clearTexture(texture, clear) {
    const rgba = [clear.r, clear.g, clear.b, clear.a].map(value => Math.round(value * 255));
    for (let pixel = 0; pixel < texture.width * texture.height; pixel++) writePixel(texture, pixel, rgba);
}

function drawTriangle(texture, verticesBuffer, indirect, offset) {
    const argumentsView = new DataView(indirect.bytes.buffer);
    assert.equal(argumentsView.getUint32(offset, true), 3);
    const codes = new DataView(verticesBuffer.bytes.buffer);
    const vertices = Array.from({ length: 3 }, (_, index) => {
        const xCode = codes.getUint32(index * 8, true) & 255;
        const yCode = codes.getUint32(index * 8 + 4, true) & 255;
        return {
            x: xCode === 76 ? -.72 : xCode === 82 ? .72 : 0,
            y: yCode === 68 ? -.68 : .72,
            color: xCode === 76 ? [.1, .95, .9] : xCode === 82 ? [.95, .1, .85] : [.1, .25, 1]
        };
    });
    for (let y = 0; y < texture.height; y++) for (let x = 0; x < texture.width; x++) {
        const point = { x: (x + .5) / texture.width * 2 - 1, y: 1 - (y + .5) / texture.height * 2 };
        const weights = barycentric(point, vertices);
        if (weights.every(value => value >= 0)) {
            const rgba = [0, 1, 2].map(channel => Math.round(255 * weights.reduce((sum, weight, index) => sum + weight * vertices[index].color[channel], 0)));
            writePixel(texture, y * texture.width + x, [...rgba, 255]);
        }
    }
}

function barycentric(point, [a, b, c]) {
    const denominator = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
    const first = ((b.y - c.y) * (point.x - c.x) + (c.x - b.x) * (point.y - c.y)) / denominator;
    const second = ((c.y - a.y) * (point.x - c.x) + (a.x - c.x) * (point.y - c.y)) / denominator;
    return [first, second, 1 - first - second];
}

function writePixel(texture, pixel, rgba) {
    const [r, g, b, a] = rgba;
    texture.bytes.set(texture.format.startsWith('bgra') ? [b, g, r, a] : [r, g, b, a], pixel * 4);
}

function copyTexture(texture, buffer, bytesPerRow, size) {
    for (let row = 0; row < size.height; row++) {
        buffer.bytes.set(texture.bytes.subarray(row * texture.width * 4, (row + 1) * texture.width * 4), row * bytesPerRow);
    }
}
