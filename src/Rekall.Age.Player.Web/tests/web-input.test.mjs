import assert from 'node:assert/strict';
import test from 'node:test';
import {
    createWebInputBridge,
    deviceLostEvent,
    fullscreenEvent,
    resizeEvent,
    visibilityEvent
} from '../wwwroot/web-input.js';

function fakeCanvas(width = 800, height = 600) {
    const target = new EventTarget();
    target.width = width;
    target.height = height;
    target.getBoundingClientRect = () => ({ left: 0, top: 0, width, height });
    target.setPointerCapture = () => {};
    target.releasePointerCapture = () => {};
    return target;
}

function fakeEnvironment({ width = 800, height = 600, focused = true } = {}) {
    const window = new EventTarget();
    window.innerWidth = width;
    window.innerHeight = height;
    const document = { hasFocus: () => focused };
    const canvas = fakeCanvas(width, height);
    const navigator = { getGamepads: () => [] };
    return { window, document, canvas, navigator };
}

function dispatch(target, type, properties = {}) {
    const event = new Event(type);
    Object.assign(event, properties);
    target.dispatchEvent(event);
}

test('reports currently held keys as raw browser key codes without gameplay mapping', () => {
    const environment = fakeEnvironment();
    const bridge = createWebInputBridge(environment);

    dispatch(environment.window, 'keydown', { code: 'KeyW' });
    dispatch(environment.window, 'keydown', { code: 'Space' });
    const snapshot = bridge.snapshot();

    assert.deepEqual(snapshot.heldKeyCodes.sort(), ['KeyW', 'Space'].sort());
    bridge.dispose();
});

test('removes a key from the held set on keyup', () => {
    const environment = fakeEnvironment();
    const bridge = createWebInputBridge(environment);

    dispatch(environment.window, 'keydown', { code: 'KeyD' });
    dispatch(environment.window, 'keyup', { code: 'KeyD' });
    const snapshot = bridge.snapshot();

    assert.deepEqual(snapshot.heldKeyCodes, []);
    bridge.dispose();
});

test('scales pointer coordinates from CSS pixels to canvas pixels and accumulates deltas', () => {
    const environment = fakeEnvironment({ width: 800, height: 600 });
    environment.canvas.getBoundingClientRect = () => ({ left: 0, top: 0, width: 400, height: 300 });
    const bridge = createWebInputBridge(environment);

    dispatch(environment.canvas, 'pointermove', { clientX: 100, clientY: 60, movementX: 0, movementY: 0 });
    const snapshot = bridge.snapshot();

    // 400 CSS px maps to 800 canvas px: a 2x scale factor.
    assert.equal(snapshot.pointerX, 200);
    assert.equal(snapshot.pointerY, 120);
    bridge.dispose();
});

test('reports wheel delta and resets it to zero after each snapshot (one-shot consumption)', () => {
    const environment = fakeEnvironment();
    const bridge = createWebInputBridge(environment);

    dispatch(environment.canvas, 'wheel', { deltaY: 42 });
    const first = bridge.snapshot();
    const second = bridge.snapshot();

    assert.equal(first.wheelDeltaY, 42);
    assert.equal(second.wheelDeltaY, 0);
    bridge.dispose();
});

test('tracks pointer button hold state across down and up events', () => {
    const environment = fakeEnvironment();
    const bridge = createWebInputBridge(environment);

    dispatch(environment.canvas, 'pointerdown', { button: 0, pointerId: 1 });
    const held = bridge.snapshot();
    dispatch(environment.canvas, 'pointerup', { button: 0, pointerId: 1 });
    const released = bridge.snapshot();

    assert.deepEqual(held.heldPointerButtons, [0]);
    assert.deepEqual(released.heldPointerButtons, []);
    bridge.dispose();
});

test('releases every held key and button when the window loses focus', () => {
    const environment = fakeEnvironment();
    const bridge = createWebInputBridge(environment);

    dispatch(environment.window, 'keydown', { code: 'KeyW' });
    dispatch(environment.canvas, 'pointerdown', { button: 0, pointerId: 1 });
    dispatch(environment.window, 'blur', {});
    const snapshot = bridge.snapshot();

    assert.deepEqual(snapshot.heldKeyCodes, []);
    assert.deepEqual(snapshot.heldPointerButtons, []);
    bridge.dispose();
});

test('reports the exact canvas pixel viewport size', () => {
    const environment = fakeEnvironment({ width: 1280, height: 720 });
    const bridge = createWebInputBridge(environment);

    const snapshot = bridge.snapshot();

    assert.equal(snapshot.viewportWidth, 1280);
    assert.equal(snapshot.viewportHeight, 720);
    bridge.dispose();
});

test('polls raw gamepad identity, axes, and pressed-button facts without device-specific mapping', () => {
    const environment = fakeEnvironment();
    environment.navigator.getGamepads = () => [
        {
            index: 0,
            id: 'Test Pad (Vendor: 0000 Product: 0000)',
            connected: true,
            axes: [0.5, -0.25],
            buttons: [{ pressed: true }, { pressed: false }]
        },
        null
    ];
    const bridge = createWebInputBridge(environment);

    const snapshot = bridge.snapshot();

    assert.equal(snapshot.gamepads.length, 1);
    assert.equal(snapshot.gamepads[0].index, 0);
    assert.deepEqual(snapshot.gamepads[0].axes, [0.5, -0.25]);
    assert.deepEqual(snapshot.gamepads[0].heldButtons, [true, false]);
    bridge.dispose();
});

test('dispose removes every listener so further events do not change state', () => {
    const environment = fakeEnvironment();
    const bridge = createWebInputBridge(environment);
    bridge.dispose();

    dispatch(environment.window, 'keydown', { code: 'KeyW' });
    const snapshot = bridge.snapshot();

    assert.deepEqual(snapshot.heldKeyCodes, []);
});

test('lifecycle helpers report stable structured facts for resize, visibility, fullscreen, and device loss', () => {
    assert.deepEqual(resizeEvent(1920, 1080), { code: 'REKALL_WEB_VIEWPORT_RESIZED', width: 1920, height: 1080 });
    assert.deepEqual(visibilityEvent(false), { code: 'REKALL_WEB_VIEWPORT_HIDDEN', visible: false });
    assert.deepEqual(fullscreenEvent(true), { code: 'REKALL_WEB_FULLSCREEN_ENTERED', fullscreen: true });
    assert.deepEqual(deviceLostEvent('context lost'), { code: 'REKALL_WEB_GPU_DEVICE_LOST', reason: 'context lost' });
});
