import gsap from "gsap";
import { Flip } from "gsap/Flip";
import { MotionPathPlugin } from "gsap/MotionPathPlugin";
import { DrawSVGPlugin } from "gsap/DrawSVGPlugin";
import { EDGE_PALETTE_SIZE, polylineToRoundedPath, routeOrthogonalEdges } from "./orthoRouter.js";

gsap.registerPlugin(Flip, MotionPathPlugin, DrawSVGPlugin);

const instances = new WeakMap();
const NS = "http://www.w3.org/2000/svg";
let nextCanvasId = 1;

function prefersReducedMotion() {
  return window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
}

function createEl(tag, attrs = {}) {
  const el = document.createElementNS(NS, tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v !== null && v !== undefined) el.setAttribute(k, String(v));
  }
  return el;
}

function semanticColors(state) {
  switch (state) {
    case "active":
      return { fill: "var(--pill-warning-bg)", stroke: "var(--warning)", ink: "var(--text)", strokeWidth: 2.5 };
    case "accepted":
    case "visited":
      return { fill: "var(--pill-positive-bg)", stroke: "var(--positive)", ink: "var(--pill-positive-ink)", strokeWidth: 2 };
    case "rejected":
    case "violation":
      return { fill: "var(--pill-negative-bg)", stroke: "var(--negative)", ink: "var(--pill-negative-ink)", strokeWidth: 2.2 };
    case "frontier":
      return { fill: "var(--frontier-soft)", stroke: "var(--frontier)", ink: "var(--text)", strokeWidth: 2 };
    case "rb-red":
      return { fill: "var(--rb-red-bg)", stroke: "var(--rb-red-bg)", ink: "var(--rb-red-ink)", strokeWidth: 2 };
    case "rb-black":
      return { fill: "var(--rb-black-bg)", stroke: "var(--rb-black-bg)", ink: "var(--rb-black-ink)", strokeWidth: 2 };
    case "double-black":
      return { fill: "var(--rb-black-bg)", stroke: "var(--negative)", ink: "var(--rb-black-ink)", strokeWidth: 3 };
    default:
      return { fill: "var(--surface)", stroke: "var(--control)", ink: "var(--text)", strokeWidth: 1.5 };
  }
}

function edgeColor(state, paletteIdx = null) {
  switch (state) {
    case "active": return "var(--warning)";
    case "accepted":
    case "visited": return "var(--positive)";
    case "rejected":
    case "violation": return "var(--negative)";
    case "frontier": return "var(--accent)";
    default: return paletteIdx !== null ? `var(--edge-c${paletteIdx % EDGE_PALETTE_SIZE})` : "var(--muted)";
  }
}

/**
 * Mounts an interactive pan/zoom SVG viewport owned completely by JS + GSAP
 * so 60fps transitions never clash with Blazor Server DOM diffing.
 */
