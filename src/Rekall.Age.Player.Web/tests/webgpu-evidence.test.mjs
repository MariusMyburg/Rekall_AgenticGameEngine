import assert from 'node:assert/strict';
import test from 'node:test';
import { publishWebGpuEvidence } from '../wwwroot/webgpu-evidence.js';

test('publishes a sanitized evidence object with the six literal public fields', () => {
    const target = {};
    const input = JSON.stringify({
        backend: 'WebGPU', protocolVersion: 1, workloadId: 'proof.webgpu.asset-independent', submittedFrames: 1,
        diagnostics: [], extra: 'must-not-leak',
        pixelProof: { passed: true, width: 64, height: 64, bytesPerRow: 256, samples: {
            background: { x: 5, y: 5, r: 4, g: 6, b: 10, a: 255 },
            cyan: { x: 17, y: 48, r: 55, g: 190, b: 231, a: 255 },
            blue: { x: 32, y: 20, r: 53, g: 81, b: 247, a: 255 },
            magenta: { x: 46, y: 48, r: 193, g: 56, b: 222, a: 255 }
        } }
    });

    const published = publishWebGpuEvidence(input, target);

    assert.deepEqual(Object.keys(published), ['backend', 'protocolVersion', 'workloadId', 'submittedFrames', 'diagnostics', 'pixelProof']);
    assert.equal(target.rekallWebGpuEvidence, published);
    assert.equal(published.pixelProof.samples.cyan.b, 231);
    assert.equal('extra' in published, false);
});

test('mirrors the sanitized six-field evidence into the DOM for isolated browser automation', () => {
    const mirror = { textContent: '' };
    const target = { document: { querySelector: selector => selector === '#rekall-webgpu-evidence' ? mirror : null } };
    const input = JSON.stringify({
        backend: 'WebGPU', protocolVersion: 1, workloadId: 'proof.webgpu.asset-independent', submittedFrames: 1,
        diagnostics: [], extra: 'must-not-leak',
        pixelProof: { passed: true, width: 64, height: 64, bytesPerRow: 256, samples: {
            background: { x: 5, y: 5, r: 4, g: 6, b: 10, a: 255 },
            cyan: { x: 17, y: 48, r: 55, g: 190, b: 231, a: 255 },
            blue: { x: 32, y: 20, r: 53, g: 81, b: 247, a: 255 },
            magenta: { x: 46, y: 48, r: 193, g: 56, b: 222, a: 255 }
        } }
    });

    const published = publishWebGpuEvidence(input, target);

    assert.equal(mirror.textContent, JSON.stringify(published));
    assert.deepEqual(JSON.parse(mirror.textContent), published);
    assert.deepEqual(Object.keys(JSON.parse(mirror.textContent)), ['backend', 'protocolVersion', 'workloadId', 'submittedFrames', 'diagnostics', 'pixelProof']);
});

test('invalid evidence writes neither the window target nor the DOM mirror', () => {
    const mirror = { textContent: 'unchanged' };
    const target = { document: { querySelector: () => mirror } };

    assert.throws(() => publishWebGpuEvidence('{"backend":"WebGPU"}', target), /REKALL_WEBGPU_EVIDENCE_INVALID/);
    assert.equal('rekallWebGpuEvidence' in target, false);
    assert.equal(mirror.textContent, 'unchanged');
});

test('fails closed instead of publishing malformed or oversized evidence', () => {
    assert.throws(() => publishWebGpuEvidence('{"backend":"WebGPU"}', {}), /REKALL_WEBGPU_EVIDENCE_INVALID/);
    assert.throws(() => publishWebGpuEvidence('x'.repeat(256 * 1024 + 1), {}), /REKALL_WEBGPU_EVIDENCE_TOO_LARGE/);
});
