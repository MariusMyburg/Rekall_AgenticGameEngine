const MAX_EVIDENCE_BYTES = 256 * 1024;
const fields = ['backend', 'protocolVersion', 'workloadId', 'submittedFrames', 'diagnostics', 'pixelProof'];

export function publishWebGpuEvidence(json, target = globalThis) {
    if (typeof json !== 'string' || new TextEncoder().encode(json).byteLength > MAX_EVIDENCE_BYTES) throw new Error('REKALL_WEBGPU_EVIDENCE_TOO_LARGE');
    let value;
    try { value = JSON.parse(json); } catch { throw new Error('REKALL_WEBGPU_EVIDENCE_INVALID'); }
    if (!value || typeof value !== 'object' || typeof value.backend !== 'string' || !Number.isInteger(value.protocolVersion)
        || typeof value.workloadId !== 'string' || !Number.isInteger(value.submittedFrames) || value.submittedFrames < 0
        || !Array.isArray(value.diagnostics) || value.diagnostics.length > 64
        || !value.diagnostics.every(item => item && typeof item.code === 'string' && typeof item.message === 'string')
        || value.pixelProof !== null && !validPixelProof(value.pixelProof)) throw new Error('REKALL_WEBGPU_EVIDENCE_INVALID');
    const published = {
        backend: value.backend,
        protocolVersion: value.protocolVersion,
        workloadId: value.workloadId,
        submittedFrames: value.submittedFrames,
        diagnostics: value.diagnostics.map(item => ({ code: item.code, message: item.message, ...(typeof item.target === 'string' ? { target: item.target } : {}) })),
        pixelProof: value.pixelProof === null ? null : sanitizePixelProof(value.pixelProof)
    };
    if (!fields.every((field, index) => Object.keys(published)[index] === field)) throw new Error('REKALL_WEBGPU_EVIDENCE_INVALID');
    target.rekallWebGpuEvidence = published;
    return published;
}

function validPixelProof(proof) {
    return proof && typeof proof === 'object' && typeof proof.passed === 'boolean'
        && Number.isInteger(proof.width) && proof.width > 0 && Number.isInteger(proof.height) && proof.height > 0
        && Number.isInteger(proof.bytesPerRow) && proof.bytesPerRow >= proof.width * 4 && proof.bytesPerRow % 256 === 0
        && proof.samples && ['background', 'cyan', 'blue', 'magenta'].every(name => validSample(proof.samples[name], proof.width, proof.height));
}

function validSample(sample, width, height) {
    return sample && Number.isInteger(sample.x) && sample.x >= 0 && sample.x < width
        && Number.isInteger(sample.y) && sample.y >= 0 && sample.y < height
        && ['r', 'g', 'b', 'a'].every(channel => Number.isInteger(sample[channel]) && sample[channel] >= 0 && sample[channel] <= 255);
}

function sanitizePixelProof(proof) {
    return {
        passed: proof.passed, width: proof.width, height: proof.height, bytesPerRow: proof.bytesPerRow,
        samples: Object.fromEntries(['background', 'cyan', 'blue', 'magenta'].map(name => [name, {
            x: proof.samples[name].x, y: proof.samples[name].y,
            r: proof.samples[name].r, g: proof.samples[name].g, b: proof.samples[name].b, a: proof.samples[name].a
        }]))
    };
}