export function mountSvgCanvas(container, dotNetRef) {
  if (!container) return;
  if (instances.has(container)) return;

  container.innerHTML = "";
  const idPrefix = `sc-${nextCanvasId++}-`;
  const svg = createEl("svg", {
    class: "w-full h-full block select-none touch-none",
    width: "100%",
    height: "100%"
  });

  const defs = createEl("defs");
  const shadow = createEl("filter", {
    id: `${idPrefix}node-elev`,
    x: "-25%",
    y: "-25%",
    width: "150%",
    height: "160%"
  });
  shadow.appendChild(createEl("feDropShadow", {
    dx: "0",
    dy: "2",
    stdDeviation: "3",
    "flood-color": "#0f172a",
    "flood-opacity": "0.10"
  }));
  defs.appendChild(shadow);
  defs.appendChild(buildArrowMarker(`${idPrefix}arrow-default`, "var(--muted)"));
  defs.appendChild(buildArrowMarker(`${idPrefix}arrow-active`, "var(--warning)"));
  defs.appendChild(buildArrowMarker(`${idPrefix}arrow-accepted`, "var(--positive)"));
  defs.appendChild(buildArrowMarker(`${idPrefix}arrow-rejected`, "var(--negative)"));
  defs.appendChild(buildArrowMarker(`${idPrefix}arrow-frontier`, "var(--accent)"));
  defs.appendChild(buildArrowMarker(`${idPrefix}arrow-selected`, "var(--accent)"));
  for (let c = 0; c < EDGE_PALETTE_SIZE; c++) {
    defs.appendChild(buildArrowMarker(`${idPrefix}arrow-c${c}`, `var(--edge-c${c})`));
  }
  svg.appendChild(defs);

  const viewport = createEl("g", { "data-role": "viewport" });
  const edgesLayer = createEl("g", { "data-role": "edges" });
  const nodesLayer = createEl("g", { "data-role": "nodes" });
  const effectsLayer = createEl("g", { "data-role": "effects" });
  viewport.appendChild(edgesLayer);
  viewport.appendChild(nodesLayer);
  viewport.appendChild(effectsLayer);
  svg.appendChild(viewport);
  container.appendChild(svg);

  const state = {
    svg,
    idPrefix,
    viewport,
    edgesLayer,
    nodesLayer,
    effectsLayer,
    dotNetRef,
    panX: 0,
    panY: 0,
    scale: 1,
    dragging: false,
    dragStart: { x: 0, y: 0 },
    pointerDownPos: { x: 0, y: 0 },
    pointerMoved: false,
    downNodeId: null,
    downEdgeId: null,
    connecting: null,
    connectPreviewGroup: null,
    lastScene: null,
    lastOverlayTag: null,
    lastViewBox: null,
    userInteracted: false,
    lastRect: { w: 0, h: 0 },
    nodePositions: new Map(),
    customOffsets: new Map(),
    pendingDropPos: new Map(),
    draggingNodeId: null,
    motionTweens: [],
    options: {
      enableConnectHandles: false,
      enableSelection: false,
      enableDoubleClickAdd: false,
      selectedNodeId: null,
      selectedEdgeId: null,
      isDirected: true
    }
  };

  const applyTransform = () => {
    viewport.setAttribute("transform", `translate(${state.panX},${state.panY}) scale(${state.scale})`);
  };

  const clientToWorld = (clientX, clientY) => {
    const rect = svg.getBoundingClientRect();
    return {
      x: (clientX - rect.left - state.panX) / state.scale,
      y: (clientY - rect.top - state.panY) / state.scale
    };
  };

  const clearConnectPreview = () => {
    if (state.connectPreviewGroup) {
      state.connectPreviewGroup.remove();
      state.connectPreviewGroup = null;
    }
    svg.classList.remove("svg-connecting");
  };

  const drawConnectPreview = () => {
    if (!state.connecting) return;
    if (!state.connectPreviewGroup) {
      state.connectPreviewGroup = createEl("g", { "data-role": "connect-preview", "pointer-events": "none" });
      state.effectsLayer.appendChild(state.connectPreviewGroup);
    }
    state.connectPreviewGroup.innerHTML = "";
    const { startX, startY, curX, curY, targetId } = state.connecting;

    let endX = curX;
    let endY = curY;
    if (targetId && state.lastScene) {
      const targetPos = state.nodePositions.get(targetId);
      const targetNode = (state.lastScene.nodes || []).find((n) => n.id === targetId);
      if (targetPos && targetNode) {
        const tr = Math.min(targetNode.width, targetNode.height) / 2;
        state.connectPreviewGroup.appendChild(createEl("circle", {
          cx: targetPos.x,
          cy: targetPos.y,
          r: tr + 6,
          fill: "none",
          stroke: "var(--accent)",
          "stroke-width": "2.4",
          "stroke-dasharray": "4 3",
          opacity: "0.9"
        }));
        const angle = Math.atan2(targetPos.y - startY, targetPos.x - startX);
        endX = targetPos.x - Math.cos(angle) * (tr + 4);
        endY = targetPos.y - Math.sin(angle) * (tr + 4);
      }
    }

    const midX = (startX + endX) / 2;
    const previewPts = Math.abs(startY - endY) < 4
      ? [{ x: startX, y: startY }, { x: endX, y: endY }]
      : [{ x: startX, y: startY }, { x: midX, y: startY }, { x: midX, y: endY }, { x: endX, y: endY }];

    state.connectPreviewGroup.appendChild(createEl("path", {
      d: polylineToRoundedPath(previewPts, 5),
      fill: "none",
      stroke: "var(--accent)",
      "stroke-width": "2.4",
      "stroke-dasharray": "5 4",
      "stroke-linecap": "round",
      "marker-end": state.options.isDirected ? `url(#${state.idPrefix}arrow-selected)` : null
    }));
  };

  svg.addEventListener("wheel", (e) => {
    e.preventDefault();
    state.userInteracted = true;
    const factor = e.deltaY < 0 ? 1.1 : 0.91;
    const rect = svg.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    const nextScale = Math.min(2.8, Math.max(0.15, state.scale * factor));
    const ratio = nextScale / state.scale;
    state.panX = mx - (mx - state.panX) * ratio;
    state.panY = my - (my - state.panY) * ratio;
    state.scale = nextScale;
    applyTransform();
  }, { passive: false });

  svg.addEventListener("pointerdown", (e) => {
    if (e.button !== 0) return;
    state.pointerDownPos = { x: e.clientX, y: e.clientY };
    state.pointerMoved = false;
    state.downNodeId = null;
    state.downEdgeId = null;

    // 1. Draw.io perimeter connection handle press
    const portEl = e.target.closest?.("[data-connect-port]");
    if (portEl && state.options.enableConnectHandles) {
      e.stopPropagation();
      const fromId = portEl.getAttribute("data-from-node-id");
      const pdx = Number(portEl.getAttribute("data-port-dx") || 0);
      const pdy = Number(portEl.getAttribute("data-port-dy") || 0);
      const fromPos = state.nodePositions.get(fromId);
      if (fromId && fromPos) {
        const wpt = clientToWorld(e.clientX, e.clientY);
        state.connecting = {
          fromId,
          startX: fromPos.x + pdx,
          startY: fromPos.y + pdy,
          curX: wpt.x,
          curY: wpt.y,
          targetId: null,
          movedAway: false
        };
        svg.classList.add("svg-connecting");
        drawConnectPreview();
        svg.setPointerCapture(e.pointerId);
        return;
      }
    }

    // 2. Node body press (click to select or drag to move)
    const nodeGroup = e.target.closest?.("[data-node-id]");
    if (nodeGroup) {
      const nodeId = nodeGroup.getAttribute("data-node-id");
      const isSentinel = nodeGroup.getAttribute("data-node-shape") === "sentinel";
      state.downNodeId = isSentinel ? null : nodeId;
      state.draggingNodeId = nodeId;
      state.dragStart = { x: e.clientX, y: e.clientY };
      svg.setPointerCapture(e.pointerId);
      return;
    }

    // 3. Edge press (click to select when selection enabled)
    const edgeHit = e.target.closest?.("[data-edge-hit]");
    if (edgeHit && state.options.enableSelection) {
      state.downEdgeId = edgeHit.getAttribute("data-edge-hit");
      svg.setPointerCapture(e.pointerId);
      return;
    }

    // 4. Empty canvas pan
    state.dragging = true;
    state.userInteracted = true;
    state.dragStart = { x: e.clientX - state.panX, y: e.clientY - state.panY };
    svg.setPointerCapture(e.pointerId);
  });

  svg.addEventListener("pointermove", (e) => {
    if (Math.hypot(e.clientX - state.pointerDownPos.x, e.clientY - state.pointerDownPos.y) > 4) {
      state.pointerMoved = true;
    }
    if (state.connecting && state.lastScene) {
      const wpt = clientToWorld(e.clientX, e.clientY);
      state.connecting.curX = wpt.x;
      state.connecting.curY = wpt.y;
      if (Math.hypot(wpt.x - state.connecting.startX, wpt.y - state.connecting.startY) > 28) {
        state.connecting.movedAway = true;
      }

      let bestTarget = null;
      let bestDist = Infinity;
      for (const n of state.lastScene.nodes || []) {
        if (n.shape === "sentinel") continue;
        if (n.id === state.connecting.fromId && (!state.connecting.movedAway || !state.options.isDirected)) continue;
        const pos = state.nodePositions.get(n.id);
        if (!pos) continue;
        const hitR = Math.max(n.width, n.height) / 2 + 16;
        const dist = Math.hypot(pos.x - wpt.x, pos.y - wpt.y);
        if (dist <= hitR && dist < bestDist) {
          bestDist = dist;
          bestTarget = n.id;
        }
      }
      state.connecting.targetId = bestTarget;
      drawConnectPreview();
      return;
    }
    if (state.draggingNodeId && state.lastScene) {
      const dx = (e.clientX - state.dragStart.x) / state.scale;
      const dy = (e.clientY - state.dragStart.y) / state.scale;
      state.dragStart = { x: e.clientX, y: e.clientY };
      const prev = state.customOffsets.get(state.draggingNodeId) || { dx: 0, dy: 0 };
      state.customOffsets.set(state.draggingNodeId, { dx: prev.dx + dx, dy: prev.dy + dy });
      renderScene(container, state.lastScene, true);
      return;
    }
    if (!state.dragging) return;
    state.panX = e.clientX - state.dragStart.x;
    state.panY = e.clientY - state.dragStart.y;
    applyTransform();
  });

  const endDrag = (e) => {
    if (state.connecting) {
      const { fromId, targetId } = state.connecting;
      state.connecting = null;
      clearConnectPreview();
      if (e && e.type === "pointerup" && targetId && state.dotNetRef) {
        state.dotNetRef.invokeMethodAsync("HandleEdgeConnected", fromId, targetId).catch(() => {});
      }
      return;
    }

    const wasClick = !state.pointerMoved;
    const clickedNode = state.downNodeId;
    const clickedEdge = state.downEdgeId;
    state.dragging = false;
    state.draggingNodeId = null;
    state.downNodeId = null;
    state.downEdgeId = null;

    if (wasClick && state.dotNetRef && e && e.type === "pointerup") {
      if (clickedNode) {
        if (state.options.enableSelection) {
          state.options.selectedNodeId = clickedNode;
          state.options.selectedEdgeId = null;
          if (state.lastScene) renderScene(container, state.lastScene, true);
          state.dotNetRef.invokeMethodAsync("HandleSelectionChanged", "node", clickedNode).catch(() => {});
        }
        state.dotNetRef.invokeMethodAsync("HandleNodeClick", clickedNode).catch(() => {});
      } else if (clickedEdge) {
        if (state.options.enableSelection) {
          state.options.selectedNodeId = null;
          state.options.selectedEdgeId = clickedEdge;
          if (state.lastScene) renderScene(container, state.lastScene, true);
          state.dotNetRef.invokeMethodAsync("HandleSelectionChanged", "edge", clickedEdge).catch(() => {});
        }
      } else {
        if (state.options.enableSelection && (state.options.selectedNodeId || state.options.selectedEdgeId)) {
          state.options.selectedNodeId = null;
          state.options.selectedEdgeId = null;
          if (state.lastScene) renderScene(container, state.lastScene, true);
          state.dotNetRef.invokeMethodAsync("HandleSelectionChanged", "none", "").catch(() => {});
        }
        const wpt = clientToWorld(e.clientX, e.clientY);
        state.dotNetRef.invokeMethodAsync("HandleCanvasClick", wpt.x, wpt.y).catch(() => {});
      }
    }
  };
  svg.addEventListener("pointerup", endDrag);
  svg.addEventListener("pointercancel", endDrag);

  svg.addEventListener("dblclick", (e) => {
    if (!state.options.enableDoubleClickAdd || !state.dotNetRef) return;
    if (e.target.closest?.("[data-node-id]") || e.target.closest?.("[data-edge-hit]") || e.target.closest?.("[data-connect-port]")) {
      return;
    }
    const wpt = clientToWorld(e.clientX, e.clientY);
    state.dotNetRef.invokeMethodAsync("HandleCanvasDoubleClick", wpt.x, wpt.y).catch(() => {});
  });

  const onKeyDown = (e) => {
    if (!state.options.enableSelection || !state.dotNetRef) return;
    if (e.key !== "Backspace" && e.key !== "Delete") return;
    if (e.metaKey || e.ctrlKey || e.altKey) return;
    if (!container.isConnected || container.offsetParent === null) return;

    const isEditable = (el) => {
      if (!el || el.nodeType !== 1) return false;
      const tag = el.tagName;
      return tag === "INPUT" || tag === "TEXTAREA" || tag === "SELECT" || el.isContentEditable;
    };
    if (isEditable(e.target) || isEditable(document.activeElement)) return;
    if (document.querySelector('[role="dialog"]')) return;

    if (state.options.selectedNodeId) {
      e.preventDefault();
      const id = state.options.selectedNodeId;
      state.options.selectedNodeId = null;
      state.dotNetRef.invokeMethodAsync("HandleDeleteSelected", "node", id).catch(() => {});
    } else if (state.options.selectedEdgeId) {
      e.preventDefault();
      const id = state.options.selectedEdgeId;
      state.options.selectedEdgeId = null;
      state.dotNetRef.invokeMethodAsync("HandleDeleteSelected", "edge", id).catch(() => {});
    }
  };
  window.addEventListener("keydown", onKeyDown);
  state.onKeyDown = onKeyDown;

  state.applyTransform = applyTransform;
  instances.set(container, state);

  if (typeof ResizeObserver !== "undefined") {
    const ro = new ResizeObserver((entries) => {
      const entry = entries[0];
      if (!entry) return;
      const { width, height } = entry.contentRect;
      if (width <= 0 || height <= 0) return;
      if (Math.abs(width - state.lastRect.w) > 4 || Math.abs(height - state.lastRect.h) > 4) {
        state.lastRect = { w: width, h: height };
        if (state.lastScene) {
          fitCanvas(container);
        }
      }
    });
    ro.observe(container);
    state.resizeObserver = ro;
  }
}

