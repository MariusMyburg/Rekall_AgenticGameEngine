import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { createWebGpuExecutor } from '../wwwroot/webgpu-device.js';

const bufferFixture = {
    version: 1,
    operation: 'create',
    resourceType: 'buffer',
    handle: { deviceId: '11111111-1111-1111-1111-111111111111', kind: 'buffer', slot: 7, generation: 1 },
    descriptor: { sizeBytes: 16, usage: 'vertex', memoryAccess: 'deviceLocal', label: 'literal-browser-fixture' }
};

test('executes the literal C# buffer packet using WebGPU usage flags', async () => {
    const calls = [];
    const device = {
        lost: new Promise(() => {}),
        queue: { onSubmittedWorkDone: async () => {}, writeBuffer: () => {}, submit: () => {} },
        pushErrorScope: () => {},
        popErrorScope: async () => null,
        addEventListener: () => {},
        createBuffer: descriptor => { calls.push(descriptor); return { destroy: () => {} }; }
    };
    const executor = createWebGpuExecutor({
        navigator: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device, limits: {} }), getPreferredCanvasFormat: () => 'bgra8unorm' } },
        canvas: { getContext: () => ({ configure: () => {} }) }
    });

    assert.equal((await executor.initialize()).succeeded, true);
    const result = executor.execute(JSON.stringify(bufferFixture));

    assert.equal(result.succeeded, true);
    assert.deepEqual(calls[0], { size: 16, usage: 32, label: 'literal-browser-fixture' });
});

test('creates a uniform-buffer binding set from literal protocol packets', async () => {
    const bindGroups = [];
    const device = {
        lost: new Promise(() => {}), queue: { onSubmittedWorkDone: async () => {}, writeBuffer: () => {}, submit: () => {} },
        pushErrorScope: () => {}, popErrorScope: async () => null, addEventListener: () => {},
        createBuffer: () => ({ destroy: () => {} }), createBindGroupLayout: descriptor => descriptor,
        createBindGroup: descriptor => { bindGroups.push(descriptor); return descriptor; }
    };
    const executor = createWebGpuExecutor({ navigator: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device, limits: {} }), getPreferredCanvasFormat: () => 'bgra8unorm' } }, canvas: { getContext: () => ({ configure: () => {} }) } });
    await executor.initialize();
    const handle = kind => ({ deviceId: '11111111-1111-1111-1111-111111111111', kind, slot: kind === 'buffer' ? 1 : kind === 'bindingLayout' ? 2 : 3, generation: 1 });

    assert.equal(executor.execute(JSON.stringify({ ...bufferFixture, handle: handle('buffer') })).succeeded, true);
    assert.equal(executor.execute(JSON.stringify({ version: 1, operation: 'create', resourceType: 'bindingLayout', handle: handle('bindingLayout'), descriptor: { entries: [{ binding: 0, type: 'uniformBuffer', visibility: 'vertex', minimumBindingSize: 16 }] } })).succeeded, true);
    const created = executor.execute(JSON.stringify({ version: 1, operation: 'create', resourceType: 'bindingSet', handle: handle('bindingSet'), descriptor: { layout: handle('bindingLayout'), entries: [{ binding: 0, resource: handle('buffer'), offset: 0, sizeBytes: 16 }] } }));

    assert.equal(created.succeeded, true);
    assert.equal(bindGroups[0].entries[0].resource.size, 16);
});

test('flush waits for shader compilation information', async () => {
    let completeCompilation;
    const compilation = new Promise(resolve => { completeCompilation = resolve; });
    const device = {
        lost: new Promise(() => {}), queue: { onSubmittedWorkDone: async () => {}, writeBuffer: () => {}, submit: () => {} },
        pushErrorScope: () => {}, popErrorScope: async () => null, addEventListener: () => {},
        createShaderModule: () => ({ getCompilationInfo: () => compilation })
    };
    const executor = createWebGpuExecutor({ navigator: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device, limits: {} }), getPreferredCanvasFormat: () => 'bgra8unorm' } }, canvas: { getContext: () => ({ configure: () => {} }) } });
    await executor.initialize();
    const created = executor.execute(JSON.stringify({ version: 1, operation: 'create', resourceType: 'shaderModule', handle: { deviceId: '11111111-1111-1111-1111-111111111111', kind: 'shaderModule', slot: 4, generation: 1 }, descriptor: { stage: 'vertex', language: 'wgsl', source: '@vertex fn main() -> @builtin(position) vec4f { return vec4f(); }', entryPoint: 'main' } }));
    assert.equal(created.succeeded, true);

    let flushed = false;
    const flush = executor.flush().then(() => { flushed = true; });
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.equal(flushed, false);
    completeCompilation({ messages: [] });
    await flush;
    assert.equal(flushed, true);
});

