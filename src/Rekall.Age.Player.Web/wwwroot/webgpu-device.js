const VERSION = 1;
const MAX_PACKET_BYTES = 16 * 1024 * 1024;
const MAX_DIAGNOSTICS = 64;
const MAX_DIAGNOSTIC_CODE_BYTES = 128;
const MAX_DIAGNOSTIC_MESSAGE_BYTES = 2048;
const MAX_DIAGNOSTIC_TARGET_BYTES = 1024;
const MAX_PENDING = 64;

const bufferUsage = { mapRead: 1, mapWrite: 2, copySource: 4, copyDestination: 8, index: 16, vertex: 32, uniform: 64, storage: 128, indirect: 256 };
const textureUsage = { copySource: 1, copyDestination: 2, textureBinding: 4, storageBinding: 8, renderAttachment: 16 };
const bufferUsageMap = { copySource: 'copySource', transferDestination: 'copyDestination', vertex: 'vertex', index: 'index', uniform: 'uniform', storage: 'storage', indirect: 'indirect', readback: 'mapRead' };
const textureUsageMap = { copySource: 'copySource', copyDestination: 'copyDestination', sampled: 'textureBinding', storage: 'storageBinding', colorAttachment: 'renderAttachment', depthStencilAttachment: 'renderAttachment', present: 'renderAttachment' };
const textureFormats = { r8Unorm: 'r8unorm', rg8Unorm: 'rg8unorm', rgba8Unorm: 'rgba8unorm', rgba8UnormSrgb: 'rgba8unorm-srgb', bgra8Unorm: 'bgra8unorm', bgra8UnormSrgb: 'bgra8unorm-srgb', rgba16Float: 'rgba16float', r32Float: 'r32float', depth24Stencil8: 'depth24plus-stencil8', depth32Float: 'depth32float' };
const primitiveTopologies = { triangleList: 'triangle-list', triangleStrip: 'triangle-strip', lineList: 'line-list', lineStrip: 'line-strip', pointList: 'point-list' };
const vertexFormats = { float32: 'float32', float32x2: 'float32x2', float32x3: 'float32x3', float32x4: 'float32x4', uint32: 'uint32', uint32x2: 'uint32x2', uint32x3: 'uint32x3', uint32x4: 'uint32x4', sint32: 'sint32', sint32x2: 'sint32x2', sint32x3: 'sint32x3', sint32x4: 'sint32x4' };
const compareOperations = { never: 'never', less: 'less', lessEqual: 'less-equal', equal: 'equal', greaterEqual: 'greater-equal', greater: 'greater', notEqual: 'not-equal', always: 'always' };
const filterModes = { nearest: 'nearest', linear: 'linear' };
const addressModes = { clampToEdge: 'clamp-to-edge', repeat: 'repeat', mirrorRepeat: 'mirror-repeat' };
const stageVisibility = { vertex: 1, fragment: 2, compute: 4 };