function buildArrowMarker(id, color) {
  const marker = createEl("marker", {
    id,
    viewBox: "0 0 10 10",
    refX: "8.5",
    refY: "5",
    markerWidth: "7",
    markerHeight: "7",
    orient: "auto-start-reverse"
  });
  marker.appendChild(createEl("path", { d: "M 0 1.5 L 9 5 L 0 8.5 z", fill: color }));
  return marker;
}

function markerForState(idPrefix, state, paletteIdx = null) {
  if (state === "active") return `url(#${idPrefix}arrow-active)`;
  if (state === "accepted" || state === "visited") return `url(#${idPrefix}arrow-accepted)`;
  if (state === "rejected" || state === "violation") return `url(#${idPrefix}arrow-rejected)`;
  if (state === "frontier") return `url(#${idPrefix}arrow-frontier)`;
  if (paletteIdx !== null) return `url(#${idPrefix}arrow-c${paletteIdx % EDGE_PALETTE_SIZE})`;
  return `url(#${idPrefix}arrow-default)`;
}

export function configureCanvas(container, options = {}) {
  const st = instances.get(container);
  if (!st) return;
  const next = {
    enableConnectHandles: Boolean(options.enableConnectHandles),
    enableSelection: Boolean(options.enableSelection),
    enableDoubleClickAdd: Boolean(options.enableDoubleClickAdd),
    selectedNodeId: options.selectedNodeId || null,
    selectedEdgeId: options.selectedEdgeId || null,
    isDirected: options.isDirected !== undefined ? Boolean(options.isDirected) : true
  };
  const changed =
    st.options.enableConnectHandles !== next.enableConnectHandles ||
    st.options.enableSelection !== next.enableSelection ||
    st.options.enableDoubleClickAdd !== next.enableDoubleClickAdd ||
    st.options.selectedNodeId !== next.selectedNodeId ||
    st.options.selectedEdgeId !== next.selectedEdgeId ||
    st.options.isDirected !== next.isDirected;
  st.options = next;
  if (changed && st.lastScene) {
    renderScene(container, st.lastScene, true);
  }
}

