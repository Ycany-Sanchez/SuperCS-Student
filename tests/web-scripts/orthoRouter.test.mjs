import test from "node:test";
import assert from "node:assert/strict";
import {
  EDGE_PALETTE_SIZE,
  assignEdgeColors,
  computeBlockyEdgePathWithWaypoints,
  computeClassicEdgePath,
  polylineToRoundedPath,
  routeOrthogonalEdges
} from "../../src/SuperCsStudent.Web/Scripts/orthoRouter.js";

function makeNodeMap(list) {
  return new Map(list.map((n) => [n.id, n]));
}

function segmentHitsRect(a, b, rect) {
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

test("assignEdgeColors gives distinct hues to 5 edges converging on one target and 4 leaving one source", () => {
  const edges = [
    { id: "e1", sourceId: "start", targetId: "rej_mid" },
    { id: "e2", sourceId: "find_b", targetId: "rej_mid" },
    { id: "e3", sourceId: "find_c", targetId: "rej_mid" },
    { id: "e4", sourceId: "mark_c", targetId: "rej_mid" },
    { id: "e5", sourceId: "back", targetId: "rej_mid" },
    { id: "e6", sourceId: "start", targetId: "mark_a" },
    { id: "e7", sourceId: "start", targetId: "acc_empty" },
    { id: "e8", sourceId: "start", targetId: "find_b" }
  ];

  const colors = assignEdgeColors(edges);
  const incomingColors = ["e1", "e2", "e3", "e4", "e5"].map((id) => colors.get(id));
  assert.equal(new Set(incomingColors).size, 5);

  const outgoingFromStart = ["e1", "e6", "e7", "e8"].map((id) => colors.get(id));
  assert.equal(new Set(outgoingFromStart).size, 4);

  for (const c of colors.values()) {
    assert.ok(Number.isInteger(c) && c >= 0 && c < EDGE_PALETTE_SIZE);
  }
});

test("routeOrthogonalEdges routes strictly orthogonal segments around obstacles with separated lanes", () => {
  const nodes = makeNodeMap([
    { id: "start", x: 96, y: 160, width: 68, height: 42, shape: "schema-node" },
    { id: "mark_a", x: 254, y: 160, width: 58, height: 42, shape: "schema-node" },
    { id: "find_b", x: 412, y: 160, width: 58, height: 42, shape: "schema-node" },
    { id: "find_c", x: 570, y: 160, width: 58, height: 42, shape: "schema-node" },
    { id: "back", x: 728, y: 160, width: 68, height: 42, shape: "schema-node" },
    { id: "rej_mid", x: 200, y: 280, width: 92, height: 40, shape: "schema-node" },
    { id: "rej_end", x: 460, y: 270, width: 68, height: 40, shape: "schema-node" }
  ]);

  const edges = [
    { id: "e_ret", sourceId: "back", targetId: "start", label: "back", isDirected: true },
    { id: "e_o1", sourceId: "start", targetId: "rej_mid", label: "other", isDirected: true },
    { id: "e_o2", sourceId: "find_b", targetId: "rej_mid", label: "other", isDirected: true },
    { id: "e_o3", sourceId: "find_c", targetId: "rej_mid", label: "other", isDirected: true },
    { id: "e_o4", sourceId: "back", targetId: "rej_mid", label: "other", isDirected: true },
    { id: "e_f1", sourceId: "start", targetId: "mark_a", label: "a", isDirected: true }
  ];

  const routed = routeOrthogonalEdges(nodes, edges);

  for (const e of edges) {
    const r = routed.get(e.id);
    assert.ok(r, `Missing route for ${e.id}`);
    assert.ok(!r.d.includes("NaN"), `Path contains NaN for ${e.id}: ${r.d}`);
    assert.ok(r.points.length >= 2);

    // Every segment must be strictly horizontal or vertical
    for (let i = 0; i < r.points.length - 1; i++) {
      const a = r.points[i];
      const b = r.points[i + 1];
      assert.ok(
        Math.abs(a.x - b.x) < 0.01 || Math.abs(a.y - b.y) < 0.01,
        `Non-orthogonal segment on ${e.id}: (${a.x},${a.y}) -> (${b.x},${b.y})`
      );
    }

    // No interior segment should cut through an unrelated obstacle node
    for (const [nid, n] of nodes.entries()) {
      if (nid === e.sourceId || nid === e.targetId) continue;
      const rect = {
        left: n.x - n.width / 2 + 2,
        right: n.x + n.width / 2 - 2,
        top: n.y - n.height / 2 + 2,
        bottom: n.y + n.height / 2 - 2
      };
      for (let i = 0; i < r.points.length - 1; i++) {
        assert.equal(
          segmentHitsRect(r.points[i], r.points[i + 1], rect),
          false,
          `Edge ${e.id} cuts through obstacle ${nid} on segment ${i}`
        );
      }
    }
  }

  // Parallel lower lanes into rej_mid should use distinct horizontal corridor Ys
  const lowerEdgeIds = ["e_o2", "e_o3", "e_o4"];
  const horizontalYs = lowerEdgeIds.map((id) => {
    const pts = routed.get(id).points;
    let bestLen = -1;
    let bestY = 0;
    for (let i = 0; i < pts.length - 1; i++) {
      if (Math.abs(pts[i].y - pts[i + 1].y) < 0.01) {
        const len = Math.abs(pts[i].x - pts[i + 1].x);
        if (len > bestLen) {
          bestLen = len;
          bestY = pts[i].y;
        }
      }
    }
    return bestY;
  });
  horizontalYs.sort((a, b) => a - b);
  for (let i = 1; i < horizontalYs.length; i++) {
    assert.ok(
      horizontalYs[i] - horizontalYs[i - 1] >= 12,
      `Expected separated lanes (>=12px), got Ys: ${horizontalYs.join(", ")}`
    );
  }
});

test("polylineToRoundedPath clamps corner radii cleanly without NaN and routes boxy self-loops", () => {
  const path = polylineToRoundedPath([
    { x: 0, y: 0 },
    { x: 4, y: 0 },
    { x: 4, y: 4 },
    { x: 4, y: 4 },
    { x: 40, y: 4 }
  ], 5);
  assert.ok(path.startsWith("M 0 0"));
  assert.ok(path.includes("Q "));
  assert.ok(!path.includes("NaN"));

  const nodes = makeNodeMap([
    { id: "q0", x: 180, y: 180, width: 52, height: 52, shape: "circle" }
  ]);
  const edges = [{ id: "loop1", sourceId: "q0", targetId: "q0", label: "a, Q", isDirected: true }];
  const routed = routeOrthogonalEdges(nodes, edges);
  const loop = routed.get("loop1");
  assert.ok(loop.points.length === 4);
  assert.ok(loop.labelY < 180 - 26);
  assert.ok(!loop.d.includes("NaN"));
});

test("label pills for multiple edges avoid direct coordinate overlap", () => {
  const nodes = makeNodeMap([
    { id: "a", x: 100, y: 120, width: 56, height: 40, shape: "schema-node" },
    { id: "b", x: 260, y: 120, width: 56, height: 40, shape: "schema-node" }
  ]);
  const edges = [
    { id: "e_ab", sourceId: "a", targetId: "b", label: "other", isDirected: true },
    { id: "e_ba", sourceId: "b", targetId: "a", label: "other", isDirected: true }
  ];
  const routed = routeOrthogonalEdges(nodes, edges);
  const r1 = routed.get("e_ab");
  const r2 = routed.get("e_ba");
  const overlapX = Math.abs(r1.labelX - r2.labelX) < 42;
  const overlapY = Math.abs(r1.labelY - r2.labelY) < 18;
  assert.equal(overlapX && overlapY, false, `Labels overlapped: (${r1.labelX},${r1.labelY}) vs (${r2.labelX},${r2.labelY})`);
});

test("computeClassicEdgePath routes classic straight/curved by default and through pinned waypoints when present", () => {
  const src = { id: "A", x: 100, y: 120, width: 52, height: 52, shape: "circle" };
  const tgt = { id: "B", x: 300, y: 120, width: 52, height: 52, shape: "circle" };

  // Default straight classic edge with 1 midpoint handle
  const plain = computeClassicEdgePath(src, tgt, 0, true, []);
  assert.ok(plain.d.startsWith("M ") && plain.d.includes("L "));
  assert.ok(!plain.d.includes("NaN"));
  assert.equal(plain.midpoints.length, 1);
  assert.ok(Math.abs(plain.midpoints[0].x - 200) < 5);

  // Curved classic edge with 1 midpoint handle near curve apex
  const curved = computeClassicEdgePath(src, tgt, 30, true, []);
  assert.ok(curved.d.includes("Q "));
  assert.equal(curved.midpoints.length, 1);

  // Pinned waypoints route cleanly through each pinned point and produce N+1 midpoint handles
  const wps = [{ x: 170, y: 210 }, { x: 240, y: 210 }];
  const pinned = computeClassicEdgePath(src, tgt, 0, true, wps);
  assert.ok(!pinned.d.includes("NaN"));
  assert.equal(pinned.points.length, 4); // start, wp0, wp1, end
  assert.deepEqual(pinned.points[1], wps[0]);
  assert.deepEqual(pinned.points[2], wps[1]);
  assert.equal(pinned.midpoints.length, 3);
});

function pointOnOrthoPolyline(pt, points, tol = 0.6) {
  for (let i = 0; i < points.length - 1; i++) {
    const a = points[i];
    const b = points[i + 1];
    const onH = Math.abs(a.y - b.y) <= tol &&
      Math.abs(pt.y - a.y) <= tol &&
      pt.x >= Math.min(a.x, b.x) - tol &&
      pt.x <= Math.max(a.x, b.x) + tol;
    const onV = Math.abs(a.x - b.x) <= tol &&
      Math.abs(pt.x - a.x) <= tol &&
      pt.y >= Math.min(a.y, b.y) - tol &&
      pt.y <= Math.max(a.y, b.y) + tol;
    if (onH || onV) return true;
  }
  return false;
}

function assertStrictlyOrthogonalNoHairpins(points, label) {
  for (let i = 0; i < points.length - 1; i++) {
    const a = points[i];
    const b = points[i + 1];
    assert.ok(
      Math.abs(a.x - b.x) < 0.05 || Math.abs(a.y - b.y) < 0.05,
      `${label}: Non-orthogonal segment: (${a.x},${a.y}) -> (${b.x},${b.y})`
    );
  }
  for (let i = 1; i < points.length - 1; i++) {
    const a = points[i - 1];
    const b = points[i];
    const c = points[i + 1];
    const dx1 = b.x - a.x;
    const dy1 = b.y - a.y;
    const dx2 = c.x - b.x;
    const dy2 = c.y - b.y;
    const dot = dx1 * dx2 + dy1 * dy2;
    assert.ok(
      dot >= -0.05,
      `${label}: 180-degree hairpin reversal at (${b.x},${b.y}) between (${a.x},${a.y}) and (${c.x},${c.y})`
    );
  }
}

test("computeBlockyEdgePathWithWaypoints routes strictly 90-degree orthogonal segments through pinned waypoints", () => {
  const src = { id: "A", x: 100, y: 120, width: 56, height: 42, shape: "schema-node" };
  const tgt = { id: "B", x: 340, y: 200, width: 56, height: 42, shape: "schema-node" };
  const wps = [{ x: 200, y: 60 }, { x: 290, y: 150 }];

  const blocky = computeBlockyEdgePathWithWaypoints(src, tgt, true, wps);
  assert.ok(!blocky.d.includes("NaN"));
  assert.ok(blocky.points.length >= 4);
  assert.equal(blocky.midpoints.length, 3);
  assertStrictlyOrthogonalNoHairpins(blocky.points, "two-waypoints");

  for (const wp of wps) {
    assert.ok(
      pointOnOrthoPolyline(wp, blocky.points),
      `Waypoint (${wp.x},${wp.y}) missing from blocky path: ${JSON.stringify(blocky.points)}`
    );
  }
  for (const mp of blocky.midpoints) {
    assert.ok(
      pointOnOrthoPolyline(mp, blocky.points),
      `Midpoint (${mp.x},${mp.y}) does not lie on blocky path: ${JSON.stringify(blocky.points)}`
    );
  }
});

test("computeBlockyEdgePathWithWaypoints keeps waypoints attached on horizontal, diagonal, arch, C-wrap, and self-loop edges", () => {
  const cases = [
    {
      name: "horizontal JFK-ORD pulled down",
      src: { id: "JFK", x: 580, y: 468, width: 52, height: 52, shape: "circle" },
      tgt: { id: "ORD", x: 144, y: 468, width: 52, height: 52, shape: "circle" },
      isDirected: false,
      wps: [{ x: 364, y: 510 }]
    },
    {
      name: "diagonal JFK-DFW pulled right",
      src: { id: "JFK", x: 580, y: 468, width: 52, height: 52, shape: "circle" },
      tgt: { id: "DFW", x: 610, y: 324, width: 52, height: 52, shape: "circle" },
      isDirected: false,
      wps: [{ x: 658, y: 385 }]
    },
    {
      name: "arch above horizontal pair",
      src: { id: "L", x: 100, y: 200, width: 52, height: 52, shape: "circle" },
      tgt: { id: "R", x: 300, y: 200, width: 52, height: 52, shape: "circle" },
      isDirected: true,
      wps: [{ x: 200, y: 80 }]
    },
    {
      name: "C-wrap left of vertical pair",
      src: { id: "T", x: 200, y: 120, width: 52, height: 52, shape: "circle" },
      tgt: { id: "B", x: 200, y: 300, width: 52, height: 52, shape: "circle" },
      isDirected: true,
      wps: [{ x: 80, y: 210 }]
    },
    {
      name: "self-loop pinned upward",
      src: { id: "Q0", x: 180, y: 180, width: 52, height: 52, shape: "circle" },
      tgt: { id: "Q0", x: 180, y: 180, width: 52, height: 52, shape: "circle" },
      isDirected: true,
      wps: [{ x: 180, y: 80 }]
    }
  ];

  for (const c of cases) {
    const blocky = computeBlockyEdgePathWithWaypoints(c.src, c.tgt, c.isDirected, c.wps);
    assert.ok(!blocky.d.includes("NaN"), `${c.name}: path contains NaN`);
    assert.ok(blocky.points.length >= 4, `${c.name}: expected multi-segment path, got ${JSON.stringify(blocky.points)}`);
    assert.equal(blocky.midpoints.length, c.wps.length + 1, `${c.name}: expected N+1 midpoints`);
    assertStrictlyOrthogonalNoHairpins(blocky.points, c.name);

    for (const wp of c.wps) {
      assert.ok(
        pointOnOrthoPolyline(wp, blocky.points),
        `${c.name}: waypoint (${wp.x},${wp.y}) detached from blocky path ${JSON.stringify(blocky.points)}`
      );
    }
    for (const mp of blocky.midpoints) {
      assert.ok(
        pointOnOrthoPolyline(mp, blocky.points),
        `${c.name}: midpoint (${mp.x},${mp.y}) detached from blocky path ${JSON.stringify(blocky.points)}`
      );
    }
  }
});
