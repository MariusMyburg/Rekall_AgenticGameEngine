// Raw, device-semantic browser input capture. This module reports only what the browser told it -- which keys are
// currently held, where the pointer is, which buttons are down, wheel motion, touch points, viewport size, and
// per-gamepad axis/button state -- as one plain JSON-able snapshot per poll. It never decides what any of this
// means for gameplay; RekallAgeWebInputBridge in C# owns that normalization so the same authored
// Rekall.InputActionMap binds identically on Windows and in the browser.

const MAX_HELD_KEYS = 64;
const MAX_TOUCHES = 16;
const MAX_GAMEPADS = 8;

export function createWebInputBridge(environment = {}) {
    const target = environment.window ?? globalThis;
    const doc = environment.document ?? target.document;
    const canvas = environment.canvas;
    const navigatorRef = environment.navigator ?? target.navigator;

    const heldKeys = new Set();
    const heldButtons = new Set();
    let pointerX = 0;
    let pointerY = 0;
    let accumulatedDeltaX = 0;
    let accumulatedDeltaY = 0;
    let accumulatedWheelDeltaY = 0;
    const touches = new Map();
    const listeners = [];

    function on(source, type, handler, options) {
        source.addEventListener(type, handler, options);
        listeners.push(() => source.removeEventListener(type, handler, options));
    }

    function onKeyDown(event) {
        if (heldKeys.size < MAX_HELD_KEYS) {
            heldKeys.add(event.code);
        }
    }

    function onKeyUp(event) {
        heldKeys.delete(event.code);
    }

    function scaledPointer(event) {
        if (!canvas) {
            return { x: event.clientX, y: event.clientY };
        }

        const bounds = canvas.getBoundingClientRect();
        const scaleX = bounds.width > 0 ? canvas.width / bounds.width : 1;
        const scaleY = bounds.height > 0 ? canvas.height / bounds.height : 1;
        return { x: (event.clientX - bounds.left) * scaleX, y: (event.clientY - bounds.top) * scaleY };
    }

    function onPointerMove(event) {
        const scaled = scaledPointer(event);
        accumulatedDeltaX += event.movementX ?? scaled.x - pointerX;
        accumulatedDeltaY += event.movementY ?? scaled.y - pointerY;
        pointerX = scaled.x;
        pointerY = scaled.y;
    }

    function onPointerDown(event) {
        heldButtons.add(event.button);
        (canvas ?? event.target)?.setPointerCapture?.(event.pointerId);
    }

    function onPointerUp(event) {
        heldButtons.delete(event.button);
        (canvas ?? event.target)?.releasePointerCapture?.(event.pointerId);
    }

    function onWheel(event) {
        accumulatedWheelDeltaY += event.deltaY;
    }

    function onTouchList(list) {
        touches.clear();
        for (const touch of Array.from(list).slice(0, MAX_TOUCHES)) {
            const scaled = scaledPointer(touch);
            touches.set(touch.identifier, { id: touch.identifier, x: scaled.x, y: scaled.y });
        }
    }

    function onTouchEvent(event) {
        onTouchList(event.touches);
    }

    function onFocusLoss() {
        heldKeys.clear();
        heldButtons.clear();
    }

    const pointerSource = canvas ?? target;
    on(target, 'keydown', onKeyDown);
    on(target, 'keyup', onKeyUp);
    on(pointerSource, 'pointermove', onPointerMove);
    on(pointerSource, 'pointerdown', onPointerDown);
    on(pointerSource, 'pointerup', onPointerUp);
    on(pointerSource, 'pointercancel', onPointerUp);
    on(pointerSource, 'wheel', onWheel, { passive: true });
    on(pointerSource, 'touchstart', onTouchEvent);
    on(pointerSource, 'touchmove', onTouchEvent);
    on(pointerSource, 'touchend', onTouchEvent);
    on(pointerSource, 'touchcancel', onTouchEvent);
    on(target, 'blur', onFocusLoss);

    function readGamepads() {
        const source = navigatorRef?.getGamepads ? navigatorRef.getGamepads() : [];
        const result = [];
        for (const pad of Array.from(source ?? []).slice(0, MAX_GAMEPADS)) {
            if (!pad) {
                continue;
            }

            result.push({
                index: pad.index,
                id: pad.id ?? '',
                connected: pad.connected !== false,
                axes: Array.from(pad.axes ?? []),
                heldButtons: Array.from(pad.buttons ?? []).map(button => (button?.pressed ?? button?.value > 0.5) === true)
            });
        }

        return result;
    }

    function snapshot() {
        const value = {
            heldKeyCodes: Array.from(heldKeys),
            pointerX,
            pointerY,
            pointerDeltaX: accumulatedDeltaX,
            pointerDeltaY: accumulatedDeltaY,
            wheelDeltaY: accumulatedWheelDeltaY,
            heldPointerButtons: Array.from(heldButtons),
            viewportWidth: canvas?.width ?? target.innerWidth ?? 0,
            viewportHeight: canvas?.height ?? target.innerHeight ?? 0,
            focused: doc?.hasFocus ? doc.hasFocus() : true,
            gamepads: readGamepads()
        };

        accumulatedDeltaX = 0;
        accumulatedDeltaY = 0;
        accumulatedWheelDeltaY = 0;
        return value;
    }

    function dispose() {
        for (const remove of listeners.splice(0)) {
            remove();
        }

        heldKeys.clear();
        heldButtons.clear();
        touches.clear();
    }

    return { snapshot, dispose };
}

export function resizeEvent(width, height) {
    return { code: 'REKALL_WEB_VIEWPORT_RESIZED', width, height };
}

export function visibilityEvent(visible) {
    return { code: visible ? 'REKALL_WEB_VIEWPORT_VISIBLE' : 'REKALL_WEB_VIEWPORT_HIDDEN', visible };
}

export function fullscreenEvent(fullscreen) {
    return { code: fullscreen ? 'REKALL_WEB_FULLSCREEN_ENTERED' : 'REKALL_WEB_FULLSCREEN_EXITED', fullscreen };
}

export function deviceLostEvent(reason) {
    return { code: 'REKALL_WEB_GPU_DEVICE_LOST', reason: reason ?? 'unknown' };
}