export function seedNodePosition(container, nodeId, x, y) {
  const st = instances.get(container);
  if (!st || !nodeId) return;
  st.pendingDropPos.set(nodeId, { x, y });
}

export function disposeCanvas(container) {
  const st = instances.get(container);
  if (!st) return;
  killMotionTweens(st);
  if (st.onKeyDown) {
    window.removeEventListener("keydown", st.onKeyDown);
  }
  if (st.resizeObserver) {
    st.resizeObserver.disconnect();
  }
  instances.delete(container);
}

function killMotionTweens(st) {
  if (!st.motionTweens || st.motionTweens.length === 0) return;
  for (const tw of st.motionTweens) {
    try { tw.kill(); } catch {}
  }
  st.motionTweens = [];
}

export function renderScene(container, scene, snap = false) {
  const st = instances.get(container);
  if (!st || !scene) return;

  const isFirstRender = !st.lastScene;
  const overlayChanged = st.lastOverlayTag !== (scene.overlayTag || null);
  const vw = scene.viewBoxWidth || 680;
  const vh = scene.viewBoxHeight || 360;
  const viewBoxChanged = !st.lastViewBox || Math.abs(st.lastViewBox.w - vw) > 24 || Math.abs(st.lastViewBox.h - vh) > 24;
  if (overlayChanged) {
    st.userInteracted = false;
    st.customOffsets.clear();
  }
  const skipMotion = snap || prefersReducedMotion();
  const prevPositions = new Map(st.nodePositions);
  st.lastScene = scene;
  st.lastOverlayTag = scene.overlayTag || null;
  st.lastViewBox = { w: vw, h: vh };

  const nodes = scene.nodes || [];
  const edges = scene.edges || [];

  // Resolve current node coordinates (applying any pending double-click drop or user drag offsets)
  const resolved = new Map();
  const activeNodeIds = new Set(nodes.map((n) => n.id));
  for (const oldId of Array.from(st.nodePositions.keys())) {
    if (!activeNodeIds.has(oldId)) {
      st.nodePositions.delete(oldId);
      st.customOffsets.delete(oldId);
    }
  }
  for (const n of nodes) {
    const pending = st.pendingDropPos.get(n.id);
    if (pending) {
      st.customOffsets.set(n.id, { dx: pending.x - n.x, dy: pending.y - n.y });
      st.pendingDropPos.delete(n.id);
    }
    const off = st.customOffsets.get(n.id) || { dx: 0, dy: 0 };
    const x = n.x + off.dx;
    const y = n.y + off.dy;
    resolved.set(n.id, { ...n, x, y });
    st.nodePositions.set(n.id, { x, y });
  }

  // Clear layers cleanly
  killMotionTweens(st);
  st.edgesLayer.innerHTML = "";
  st.edgesLayer.classList.remove("svg-edges-hovering");
  st.nodesLayer.innerHTML = "";
  st.effectsLayer.innerHTML = "";
  st.connectPreviewGroup = null;

  const isTreeScene = scene.routingMode === "tree";
  const orthoRoutes = isTreeScene ? null : routeOrthogonalEdges(resolved, edges, { cornerRadius: 5 });
  const hasAnyActiveEdge = edges.some((e) => e.semanticState === "active");

  // Render edges
  const activeEdgePaths = [];
  for (let idx = 0; idx < edges.length; idx++) {
    const e = edges[idx];
    const src = resolved.get(e.sourceId);
    const tgt = resolved.get(e.targetId);
    if (!src || !tgt) continue;

    const key = e.id ?? `${e.sourceId}->${e.targetId}#${idx}`;
    const edgeId = e.id || key;
    const ortho = orthoRoutes ? orthoRoutes.get(key) : null;
    const { d, labelX, labelY } = ortho || computeEdgePath(src, tgt, e.curveOffset || 0, Boolean(e.isDirected));
    const paletteIdx = (!isTreeScene && ortho) ? ortho.colorIndex : null;
    const isSelectedEdge = st.options.enableSelection && st.options.selectedEdgeId === edgeId;
    const stroke = isSelectedEdge ? "var(--accent)" : edgeColor(e.semanticState, paletteIdx);
    const isActive = isSelectedEdge || e.semanticState === "active" || e.semanticState === "accepted" || e.semanticState === "frontier" || e.semanticState === "rejected" || e.semanticState === "violation";
    const isSteppingActive = e.semanticState === "active";

    const baseOpacity = e.isDimmed
      ? "0.22"
      : (isActive ? "1" : (isTreeScene ? "0.72" : (hasAnyActiveEdge ? "0.52" : "0.90")));

    const edgeGroup = createEl("g", {
      class: `${isTreeScene ? "svg-tree-edge" : "svg-edge-group"}${!isTreeScene && st.options.enableSelection ? " cursor-pointer" : ""}`,
      "data-edge-id": edgeId,
      "data-edge-hit": !isTreeScene ? edgeId : null,
      opacity: baseOpacity
    });

    // Subtle background gap-casing beneath multi-segment orthogonal wires so crossings read clearly
    if (!isTreeScene && ortho && ortho.isMultiSegment && !e.isDimmed) {
      edgeGroup.appendChild(createEl("path", {
        d,
        fill: "none",
        stroke: "var(--canvas-well)",
        "stroke-width": (isSteppingActive || isSelectedEdge) ? "6" : "4.5",
        "stroke-linecap": "round",
        "stroke-linejoin": "round",
        "pointer-events": "none"
      }));
    }

    if (isSelectedEdge) {
      edgeGroup.appendChild(createEl("path", {
        d,
        fill: "none",
        stroke: "var(--accent)",
        "stroke-width": "7.5",
        "stroke-linecap": "round",
        "stroke-linejoin": "round",
        opacity: "0.26",
        "pointer-events": "none"
      }));
    }

    const pathEl = createEl("path", {
      d,
      fill: "none",
      stroke,
      "stroke-width": (isSteppingActive || isSelectedEdge) ? "2.8" : (isActive ? "2.4" : (isTreeScene ? "1.5" : "1.95")),
      "stroke-linecap": "round",
      "stroke-linejoin": "round",
      "stroke-dasharray": e.isDashed ? "5 4" : null,
      "marker-end": e.isDirected
        ? (isSelectedEdge ? `url(#${st.idPrefix}arrow-selected)` : markerForState(st.idPrefix, e.semanticState, paletteIdx))
        : null
    });
    edgeGroup.appendChild(pathEl);

    if (!isTreeScene) {
      // Wide invisible hit path for effortless wire hover isolation & click selection
      const hitEl = createEl("path", {
        d,
        fill: "none",
        stroke: "transparent",
        "stroke-width": "13",
        class: st.options.enableSelection ? "cursor-pointer" : null,
        "data-edge-hit": edgeId
      });
      edgeGroup.appendChild(hitEl);
      edgeGroup.addEventListener("pointerenter", () => {
        st.edgesLayer.classList.add("svg-edges-hovering");
        edgeGroup.classList.add("svg-edge-hovered");
      });
      edgeGroup.addEventListener("pointerleave", () => {
        edgeGroup.classList.remove("svg-edge-hovered");
        if (!st.edgesLayer.querySelector(".svg-edge-hovered")) {
          st.edgesLayer.classList.remove("svg-edges-hovering");
        }
      });
    }

    if (isSteppingActive && !skipMotion) {
      activeEdgePaths.push({ pathEl, stroke });
    }

    if (e.label) {
      const pillW = Math.max(24, e.label.length * 7.2 + 14);
      const useColoredPill = !isTreeScene || isActive;
      const labelBg = createEl("rect", {
        x: labelX - pillW / 2,
        y: labelY - 10,
        width: pillW,
        height: 19,
        rx: 5,
        fill: "var(--surface)",
        stroke: useColoredPill ? stroke : "var(--border)",
        "stroke-width": isActive ? "1.4" : (useColoredPill ? "1.15" : "1"),
        opacity: e.isDimmed ? "0.35" : "0.97"
      });
      const labelTxt = createEl("text", {
        x: labelX,
        y: labelY + 3.2,
        "text-anchor": "middle",
        fill: useColoredPill ? stroke : "var(--text)",
        "font-family": "var(--font-mono)",
        "font-size": "11",
        "font-weight": "600",
        opacity: e.isDimmed ? "0.3" : "1"
      });
      labelTxt.textContent = e.label;
      edgeGroup.appendChild(labelBg);
      edgeGroup.appendChild(labelTxt);
    }

    st.edgesLayer.appendChild(edgeGroup);
  }

  // Render nodes with smooth GSAP coordinate interpolation from previous positions
  for (const n of resolved.values()) {
    const colors = semanticColors(n.semanticState);
    const isSelectedNode = st.options.enableSelection && st.options.selectedNodeId === n.id && n.shape !== "sentinel";
    const g = createEl("g", {
      "data-node-id": n.id,
      "data-node-shape": n.shape || "circle",
      class: `cursor-grab active:cursor-grabbing${isSelectedNode ? " svg-node-selected" : ""}`,
      opacity: n.isDimmed ? "0.26" : "1"
    });

    const prev = prevPositions.get(n.id);
    if (!skipMotion && prev && (Math.abs(prev.x - n.x) > 0.5 || Math.abs(prev.y - n.y) > 0.5)) {
      gsap.set(g, { x: prev.x, y: prev.y });
      gsap.to(g, { x: n.x, y: n.y, duration: 0.34, ease: "power2.out" });
    } else {
      gsap.set(g, { x: n.x, y: n.y });
    }

    // Start caret (>) for automata / TM schemas
    if (n.isStart) {
      const caretX = -(n.width / 2) - 18;
      g.appendChild(createEl("path", {
        d: `M ${caretX - 6} -7 L ${caretX + 5} 0 L ${caretX - 6} 7`,
        fill: "none",
        stroke: "var(--accent)",
        "stroke-width": "2.2",
        "stroke-linecap": "round",
        "stroke-linejoin": "round"
      }));
    }

    drawNodeShape(g, n, colors, st.idPrefix);

    // Selection ring around selected node
    if (isSelectedNode) {
      if (n.shape === "schema-node" || n.shape === "box" || n.shape === "pill" || n.shape === "multi-key") {
        const selW = n.shape === "multi-key" && n.multiKeys?.length
          ? Math.max(n.width, n.multiKeys.length * 34)
          : n.width;
        const selH = n.shape === "multi-key" ? 36 : n.height;
        const rx = n.shape === "pill" ? selH / 2 + 4 : 12;
        g.insertBefore(createEl("rect", {
          x: -selW / 2 - 5,
          y: -selH / 2 - 5,
          width: selW + 10,
          height: selH + 10,
          rx,
          fill: "none",
          stroke: "var(--accent)",
          "stroke-width": "2.4",
          "pointer-events": "none"
        }), g.firstChild);
      } else {
        const r = Math.min(n.width, n.height) / 2;
        g.insertBefore(createEl("circle", {
          cx: 0,
          cy: 0,
          r: r + 6,
          fill: "none",
          stroke: "var(--accent)",
          "stroke-width": "2.4",
          "pointer-events": "none"
        }), g.firstChild);
      }
    }

    // Draw.io perimeter connection handles (visible on hover / selection / active drag)
    if (st.options.enableConnectHandles && n.shape !== "sentinel") {
      const isBox = n.shape === "schema-node" || n.shape === "box" || n.shape === "pill";
      const rx = isBox ? n.width / 2 : Math.min(n.width, n.height) / 2;
      const ry = isBox ? n.height / 2 : Math.min(n.width, n.height) / 2;
      const ports = [
        [0, -ry],
        [rx, 0],
        [0, ry],
        [-rx, 0]
      ];
      for (const [px, py] of ports) {
        g.appendChild(createEl("circle", {
          cx: px,
          cy: py,
          r: "5.2",
          fill: "var(--surface)",
          stroke: "var(--accent)",
          "stroke-width": "1.9",
          class: "svg-connect-port",
          "data-connect-port": "true",
          "data-from-node-id": n.id,
          "data-port-dx": px,
          "data-port-dy": py
        }));
      }
    }

    // Optional corner badge (balance factor, height, (R)/(B), distance D[v])
    if (n.badge) {
      const bx = n.width / 2 - 2;
      const by = -(n.height / 2) - 2;
      const bw = Math.max(20, n.badge.length * 6.5 + 8);
      g.appendChild(createEl("rect", {
        x: bx - bw / 2,
        y: by - 9,
        width: bw,
        height: 17,
        rx: 4,
        fill: n.semanticState === "violation" ? "var(--pill-negative-bg)" : "var(--surface)",
        stroke: n.semanticState === "violation" ? "var(--negative)" : "var(--control)",
        "stroke-width": "1"
      }));
      const bTxt = createEl("text", {
        x: bx,
        y: by + 3,
        "text-anchor": "middle",
        fill: n.semanticState === "violation" ? "var(--pill-negative-ink)" : "var(--muted)",
        "font-family": "var(--font-mono)",
        "font-size": "10",
        "font-weight": "600"
      });
      bTxt.textContent = n.badge;
      g.appendChild(bTxt);
    }

    if (n.subLabel) {
      const sub = createEl("text", {
        x: 0,
        y: n.height / 2 + 15,
        "text-anchor": "middle",
        fill: "var(--muted)",
        "font-family": "var(--font-mono)",
        "font-size": "11"
      });
      sub.textContent = n.subLabel;
      g.appendChild(sub);
    }

    if (n.semanticState === "active" && !skipMotion) {
      gsap.fromTo(g, { scale: 0.94 }, { scale: 1, duration: 0.24, ease: "power2.out", transformOrigin: "0 0" });
    }

    st.nodesLayer.appendChild(g);
  }

  // Animate active edge draw + traveling token pulse (skip when explicit activeMotion drives traversal)
  if (!scene.activeMotion) {
    for (const { pathEl, stroke } of activeEdgePaths) {
      try {
        const drawTw = gsap.fromTo(pathEl, { drawSVG: "0%" }, { drawSVG: "100%", duration: 0.3, ease: "power1.out" });
        st.motionTweens.push(drawTw);
        const dot = createEl("circle", { r: "4", fill: stroke, "pointer-events": "none" });
        st.effectsLayer.appendChild(dot);
        animateDotAlongPath(st, pathEl, dot, 0.36, () => dot.remove());
      } catch {
        // Fallback safely if path length is 0
      }
    }
  }

  // Render explicit node A -> node B traversal motion (recursive tree walk / BFS hop / visit beat)
  if (scene.activeMotion) {
    renderActiveMotion(st, st.effectsLayer, resolved, scene.activeMotion, skipMotion);
  }

  if (isFirstRender || overlayChanged || (!st.userInteracted && viewBoxChanged)) {
    fitCanvas(container);
  }
}