function bounded(value, maximum) { const text = String(value ?? ''); let end = text.length; while (end > 0 && bytes(text.slice(0, end)) > maximum) end--; return text.slice(0, end); }
function result(succeeded = true, diagnostics = [], extra = {}) { return { succeeded: succeeded && diagnostics.length === 0, diagnostics: diagnostics.slice(0, MAX_DIAGNOSTICS), ...extra }; }
function diagnostic(code, message, target) { return { code: bounded(code, MAX_DIAGNOSTIC_CODE_BYTES), message: bounded(message, MAX_DIAGNOSTIC_MESSAGE_BYTES), ...(target ? { target: bounded(target, MAX_DIAGNOSTIC_TARGET_BYTES) } : {}) }; }
function enumValue(map, value, name) { const mapped = map[value]; if (!mapped) throw new Error(`REKALL_WEBGPU_${name}_INVALID`); return mapped; }
function key(handle) {
    if (!handle || typeof handle !== 'object' || typeof handle.deviceId !== 'string' || typeof handle.kind !== 'string' || !Number.isInteger(handle.slot) || !Number.isInteger(handle.generation) || handle.slot < 0 || handle.generation <= 0) throw new Error('REKALL_WEBGPU_HANDLE_INVALID');
    return `${handle.deviceId}/${handle.kind}/${handle.slot}/${handle.generation}`;
}
function bytes(packet) { return new TextEncoder().encode(packet).byteLength; }
function decodeBase64(value) { const decoded = atob(value); return Uint8Array.from(decoded, character => character.charCodeAt(0)); }
function bitFlags(value, map, names) {
    if (typeof value !== 'string') throw new Error('REKALL_WEBGPU_USAGE_INVALID');
    return value.split(',').reduce((flags, name) => {
        const flag = map[name.trim()]; if (!flag) throw new Error(`REKALL_WEBGPU_${names}_INVALID`); return flags | flag;
    }, 0);
}
function gpuBufferUsage(name) { return globalThis.GPUBufferUsage?.[name.toUpperCase()] ?? bufferUsage[name]; }
function gpuTextureUsage(name) { return globalThis.GPUTextureUsage?.[name.replace(/([A-Z])/g, '_$1').toUpperCase()] ?? textureUsage[name]; }
function visibility(value) { return value.split(',').reduce((flags, name) => flags | enumValue(stageVisibility, name.trim(), 'SHADER_STAGE'), 0); }

