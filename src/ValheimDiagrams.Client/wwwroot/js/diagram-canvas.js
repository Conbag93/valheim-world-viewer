// Browser glue for DiagramCanvas. Pointer input is handled here and only reaches .NET when
// something changes: a pan step, a zoom, a different shape under the pointer, or a click.
// The cursor read-out is written straight to the DOM, so plain mouse movement costs no .NET work.
// Height slicing follows Minutor: Shift+wheel moves the cut a step, [ and ] jump a storey.
// On touch screens one finger pans and two fingers pinch to zoom.
const canvases = new WeakMap();

const DRAG_THRESHOLD = 4; // px of movement before a press becomes a pan rather than a click
const ZOOM_STEP = 1.15;

export function init(el, dotnet, readout) {
    const s = { drag: null, moved: 0, hover: null, pointers: new Map(), pinch: null };

    // Distance and midpoint (relative to the canvas) of the first two touch points.
    const pinchOf = () => {
        const [a, b] = [...s.pointers.values()];
        const r = el.getBoundingClientRect();
        return {
            dist: Math.hypot(a.x - b.x, a.y - b.y) || 1,
            x: (a.x + b.x) / 2 - r.left,
            y: (a.y + b.y) / 2 - r.top,
        };
    };

    const shapeId = target => {
        const g = target.closest?.('[data-id]');
        return g && el.contains(g) ? Number(g.dataset.id) : null;
    };

    const setHover = id => {
        if (id === s.hover) return;
        s.hover = id;
        dotnet.invokeMethod('OnShapeHover', id);
    };

    // Diagram coordinates: SVG user units are metres with y flipped (svg y = -north).
    const showCursor = e => {
        if (!readout) return;
        const ctm = el.getScreenCTM();
        if (!ctm) return;
        const p = new DOMPoint(e.clientX, e.clientY).matrixTransform(ctm.inverse());
        readout.textContent = `x ${p.x.toFixed(1)} m · y ${(-p.y).toFixed(1)} m`;
    };

    const handlers = {
        wheel: e => {
            e.preventDefault(); // non-passive so the page doesn't scroll
            if (e.shiftKey) {
                // Browsers often turn Shift+wheel into horizontal scroll, so read either axis.
                const d = e.deltaY || e.deltaX;
                if (d) dotnet.invokeMethod('OnDepth', d < 0 ? 1 : -1);
                return;
            }
            const r = el.getBoundingClientRect();
            dotnet.invokeMethod('OnZoom', e.clientX - r.left, e.clientY - r.top, e.deltaY < 0 ? ZOOM_STEP : 1 / ZOOM_STEP);
        },
        pointerdown: e => {
            if (e.button !== 0) return;
            s.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
            if (s.pointers.size === 2) {
                // A second finger turns the pan into a pinch; it never ends in a click.
                for (const id of s.pointers.keys()) try { el.setPointerCapture(id); } catch { }
                s.pinch = pinchOf();
                s.drag = null;
                s.moved = DRAG_THRESHOLD + 1;
                setHover(null);
                return;
            }
            if (s.pointers.size > 2) return;
            s.drag = { x: e.clientX, y: e.clientY, id: e.pointerId };
            s.moved = 0;
        },
        pointermove: e => {
            showCursor(e);
            if (s.pointers.has(e.pointerId)) s.pointers.set(e.pointerId, { x: e.clientX, y: e.clientY });
            if (s.pinch && s.pointers.size >= 2) {
                const p = pinchOf(), was = s.pinch;
                s.pinch = p;
                if (p.x !== was.x || p.y !== was.y) dotnet.invokeMethod('OnPan', p.x - was.x, p.y - was.y);
                if (p.dist !== was.dist) dotnet.invokeMethod('OnZoom', p.x, p.y, p.dist / was.dist);
                return;
            }
            if (!s.drag) return;
            const dx = e.clientX - s.drag.x, dy = e.clientY - s.drag.y;
            if (dx === 0 && dy === 0) return;
            // Capture only once it's clearly a drag; capturing on press would retarget the click.
            if (s.moved <= DRAG_THRESHOLD && s.moved + Math.abs(dx) + Math.abs(dy) > DRAG_THRESHOLD) {
                try { el.setPointerCapture(s.drag.id); } catch { }
                el.classList.add('dragging');
                setHover(null);
            }
            s.moved += Math.abs(dx) + Math.abs(dy);
            s.drag.x = e.clientX;
            s.drag.y = e.clientY;
            dotnet.invokeMethod('OnPan', dx, dy);
        },
        pointerup: e => {
            s.pointers.delete(e.pointerId);
            if (s.pinch) {
                // Lifting one finger of a pinch carries on as a pan with the other.
                s.pinch = null;
                const [id, p] = [...s.pointers.entries()][0] ?? [];
                s.drag = id === undefined ? null : { x: p.x, y: p.y, id };
                if (!s.drag) el.classList.remove('dragging');
                return;
            }
            s.drag = null;
            el.classList.remove('dragging');
        },
        pointerover: e => {
            if (!s.drag || s.moved <= DRAG_THRESHOLD) setHover(shapeId(e.target));
        },
        pointerleave: () => {
            setHover(null);
            if (readout) readout.textContent = '';
        },
        click: e => {
            if (s.moved > DRAG_THRESHOLD) return; // the click ended a pan, not a selection
            dotnet.invokeMethod('OnShapeClick', shapeId(e.target));
        },
    };
    handlers.pointercancel = handlers.pointerup;

    const onKey = e => {
        if (e.ctrlKey || e.metaKey || e.altKey || e.target.closest?.('input, select, textarea, [contenteditable]')) return;
        if (e.key === ']') dotnet.invokeMethod('OnDepth', 2);
        else if (e.key === '[') dotnet.invokeMethod('OnDepth', -2);
        else return;
        e.preventDefault();
    };
    document.addEventListener('keydown', onKey);

    for (const [type, fn] of Object.entries(handlers))
        el.addEventListener(type, fn, type === 'wheel' ? { passive: false } : undefined);

    const ro = new ResizeObserver(entries => {
        const r = entries[0].contentRect;
        dotnet.invokeMethodAsync('OnResize', r.width, r.height);
    });
    ro.observe(el);
    canvases.set(el, { ro, handlers, onKey });

    const r = el.getBoundingClientRect();
    return [r.width, r.height];
}