/**
 * Animates an SVG <circle> along a <path> strictly in SVG user-space using native
 * getPointAtLength(). Avoids GSAP MotionPathPlugin align()/getBoundingClientRect()
 * matrix inversion bugs on zero-width vertical or zero-height horizontal paths.
 */
function animateDotAlongPath(st, pathEl, dotEl, duration, onComplete) {
  const totalLen = typeof pathEl.getTotalLength === "function" ? pathEl.getTotalLength() : 0;
  if (!totalLen || !Number.isFinite(totalLen)) {
    if (onComplete) onComplete();
    return;
  }
  const startPt = pathEl.getPointAtLength(0);
  dotEl.setAttribute("cx", String(startPt.x));
  dotEl.setAttribute("cy", String(startPt.y));
  const prog = { t: 0 };
  const tw = gsap.to(prog, {
    t: 1,
    duration,
    ease: "power1.inOut",
    onUpdate: () => {
      const pt = pathEl.getPointAtLength(prog.t * totalLen);
      dotEl.setAttribute("cx", String(pt.x));
      dotEl.setAttribute("cy", String(pt.y));
    },
    onComplete
  });
  st.motionTweens.push(tw);
}

function renderActiveMotion(st, effectsLayer, resolved, motion, skipMotion) {
  const from = resolved.get(motion.fromNodeId);
  const to = resolved.get(motion.toNodeId);
  if (!from || !to) return;

  const color = motion.color || (motion.kind === "visit" ? "var(--warning)" : "var(--accent)");

  if (from.id === to.id) {
    const r = Math.min(to.width, to.height) / 2;
    const pulseRing = createEl("circle", {
      cx: to.x,
      cy: to.y,
      r: r + 5,
      fill: "none",
      stroke: color,
      "stroke-width": "2.4",
      opacity: "0.75",
      "pointer-events": "none"
    });
    const dot = createEl("circle", {
      cx: to.x,
      cy: to.y - r - 3,
      r: "5.2",
      fill: color,
      stroke: "var(--surface)",
      "stroke-width": "1.8",
      "pointer-events": "none"
    });
    effectsLayer.appendChild(pulseRing);
    effectsLayer.appendChild(dot);

    if (!skipMotion) {
      st.motionTweens.push(
        gsap.fromTo(
          pulseRing,
          { attr: { r: r + 1 }, opacity: 0.9 },
          { attr: { r: r + 11 }, opacity: 0, duration: 0.48, ease: "power2.out" }
        ),
        gsap.fromTo(
          dot,
          { attr: { r: 2.6 } },
          { attr: { r: 5.2 }, duration: 0.24, ease: "back.out(2)" }
        )
      );
    }
    return;
  }

  let d;
  let endX;
  let endY;

  if (motion.kind === "hop") {
    const mx = (from.x + to.x) / 2;
    const my = Math.min(from.y, to.y) - 34;
    d = `M ${from.x} ${from.y} Q ${mx} ${my} ${to.x} ${to.y}`;
    endX = to.x;
    endY = to.y;
  } else if (to.y >= from.y) {
    // Descending along real tree S-curve (parent -> child)
    const sy = from.y + from.height / 2;
    const ty = to.y - to.height / 2;
    const midY = (sy + ty) / 2;
    d = `M ${from.x} ${sy} C ${from.x} ${midY}, ${to.x} ${midY}, ${to.x} ${ty}`;
    endX = to.x;
    endY = ty;
  } else {
    // Returning upward along exact same tree S-curve (child -> parent)
    const sy = from.y - from.height / 2;
    const ty = to.y + to.height / 2;
    const midY = (sy + ty) / 2;
    d = `M ${from.x} ${sy} C ${from.x} ${midY}, ${to.x} ${midY}, ${to.x} ${ty}`;
    endX = to.x;
    endY = ty;
  }

  const trail = createEl("path", {
    d,
    fill: "none",
    stroke: color,
    "stroke-width": "2.6",
    "stroke-linecap": "round",
    "stroke-dasharray": "5 4",
    opacity: "0.92",
    "pointer-events": "none"
  });
  const dot = createEl("circle", {
    cx: endX,
    cy: endY,
    r: "5.8",
    fill: color,
    stroke: "var(--surface)",
    "stroke-width": "2",
    "pointer-events": "none"
  });

  effectsLayer.appendChild(trail);
  effectsLayer.appendChild(dot);

  if (!skipMotion) {
    try {
      st.motionTweens.push(
        gsap.fromTo(trail, { opacity: 0.25 }, { opacity: 0.92, duration: 0.28, ease: "power1.out" })
      );
      animateDotAlongPath(st, trail, dot, 0.38);
    } catch {
      // Static fallback already positioned at (endX, endY)
    }
  }
}

