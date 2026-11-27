export const EDGE_PALETTE_SIZE = 8;

const CORNER_RADIUS = 5;
const OBSTACLE_PAD = 11;
const LANE_SPACING = 17;

/**
 * Greedily assigns each edge an index in [0, EDGE_PALETTE_SIZE) so edges sharing a source
 * or target node get distinct hues whenever possible.
 */
export function assignEdgeColors(edges) {
  const result = new Map();
  const usedByNode = new Map();
  const globalCounts = new Array(EDGE_PALETTE_SIZE).fill(0);

  const getUsed = (nodeId) => {
    let counts = usedByNode.get(nodeId);
    if (!counts) {
      counts = new Array(EDGE_PALETTE_SIZE).fill(0);
      usedByNode.set(nodeId, counts);
    }
    return counts;
  };

  for (let i = 0; i < edges.length; i++) {
    const e = edges[i];
    const key = edgeKey(e, i);
    const srcUsed = getUsed(e.sourceId);
    const tgtUsed = getUsed(e.targetId);

    let bestColor = 0;
    let bestScore = Infinity;
    for (let c = 0; c < EDGE_PALETTE_SIZE; c++) {
      const incidentPenalty = (srcUsed[c] + tgtUsed[c]) * 100;
      const score = incidentPenalty + globalCounts[c] * 3 + c * 0.01;
      if (score < bestScore) {
        bestScore = score;
        bestColor = c;
      }
    }

    result.set(key, bestColor);
    srcUsed[bestColor]++;
    if (e.targetId !== e.sourceId) {
      tgtUsed[bestColor]++;
    }
    globalCounts[bestColor]++;
  }

  return result;
}

function edgeKey(e, idx) {
  return e.id ?? `${e.sourceId}->${e.targetId}#${idx}`;
}

function nodeBox(n) {
  const isCircle = !n.shape || n.shape === "circle" || n.shape === "double-circle";
  const hw = isCircle ? Math.min(n.width, n.height) / 2 : n.width / 2;
  const hh = isCircle ? Math.min(n.width, n.height) / 2 : n.height / 2;
  return {
    cx: n.x,
    cy: n.y,
    hw,
    hh,
    isCircle,
    left: n.x - hw,
    right: n.x + hw,
    top: n.y - hh,
    bottom: n.y + hh
  };
}

function portOnBox(box, side, offset = 0, extraOut = 0) {
  const clamped = box.isCircle
    ? Math.max(-box.hw * 0.58, Math.min(box.hw * 0.58, offset))
    : (side === "N" || side === "S"
      ? Math.max(-(box.hw - 8), Math.min(box.hw - 8, offset))
      : Math.max(-(box.hh - 7), Math.min(box.hh - 7, offset)));

  if (box.isCircle) {
    const r = box.hw;
    const reach = Math.sqrt(Math.max(1, r * r - clamped * clamped)) + extraOut;
    switch (side) {
      case "N": return { x: box.cx + clamped, y: box.cy - reach };
      case "S": return { x: box.cx + clamped, y: box.cy + reach };
      case "W": return { x: box.cx - reach, y: box.cy + clamped };
      default: return { x: box.cx + reach, y: box.cy + clamped };
    }
  }

  switch (side) {
    case "N": return { x: box.cx + clamped, y: box.top - extraOut };
    case "S": return { x: box.cx + clamped, y: box.bottom + extraOut };
    case "W": return { x: box.left - extraOut, y: box.cy + clamped };
    default: return { x: box.right + extraOut, y: box.cy + clamped };
  }
}

function segmentIntersectsRect(a, b, rect) {
  const minX = Math.min(a.x, b.x);
  const maxX = Math.max(a.x, b.x);
  const minY = Math.min(a.y, b.y);
  const maxY = Math.max(a.y, b.y);
  return (
    maxX > rect.left &&
    minX < rect.right &&
    maxY > rect.top &&
    minY < rect.bottom
  );
}

function pathHitsAnyObstacle(points, obstacles) {
  for (let i = 0; i < points.length - 1; i++) {
    const a = points[i];
    const b = points[i + 1];
    for (let j = 0; j < obstacles.length; j++) {
      if (segmentIntersectsRect(a, b, obstacles[j])) return true;
    }
  }
  return false;
}

function simplifyOrthoPoints(rawPoints) {
  const dedup = [];
  for (const p of rawPoints) {
    if (!Number.isFinite(p.x) || !Number.isFinite(p.y)) continue;
    const prev = dedup[dedup.length - 1];
    if (!prev || Math.hypot(p.x - prev.x, p.y - prev.y) > 0.25) {
      dedup.push({ x: Math.round(p.x * 10) / 10, y: Math.round(p.y * 10) / 10 });
    }
  }
  if (dedup.length <= 2) return dedup;

  const clean = [dedup[0]];
  for (let i = 1; i < dedup.length - 1; i++) {
    const a = clean[clean.length - 1];
    const b = dedup[i];
    const c = dedup[i + 1];
    const colinearX = Math.abs(a.x - b.x) < 0.05 && Math.abs(b.x - c.x) < 0.05;
    const colinearY = Math.abs(a.y - b.y) < 0.05 && Math.abs(b.y - c.y) < 0.05;
    const sameDir = (b.x - a.x) * (c.x - b.x) + (b.y - a.y) * (c.y - b.y) > 0;
    if (!(sameDir && (colinearX || colinearY))) {
      clean.push(b);
    }
  }
  clean.push(dedup[dedup.length - 1]);
  return clean;
}

