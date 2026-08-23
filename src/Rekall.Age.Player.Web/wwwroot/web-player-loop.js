// A thin requestAnimationFrame driver. It owns only browser frame timing, pause/resume, and one clamp on an
// oversized frame gap (a backgrounded tab, a slow first frame) so a single JS-side clamp -- matching
// RekallAgeRuntimeSimulationClockOptions.MaximumAccumulatedSeconds's default -- cannot itself request an unbounded
// catch-up burst from the fixed-step clock. Fixed-step accumulation, catch-up stepping, and per-step input belong to
// RekallAgeRuntimeSimulationClock in C#; this module never simulates or renders anything itself.

const MAX_FRAME_SECONDS = 1;

export function createFrameLoop(environment = {}) {
    const raf = environment.requestAnimationFrame ?? globalThis.requestAnimationFrame?.bind(globalThis);
    const caf = environment.cancelAnimationFrame ?? globalThis.cancelAnimationFrame?.bind(globalThis);
    if (typeof raf !== 'function') {
        throw new Error('REKALL_WEB_FRAME_LOOP_NO_RAF');
    }

    let onTick = environment.onTick ?? (() => {});
    let handle = null;
    let lastTimestamp = null;
    let paused = false;
    let running = false;

    function frame(timestamp) {
        if (!running) {
            return;
        }

        const elapsedSeconds = lastTimestamp === null
            ? 0
            : Math.max(0, Math.min(MAX_FRAME_SECONDS, (timestamp - lastTimestamp) / 1000));
        lastTimestamp = timestamp;
        onTick(paused ? 0 : elapsedSeconds);
        handle = raf(frame);
    }

    function start() {
        if (running) {
            return;
        }

        running = true;
        lastTimestamp = null;
        handle = raf(frame);
    }

    function stop() {
        running = false;
        if (handle !== null) {
            caf?.(handle);
            handle = null;
        }
    }

    function pause() {
        paused = true;
    }

    function resume() {
        // Dropping the last timestamp avoids reporting one giant elapsed delta for the wall-clock time spent
        // paused; the next frame after resume reports elapsedSeconds = 0, same as the very first frame.
        paused = false;
        lastTimestamp = null;
    }

    function setOnTick(handler) {
        onTick = handler;
    }

    return {
        start,
        stop,
        pause,
        resume,
        setOnTick,
        get paused() { return paused; },
        get running() { return running; }
    };
}