test('maps render clear values, load semantics, and attachment mip/layer views exactly', async () => {
    let passDescriptor;
    const textureDescriptors = [];
    const device = {
        lost: new Promise(() => {}), queue: { onSubmittedWorkDone: async () => {}, writeBuffer: () => {}, submit: () => {} },
        pushErrorScope: () => {}, popErrorScope: async () => null, addEventListener: () => {},
        createTexture: descriptor => ({ createView: view => { textureDescriptors.push(view); return { view }; }, destroy: () => {} }),
        createCommandEncoder: () => ({ beginRenderPass: descriptor => { passDescriptor = descriptor; return { end: () => {} }; }, finish: () => ({}) })
    };
    const executor = createWebGpuExecutor({ navigator: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device, limits: {} }), getPreferredCanvasFormat: () => 'bgra8unorm' } }, canvas: { getContext: () => ({ configure: () => {} }) } });
    await executor.initialize();
    const handle = (kind, slot) => ({ deviceId: '11111111-1111-1111-1111-111111111111', kind, slot, generation: 1 });
    const texture = descriptor => executor.execute(JSON.stringify({ version: 1, operation: 'create', resourceType: 'texture', handle: handle('texture', descriptor.format === 'depth32Float' ? 2 : 1), descriptor }));
    assert.equal(texture({ dimension: 'texture2D', width: 32, height: 16, depth: 1, mipLevels: 3, arrayLayers: 4, sampleCount: 1, format: 'rgba8Unorm', usage: 'colorAttachment' }).succeeded, true);
    assert.equal(texture({ dimension: 'texture2D', width: 32, height: 16, depth: 1, mipLevels: 3, arrayLayers: 4, sampleCount: 1, format: 'depth32Float', usage: 'depthStencilAttachment' }).succeeded, true);
    assert.equal(executor.execute(JSON.stringify({ version: 1, operation: 'create', resourceType: 'renderTarget', handle: handle('renderTarget', 3), descriptor: { colorAttachments: [{ texture: handle('texture', 1), mipLevel: 2, arrayLayer: 3 }], depthStencilAttachment: { texture: handle('texture', 2), mipLevel: 1, arrayLayer: 2 }, width: 8, height: 4 } })).succeeded, true);
    assert.equal(executor.execute(JSON.stringify({ version: 1, operation: 'submit', commands: [{ kind: 'beginRenderPass', data: { descriptor: { renderTarget: handle('renderTarget', 3), colorClearValues: [{ red: .1, green: .2, blue: .3, alpha: .4 }], label: 'exact-pass' } } }, { kind: 'endRenderPass', data: {} }] })).succeeded, true);

    assert.deepEqual(passDescriptor.colorAttachments[0].clearValue, { r: .1, g: .2, b: .3, a: .4 });
    assert.equal(passDescriptor.colorAttachments[0].loadOp, 'clear');
    assert.equal(passDescriptor.depthStencilAttachment.depthLoadOp, 'load');
    assert.deepEqual(textureDescriptors, [{ baseMipLevel: 2, mipLevelCount: 1, baseArrayLayer: 3, arrayLayerCount: 1 }, { baseMipLevel: 1, mipLevelCount: 1, baseArrayLayer: 2, arrayLayerCount: 1 }]);
});

