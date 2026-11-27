import test from "node:test";
import assert from "node:assert/strict";
import {
  EDGE_PALETTE_SIZE,
  assignEdgeColors,
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