/**
 * Converts an orthogonal polyline into an SVG path string with crisp rounded elbows.
 */
export function polylineToRoundedPath(points, radius = CORNER_RADIUS) {
  const pts = simplifyOrthoPoints(points);
  if (pts.length === 0) return "";
  if (pts.length === 1) return `M ${pts[0].x} ${pts[0].y}`;
  if (pts.length === 2) return `M ${pts[0].x} ${pts[0].y} L ${pts[1].x} ${pts[1].y}`;

  const fmt = (v) => (Math.round(v * 10) / 10);
  const parts = [`M ${fmt(pts[0].x)} ${fmt(pts[0].y)}`];

  for (let i = 1; i < pts.length - 1; i++) {
    const prev = pts[i - 1];
    const cur = pts[i];
    const next = pts[i + 1];

    const dx1 = cur.x - prev.x;
    const dy1 = cur.y - prev.y;
    const len1 = Math.hypot(dx1, dy1);
    const dx2 = next.x - cur.x;
    const dy2 = next.y - cur.y;
    const len2 = Math.hypot(dx2, dy2);

    if (len1 < 0.5 || len2 < 0.5) continue;

    const r = Math.min(radius, len1 / 2, len2 / 2);
    if (r < 0.5) {
      parts.push(`L ${fmt(cur.x)} ${fmt(cur.y)}`);
      continue;
    }

    const bx = cur.x - (dx1 / len1) * r;
    const by = cur.y - (dy1 / len1) * r;
    const ax = cur.x + (dx2 / len2) * r;
    const ay = cur.y + (dy2 / len2) * r;
    parts.push(`L ${fmt(bx)} ${fmt(by)} Q ${fmt(cur.x)} ${fmt(cur.y)} ${fmt(ax)} ${fmt(ay)}`);
  }

  const last = pts[pts.length - 1];
  parts.push(`L ${fmt(last.x)} ${fmt(last.y)}`);
  return parts.join(" ");
}

function trackOverlaps(usedTracks, coord, spanMin, spanMax, minGap = LANE_SPACING - 2) {
  for (const t of usedTracks) {
    if (Math.abs(t.coord - coord) < minGap) {
      if (Math.min(spanMax, t.max) - Math.max(spanMin, t.min) > -8) {
        return true;
      }
    }
  }
  return false;
}

function allocateHorizontalLane(baseY, direction, spanMin, spanMax, obstacles, usedHoriz) {
  const step = direction >= 0 ? LANE_SPACING : -LANE_SPACING;
  for (let k = 0; k < 24; k++) {
    const y = baseY + k * step;
    if (trackOverlaps(usedHoriz, y, spanMin, spanMax)) continue;
    if (!pathHitsAnyObstacle([{ x: spanMin, y }, { x: spanMax, y }], obstacles)) {
      usedHoriz.push({ coord: y, min: spanMin, max: spanMax });
      return y;
    }
  }
  const fallbackY = baseY + (direction >= 0 ? 1 : -1) * LANE_SPACING * 4;
  usedHoriz.push({ coord: fallbackY, min: spanMin, max: spanMax });
  return fallbackY;
}

function allocateVerticalLane(baseX, direction, spanMin, spanMax, obstacles, usedVert) {
  const step = direction >= 0 ? LANE_SPACING : -LANE_SPACING;
  for (let k = 0; k < 24; k++) {
    const x = baseX + k * step;
    if (trackOverlaps(usedVert, x, spanMin, spanMax)) continue;
    if (!pathHitsAnyObstacle([{ x, y: spanMin }, { x, y: spanMax }], obstacles)) {
      usedVert.push({ coord: x, min: spanMin, max: spanMax });
      return x;
    }
  }
  const fallbackX = baseX + (direction >= 0 ? 1 : -1) * LANE_SPACING * 4;
  usedVert.push({ coord: fallbackX, min: spanMin, max: spanMax });
  return fallbackX;
}

function buildObstacleList(allBoxes, excludeSrcId, excludeTgtId, pad = OBSTACLE_PAD) {
  const list = [];
  for (const [id, b] of allBoxes.entries()) {
    if (id === excludeSrcId || id === excludeTgtId) continue;
    list.push({
      id,
      left: b.left - pad,
      right: b.right + pad,
      top: b.top - pad,
      bottom: b.bottom + pad
    });
  }
  return list;
}

function buildStrictObstacleList(allBoxes, excludeSrcId, excludeTgtId) {
  return buildObstacleList(allBoxes, excludeSrcId, excludeTgtId, 4);
}

