import assert from 'node:assert/strict';
import test from 'node:test';
import { createFrameLoop } from '../wwwroot/web-player-loop.js';

function fakeRaf() {
    const queue = [];
    const requestAnimationFrame = callback => {
        queue.push(callback);
        return queue.length;
    };
    const cancelAnimationFrame = handle => {
        queue[handle - 1] = null;
    };
    const tick = timestampMs => {
        const callbacks = queue.splice(0);
        for (const callback of callbacks) {
            callback?.(timestampMs);
        }
    };
    return { requestAnimationFrame, cancelAnimationFrame, tick, pendingCount: () => queue.filter(Boolean).length };
}

test('reports zero elapsed seconds on the first frame after start', () => {
    const raf = fakeRaf();
    const ticks = [];
    const loop = createFrameLoop({ ...raf, onTick: seconds => ticks.push(seconds) });

    loop.start();
    raf.tick(1000);

    assert.deepEqual(ticks, [0]);
});

test('reports the real elapsed time between two frame timestamps', () => {
    const raf = fakeRaf();
    const ticks = [];
    const loop = createFrameLoop({ ...raf, onTick: seconds => ticks.push(seconds) });

    loop.start();
    raf.tick(1000);
    raf.tick(1016.6666);

    assert.equal(ticks[0], 0);
    assert.ok(Math.abs(ticks[1] - (16.6666 / 1000)) < 0.0001);
});

test('clamps an oversized frame gap (a backgrounded tab) instead of forwarding it uncapped', () => {
    const raf = fakeRaf();
    const ticks = [];
    const loop = createFrameLoop({ ...raf, onTick: seconds => ticks.push(seconds) });

    loop.start();
    raf.tick(0);
    raf.tick(120_000); // two full minutes later

    assert.equal(ticks[1], 1); // clamped to MAX_FRAME_SECONDS
});

test('still calls onTick with zero elapsed seconds while paused instead of stopping the loop', () => {
    const raf = fakeRaf();
    const ticks = [];
    const loop = createFrameLoop({ ...raf, onTick: seconds => ticks.push(seconds) });

    loop.start();
    raf.tick(0);
    loop.pause();
    raf.tick(16);
    raf.tick(32);

    assert.deepEqual(ticks, [0, 0, 0]);
    assert.equal(loop.paused, true);
    assert.equal(loop.running, true);
});

test('resume reports zero elapsed seconds for the first frame instead of the paused duration', () => {
    const raf = fakeRaf();
    const ticks = [];
    const loop = createFrameLoop({ ...raf, onTick: seconds => ticks.push(seconds) });

    loop.start();
    raf.tick(0);
    loop.pause();
    raf.tick(5000); // five seconds "pass" while paused
    loop.resume();
    raf.tick(5016);

    assert.equal(ticks.at(-1), 0);
});

test('stop cancels the pending animation frame so no further ticks are delivered', () => {
    const raf = fakeRaf();
    const ticks = [];
    const loop = createFrameLoop({ ...raf, onTick: seconds => ticks.push(seconds) });

    loop.start();
    raf.tick(0);
    loop.stop();

    assert.equal(raf.pendingCount(), 0);
    assert.equal(loop.running, false);
});

test('start after stop begins a fresh timing baseline rather than reusing the old timestamp', () => {
    const raf = fakeRaf();
    const ticks = [];
    const loop = createFrameLoop({ ...raf, onTick: seconds => ticks.push(seconds) });

    loop.start();
    raf.tick(0);
    raf.tick(1000);
    loop.stop();
    loop.start();
    raf.tick(50_000);

    assert.equal(ticks.at(-1), 0);
});

test('a resize between ticks does not corrupt frame timing since it carries no timing state', () => {
    const raf = fakeRaf();
    const ticks = [];
    const loop = createFrameLoop({ ...raf, onTick: seconds => ticks.push(seconds) });

    loop.start();
    raf.tick(0);
    // A resize is handled entirely outside this module (canvas + viewport size only); simulate it by simply
    // continuing the frame sequence and confirming elapsed-time math is unaffected.
    raf.tick(16);

    assert.ok(ticks[1] > 0 && ticks[1] < 1);
});

test('setOnTick swaps the active handler for subsequent frames', () => {
    const raf = fakeRaf();
    const first = [];
    const second = [];
    const loop = createFrameLoop({ ...raf, onTick: seconds => first.push(seconds) });

    loop.start();
    raf.tick(0);
    loop.setOnTick(seconds => second.push(seconds));
    raf.tick(16);

    assert.equal(first.length, 1);
    assert.equal(second.length, 1);
});

test('throws a stable diagnostic code when no requestAnimationFrame implementation is available', () => {
    assert.throws(() => createFrameLoop({ requestAnimationFrame: undefined, cancelAnimationFrame: undefined }), /REKALL_WEB_FRAME_LOOP_NO_RAF/);
});