test('upload buffers use copy destination rather than illegal map write', async () => {
    const buffers = [];
    const device = { lost: new Promise(() => {}), queue: { onSubmittedWorkDone: async () => {}, writeBuffer: () => {}, submit: () => {} }, pushErrorScope: () => {}, popErrorScope: async () => null, addEventListener: () => {}, createBuffer: descriptor => { buffers.push(descriptor); return { destroy: () => {} }; } };
    const executor = createWebGpuExecutor({ navigator: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device, limits: {} }), getPreferredCanvasFormat: () => 'bgra8unorm' } }, canvas: { getContext: () => ({ configure: () => {} }) } });
    await executor.initialize();
    const upload = { version: 1, operation: 'create', resourceType: 'buffer', handle: { deviceId: '11111111-1111-1111-1111-111111111111', kind: 'buffer', slot: 8, generation: 1 }, descriptor: { sizeBytes: 16, usage: 'uniform, vertex, transferDestination', memoryAccess: 'upload' } };
    assert.equal(executor.execute(JSON.stringify(upload)).succeeded, true);
    assert.equal(buffers[0].usage, 64 | 32 | 8);
});

test('maps only readback plus transfer destination to legal WebGPU MAP_READ usage', async () => {
    const buffers = [];
    const device = { lost: new Promise(() => {}), limits: {}, features: new Set(), queue: { onSubmittedWorkDone: async () => {} }, pushErrorScope: () => {}, popErrorScope: async () => null, addEventListener: () => {}, createBuffer: descriptor => { buffers.push(descriptor); return { destroy: () => {} }; } };
    const executor = createWebGpuExecutor({ navigator: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device }), getPreferredCanvasFormat: () => 'bgra8unorm' } }, canvas: { getContext: () => ({ configure: () => {} }) } });
    await executor.initialize();
    const create = (slot, usage) => executor.execute(JSON.stringify({ version: 1, operation: 'create', resourceType: 'buffer', handle: { ...bufferFixture.handle, slot }, descriptor: { sizeBytes: 16, usage, memoryAccess: 'readback' } }));

    assert.equal(create(20, 'readback, transferDestination').succeeded, true);
    assert.equal(buffers[0].usage, 1 | 8);
    assert.equal(create(21, 'readback, uniform').diagnostics[0].code, 'REKALL_WEBGPU_BUFFER_USAGE_COMBINATION_UNSUPPORTED');
    assert.equal(create(22, 'readback, vertex').diagnostics[0].code, 'REKALL_WEBGPU_BUFFER_USAGE_COMBINATION_UNSUPPORTED');
});

test('pops each error scope after executing the packet and faults flush on a validation error', async () => {
    const order = [];
    const device = { lost: new Promise(() => {}), queue: { onSubmittedWorkDone: async () => {}, writeBuffer: () => {}, submit: () => {} }, pushErrorScope: () => order.push('push'), popErrorScope: () => { order.push('pop'); return Promise.resolve({ message: 'invalid usage' }); }, addEventListener: () => {}, createBuffer: () => { order.push('create'); return { destroy: () => {} }; } };
    const executor = createWebGpuExecutor({ navigator: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device, limits: {} }), getPreferredCanvasFormat: () => 'bgra8unorm' } }, canvas: { getContext: () => ({ configure: () => {} }) } });
    await executor.initialize();
    assert.equal(executor.execute(JSON.stringify(bufferFixture)).succeeded, true);
    assert.deepEqual(order, ['push', 'create', 'pop']);
    const flushed = await executor.flush();
    assert.equal(flushed.succeeded, false);
    assert.equal(flushed.diagnostics[0].code, 'REKALL_WEBGPU_VALIDATION_ERROR');
});