function selectFreeLabelPosition(points, label, allBoxes, placedLabels) {
  if (!label || points.length < 2) {
    const mid = points[Math.floor(points.length / 2)] || { x: 0, y: 0 };
    return { labelX: mid.x, labelY: mid.y };
  }

  const pillW = Math.max(24, label.length * 7.2 + 14);
  const pillH = 19;

  // Sort segments by length descending so labels sit on the cleanest straight run
  const segments = [];
  for (let i = 0; i < points.length - 1; i++) {
    const a = points[i];
    const b = points[i + 1];
    const len = Math.hypot(b.x - a.x, b.y - a.y);
    if (len >= 12) {
      segments.push({ a, b, len, isHoriz: Math.abs(a.y - b.y) < 0.1 });
    }
  }
  segments.sort((u, v) => v.len - u.len);

  const collides = (cx, cy) => {
    const rect = {
      left: cx - pillW / 2 - 2,
      right: cx + pillW / 2 + 2,
      top: cy - pillH / 2 - 2,
      bottom: cy + pillH / 2 + 2
    };
    for (const box of allBoxes.values()) {
      if (rect.right > box.left - 3 && rect.left < box.right + 3 && rect.bottom > box.top - 3 && rect.top < box.bottom + 3) {
        return true;
      }
    }
    for (const other of placedLabels) {
      if (rect.right > other.left && rect.left < other.right && rect.bottom > other.top && rect.top < other.bottom) {
        return true;
      }
    }
    return false;
  };

  const offsets = [0, 22, -22, 44, -44, 68, -68, 92, -92];
  for (const seg of segments) {
    const mx = (seg.a.x + seg.b.x) / 2;
    const my = (seg.a.y + seg.b.y) / 2;
    const maxSlide = Math.max(0, seg.len / 2 - 14);
    for (const off of offsets) {
      if (Math.abs(off) > maxSlide && off !== 0) continue;
      const cx = seg.isHoriz ? mx + off : mx;
      const cy = seg.isHoriz ? my : my + off;
      if (!collides(cx, cy)) {
        placedLabels.push({
          left: cx - pillW / 2 - 2,
          right: cx + pillW / 2 + 2,
          top: cy - pillH / 2 - 2,
          bottom: cy + pillH / 2 + 2
        });
        return { labelX: Math.round(cx * 10) / 10, labelY: Math.round(cy * 10) / 10 };
      }
    }
  }

  // Fallback: midpoint of longest segment with slight perpendicular nudge if overlapping
  const best = segments[0] || { a: points[0], b: points[points.length - 1], isHoriz: true };
  let cx = (best.a.x + best.b.x) / 2;
  let cy = (best.a.y + best.b.y) / 2;
  for (let shift = 1; shift <= 4; shift++) {
    const tryY = best.isHoriz ? cy - shift * 18 : cy;
    const tryX = best.isHoriz ? cx : cx + shift * 22;
    if (!collides(tryX, tryY)) {
      cx = tryX;
      cy = tryY;
      break;
    }
  }
  placedLabels.push({
    left: cx - pillW / 2 - 2,
    right: cx + pillW / 2 + 2,
    top: cy - pillH / 2 - 2,
    bottom: cy + pillH / 2 + 2
  });
  return { labelX: Math.round(cx * 10) / 10, labelY: Math.round(cy * 10) / 10 };
}

function borderAnchorToward(node, targetPt, extraOut = 0) {
  const box = nodeBox(node);
  const dx = targetPt.x - box.cx;
  const dy = targetPt.y - box.cy;
  const dist = Math.hypot(dx, dy);
  if (dist < 0.001) {
    return { x: box.cx + box.hw + extraOut, y: box.cy };
  }
  const ux = dx / dist;
  const uy = dy / dist;

  if (box.isCircle) {
    const r = box.hw + extraOut;
    return { x: box.cx + ux * r, y: box.cy + uy * r };
  }

  const hw = box.hw + extraOut;
  const hh = box.hh + extraOut;
  const tx = Math.abs(ux) > 1e-6 ? hw / Math.abs(ux) : Infinity;
  const ty = Math.abs(uy) > 1e-6 ? hh / Math.abs(uy) : Infinity;
  const t = Math.min(tx, ty);
  return { x: box.cx + ux * t, y: box.cy + uy * t };
}

function buildMidpoints(pts) {
  const mids = [];
  for (let i = 0; i < pts.length - 1; i++) {
    mids.push({
      x: Math.round(((pts[i].x + pts[i + 1].x) / 2) * 10) / 10,
      y: Math.round(((pts[i].y + pts[i + 1].y) / 2) * 10) / 10,
      insertIndex: i
    });
  }
  return mids;
}

function longestSegmentMidpoint(pts) {
  let bestLen = -1;
  let best = { x: pts[0]?.x || 0, y: pts[0]?.y || 0 };
  for (let i = 0; i < pts.length - 1; i++) {
    const a = pts[i];
    const b = pts[i + 1];
    const len = Math.hypot(b.x - a.x, b.y - a.y);
    if (len > bestLen) {
      bestLen = len;
      best = {
        x: Math.round(((a.x + b.x) / 2) * 10) / 10,
        y: Math.round(((a.y + b.y) / 2) * 10) / 10
      };
    }
  }
  return best;
}

/**
 * Classic straight/curved Bezier edge path, upgraded to support Draw.io-style pinned waypoints.
 */
