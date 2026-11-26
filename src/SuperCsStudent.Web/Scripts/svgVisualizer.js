import gsap from "gsap";
import { Flip } from "gsap/Flip";
import { MotionPathPlugin } from "gsap/MotionPathPlugin";
import { DrawSVGPlugin } from "gsap/DrawSVGPlugin";

gsap.registerPlugin(Flip, MotionPathPlugin, DrawSVGPlugin);

const instances = new WeakMap();
const NS = "http://www.w3.org/2000/svg";

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

function edgeColor(state) {
  switch (state) {
    case "active": return "var(--warning)";
    case "accepted":
    case "visited": return "var(--positive)";
    case "rejected":
    case "violation": return "var(--negative)";
    case "frontier": return "var(--accent)";
    default: return "var(--muted)";
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
  const svg = createEl("svg", {
    class: "w-full h-full block select-none touch-none",
    width: "100%",
    height: "100%"
  });

  const defs = createEl("defs");
  defs.appendChild(buildArrowMarker("arrow-default", "var(--muted)"));
  defs.appendChild(buildArrowMarker("arrow-active", "var(--warning)"));
  defs.appendChild(buildArrowMarker("arrow-accepted", "var(--positive)"));
  defs.appendChild(buildArrowMarker("arrow-rejected", "var(--negative)"));
  defs.appendChild(buildArrowMarker("arrow-frontier", "var(--accent)"));
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
    lastScene: null,
    nodePositions: new Map(),
    customOffsets: new Map(),
    draggingNodeId: null
  };

  const applyTransform = () => {
    viewport.setAttribute("transform", `translate(${state.panX},${state.panY}) scale(${state.scale})`);
  };

  svg.addEventListener("wheel", (e) => {
    e.preventDefault();
    const factor = e.deltaY < 0 ? 1.1 : 0.91;
    const rect = svg.getBoundingClientRect();
    const mx = e.clientX - rect.left;
    const my = e.clientY - rect.top;
    const nextScale = Math.min(2.8, Math.max(0.35, state.scale * factor));
    const ratio = nextScale / state.scale;
    state.panX = mx - (mx - state.panX) * ratio;
    state.panY = my - (my - state.panY) * ratio;
    state.scale = nextScale;
    applyTransform();
  }, { passive: false });

  svg.addEventListener("pointerdown", (e) => {
    const nodeGroup = e.target.closest?.("[data-node-id]");
    if (nodeGroup) {
      state.draggingNodeId = nodeGroup.getAttribute("data-node-id");
      state.dragStart = { x: e.clientX, y: e.clientY };
      svg.setPointerCapture(e.pointerId);
      return;
    }
    state.dragging = true;
    state.dragStart = { x: e.clientX - state.panX, y: e.clientY - state.panY };
    svg.setPointerCapture(e.pointerId);
  });

  svg.addEventListener("pointermove", (e) => {
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

  const endDrag = () => {
    state.dragging = false;
    state.draggingNodeId = null;
  };
  svg.addEventListener("pointerup", endDrag);
  svg.addEventListener("pointercancel", endDrag);

  state.applyTransform = applyTransform;
  instances.set(container, state);
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

function markerForState(state) {
  if (state === "active") return "url(#arrow-active)";
  if (state === "accepted" || state === "visited") return "url(#arrow-accepted)";
  if (state === "rejected" || state === "violation") return "url(#arrow-rejected)";
  if (state === "frontier") return "url(#arrow-frontier)";
  return "url(#arrow-default)";
}

export function renderScene(container, scene, snap = false) {
  const st = instances.get(container);
  if (!st || !scene) return;

  const isFirstRender = !st.lastScene;
  const skipMotion = snap || prefersReducedMotion();
  const prevPositions = new Map(st.nodePositions);
  st.lastScene = scene;

  const nodes = scene.nodes || [];
  const edges = scene.edges || [];

  // Resolve current node coordinates (applying any user drag offsets)
  const resolved = new Map();
  for (const n of nodes) {
    const off = st.customOffsets.get(n.id) || { dx: 0, dy: 0 };
    const x = n.x + off.dx;
    const y = n.y + off.dy;
    resolved.set(n.id, { ...n, x, y });
    st.nodePositions.set(n.id, { x, y });
  }

  // Clear layers cleanly
  st.edgesLayer.innerHTML = "";
  st.nodesLayer.innerHTML = "";
  st.effectsLayer.innerHTML = "";

  // Render edges
  const activeEdgePaths = [];
  for (const e of edges) {
    const src = resolved.get(e.sourceId);
    const tgt = resolved.get(e.targetId);
    if (!src || !tgt) continue;

    const { d, labelX, labelY } = computeEdgePath(src, tgt, e.curveOffset || 0);
    const stroke = edgeColor(e.semanticState);
    const isActive = e.semanticState === "active" || e.semanticState === "accepted" || e.semanticState === "frontier";
    const pathEl = createEl("path", {
      d,
      fill: "none",
      stroke,
      "stroke-width": isActive ? "2.3" : "1.5",
      "stroke-dasharray": e.isDashed ? "5 4" : null,
      "marker-end": e.isDirected ? markerForState(e.semanticState) : null,
      opacity: e.isDimmed ? "0.24" : "1"
    });
    st.edgesLayer.appendChild(pathEl);

    if (e.semanticState === "active" && !skipMotion) {
      activeEdgePaths.push({ pathEl, stroke });
    }

    if (e.label) {
      const labelBg = createEl("rect", {
        x: labelX - Math.max(11, e.label.length * 4 + 6),
        y: labelY - 10,
        width: Math.max(22, e.label.length * 8 + 12),
        height: 18,
        rx: 4,
        fill: "var(--canvas-well)",
        opacity: e.isDimmed ? "0.4" : "0.94"
      });
      const labelTxt = createEl("text", {
        x: labelX,
        y: labelY + 3,
        "text-anchor": "middle",
        fill: isActive ? stroke : "var(--text)",
        "font-family": "var(--font-mono)",
        "font-size": "11.5",
        "font-weight": isActive ? "600" : "500",
        opacity: e.isDimmed ? "0.3" : "1"
      });
      labelTxt.textContent = e.label;
      st.edgesLayer.appendChild(labelBg);
      st.edgesLayer.appendChild(labelTxt);
    }
  }

  // Render nodes with smooth GSAP coordinate interpolation from previous positions
  for (const n of resolved.values()) {
    const colors = semanticColors(n.semanticState);
    const g = createEl("g", {
      "data-node-id": n.id,
      class: "cursor-grab active:cursor-grabbing",
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

    drawNodeShape(g, n, colors);

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

  // Animate active edge draw + traveling token pulse via DrawSVGPlugin & MotionPathPlugin
  for (const { pathEl, stroke } of activeEdgePaths) {
    try {
      gsap.fromTo(pathEl, { drawSVG: "0%" }, { drawSVG: "100%", duration: 0.3, ease: "power1.out" });
      const dot = createEl("circle", { r: "4", fill: stroke });
      st.effectsLayer.appendChild(dot);
      gsap.to(dot, {
        duration: 0.36,
        ease: "power1.inOut",
        motionPath: { path: pathEl, align: pathEl, alignOrigin: [0.5, 0.5] },
        onComplete: () => dot.remove()
      });
    } catch {
      // Fallback safely if path length is 0
    }
  }

  if (isFirstRender) {
    fitCanvas(container);
  }
}

function drawNodeShape(g, n, colors) {
  if (n.shape === "sentinel") {
    g.appendChild(createEl("rect", {
      x: -7,
      y: -7,
      width: 14,
      height: 14,
      rx: 2,
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
    const h = 34;
    g.appendChild(createEl("rect", {
      x: -totalW / 2,
      y: -h / 2,
      width: totalW,
      height: h,
      rx: 6,
      fill: colors.fill,
      stroke: colors.stroke,
      "stroke-width": colors.strokeWidth
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
    const rx = n.shape === "pill" ? n.height / 2 : 8;
    g.appendChild(createEl("rect", {
      x: -n.width / 2,
      y: -n.height / 2,
      width: n.width,
      height: n.height,
      rx,
      fill: colors.fill,
      stroke: colors.stroke,
      "stroke-width": colors.strokeWidth
    }));
  } else {
    const r = Math.min(n.width, n.height) / 2;
    g.appendChild(createEl("circle", {
      cx: 0,
      cy: 0,
      r,
      fill: colors.fill,
      stroke: colors.stroke,
      "stroke-width": colors.strokeWidth
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

function computeEdgePath(src, tgt, curveOffset) {
  if (src.id === tgt.id) {
    const r = Math.min(src.width, src.height) / 2;
    const x1 = src.x - r * 0.45;
    const y1 = src.y - r * 0.85;
    const x2 = src.x + r * 0.45;
    const y2 = src.y - r * 0.85;
    const topY = src.y - r - 42;
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
      labelX: (sx + tx) / 2 - uy * 12,
      labelY: (sy + ty) / 2 + ux * 12
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
  const nextScale = Math.min(2.8, Math.max(0.35, st.scale * factor));
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
  const vw = st.lastScene.viewBoxWidth || 680;
  const vh = st.lastScene.viewBoxHeight || 360;
  const scale = Math.min(1.25, Math.min((rect.width - 32) / vw, (rect.height - 32) / vh));
  st.scale = Math.max(0.4, scale);
  st.panX = (rect.width - vw * st.scale) / 2;
  st.panY = (rect.height - vh * st.scale) / 2;
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

export function exportSvgMarkup(container) {
  const st = instances.get(container);
  if (!st) return "";
  const clone = st.svg.cloneNode(true);
  clone.setAttribute("xmlns", NS);
  return new XMLSerializer().serializeToString(clone);
}