function drawNodeShape(g, n, colors, idPrefix = "") {
  const showHalo = n.semanticState === "active" || n.semanticState === "violation" || n.semanticState === "frontier" || n.semanticState === "accepted";

  if (n.shape === "sentinel") {
    g.appendChild(createEl("rect", {
      x: -7,
      y: -7,
      width: 14,
      height: 14,
      rx: 3,
      fill: "var(--rb-black-bg)",
      stroke: "var(--control)",
      "stroke-width": "1"
    }));
    return;
  }

  if (n.shape === "multi-key" && n.multiKeys && n.multiKeys.length > 0) {
    const keys = n.multiKeys;
    const cellW = 34;
    const totalW = Math.max(n.width, keys.length * cellW);
    const h = 36;
    if (showHalo) {
      g.appendChild(createEl("rect", {
        x: -totalW / 2 - 4,
        y: -h / 2 - 4,
        width: totalW + 8,
        height: h + 8,
        rx: 10,
        fill: "none",
        stroke: colors.stroke,
        "stroke-width": "3",
        "stroke-opacity": "0.22"
      }));
    }
    g.appendChild(createEl("rect", {
      x: -totalW / 2,
      y: -h / 2,
      width: totalW,
      height: h,
      rx: 8,
      fill: colors.fill,
      stroke: colors.stroke,
      "stroke-width": colors.strokeWidth,
      filter: `url(#${idPrefix}node-elev)`
    }));
    keys.forEach((k, idx) => {
      const cx = -totalW / 2 + (idx + 0.5) * (totalW / keys.length);
      if (idx > 0) {
        const sepX = -totalW / 2 + idx * (totalW / keys.length);
        g.appendChild(createEl("line", {
          x1: sepX,
          y1: -h / 2,
          x2: sepX,
          y2: h / 2,
          stroke: "var(--border)",
          "stroke-width": "1"
        }));
      }
      const txt = createEl("text", {
        x: cx,
        y: 4.5,
        "text-anchor": "middle",
        fill: colors.ink,
        "font-family": "var(--font-mono)",
        "font-size": "12.5",
        "font-weight": "600"
      });
      txt.textContent = k;
      g.appendChild(txt);
    });
    return;
  }

  if (n.shape === "schema-node" || n.shape === "box" || n.shape === "pill") {
    const rx = n.shape === "pill" ? n.height / 2 : 10;
    if (showHalo) {
      g.appendChild(createEl("rect", {
        x: -n.width / 2 - 4,
        y: -n.height / 2 - 4,
        width: n.width + 8,
        height: n.height + 8,
        rx: rx + 3,
        fill: "none",
        stroke: colors.stroke,
        "stroke-width": "3",
        "stroke-opacity": "0.22"
      }));
    }
    g.appendChild(createEl("rect", {
      x: -n.width / 2,
      y: -n.height / 2,
      width: n.width,
      height: n.height,
      rx,
      fill: colors.fill,
      stroke: colors.stroke,
      "stroke-width": colors.strokeWidth,
      filter: `url(#${idPrefix}node-elev)`
    }));
  } else {
    const r = Math.min(n.width, n.height) / 2;
    if (showHalo) {
      g.appendChild(createEl("circle", {
        cx: 0,
        cy: 0,
        r: r + 5,
        fill: "none",
        stroke: colors.stroke,
        "stroke-width": "3.5",
        "stroke-opacity": "0.22"
      }));
    }
    g.appendChild(createEl("circle", {
      cx: 0,
      cy: 0,
      r,
      fill: colors.fill,
      stroke: colors.stroke,
      "stroke-width": colors.strokeWidth,
      filter: `url(#${idPrefix}node-elev)`
    }));
    if (n.isAccepting || n.shape === "double-circle") {
      g.appendChild(createEl("circle", {
        cx: 0,
        cy: 0,
        r: r - 4.5,
        fill: "none",
        stroke: colors.stroke,
        "stroke-width": "1.5"
      }));
    }
    if (n.semanticState === "double-black") {
      g.appendChild(createEl("circle", {
        cx: 0,
        cy: 0,
        r: r + 4,
        fill: "none",
        stroke: "var(--negative)",
        "stroke-width": "1.75",
        "stroke-dasharray": "4 2"
      }));
    }
  }

  const label = createEl("text", {
    x: 0,
    y: 4.5,
    "text-anchor": "middle",
    fill: colors.ink,
    "font-family": "var(--font-mono)",
    "font-size": n.label.length > 5 ? "11" : "13",
    "font-weight": "600"
  });
  label.textContent = n.label;
  g.appendChild(label);
}