// Height slice: hide shapes above the ceiling (or below the floor) and darken those under the
// current storey. Works on the shapes' data-y; the darkened fill goes in an inline style so it
// sits on top of the fill attribute Blazor manages. ceiling = null clears the slice.
export function applySlice(el, ceiling, floor, shadeFrom, shadeDepth, maxShade) {
    for (const g of el.querySelectorAll('g.el[data-y]')) {
        const y = parseFloat(g.dataset.y);
        const hidden = ceiling !== null && (y > ceiling || (floor !== null && y < floor));
        g.style.display = hidden ? 'none' : '';
        const shade = ceiling === null || hidden || y >= shadeFrom ? 0 : Math.min((shadeFrom - y) / shadeDepth, 1) * maxShade;
        for (const shape of g.children) {
            const fill = shape.getAttribute('fill');
            shape.style.fill = shade > 0 && fill?.startsWith('#') ? darken(fill, shade) : '';
        }
    }
}

function darken(hex, amount) {
    const n = parseInt(hex.slice(1, 7), 16);
    const c = v => Math.round(v * (1 - amount)).toString(16).padStart(2, '0');
    return `#${c(n >> 16 & 255)}${c(n >> 8 & 255)}${c(n & 255)}`;
}

export function dispose(el) {
    const c = canvases.get(el);
    if (!c) return;
    c.ro.disconnect();
    document.removeEventListener('keydown', c.onKey);
    for (const [type, fn] of Object.entries(c.handlers)) el.removeEventListener(type, fn);
    canvases.delete(el);
}