test('derives a whole 3D mip upload from the retained texture descriptor and strict v1 packet', async () => {
    const writes = [];
    const device = { lost: new Promise(() => {}), queue: { onSubmittedWorkDone: async () => {}, writeBuffer: () => {}, submit: () => {}, writeTexture: (...args) => writes.push(args) }, pushErrorScope: () => {}, popErrorScope: async () => null, addEventListener: () => {}, createTexture: () => ({ createView: () => ({}), destroy: () => {} }) };
    const executor = createWebGpuExecutor({ navigator: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device, limits: {} }), getPreferredCanvasFormat: () => 'bgra8unorm' } }, canvas: { getContext: () => ({ configure: () => {} }) } });
    await executor.initialize();
    const handle = { deviceId: '11111111-1111-1111-1111-111111111111', kind: 'texture', slot: 9, generation: 1 };
    assert.equal(executor.execute(JSON.stringify({ version: 1, operation: 'create', resourceType: 'texture', handle, descriptor: { dimension: 'texture3D', width: 8, height: 4, depth: 4, mipLevels: 3, arrayLayers: 1, sampleCount: 1, format: 'rgba8Unorm', usage: 'copyDestination' } })).succeeded, true);
    const csharpPacket = readFileSync(new URL('./fixtures/webgpu-write-texture-3d-v1.json', import.meta.url), 'utf8').trim();
    assert.equal(executor.execute(csharpPacket).succeeded, true);
    assert.deepEqual(writes[0][0].origin, { x: 0, y: 0, z: 0 });
    assert.deepEqual(writes[0][2], { bytesPerRow: 16, rowsPerImage: 2 });
    assert.deepEqual(writes[0][3], { width: 4, height: 2, depthOrArrayLayers: 2 });
});

test('fails closed before a 65th error scope while balancing every pushed scope and propagating validation', async () => {
    let pushes = 0; let pops = 0;
    const device = {
        lost: new Promise(() => {}), queue: { onSubmittedWorkDone: async () => {}, writeBuffer: () => {}, submit: () => {} },
        pushErrorScope: () => { pushes++; },
        popErrorScope: () => { pops++; return Promise.resolve(pops === 64 ? { message: 'bounded validation failure' } : null); },
        addEventListener: () => {}, createBuffer: () => ({ destroy: () => {} })
    };
    const executor = createWebGpuExecutor({ navigator: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device, limits: {} }), getPreferredCanvasFormat: () => 'bgra8unorm' } }, canvas: { getContext: () => ({ configure: () => {} }) } });
    await executor.initialize();

    const results = Array.from({ length: 65 }, (_, index) => executor.execute(JSON.stringify({ ...bufferFixture, handle: { ...bufferFixture.handle, slot: index } })));

    assert.equal(results[64].succeeded, false);
    assert.equal(results[64].diagnostics[0].code, 'REKALL_WEBGPU_PENDING_OVERFLOW');
    assert.equal(pushes, 64);
    assert.equal(pops, 64);
    const flushed = await executor.flush();
    assert.equal(flushed.succeeded, false);
    assert.ok(flushed.diagnostics.some(item => item.code === 'REKALL_WEBGPU_VALIDATION_ERROR'));
});

test('uses explicit texture binding metadata instead of guessing WebGPU defaults', async () => {
    let layout;
    const device = { lost: new Promise(() => {}), queue: { onSubmittedWorkDone: async () => {}, writeBuffer: () => {}, submit: () => {} }, pushErrorScope: () => {}, popErrorScope: async () => null, addEventListener: () => {}, createBindGroupLayout: descriptor => { layout = descriptor; return descriptor; } };
    const executor = createWebGpuExecutor({ navigator: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device, limits: {} }), getPreferredCanvasFormat: () => 'bgra8unorm' } }, canvas: { getContext: () => ({ configure: () => {} }) } });
    await executor.initialize();
    const handle = { deviceId: '11111111-1111-1111-1111-111111111111', kind: 'bindingLayout', slot: 10, generation: 1 };
    const packet = { version: 1, operation: 'create', resourceType: 'bindingLayout', handle, descriptor: { entries: [{ binding: 0, type: 'sampledTexture', visibility: 'fragment', texture: { sampleType: 'depth', viewDimension: 'cubeArray', multisampled: true } }, { binding: 1, type: 'storageTexture', visibility: 'compute', texture: { viewDimension: 'texture3D', storageFormat: 'r32Float', storageAccess: 'readWrite' } }] } };
    assert.equal(executor.execute(JSON.stringify(packet)).succeeded, true);
    assert.deepEqual(layout.entries[0].texture, { sampleType: 'depth', viewDimension: 'cube-array', multisampled: true });
    assert.deepEqual(layout.entries[1].storageTexture, { access: 'read-write', format: 'r32float', viewDimension: '3d' });
});

