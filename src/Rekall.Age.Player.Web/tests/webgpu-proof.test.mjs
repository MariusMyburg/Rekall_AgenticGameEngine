import assert from 'node:assert/strict';
import test from 'node:test';
import { createWebGpuExecutor } from '../wwwroot/webgpu-device.js';

const deviceId = '11111111-1111-1111-1111-111111111111';
const handle = (kind, slot) => ({ deviceId, kind, slot, generation: 1 });
const outputTexture = handle('texture', 1);
const outputTarget = handle('renderTarget', 2);

test('copies arbitrary current-texture bytes in the same submission with aligned bounded readback', async () => {
    const fake = createReadbackOnlyWebGpu((x, y) => [x, y, 17, 255]);
    const executor = createWebGpuExecutor(fake.environment);
    const initialized = await executor.initialize();
    assert.equal(initialized.succeeded, true);
    submitNonRenderingCanvasPass(executor, 'rgba8Unorm');

    assert.equal((await executor.flush()).succeeded, true);
    const readback = await executor.readPixels();

    // This fake proves transport only. Its arbitrary bytes intentionally do not
    // satisfy the production triangle's pixel-proof thresholds.
    assert.equal(readback.succeeded, false);
    assert.equal(readback.pixelProof.passed, false);
    assert.equal(readback.diagnostics[0].code, 'REKALL_WEBGPU_PIXEL_PROOF_FAILED');
    assert.equal(readback.pixelProof.bytesPerRow, 256);
    assert.deepEqual(readback.pixelProof.samples.background, { x: 5, y: 5, r: 5, g: 5, b: 17, a: 255 });
    assert.deepEqual(readback.pixelProof.samples.blue, { x: 32, y: 20, r: 32, g: 20, b: 17, a: 255 });
    assert.deepEqual(fake.configurations[0], { device: fake.device, format: 'rgba8unorm', alphaMode: 'opaque', usage: 17 });
    assert.deepEqual(fake.copyCalls[0].layout, { offset: 0, bytesPerRow: 256, rowsPerImage: 64 });
    assert.deepEqual(fake.order.slice(-7), [
        'getCurrentTexture',
        'beginRenderPass',
        'endRenderPass',
        'copyTextureToBuffer',
        'finish',
        'submit',
        'mapAsync'
    ]);
    assert.equal(fake.errorScopes.pushed, 2);
    assert.equal(fake.errorScopes.popped, 2);
});

test('production pixel proof rejects a copied canvas whose non-rendering fake is all dark', async () => {
    const fake = createReadbackOnlyWebGpu(() => [0, 0, 0, 255]);
    const executor = createWebGpuExecutor(fake.environment);
    await executor.initialize();
    submitNonRenderingCanvasPass(executor, 'rgba8Unorm');
    await executor.flush();

    const readback = await executor.readPixels();

    assert.equal(readback.succeeded, false);
    assert.equal(readback.pixelProof.passed, false);
    assert.equal(readback.diagnostics[0].code, 'REKALL_WEBGPU_PIXEL_PROOF_FAILED');
});

function submitNonRenderingCanvasPass(executor, format) {
    const execute = packet => {
        const result = executor.execute(JSON.stringify({ version: 1, ...packet }));
        assert.equal(result.succeeded, true, JSON.stringify(result.diagnostics));
    };
    execute({ operation: 'importCanvasOutput', texture: outputTexture, renderTarget: outputTarget, width: 64, height: 64, format });
    execute({ operation: 'submit', label: 'transport-only', commands: [
        { kind: 'beginRenderPass', data: { descriptor: { renderTarget: outputTarget, colorClearValues: [{ red: 0, green: 0, blue: 0, alpha: 1 }], label: 'transport-only' } } },
        { kind: 'endRenderPass', data: {} }
    ] });
}

function createReadbackOnlyWebGpu(pixel) {
    const width = 64;
    const height = 64;
    const configurations = [];
    const copyCalls = [];
    const order = [];
    const errorScopes = { pushed: 0, popped: 0 };
    const canvasTexture = makeTexture(width, height, pixel);
    const queue = {
        submit(commandBuffers) {
            order.push('submit');
            commandBuffers.forEach(commandBuffer => commandBuffer.execute());
        },
        async onSubmittedWorkDone() {}
    };
    const device = {
        limits: {},
        features: new Set(),
        lost: new Promise(() => {}),
        queue,
        pushErrorScope() { errorScopes.pushed++ },
        async popErrorScope() { errorScopes.popped++; return null },
        addEventListener() {},
        createBuffer(descriptor) { return makeReadbackBuffer(descriptor, order); },
        createCommandEncoder() { return makeCopyEncoder(canvasTexture, copyCalls, order); }
    };
    const context = {
        configure(descriptor) { configurations.push(descriptor); },
        getCurrentTexture() { order.push('getCurrentTexture'); return canvasTexture; }
    };
    return {
        device,
        configurations,
        copyCalls,
        order,
        errorScopes,
        environment: {
            navigator: { gpu: {
                async requestAdapter() { return { limits: {}, async requestDevice() { return device; } }; },
                getPreferredCanvasFormat() { return 'rgba8unorm'; }
            } },
            canvas: { width, height, getContext() { return context; } }
        }
    };
}

function makeReadbackBuffer(descriptor, order) {
    return {
        bytes: new Uint8Array(descriptor.size),
        async mapAsync() { order.push('mapAsync'); },
        getMappedRange() { return this.bytes.buffer; },
        unmap() {},
        destroy() {}
    };
}

function makeTexture(width, height, pixel) {
    const bytes = new Uint8Array(width * height * 4);
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) bytes.set(pixel(x, y), (y * width + x) * 4);
    }
    const texture = { width, height, bytes };
    texture.createView = () => ({ texture });
    return texture;
}

function makeCopyEncoder(canvasTexture, copyCalls, order) {
    const operations = [];
    return {
        beginRenderPass() {
            order.push('beginRenderPass');
            return { end() { order.push('endRenderPass'); } };
        },
        copyTextureToBuffer(source, destination, size) {
            order.push('copyTextureToBuffer');
            copyCalls.push({
                source,
                destination,
                size,
                layout: { offset: destination.offset ?? 0, bytesPerRow: destination.bytesPerRow, rowsPerImage: destination.rowsPerImage }
            });
            operations.push(() => copyTexture(source.texture, destination.buffer, destination.bytesPerRow, size));
        },
        finish() {
            order.push('finish');
            return { execute() { operations.forEach(operation => operation()); } };
        }
    };
}

function copyTexture(texture, buffer, bytesPerRow, size) {
    for (let row = 0; row < size.height; row++) {
        buffer.bytes.set(texture.bytes.subarray(row * texture.width * 4, (row + 1) * texture.width * 4), row * bytesPerRow);
    }
}