export function createWebGpuExecutor(environment = {}) {
    const runtime = environment.navigator ?? globalThis.navigator;
    let canvas = environment.canvas;
    const maps = { buffer: new Map(), texture: new Map(), sampler: new Map(), shaderModule: new Map(), bindingLayout: new Map(), bindingSet: new Map(), renderPipeline: new Map(), computePipeline: new Map(), renderTarget: new Map() };
    const pendingDiagnostics = [];
    const pendingScopes = [];
    const pendingCompilations = [];
    let adapter; let device; let context; let canvasFormat; let initialized = false; let lost = false; let pendingOverflow = false;

    const addDiagnostic = item => { if (pendingDiagnostics.length < MAX_DIAGNOSTICS) pendingDiagnostics.push(item); else pendingOverflow = true; };
    const object = (handle, expectedKind) => { if (expectedKind && handle?.kind !== expectedKind) throw new Error('REKALL_WEBGPU_RESOURCE_KIND_MISMATCH'); const map = maps[expectedKind ?? handle?.kind]; const value = map?.get(key(handle)); if (!value) throw new Error('REKALL_WEBGPU_RESOURCE_MISSING'); return value; };
    const store = (handle, value) => { const map = maps[handle.kind]; if (!map || map.has(key(handle))) throw new Error('REKALL_WEBGPU_RESOURCE_DUPLICATE'); map.set(key(handle), value); };
    const beginScope = () => device.pushErrorScope?.('validation');
    const endScope = () => { if (pendingScopes.length >= MAX_PENDING) { pendingOverflow = true; return; } pendingScopes.push(Promise.resolve(device.popErrorScope?.()).then(error => { if (error) addDiagnostic(diagnostic('REKALL_WEBGPU_VALIDATION_ERROR', error.message ?? 'WebGPU validation failed.')); }).catch(error => addDiagnostic(diagnostic('REKALL_WEBGPU_ERROR_SCOPE_FAILED', 'WebGPU error scope failed.', error?.name)))); };

    async function initialize(canvasSelector = '#viewport') {
        try {
            if (initialized) return result(true, pendingDiagnostics.splice(0));
            canvas ??= globalThis.document?.querySelector(canvasSelector);
            if (!runtime?.gpu || !canvas?.getContext) return result(false, [diagnostic('REKALL_WEBGPU_UNAVAILABLE', 'WebGPU or the player canvas is unavailable.')]);
            adapter = await runtime.gpu.requestAdapter();
            if (!adapter) return result(false, [diagnostic('REKALL_WEBGPU_ADAPTER_UNAVAILABLE', 'No compatible WebGPU adapter is available.')]);
            device = await adapter.requestDevice();
            context = canvas.getContext('webgpu');
            if (!context) return result(false, [diagnostic('REKALL_WEBGPU_CONTEXT_UNAVAILABLE', 'The player canvas cannot create a WebGPU context.')]);
            canvasFormat = runtime.gpu.getPreferredCanvasFormat();
            context.configure({ device, format: canvasFormat, alphaMode: 'opaque' });
            device.addEventListener?.('uncapturederror', event => addDiagnostic(diagnostic('REKALL_WEBGPU_UNCAPTURED_ERROR', event.error?.message ?? 'WebGPU reported an uncaptured error.')));
            device.lost?.then(info => { lost = true; addDiagnostic(diagnostic('REKALL_WEBGPU_DEVICE_LOST', info?.message ?? 'The WebGPU device was lost.', info?.reason)); });
            initialized = true;
            return result(true, pendingDiagnostics.splice(0), { capabilities: { preferredCanvasFormat: canvasFormat, limits: adapter.limits ?? device.limits ?? {}, features: Array.from(device.features ?? []) } });
        } catch (error) { return result(false, [diagnostic('REKALL_WEBGPU_INITIALIZE_FAILED', 'WebGPU initialization failed.', error?.name)]); }
    }

    function create(packet) {
        if (packet.resourceType !== packet.handle?.kind) throw new Error('REKALL_WEBGPU_RESOURCE_KIND_MISMATCH');
        const descriptor = packet.descriptor;
        if (!descriptor || typeof descriptor !== 'object') throw new Error('REKALL_WEBGPU_DESCRIPTOR_INVALID');
        switch (packet.resourceType) {
            case 'buffer': {
                let usage = bitFlags(descriptor.usage, Object.fromEntries(Object.entries(bufferUsageMap).map(([age, gpu]) => [age, gpuBufferUsage(gpu)])), 'BUFFER_USAGE');
                if (descriptor.memoryAccess === 'upload') usage |= gpuBufferUsage('copyDestination');
                else if (descriptor.memoryAccess === 'readback') usage |= gpuBufferUsage('mapRead');
                else if (descriptor.memoryAccess !== 'deviceLocal') throw new Error('REKALL_WEBGPU_MEMORY_ACCESS_INVALID');
                store(packet.handle, { value: device.createBuffer({ size: descriptor.sizeBytes, usage, label: descriptor.label }), descriptor }); break;
            }
            case 'texture': store(packet.handle, { value: device.createTexture({ size: { width: descriptor.width, height: descriptor.height, depthOrArrayLayers: descriptor.dimension === 'texture3D' ? descriptor.depth : descriptor.arrayLayers }, dimension: enumValue({ texture1D: '1d', texture2D: '2d', texture3D: '3d', cube: '2d' }, descriptor.dimension, 'TEXTURE_DIMENSION'), format: enumValue(textureFormats, descriptor.format, 'TEXTURE_FORMAT'), usage: bitFlags(descriptor.usage, Object.fromEntries(Object.entries(textureUsageMap).map(([age, gpu]) => [age, gpuTextureUsage(gpu)])), 'TEXTURE_USAGE'), mipLevelCount: descriptor.mipLevels, sampleCount: descriptor.sampleCount, label: descriptor.label }), descriptor }); break;
            case 'sampler': store(packet.handle, { value: device.createSampler({ minFilter: enumValue(filterModes, descriptor.minFilter, 'FILTER'), magFilter: enumValue(filterModes, descriptor.magFilter, 'FILTER'), mipmapFilter: enumValue(filterModes, descriptor.mipmapFilter, 'FILTER'), addressModeU: enumValue(addressModes, descriptor.addressU, 'ADDRESS_MODE'), addressModeV: enumValue(addressModes, descriptor.addressV, 'ADDRESS_MODE'), addressModeW: enumValue(addressModes, descriptor.addressW, 'ADDRESS_MODE'), lodMinClamp: descriptor.minimumLod, lodMaxClamp: descriptor.maximumLod, maxAnisotropy: descriptor.maximumAnisotropy, ...(descriptor.compare ? { compare: enumValue(compareOperations, descriptor.compare, 'COMPARE') } : {}), label: descriptor.label }), descriptor }); break;
            case 'shaderModule': {
                if (descriptor.language !== 'wgsl') throw new Error('REKALL_WEBGPU_SHADER_LANGUAGE_REQUIRED');
                const value = device.createShaderModule({ code: descriptor.source, label: descriptor.label });
                if (pendingCompilations.length >= MAX_PENDING) throw new Error('REKALL_WEBGPU_PENDING_OVERFLOW');
                pendingCompilations.push(Promise.resolve(value.getCompilationInfo?.()).then(info => info?.messages?.filter(message => message.type === 'error').forEach(message => addDiagnostic(diagnostic('REKALL_WEBGPU_SHADER_COMPILATION_ERROR', message.message, `${message.lineNum}:${message.linePos}`)))).catch(error => addDiagnostic(diagnostic('REKALL_WEBGPU_SHADER_COMPILATION_INFO_FAILED', 'WebGPU shader compilation information failed.', error?.name))));
                store(packet.handle, { value, descriptor }); break;
            }
            case 'bindingLayout': {
                const entries = descriptor.entries.map(entry => ({ binding: entry.binding, visibility: visibility(entry.visibility), ...bindingLayoutEntry(entry) }));
                store(packet.handle, { value: device.createBindGroupLayout({ entries, label: descriptor.label }), descriptor }); break;
            }
            case 'bindingSet': {
                const layout = object(descriptor.layout, 'bindingLayout'); const entries = descriptor.entries.map(entry => ({ binding: entry.binding, resource: bindingResource(entry, layout.descriptor.entries.find(item => item.binding === entry.binding)) }));
                store(packet.handle, { value: device.createBindGroup({ layout: layout.value, entries, label: descriptor.label }), descriptor }); break;
            }
            case 'renderPipeline': store(packet.handle, createRenderPipeline(descriptor)); break;
            case 'computePipeline': store(packet.handle, createComputePipeline(descriptor)); break;
            case 'renderTarget': store(packet.handle, { descriptor }); break;
            default: throw new Error('REKALL_WEBGPU_RESOURCE_KIND_INVALID');
        }
    }

    function bindingLayoutEntry(entry) {
        const minimumBindingSize = entry.minimumBindingSize ?? 0;
        if (entry.type === 'uniformBuffer') return { buffer: { type: 'uniform', minBindingSize: minimumBindingSize } };
        if (entry.type === 'readOnlyStorageBuffer') return { buffer: { type: 'read-only-storage', minBindingSize: minimumBindingSize } };
        if (entry.type === 'storageBuffer') return { buffer: { type: 'storage', minBindingSize: minimumBindingSize } };
        if (entry.type === 'sampler') return { sampler: { type: 'filtering' } };
        if (entry.type === 'comparisonSampler') return { sampler: { type: 'comparison' } };
        const metadata = entry.texture;
        const viewDimension = enumValue({ texture1D: '1d', texture2D: '2d', texture2DArray: '2d-array', cube: 'cube', cubeArray: 'cube-array', texture3D: '3d' }, metadata?.viewDimension ?? 'texture2D', 'TEXTURE_VIEW_DIMENSION');
        if (entry.type === 'sampledTexture') return { texture: { sampleType: enumValue({ float: 'float', unfilterableFloat: 'unfilterable-float', depth: 'depth', sint: 'sint', uint: 'uint' }, metadata?.sampleType ?? 'float', 'TEXTURE_SAMPLE_TYPE'), viewDimension, multisampled: metadata?.multisampled ?? false } };
        if (entry.type === 'readOnlyStorageTexture' || entry.type === 'storageTexture') return { storageTexture: { access: enumValue({ readOnly: 'read-only', writeOnly: 'write-only', readWrite: 'read-write' }, metadata?.storageAccess ?? (entry.type === 'readOnlyStorageTexture' ? 'readOnly' : 'writeOnly'), 'STORAGE_ACCESS'), format: enumValue(textureFormats, metadata?.storageFormat ?? 'rgba8Unorm', 'TEXTURE_FORMAT'), viewDimension } };
        throw new Error('REKALL_WEBGPU_BINDING_TYPE_INVALID');
    }
    function bindingResource(entry, layoutEntry) {
        if (!layoutEntry) throw new Error('REKALL_WEBGPU_BINDING_MISSING');
        const type = layoutEntry.type;
        if (type.endsWith('Buffer')) { const resource = object(entry.resource, 'buffer'); return { buffer: resource.value, offset: entry.offset ?? 0, ...(entry.sizeBytes ? { size: entry.sizeBytes } : {}) }; }
        if (type === 'sampler' || type === 'comparisonSampler') return object(entry.resource, 'sampler').value;
        const resource = object(entry.resource, 'texture');
        return resource.value.createView();
    }
    function createRenderPipeline(descriptor) {
        const vertex = object(descriptor.vertexShader, 'shaderModule'); const fragment = object(descriptor.fragmentShader, 'shaderModule');
        const layouts = descriptor.bindingLayouts.map(handle => object(handle, 'bindingLayout').value);
        return { value: device.createRenderPipeline({ layout: device.createPipelineLayout({ bindGroupLayouts: layouts }), vertex: { module: vertex.value, entryPoint: vertex.descriptor.entryPoint, buffers: descriptor.vertexBuffers.map(layout => ({ arrayStride: layout.strideBytes, stepMode: enumValue({ vertex: 'vertex', instance: 'instance' }, layout.stepMode, 'VERTEX_STEP_MODE'), attributes: layout.attributes.map(attribute => ({ shaderLocation: attribute.location, format: enumValue(vertexFormats, attribute.format, 'VERTEX_FORMAT'), offset: attribute.offsetBytes })) })) }, fragment: { module: fragment.value, entryPoint: fragment.descriptor.entryPoint, targets: descriptor.colorTargets.map(target => ({ format: enumValue(textureFormats, target.format, 'TEXTURE_FORMAT'), writeMask: target.writeMask, ...(target.blendEnabled ? { blend: { color: { srcFactor: 'src-alpha', dstFactor: 'one-minus-src-alpha', operation: 'add' }, alpha: { srcFactor: 'one', dstFactor: 'one-minus-src-alpha', operation: 'add' } } } : {}) })) }, primitive: { topology: enumValue(primitiveTopologies, descriptor.topology, 'TOPOLOGY'), cullMode: enumValue({ none: 'none', front: 'front', back: 'back' }, descriptor.cullMode, 'CULL_MODE'), frontFace: enumValue({ clockwise: 'cw', counterClockwise: 'ccw' }, descriptor.frontFace, 'FRONT_FACE') }, ...(descriptor.depthStencil ? { depthStencil: { format: enumValue(textureFormats, descriptor.depthStencil.format, 'TEXTURE_FORMAT'), depthWriteEnabled: descriptor.depthStencil.depthWriteEnabled, depthCompare: enumValue(compareOperations, descriptor.depthStencil.depthCompare, 'COMPARE') } } : {}), label: descriptor.label }), descriptor };
    }
    function createComputePipeline(descriptor) { const shader = object(descriptor.computeShader, 'shaderModule'); return { value: device.createComputePipeline({ layout: device.createPipelineLayout({ bindGroupLayouts: descriptor.bindingLayouts.map(handle => object(handle, 'bindingLayout').value) }), compute: { module: shader.value, entryPoint: shader.descriptor.entryPoint }, label: descriptor.label }), descriptor }; }
    function attachmentView(attachment) {
        const texture = object(attachment.texture);
        if (texture.canvasOutput) return context.getCurrentTexture().createView();
        return texture.value.createView({ baseMipLevel: attachment.mipLevel ?? 0, mipLevelCount: 1, baseArrayLayer: attachment.arrayLayer ?? 0, arrayLayerCount: 1 });
    }
    function executeSubmission(packet) {
        const encoder = device.createCommandEncoder({ label: packet.label }); let pass = null;
        for (const command of packet.commands) {
            const data = command.data;
            if (command.kind === 'copyBuffer') encoder.copyBufferToBuffer(object(data.source, 'buffer').value, data.sourceOffset, object(data.destination, 'buffer').value, data.destinationOffset, data.sizeBytes);
            else if (command.kind === 'beginRenderPass') {
                const target = object(data.descriptor.renderTarget).descriptor;
                pass = encoder.beginRenderPass({
                    colorAttachments: target.colorAttachments.map((attachment, index) => {
                        const clear = data.descriptor.colorClearValues[index];
                        return { view: attachmentView(attachment), ...(clear ? { clearValue: { r: clear.red, g: clear.green, b: clear.blue, a: clear.alpha }, loadOp: 'clear' } : { loadOp: 'load' }), storeOp: 'store' };
                    }),
                    ...(target.depthStencilAttachment ? { depthStencilAttachment: {
                        view: attachmentView(target.depthStencilAttachment),
                        ...(data.descriptor.depthClearValue !== undefined ? { depthClearValue: data.descriptor.depthClearValue, depthLoadOp: 'clear' } : { depthLoadOp: 'load' }),
                        depthStoreOp: 'store',
                        ...(data.descriptor.stencilClearValue !== undefined ? { stencilClearValue: data.descriptor.stencilClearValue, stencilLoadOp: 'clear' } : { stencilLoadOp: 'load' }), stencilStoreOp: 'store'
                    } } : {}), label: data.descriptor.label
                });
            }
            else if (command.kind === 'setRenderPipeline') pass.setPipeline(object(data.pipeline, 'renderPipeline').value);
            else if (command.kind === 'setComputePipeline') pass.setPipeline(object(data.pipeline, 'computePipeline').value);
            else if (command.kind === 'setBindingSet') pass.setBindGroup(data.index, object(data.bindingSet, 'bindingSet').value);
            else if (command.kind === 'setVertexBuffer') pass.setVertexBuffer(data.slot, object(data.buffer, 'buffer').value, data.offset, data.sizeBytes || undefined);
            else if (command.kind === 'setIndexBuffer') pass.setIndexBuffer(object(data.buffer, 'buffer').value, enumValue({ uint16: 'uint16', uint32: 'uint32' }, data.format, 'INDEX_FORMAT'), data.offset, data.sizeBytes || undefined);
            else if (command.kind === 'draw') pass.draw(data.vertexCount, data.instanceCount, data.firstVertex, data.firstInstance);
            else if (command.kind === 'drawIndexed') pass.drawIndexed(data.indexCount, data.instanceCount, data.firstIndex, data.baseVertex, data.firstInstance);
            else if (command.kind === 'drawIndirect' || command.kind === 'drawIndexedIndirect') { const buffer = object(data.buffer, 'buffer').value; for (let index = 0; index < data.drawCount; index++) command.kind === 'drawIndirect' ? pass.drawIndirect(buffer, data.offset + index * data.strideBytes) : pass.drawIndexedIndirect(buffer, data.offset + index * data.strideBytes); }
            else if (command.kind === 'beginComputePass') pass = encoder.beginComputePass({ label: data.label });
            else if (command.kind === 'dispatch') pass.dispatchWorkgroups(data.groupCountX, data.groupCountY, data.groupCountZ);
            else if (command.kind === 'dispatchIndirect') pass.dispatchWorkgroupsIndirect(object(data.buffer, 'buffer').value, data.offset);
            else if (command.kind === 'endRenderPass' || command.kind === 'endComputePass') { pass.end(); pass = null; }
            else throw new Error('REKALL_WEBGPU_COMMAND_KIND_INVALID');
        }
        if (pass) throw new Error('REKALL_WEBGPU_PASS_UNTERMINATED');
        device.queue.submit([encoder.finish()]);
    }
    function execute(packetJson) {
        let scoped = false;
        try {
            if (!initialized || lost) return result(false, [diagnostic(lost ? 'REKALL_WEBGPU_DEVICE_LOST' : 'REKALL_WEBGPU_NOT_INITIALIZED', 'The WebGPU executor is not available.')]);
            if (typeof packetJson !== 'string' || bytes(packetJson) > MAX_PACKET_BYTES) return result(false, [diagnostic('REKALL_WEBGPU_PROTOCOL_PACKET_TOO_LARGE', 'WebGPU protocol packets must not exceed 16777216 UTF-8 bytes.')]);
            const packet = JSON.parse(packetJson); if (!packet || packet.version !== VERSION || typeof packet.operation !== 'string') throw new Error('REKALL_WEBGPU_PROTOCOL_INVALID');
            beginScope(); scoped = true;
            if (packet.operation === 'create') create(packet);
            else if (packet.operation === 'destroy') { const resource = object(packet.handle); resource.value?.destroy?.(); maps[packet.handle.kind].delete(key(packet.handle)); }
            else if (packet.operation === 'writeBuffer') device.queue.writeBuffer(object(packet.handle).value, packet.offset, decodeBase64(packet.dataBase64));
            else if (packet.operation === 'writeTexture') {
                const resource = object(packet.handle); const descriptor = resource.descriptor; const data = decodeBase64(packet.dataBase64);
                const bytesPerPixel = descriptor.format === 'r8Unorm' ? 1 : descriptor.format === 'rg8Unorm' ? 2 : descriptor.format === 'rgba16Float' ? 8 : 4;
                const width = Math.max(1, descriptor.width >> packet.mipLevel); const height = descriptor.dimension === 'texture1D' ? 1 : Math.max(1, descriptor.height >> packet.mipLevel);
                const depth = packet.depth ?? 1;
                if (!Number.isInteger(packet.mipLevel) || packet.mipLevel < 0 || !Number.isInteger(packet.arrayLayer) || packet.arrayLayer < 0 || !Number.isInteger(depth) || depth < 1) throw new Error('REKALL_WEBGPU_TEXTURE_UPLOAD_INVALID');
                device.queue.writeTexture({ texture: resource.value, mipLevel: packet.mipLevel, origin: { x: 0, y: 0, z: packet.arrayLayer } }, data, { bytesPerRow: width * bytesPerPixel, rowsPerImage: height }, { width, height, depthOrArrayLayers: depth });
            }
            else if (packet.operation === 'importCanvasOutput') { if (enumValue(textureFormats, packet.format, 'TEXTURE_FORMAT') !== canvasFormat) throw new Error('REKALL_WEBGPU_CANVAS_FORMAT_MISMATCH'); store(packet.texture, { canvasOutput: true, descriptor: { format: packet.format, width: packet.width, height: packet.height } }); store(packet.renderTarget, { descriptor: { colorAttachments: [{ texture: packet.texture }], depthStencilAttachment: null, width: packet.width, height: packet.height, label: packet.label } }); }
            else if (packet.operation === 'submit') executeSubmission(packet);
            else throw new Error('REKALL_WEBGPU_OPERATION_INVALID');
            endScope(); scoped = false;
            const diagnostics = pendingDiagnostics.splice(0); if (pendingOverflow) diagnostics.push(diagnostic('REKALL_WEBGPU_PENDING_OVERFLOW', 'WebGPU pending diagnostics or completion work exceeded the bounded limit.'));
            return result(diagnostics.length === 0, diagnostics);
        } catch (error) { if (scoped) endScope(); const code = error?.message?.startsWith('REKALL_WEBGPU_') ? error.message : 'REKALL_WEBGPU_EXECUTION_FAILED'; return result(false, [diagnostic(code, 'The WebGPU executor rejected an AGE protocol packet.', error?.name)]); }
    }
    async function flush() {
        try { await Promise.all([...pendingScopes.splice(0), ...pendingCompilations.splice(0)]); await device?.queue?.onSubmittedWorkDone?.(); const diagnostics = pendingDiagnostics.splice(0); if (pendingOverflow) diagnostics.push(diagnostic('REKALL_WEBGPU_PENDING_OVERFLOW', 'WebGPU pending diagnostics or completion work exceeded the bounded limit.')); pendingOverflow = false; return result(!lost && diagnostics.length === 0, diagnostics); }
        catch (error) { return result(false, [diagnostic('REKALL_WEBGPU_FLUSH_FAILED', 'WebGPU queue completion failed.', error?.name)]); }
    }
    return { initialize, execute, flush };
}