test('creates bind-group texture views with declared dimension and descriptor-derived aspect mip and layers', async () => {
    const views = [];
    const device = {
        lost: new Promise(() => {}), queue: { onSubmittedWorkDone: async () => {}, writeBuffer: () => {}, submit: () => {} },
        pushErrorScope: () => {}, popErrorScope: async () => null, addEventListener: () => {},
        createTexture: () => ({ createView: descriptor => { views.push(descriptor); return descriptor; }, destroy: () => {} }),
        createBindGroupLayout: descriptor => descriptor, createBindGroup: descriptor => descriptor
    };
    const executor = createWebGpuExecutor({ navigator: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device, limits: {} }), getPreferredCanvasFormat: () => 'bgra8unorm' } }, canvas: { getContext: () => ({ configure: () => {} }) } });
    await executor.initialize();
    const handle = (kind, slot) => ({ deviceId: '11111111-1111-1111-1111-111111111111', kind, slot, generation: 1 });
    const texture = handle('texture', 1); const layout = handle('bindingLayout', 2); const set = handle('bindingSet', 3);
    assert.equal(executor.execute(JSON.stringify({ version: 1, operation: 'create', resourceType: 'texture', handle: texture, descriptor: { dimension: 'cube', width: 16, height: 16, depth: 1, mipLevels: 5, arrayLayers: 12, sampleCount: 1, format: 'depth32Float', usage: 'sampled' } })).succeeded, true);
    assert.equal(executor.execute(JSON.stringify({ version: 1, operation: 'create', resourceType: 'bindingLayout', handle: layout, descriptor: { entries: [{ binding: 0, type: 'sampledTexture', visibility: 'fragment', texture: { sampleType: 'depth', viewDimension: 'cubeArray', multisampled: false, storageFormat: 'rgba8Unorm', storageAccess: 'writeOnly' } }] } })).succeeded, true);
    assert.equal(executor.execute(JSON.stringify({ version: 1, operation: 'create', resourceType: 'bindingSet', handle: set, descriptor: { layout, entries: [{ binding: 0, resource: texture, offset: 0, sizeBytes: 0 }] } })).succeeded, true);
    assert.deepEqual(views, [{ dimension: 'cube-array', aspect: 'depth-only', baseMipLevel: 0, mipLevelCount: 5, baseArrayLayer: 0, arrayLayerCount: 12 }]);
});

test('reports enabled device capabilities from device limits and preferred canvas format', async () => {
    const adapter = { limits: { maxBufferSize: 999 }, requestDevice: async () => device };
    const device = { limits: { maxBufferSize: 123, maxTextureDimension1D: 64 }, features: new Set(['timestamp-query']), lost: new Promise(() => {}), queue: {}, addEventListener: () => {} };
    const executor = createWebGpuExecutor({ navigator: { gpu: { requestAdapter: async () => adapter, getPreferredCanvasFormat: () => 'rgba8unorm' } }, canvas: { getContext: () => ({ configure: () => {} }) } });

    const initialized = await executor.initialize();

    assert.equal(initialized.capabilities.preferredCanvasFormat, 'rgba8unorm');
    assert.equal(initialized.capabilities.limits.maxBufferSize, 123);
    assert.equal(initialized.capabilities.limits.maxTextureDimension1D, 64);
    assert.deepEqual(initialized.capabilities.features, ['timestamp-query']);
});