function computeEdgePath(src, tgt, curveOffset, isDirected) {
  if (src.id === tgt.id) {
    const r = Math.min(src.width, src.height) / 2;
    const x1 = src.x - r * 0.45;
    const y1 = src.y - r * 0.85;
    const x2 = src.x + r * 0.45;
    const y2 = src.y - r * 0.85;
    const topY = src.y - r - 44;
    return {
      d: `M ${x1} ${y1} C ${src.x - 36} ${topY}, ${src.x + 36} ${topY}, ${x2} ${y2}`,
      labelX: src.x,
      labelY: topY - 4
    };
  }

  const dx = tgt.x - src.x;
  const dy = tgt.y - src.y;
  const dist = Math.hypot(dx, dy) || 1;
  const ux = dx / dist;
  const uy = dy / dist;

  // Smooth cubic Bezier S-curve for hierarchical tree edges (undirected downward connections)
  if (!isDirected && !curveOffset && dy > 24 && Math.abs(dx) > 4) {
    const sy = src.y + src.height / 2;
    const ty = tgt.y - tgt.height / 2;
    const midY = (sy + ty) / 2;
    return {
      d: `M ${src.x} ${sy} C ${src.x} ${midY}, ${tgt.x} ${midY}, ${tgt.x} ${ty}`,
      labelX: (src.x + tgt.x) / 2,
      labelY: midY
    };
  }

  const srcR = (src.shape === "circle" || src.shape === "double-circle")
    ? Math.min(src.width, src.height) / 2
    : Math.min(src.width, src.height) / 2 + 4;
  const tgtR = (tgt.shape === "circle" || tgt.shape === "double-circle")
    ? Math.min(tgt.width, tgt.height) / 2 + 3
    : Math.min(tgt.width, tgt.height) / 2 + 6;

  const sx = src.x + ux * srcR;
  const sy = src.y + uy * srcR;
  const tx = tgt.x - ux * tgtR;
  const ty = tgt.y - uy * tgtR;

  if (!curveOffset) {
    return {
      d: `M ${sx} ${sy} L ${tx} ${ty}`,
      labelX: (sx + tx) / 2 - uy * 13,
      labelY: (sy + ty) / 2 + ux * 13
    };
  }

  const mx = (sx + tx) / 2 - uy * curveOffset;
  const my = (sy + ty) / 2 + ux * curveOffset;
  return {
    d: `M ${sx} ${sy} Q ${mx} ${my} ${tx} ${ty}`,
    labelX: 0.25 * sx + 0.5 * mx + 0.25 * tx,
    labelY: 0.25 * sy + 0.5 * my + 0.25 * ty - 4
  };
}

