import assert from 'node:assert/strict';
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