test('rejects wrong-kind operation and render-attachment handles before WebGPU lookup', async () => {
    const device = { limits: {}, features: new Set(), lost: new Promise(() => {}), queue: { onSubmittedWorkDone: async () => {}, writeBuffer: () => {}, writeTexture: () => {}, submit: () => {} }, pushErrorScope: () => {}, popErrorScope: async () => null, addEventListener: () => {} };
    const executor = createWebGpuExecutor({ navigator: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device }), getPreferredCanvasFormat: () => 'bgra8unorm' } }, canvas: { getContext: () => ({ configure: () => {} }) } });
    await executor.initialize();
    const wrong = (kind, slot = 1) => ({ deviceId: '11111111-1111-1111-1111-111111111111', kind, slot, generation: 1 });
    const packets = [
        { version: 1, operation: 'writeBuffer', handle: wrong('texture'), offset: 0, dataBase64: 'AA==' },
        { version: 1, operation: 'writeTexture', handle: wrong('buffer'), mipLevel: 0, arrayLayer: 0, dataBase64: 'AA==' },
        { version: 1, operation: 'importCanvasOutput', texture: wrong('buffer'), renderTarget: wrong('renderTarget', 2), width: 1, height: 1, format: 'bgra8Unorm' },
        { version: 1, operation: 'importCanvasOutput', texture: wrong('texture'), renderTarget: wrong('texture', 2), width: 1, height: 1, format: 'bgra8Unorm' },
        { version: 1, operation: 'create', resourceType: 'renderTarget', handle: wrong('renderTarget', 3), descriptor: { colorAttachments: [{ texture: wrong('buffer') }], depthStencilAttachment: null, width: 1, height: 1 } }
    ];
    for (const packet of packets) {
        const rejected = executor.execute(JSON.stringify(packet));
        assert.equal(rejected.succeeded, false);
        assert.equal(rejected.diagnostics[0].code, 'REKALL_WEBGPU_RESOURCE_KIND_MISMATCH');
    }
});

test('bounds diagnostic count and unicode strings without oversized responses', async () => {
    const executor = createWebGpuExecutor({ navigator: {}, canvas: null });
    const unavailable = await executor.initialize('x'.repeat(10000));
    const json = JSON.stringify(unavailable);
    assert.ok(Buffer.byteLength(json, 'utf8') < 4096);
    assert.ok(Buffer.byteLength(unavailable.diagnostics[0].message, 'utf8') <= 2048);
    assert.ok(unavailable.diagnostics.length <= 64);
});

test('executes canonical uint16 and uint32 index-buffer commands', async () => {
    const indexFormats = [];
    const pass = { setIndexBuffer: (_buffer, format) => indexFormats.push(format), end: () => {} };
    const device = {
        lost: new Promise(() => {}),
        queue: { onSubmittedWorkDone: async () => {}, writeBuffer: () => {}, submit: () => {} },
        pushErrorScope: () => {}, popErrorScope: async () => null, addEventListener: () => {},
        createBuffer: () => ({ destroy: () => {} }),
        createCommandEncoder: () => ({ beginRenderPass: () => pass, finish: () => ({}) })
    };
    const context = { configure: () => {}, getCurrentTexture: () => ({ createView: () => ({}) }) };
    const executor = createWebGpuExecutor({ navigator: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device, limits: {} }), getPreferredCanvasFormat: () => 'bgra8unorm' } }, canvas: { getContext: () => context } });
    await executor.initialize();
    const handle = (kind, slot) => ({ deviceId: '11111111-1111-1111-1111-111111111111', kind, slot, generation: 1 });
    const buffer = handle('buffer', 1); const texture = handle('texture', 2); const target = handle('renderTarget', 3);
    assert.equal(executor.execute(JSON.stringify({ version: 1, operation: 'create', resourceType: 'buffer', handle: buffer, descriptor: { sizeBytes: 32, usage: 'index', memoryAccess: 'deviceLocal' } })).succeeded, true);
    assert.equal(executor.execute(JSON.stringify({ version: 1, operation: 'importCanvasOutput', texture, renderTarget: target, width: 8, height: 8, format: 'bgra8Unorm' })).succeeded, true);

    for (const format of ['uint16', 'uint32']) {
        const result = executor.execute(JSON.stringify({ version: 1, operation: 'submit', commands: [
            { kind: 'beginRenderPass', data: { descriptor: { renderTarget: target, colorClearValues: [] } } },
            { kind: 'setIndexBuffer', data: { buffer, format, offset: 0, sizeBytes: 16 } },
            { kind: 'endRenderPass', data: {} }
        ] }));
        assert.equal(result.succeeded, true);
    }

    assert.deepEqual(indexFormats, ['uint16', 'uint32']);
});