export function zoomCanvas(container, factor) {
  const st = instances.get(container);
  if (!st) return;
  const rect = st.svg.getBoundingClientRect();
  const cx = rect.width / 2;
  const cy = rect.height / 2;
  const nextScale = Math.min(2.8, Math.max(0.15, st.scale * factor));
  const ratio = nextScale / st.scale;
  st.panX = cx - (cx - st.panX) * ratio;
  st.panY = cy - (cy - st.panY) * ratio;
  st.scale = nextScale;
  st.applyTransform();
}

export function fitCanvas(container) {
  const st = instances.get(container);
  if (!st || !st.lastScene) return;
  const rect = st.svg.getBoundingClientRect();
  if (rect.width === 0 || rect.height === 0) return;
  st.userInteracted = false;
  st.lastRect = { w: rect.width, h: rect.height };

  let minX = 0;
  let minY = 0;
  let vw = st.lastScene.viewBoxWidth || 680;
  let vh = st.lastScene.viewBoxHeight || 360;

  try {
    const bbox = st.viewport.getBBox();
    if (bbox && bbox.width > 1 && bbox.height > 1) {
      const pad = 28;
      minX = bbox.x - pad;
      minY = bbox.y - pad;
      vw = bbox.width + pad * 2;
      vh = bbox.height + pad * 2;
    }
  } catch {
    // Fallback to scene viewBox when detached or hidden
  }

  const scale = Math.min(1.25, Math.min((rect.width - 28) / vw, (rect.height - 28) / vh));
  st.scale = Math.max(0.15, scale);
  st.panX = (rect.width - vw * st.scale) / 2 - minX * st.scale;
  st.panY = (rect.height - vh * st.scale) / 2 - minY * st.scale;
  st.applyTransform();
}

export function resetNodeOffsets(container) {
  const st = instances.get(container);
  if (!st) return;
  st.customOffsets.clear();
  if (st.lastScene) {
    renderScene(container, st.lastScene, false);
    fitCanvas(container);
  }
}

function buildResolvedSvgClone(st) {
  const rect = st.svg.getBoundingClientRect();
  const width = Math.max(320, Math.round(rect.width || 800));
  const height = Math.max(240, Math.round(rect.height || 500));
  const computed = getComputedStyle(document.documentElement);

  const resolveVars = (val) =>
    val.replace(/var\((--[a-zA-Z0-9-]+)\)/g, (_, name) => {
      const resolved = computed.getPropertyValue(name).trim();
      return resolved || "#131416";
    });

  const clone = st.svg.cloneNode(true);
  clone.querySelectorAll("[data-connect-port], [data-role='connect-preview']").forEach((el) => el.remove());
  clone.setAttribute("xmlns", NS);
  clone.setAttribute("width", String(width));
  clone.setAttribute("height", String(height));
  clone.setAttribute("viewBox", `0 0 ${width} ${height}`);

  const bg = createEl("rect", {
    x: "0",
    y: "0",
    width: String(width),
    height: String(height),
    fill: resolveVars("var(--canvas-well)")
  });
  clone.insertBefore(bg, clone.firstChild);

  const allEls = [clone, ...clone.querySelectorAll("*")];
  for (const el of allEls) {
    for (const attr of Array.from(el.attributes || [])) {
      if (attr.value && attr.value.includes("var(")) {
        el.setAttribute(attr.name, resolveVars(attr.value));
      }
    }
  }

  return { markup: new XMLSerializer().serializeToString(clone), width, height };
}

export function exportSvgMarkup(container) {
  const st = instances.get(container);
  if (!st) return "";
  return buildResolvedSvgClone(st).markup;
}

export function exportPngFile(container, filename = "diagram.png", scale = 2) {
  const st = instances.get(container);
  if (!st) return false;
  const { markup, width, height } = buildResolvedSvgClone(st);
  const svgBlob = new Blob([markup], { type: "image/svg+xml;charset=utf-8" });
  const url = URL.createObjectURL(svgBlob);

  return new Promise((resolve) => {
    const img = new Image();
    img.onload = () => {
      try {
        const canvas = document.createElement("canvas");
        canvas.width = Math.round(width * scale);
        canvas.height = Math.round(height * scale);
        const ctx = canvas.getContext("2d");
        ctx.scale(scale, scale);
        ctx.drawImage(img, 0, 0, width, height);
        URL.revokeObjectURL(url);
        canvas.toBlob((blob) => {
          if (!blob) {
            resolve(false);
            return;
          }
          const dlUrl = URL.createObjectURL(blob);
          const a = document.createElement("a");
          a.href = dlUrl;
          a.download = filename;
          document.body.appendChild(a);
          a.click();
          a.remove();
          setTimeout(() => URL.revokeObjectURL(dlUrl), 1000);
          resolve(true);
        }, "image/png");
      } catch {
        URL.revokeObjectURL(url);
        resolve(false);
      }
    };
    img.onerror = () => {
      URL.revokeObjectURL(url);
      resolve(false);
    };
    img.src = url;
  });
}