export function computeClassicEdgePath(src, tgt, curveOffset = 0, isDirected = true, waypoints = []) {
  const cleanWps = (waypoints || []).filter((p) => p && Number.isFinite(p.x) && Number.isFinite(p.y));

  if (cleanWps.length > 0) {
    const startPt = borderAnchorToward(src, cleanWps[0], 0);
    const endPt = borderAnchorToward(tgt, cleanWps[cleanWps.length - 1], isDirected ? 3 : 0);
    const pts = [startPt, ...cleanWps.map((p) => ({ x: p.x, y: p.y })), endPt];
    const d = polylineToRoundedPath(pts, 6);
    const lbl = longestSegmentMidpoint(pts);
    return {
      d,
      points: pts,
      labelX: lbl.x,
      labelY: lbl.y,
      midpoints: buildMidpoints(pts),
      isMultiSegment: true
    };
  }

  if (src.id === tgt.id) {
    const r = Math.min(src.width, src.height) / 2;
    const x1 = src.x - r * 0.45;
    const y1 = src.y - r * 0.85;
    const x2 = src.x + r * 0.45;
    const y2 = src.y - r * 0.85;
    const topY = src.y - r - 44;
    const apexY = 0.125 * y1 + 0.75 * topY + 0.125 * y2;
    return {
      d: `M ${x1} ${y1} C ${src.x - 36} ${topY}, ${src.x + 36} ${topY}, ${x2} ${y2}`,
      points: [{ x: x1, y: y1 }, { x: src.x, y: apexY }, { x: x2, y: y2 }],
      labelX: src.x,
      labelY: topY - 4,
      midpoints: [{ x: Math.round(src.x * 10) / 10, y: Math.round(apexY * 10) / 10, insertIndex: 0 }],
      isMultiSegment: false
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
    const midX = (sx + tx) / 2;
    const midY = (sy + ty) / 2;
    return {
      d: `M ${sx} ${sy} L ${tx} ${ty}`,
      points: [{ x: sx, y: sy }, { x: tx, y: ty }],
      labelX: midX - uy * 13,
      labelY: midY + ux * 13,
      midpoints: [{ x: Math.round(midX * 10) / 10, y: Math.round(midY * 10) / 10, insertIndex: 0 }],
      isMultiSegment: false
    };
  }

  const mx = (sx + tx) / 2 - uy * curveOffset;
  const my = (sy + ty) / 2 + ux * curveOffset;
  const apexX = 0.25 * sx + 0.5 * mx + 0.25 * tx;
  const apexY = 0.25 * sy + 0.5 * my + 0.25 * ty;
  return {
    d: `M ${sx} ${sy} Q ${mx} ${my} ${tx} ${ty}`,
    points: [{ x: sx, y: sy }, { x: tx, y: ty }],
    labelX: apexX,
    labelY: apexY - 4,
    midpoints: [{ x: Math.round(apexX * 10) / 10, y: Math.round(apexY * 10) / 10, insertIndex: 0 }],
    isMultiSegment: false
  };
}

function buildOrthoLeg(a, b, startAxis, endAxis) {
  const dx = b.x - a.x;
  const dy = b.y - a.y;
  if (Math.abs(dx) < 0.5 && Math.abs(dy) < 0.5) {
    return {
      pts: [a, b],
      startDir: { x: 0, y: 0 },
      endDir: { x: 0, y: 0 },
      bends: 0
    };
  }
  if (Math.abs(dy) < 0.5) {
    const dir = { x: Math.sign(dx), y: 0 };
    return {
      pts: [a, b],
      startDir: dir,
      endDir: dir,
      bends: (startAxis === "H" ? 0 : 1) + (endAxis === "H" ? 0 : 1)
    };
  }
  if (Math.abs(dx) < 0.5) {
    const dir = { x: 0, y: Math.sign(dy) };
    return {
      pts: [a, b],
      startDir: dir,
      endDir: dir,
      bends: (startAxis === "V" ? 0 : 1) + (endAxis === "V" ? 0 : 1)
    };
  }

  if (startAxis === "H" && endAxis === "V") {
    return {
      pts: [a, { x: b.x, y: a.y }, b],
      startDir: { x: Math.sign(dx), y: 0 },
      endDir: { x: 0, y: Math.sign(dy) },
      bends: 1
    };
  }
  if (startAxis === "V" && endAxis === "H") {
    return {
      pts: [a, { x: a.x, y: b.y }, b],
      startDir: { x: 0, y: Math.sign(dy) },
      endDir: { x: Math.sign(dx), y: 0 },
      bends: 1
    };
  }
  if (startAxis === "H" && endAxis === "H") {
    const midX = (a.x + b.x) / 2;
    return {
      pts: [a, { x: midX, y: a.y }, { x: midX, y: b.y }, b],
      startDir: { x: Math.sign(dx), y: 0 },
      endDir: { x: Math.sign(dx), y: 0 },
      bends: 2
    };
  }
  const midY = (a.y + b.y) / 2;
  return {
    pts: [a, { x: a.x, y: midY }, { x: b.x, y: midY }, b],
    startDir: { x: 0, y: Math.sign(dy) },
    endDir: { x: 0, y: Math.sign(dy) },
    bends: 2
  };
}

/**
 * Routes a single Blocky 90-degree orthogonal arrow pinned through one or more Draw.io waypoints.
 * Picks minimum-bend, non-reversing L/Z legs via small DP so every pinned waypoint stays attached
 * to the rendered path without 180-degree hairpin collapses.
 */
export function computeBlockyEdgePathWithWaypoints(src, tgt, isDirected = true, waypoints = [], cornerRadius = CORNER_RADIUS) {
  const cleanWps = (waypoints || []).filter((p) => p && Number.isFinite(p.x) && Number.isFinite(p.y));
  if (cleanWps.length === 0) {
    return computeClassicEdgePath(src, tgt, 0, isDirected, []);
  }

  const srcBox = nodeBox(src);
  const tgtBox = nodeBox(tgt);
  const firstWp = cleanWps[0];
  const lastWp = cleanWps[cleanWps.length - 1];

  const pickSide = (box, pt) => {
    const dx = pt.x - box.cx;
    const dy = pt.y - box.cy;
    if (Math.abs(dx) >= Math.abs(dy)) return dx >= 0 ? "E" : "W";
    return dy >= 0 ? "S" : "N";
  };

  const srcSide = pickSide(srcBox, firstWp);
  const tgtSide = pickSide(tgtBox, lastWp);
  const isSelfLoop = src.id === tgt.id;
  let startPt;
  let endPt;
  if (isSelfLoop && srcSide === tgtSide) {
    const spanBase = (srcSide === "N" || srcSide === "S") ? srcBox.hw : srcBox.hh;
    const span = Math.min(18, Math.max(11, spanBase * 0.45));
    startPt = portOnBox(srcBox, srcSide, -span, 0);
    endPt = portOnBox(tgtBox, tgtSide, span, isDirected ? 3 : 0);
  } else {
    startPt = portOnBox(srcBox, srcSide, 0, 0);
    endPt = portOnBox(tgtBox, tgtSide, 0, isDirected ? 3 : 0);
  }

  const anchors = [startPt, ...cleanWps.map((p) => ({ x: p.x, y: p.y })), endPt];
  const legCount = anchors.length - 1;
  const srcAxis = (srcSide === "E" || srcSide === "W") ? "H" : "V";
  const tgtAxis = (tgtSide === "E" || tgtSide === "W") ? "H" : "V";
  const axisPairs = [
    ["H", "H"],
    ["H", "V"],
    ["V", "H"],
    ["V", "V"]
  ];

  // Build candidate orthogonal legs for each anchor-to-anchor span
  const legOptions = [];
  for (let k = 0; k < legCount; k++) {
    const opts = [];
    for (let s = 0; s < axisPairs.length; s++) {
      const [sa, ea] = axisPairs[s];
      if (k === 0 && sa !== srcAxis) {
        opts.push(null);
        continue;
      }
      if (k === legCount - 1 && ea !== tgtAxis) {
        opts.push(null);
        continue;
      }
      opts.push(buildOrthoLeg(anchors[k], anchors[k + 1], sa, ea));
    }
    legOptions.push(opts);
  }

  // DP across legs to minimize bends while forbidding 180-degree hairpin reversals at waypoints
  const dpCost = Array.from({ length: legCount }, () => new Array(4).fill(Infinity));
  const dpPrev = Array.from({ length: legCount }, () => new Array(4).fill(-1));

  for (let s = 0; s < 4; s++) {
    const leg = legOptions[0][s];
    if (leg) dpCost[0][s] = leg.bends;
  }

  for (let k = 1; k < legCount; k++) {
    for (let cur = 0; cur < 4; cur++) {
      const curLeg = legOptions[k][cur];
      if (!curLeg) continue;
      for (let prev = 0; prev < 4; prev++) {
        if (!Number.isFinite(dpCost[k - 1][prev])) continue;
        const prevLeg = legOptions[k - 1][prev];
        const dot = prevLeg.endDir.x * curLeg.startDir.x + prevLeg.endDir.y * curLeg.startDir.y;
        const junctionCost = dot < -0.5 ? 1000 : (dot > 0.5 ? 0 : 1.1);
        const total = dpCost[k - 1][prev] + junctionCost + curLeg.bends;
        if (total < dpCost[k][cur]) {
          dpCost[k][cur] = total;
          dpPrev[k][cur] = prev;
        }
      }
    }
  }

  let bestState = 0;
  let bestTotal = Infinity;
  for (let s = 0; s < 4; s++) {
    if (dpCost[legCount - 1][s] < bestTotal) {
      bestTotal = dpCost[legCount - 1][s];
      bestState = s;
    }
  }

  const chosenLegs = new Array(legCount);
  let curState = bestState;
  for (let k = legCount - 1; k >= 0; k--) {
    chosenLegs[k] = legOptions[k][curState];
    curState = dpPrev[k][curState];
  }

  const rawPts = [startPt];
  const legMidpoints = [];
  for (let k = 0; k < legCount; k++) {
    const leg = chosenLegs[k];
    for (let i = 1; i < leg.pts.length; i++) {
      rawPts.push(leg.pts[i]);
    }
    const mp = longestSegmentMidpoint(leg.pts);
    legMidpoints.push({ x: mp.x, y: mp.y, insertIndex: k });
  }

  const cleanPts = simplifyOrthoPoints(rawPts);
  const d = polylineToRoundedPath(cleanPts, cornerRadius);
  const lbl = longestSegmentMidpoint(cleanPts);

  return {
    d,
    points: cleanPts,
    labelX: lbl.x,
    labelY: lbl.y,
    midpoints: legMidpoints,
    isMultiSegment: true
  };
}

/**
 * Routes all edges in a state graph or network diagram as strictly orthogonal 90-degree
 * wires with 5px rounded corners, distinct per-edge colors, spread node ports, and
 * obstacle-avoiding separated parallel channels.
 */
export function routeOrthogonalEdges(nodesMap, edges, options = {}) {
  const cornerRadius = options.cornerRadius ?? CORNER_RADIUS;
  const colorMap = assignEdgeColors(edges);
  const boxes = new Map();
  for (const [id, n] of nodesMap.entries()) {
    boxes.set(id, nodeBox(n));
  }

  const pairSet = new Set(edges.map((e) => `${e.sourceId}=>${e.targetId}`));
  const isAntiparallel = (e) =>
    e.sourceId !== e.targetId && pairSet.has(`${e.targetId}=>${e.sourceId}`);

  // Pass 1: Decide preliminary exit/entry sides for each edge so busy nodes can spread ports cleanly
  const plans = [];
  const portBuckets = new Map();
  const addPortUsage = (nodeId, side, planIdx, isSource, sortCoord) => {
    const key = `${nodeId}:${side}`;
    let bucket = portBuckets.get(key);
    if (!bucket) {
      bucket = [];
      portBuckets.set(key, bucket);
    }
    bucket.push({ planIdx, isSource, sortCoord });
  };

  const selfLoopCounts = new Map();

  // Compute scene vertical extent for clean outer corridors
  let sceneMinY = Infinity;
  let sceneMaxY = -Infinity;
  for (const b of boxes.values()) {
    if (b.top < sceneMinY) sceneMinY = b.top;
    if (b.bottom > sceneMaxY) sceneMaxY = b.bottom;
  }

  for (let i = 0; i < edges.length; i++) {
    const e = edges[i];
    const key = edgeKey(e, i);
    const srcBox = boxes.get(e.sourceId);
    const tgtBox = boxes.get(e.targetId);
    if (!srcBox || !tgtBox) continue;

    const colorIndex = colorMap.get(key) ?? (i % EDGE_PALETTE_SIZE);
    const planIdx = plans.length;

    if (e.sourceId === e.targetId) {
      const loopIndex = selfLoopCounts.get(e.sourceId) || 0;
      selfLoopCounts.set(e.sourceId, loopIndex + 1);
      plans.push({
        key,
        edge: e,
        colorIndex,
        kind: "self-loop",
        loopIndex,
        srcSide: "N",
        tgtSide: "N",
        srcOffset: 0,
        tgtOffset: 0
      });
      continue;
    }

    const dx = tgtBox.cx - srcBox.cx;
    const dy = tgtBox.cy - srcBox.cy;
    const obstacles = buildObstacleList(boxes, e.sourceId, e.targetId);
    const anti = isAntiparallel(e);
    let kind = "general";
    let srcSide = "E";
    let tgtSide = "W";
    let channelDir = 0; // -1 top, +1 bottom

    if (Math.abs(dy) <= 22) {
      const directStart = portOnBox(srcBox, dx >= 0 ? "E" : "W", 0);
      const directEnd = portOnBox(tgtBox, dx >= 0 ? "W" : "E", 0, e.isDirected ? 3 : 0);
      const directBlocked = pathHitsAnyObstacle([directStart, directEnd], obstacles);
      if (!directBlocked) {
        kind = "direct-h";
        srcSide = dx >= 0 ? "E" : "W";
        tgtSide = dx >= 0 ? "W" : "E";
      } else {
        // Long skip or return wire over/under intermediate nodes
        kind = "channel-h";
        const useTop = dx < 0 || (e.curveOffset && e.curveOffset < 0);
        channelDir = useTop ? -1 : 1;
        srcSide = useTop ? "N" : "S";
        tgtSide = useTop ? "N" : "S";
      }
    } else if (Math.abs(dx) <= 22) {
      const directStart = portOnBox(srcBox, dy >= 0 ? "S" : "N", 0);
      const directEnd = portOnBox(tgtBox, dy >= 0 ? "N" : "S", 0, e.isDirected ? 3 : 0);
      const directBlocked = pathHitsAnyObstacle([directStart, directEnd], obstacles);
      if (!directBlocked) {
        kind = "direct-v";
        srcSide = dy >= 0 ? "S" : "N";
        tgtSide = dy >= 0 ? "N" : "S";
      } else {
        kind = "channel-v";
        srcSide = "E";
        tgtSide = "E";
      }
    } else {
      // Diagonal relation: check clean L/Z vs lower/upper corridor
      const goingDown = dy > 0;
      const goingRight = dx > 0;
      const sideH = goingRight ? "E" : "W";
      const oppH = goingRight ? "W" : "E";
      const sideV = goingDown ? "S" : "N";
      const oppV = goingDown ? "N" : "S";

      // Test VH (exit vertical from src, turn horizontal into tgt's side)
      const vhStart = portOnBox(srcBox, sideV, 0);
      const vhEnd = portOnBox(tgtBox, oppH, 0, e.isDirected ? 3 : 0);
      const vhPts = [vhStart, { x: vhStart.x, y: vhEnd.y }, vhEnd];
      const vhClear = !pathHitsAnyObstacle(vhPts, obstacles);

      // Test HV (exit horizontal from src, turn vertical into tgt's top/bottom)
      const hvStart = portOnBox(srcBox, sideH, 0);
      const hvEnd = portOnBox(tgtBox, oppV, 0, e.isDirected ? 3 : 0);
      const hvPts = [hvStart, { x: hvEnd.x, y: hvStart.y }, hvEnd];
      const hvClear = !pathHitsAnyObstacle(hvPts, obstacles);

      if (anti && goingRight) {
        if (hvClear) {
          kind = "hv";
          srcSide = sideH;
          tgtSide = oppV;
        } else if (vhClear) {
          kind = "vh";
          srcSide = sideV;
          tgtSide = oppH;
        } else {
          kind = "channel-h";
          channelDir = goingDown ? 1 : -1;
          srcSide = goingDown ? "S" : "N";
          tgtSide = goingDown ? "S" : "N";
        }
      } else if (vhClear && (!hvClear || Math.abs(dx) > 180)) {
        kind = "vh";
        srcSide = sideV;
        tgtSide = oppH;
      } else if (hvClear) {
        kind = "hv";
        srcSide = sideH;
        tgtSide = oppV;
      } else {
        // Obstacles lie between src and tgt (e.g. find_c -> rej_mid blocked by rej_end)
        kind = "channel-h";
        channelDir = goingDown ? 1 : -1;
        srcSide = goingDown ? "S" : "N";
        tgtSide = goingDown ? "S" : "N";
      }
    }

    plans.push({
      key,
      edge: e,
      colorIndex,
      kind,
      channelDir,
      srcSide,
      tgtSide,
      srcOffset: 0,
      tgtOffset: 0,
      anti
    });

    const srcSort = (srcSide === "N" || srcSide === "S") ? tgtBox.cx : tgtBox.cy;
    const tgtSort = (tgtSide === "N" || tgtSide === "S") ? srcBox.cx : srcBox.cy;
    addPortUsage(e.sourceId, srcSide, planIdx, true, srcSort);
    addPortUsage(e.targetId, tgtSide, planIdx, false, tgtSort);
  }

  // Pass 2: Spread ports evenly on each (node, side) bucket to avoid stacked endpoints
  for (const [bucketKey, entries] of portBuckets.entries()) {
    if (entries.length <= 1) continue;
    const nodeId = bucketKey.slice(0, bucketKey.lastIndexOf(":"));
    const side = bucketKey.slice(bucketKey.lastIndexOf(":") + 1);
    const box = boxes.get(nodeId);
    if (!box) continue;

    entries.sort((a, b) => a.sortCoord - b.sortCoord);
    const maxHalf = (side === "N" || side === "S")
      ? (box.isCircle ? box.hw * 0.56 : Math.max(8, box.hw - 10))
      : (box.isCircle ? box.hh * 0.56 : Math.max(7, box.hh - 8));
    const desiredSpan = Math.min(maxHalf * 2, (entries.length - 1) * 14);
    const startOff = -desiredSpan / 2;
    const step = entries.length > 1 ? desiredSpan / (entries.length - 1) : 0;

    entries.forEach((entry, idx) => {
      const off = Math.round((startOff + idx * step) * 10) / 10;
      if (entry.isSource) {
        plans[entry.planIdx].srcOffset = off;
      } else {
        plans[entry.planIdx].tgtOffset = off;
      }
    });
  }

  // Pass 3: Generate strictly orthogonal points with separated parallel lanes
  const usedHoriz = [];
  const usedVert = [];
  const placedLabels = [];
  const routed = new Map();

  for (const plan of plans) {
    const e = plan.edge;
    const srcBox = boxes.get(e.sourceId);
    const tgtBox = boxes.get(e.targetId);
    const tgtGap = e.isDirected ? 3 : 0;
    const obstacles = buildObstacleList(boxes, e.sourceId, e.targetId);
    const strictObstacles = buildStrictObstacleList(boxes, e.sourceId, e.targetId);
    let pts;

    if (plan.kind === "self-loop") {
      const span = Math.min(18, Math.max(11, srcBox.hw * 0.45));
      const p1 = portOnBox(srcBox, "N", -span, 0);
      const p4 = portOnBox(srcBox, "N", span, tgtGap);
      const baseTopY = srcBox.top - 30 - plan.loopIndex * 20;
      const topY = allocateHorizontalLane(baseTopY, -1, p1.x, p4.x, strictObstacles, usedHoriz);
      pts = [p1, { x: p1.x, y: topY }, { x: p4.x, y: topY }, p4];
    } else if (plan.kind === "direct-h") {
      const p1 = portOnBox(srcBox, plan.srcSide, plan.srcOffset, 0);
      const p2 = portOnBox(tgtBox, plan.tgtSide, plan.tgtOffset, tgtGap);
      if (Math.abs(p1.y - p2.y) <= 1) {
        pts = [p1, { x: p2.x, y: p1.y }];
        usedHoriz.push({ coord: p1.y, min: Math.min(p1.x, p2.x), max: Math.max(p1.x, p2.x) });
      } else {
        const midX = (p1.x + p2.x) / 2;
        pts = [p1, { x: midX, y: p1.y }, { x: midX, y: p2.y }, p2];
      }
    } else if (plan.kind === "direct-v") {
      const p1 = portOnBox(srcBox, plan.srcSide, plan.srcOffset, 0);
      const p2 = portOnBox(tgtBox, plan.tgtSide, plan.tgtOffset, tgtGap);
      if (Math.abs(p1.x - p2.x) <= 1) {
        pts = [p1, { x: p1.x, y: p2.y }];
        usedVert.push({ coord: p1.x, min: Math.min(p1.y, p2.y), max: Math.max(p1.y, p2.y) });
      } else {
        const midY = (p1.y + p2.y) / 2;
        pts = [p1, { x: p1.x, y: midY }, { x: p2.x, y: midY }, p2];
      }
    } else if (plan.kind === "vh") {
      const p1 = portOnBox(srcBox, plan.srcSide, plan.srcOffset, 0);
      const p2 = portOnBox(tgtBox, plan.tgtSide, plan.tgtOffset, tgtGap);
      const candidate = [p1, { x: p1.x, y: p2.y }, p2];
      const spanMin = Math.min(p1.x, p2.x);
      const spanMax = Math.max(p1.x, p2.x);
      if (!pathHitsAnyObstacle(candidate, strictObstacles) && !trackOverlaps(usedHoriz, p2.y, spanMin, spanMax, 12)) {
        usedHoriz.push({ coord: p2.y, min: spanMin, max: spanMax });
        pts = candidate;
      } else {
        // Shift onto a clean parallel horizontal lane and elbow into target side
        const dir = p2.y >= p1.y ? 1 : -1;
        const laneY = allocateHorizontalLane(p2.y + dir * LANE_SPACING, dir, spanMin, spanMax, obstacles, usedHoriz);
        const approachX = plan.tgtSide === "E" ? tgtBox.right + 18 : tgtBox.left - 18;
        pts = [p1, { x: p1.x, y: laneY }, { x: approachX, y: laneY }, { x: approachX, y: p2.y }, p2];
      }
    } else if (plan.kind === "hv") {
      const p1 = portOnBox(srcBox, plan.srcSide, plan.srcOffset, 0);
      const p2 = portOnBox(tgtBox, plan.tgtSide, plan.tgtOffset, tgtGap);
      const candidate = [p1, { x: p2.x, y: p1.y }, p2];
      const spanMin = Math.min(p1.x, p2.x);
      const spanMax = Math.max(p1.x, p2.x);
      if (!pathHitsAnyObstacle(candidate, strictObstacles) && !trackOverlaps(usedHoriz, p1.y, spanMin, spanMax, 12)) {
        usedHoriz.push({ coord: p1.y, min: spanMin, max: spanMax });
        pts = candidate;
      } else {
        const dir = p1.y <= p2.y ? -1 : 1;
        const laneY = allocateHorizontalLane(p1.y + dir * LANE_SPACING, dir, spanMin, spanMax, obstacles, usedHoriz);
        const exitX = plan.srcSide === "E" ? srcBox.right + 18 : srcBox.left - 18;
        pts = [p1, { x: exitX, y: p1.y }, { x: exitX, y: laneY }, { x: p2.x, y: laneY }, p2];
      }
    } else if (plan.kind === "channel-v") {
      const p1 = portOnBox(srcBox, plan.srcSide, plan.srcOffset, 0);
      const p2 = portOnBox(tgtBox, plan.tgtSide, plan.tgtOffset, tgtGap);
      const spanMin = Math.min(p1.y, p2.y);
      const spanMax = Math.max(p1.y, p2.y);
      let maxRight = Math.max(srcBox.right, tgtBox.right);
      for (const ob of obstacles) {
        if (ob.bottom > spanMin && ob.top < spanMax) {
          maxRight = Math.max(maxRight, ob.right);
        }
      }
      const laneX = allocateVerticalLane(maxRight + 20, 1, spanMin, spanMax, obstacles, usedVert);
      pts = [p1, { x: laneX, y: p1.y }, { x: laneX, y: p2.y }, p2];
    } else {
      // channel-h: obstacle-avoiding top or bottom horizontal corridor with separated lanes
      const p1 = portOnBox(srcBox, plan.srcSide, plan.srcOffset, 0);
      const p2 = portOnBox(tgtBox, plan.tgtSide, plan.tgtOffset, tgtGap);
      const spanMin = Math.min(p1.x, p2.x);
      const spanMax = Math.max(p1.x, p2.x);
      const useTop = plan.channelDir < 0;

      // Find vertical bounds of any obstacle inside the horizontal span
      let minObsTop = Math.min(srcBox.top, tgtBox.top);
      let maxObsBottom = Math.max(srcBox.bottom, tgtBox.bottom);
      for (const ob of obstacles) {
        if (ob.right > spanMin - 12 && ob.left < spanMax + 12) {
          minObsTop = Math.min(minObsTop, ob.top);
          maxObsBottom = Math.max(maxObsBottom, ob.bottom);
        }
      }

      const baseY = useTop
        ? Math.min(minObsTop - 20, sceneMinY - 18)
        : Math.max(maxObsBottom + 20, Math.max(srcBox.bottom, tgtBox.bottom) + 22);

      // Verify vertical drops at p1.x and p2.x don't slice through unrelated nodes
      const laneY = allocateHorizontalLane(baseY, useTop ? -1 : 1, spanMin, spanMax, obstacles, usedHoriz);
      const drop1Blocked = pathHitsAnyObstacle([p1, { x: p1.x, y: laneY }], strictObstacles);
      const drop2Blocked = pathHitsAnyObstacle([{ x: p2.x, y: laneY }, p2], strictObstacles);

      let x1 = p1.x;
      let x2 = p2.x;
      pts = [p1];
      if (drop1Blocked) {
        const stubY = useTop ? srcBox.top - 14 : srcBox.bottom + 14;
        const detourX = allocateVerticalLane(
          p1.x >= p2.x ? srcBox.right + 16 : srcBox.left - 16,
          p1.x >= p2.x ? 1 : -1,
          Math.min(stubY, laneY),
          Math.max(stubY, laneY),
          strictObstacles,
          usedVert
        );
        pts.push({ x: p1.x, y: stubY }, { x: detourX, y: stubY });
        x1 = detourX;
      }
      pts.push({ x: x1, y: laneY });
      if (drop2Blocked) {
        const stubY = useTop ? tgtBox.top - 14 : tgtBox.bottom + 14;
        const detourX = allocateVerticalLane(
          p2.x <= p1.x ? tgtBox.left - 16 : tgtBox.right + 16,
          p2.x <= p1.x ? -1 : 1,
          Math.min(stubY, laneY),
          Math.max(stubY, laneY),
          strictObstacles,
          usedVert
        );
        pts.push({ x: detourX, y: laneY }, { x: detourX, y: stubY }, { x: p2.x, y: stubY });
        x2 = detourX;
      } else {
        pts.push({ x: x2, y: laneY });
      }
      pts.push(p2);
    }

    const cleanPts = simplifyOrthoPoints(pts);
    const d = polylineToRoundedPath(cleanPts, cornerRadius);
    const { labelX, labelY } = selectFreeLabelPosition(cleanPts, e.label, boxes, placedLabels);

    routed.set(plan.key, {
      d,
      points: cleanPts,
      labelX,
      labelY,
      midpoints: buildMidpoints(cleanPts).map((m) => ({ ...m, insertIndex: 0 })),
      colorIndex: plan.colorIndex,
      isMultiSegment: cleanPts.length > 2
    });
  }

  return routed;
}
