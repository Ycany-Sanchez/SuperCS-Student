import { scaleLinear } from "d3-scale";
import { line, area } from "d3-shape";

/**
 * Renders a restrained complexity growth SVG chart (exact T(n) vs asymptotic reference curves)
 * using vanilla d3-scale + d3-shape.
 */
export function renderGrowthChart(container, payload) {
  if (!container || !payload) return;
  container.innerHTML = "";

  const width = Math.max(container.clientWidth || 520, 320);
  const height = 216;
  const margin = { top: 16, right: 18, bottom: 30, left: 48 };
  const plotW = width - margin.left - margin.right;
  const plotH = height - margin.top - margin.bottom;

  const series = payload.series || [];
  if (series.length === 0) return;

  const allPoints = series.flatMap((s) => s.points || []);
  const maxX = Math.max(...allPoints.map((p) => p.n), 1);
  const maxYRaw = Math.max(...allPoints.map((p) => p.value), 1);
  const maxY = maxYRaw * 1.12;

  const x = scaleLinear().domain([1, maxX]).range([0, plotW]);
  const y = scaleLinear().domain([0, maxY]).nice(4).range([plotH, 0]);

  const ns = "http://www.w3.org/2000/svg";
  const svg = document.createElementNS(ns, "svg");
  svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
  svg.setAttribute("class", "w-full h-auto block select-none");

  const g = document.createElementNS(ns, "g");
  g.setAttribute("transform", `translate(${margin.left},${margin.top})`);
  svg.appendChild(g);

  // Horizontal hairline gridlines (4-5 ticks, no y-axis line)
  const yTicks = y.ticks(4);
  for (const t of yTicks) {
    const ty = y(t);
    const gl = document.createElementNS(ns, "line");
    gl.setAttribute("x1", "0");
    gl.setAttribute("x2", String(plotW));
    gl.setAttribute("y1", String(ty));
    gl.setAttribute("y2", String(ty));
    gl.setAttribute("stroke", "var(--grid)");
    gl.setAttribute("stroke-width", "1");
    g.appendChild(gl);

    const lbl = document.createElementNS(ns, "text");
    lbl.setAttribute("x", "-8");
    lbl.setAttribute("y", String(ty + 4));
    lbl.setAttribute("text-anchor", "end");
    lbl.setAttribute("fill", "var(--muted)");
    lbl.setAttribute("font-size", "11");
    lbl.setAttribute("class", "tnum");
    lbl.textContent = formatCompact(t);
    g.appendChild(lbl);
  }

  // Baseline + X ticks
  const baseLine = document.createElementNS(ns, "line");
  baseLine.setAttribute("x1", "0");
  baseLine.setAttribute("x2", String(plotW));
  baseLine.setAttribute("y1", String(plotH));
  baseLine.setAttribute("y2", String(plotH));
  baseLine.setAttribute("stroke", "var(--control)");
  baseLine.setAttribute("stroke-width", "1.5");
  g.appendChild(baseLine);

  const xTicks = x.ticks(5).filter((v) => Number.isInteger(v));
  for (const t of xTicks) {
    const tx = x(t);
    const lbl = document.createElementNS(ns, "text");
    lbl.setAttribute("x", String(tx));
    lbl.setAttribute("y", String(plotH + 18));
    lbl.setAttribute("text-anchor", "middle");
    lbl.setAttribute("fill", "var(--muted)");
    lbl.setAttribute("font-size", "11");
    lbl.setAttribute("class", "tnum");
    lbl.textContent = `n=${t}`;
    g.appendChild(lbl);
  }

  const lineGen = line()
    .x((d) => x(d.n))
    .y((d) => y(d.value));

  const areaGen = area()
    .x((d) => x(d.n))
    .y0(plotH)
    .y1((d) => y(d.value));

  // Render reference dashed curves first, primary T(n) curve last
  for (const s of series) {
    if (!s.points || s.points.length === 0) continue;

    if (s.isPrimary) {
      const areaPath = document.createElementNS(ns, "path");
      areaPath.setAttribute("d", areaGen(s.points));
      areaPath.setAttribute("fill", "var(--accent)");
      areaPath.setAttribute("fill-opacity", "0.10");
      g.appendChild(areaPath);
    }

    const path = document.createElementNS(ns, "path");
    path.setAttribute("d", lineGen(s.points));
    path.setAttribute("fill", "none");
    path.setAttribute("stroke", s.isPrimary ? "var(--accent)" : (s.stroke || "var(--muted)"));
    path.setAttribute("stroke-width", s.isPrimary ? "2.25" : "1.4");
    if (!s.isPrimary) {
      path.setAttribute("stroke-dasharray", "4 4");
      path.setAttribute("opacity", "0.7");
    }
    g.appendChild(path);
  }

  // Hover crosshair + crisp tooltip
  const hoverLine = document.createElementNS(ns, "line");
  hoverLine.setAttribute("y1", "0");
  hoverLine.setAttribute("y2", String(plotH));
  hoverLine.setAttribute("stroke", "var(--control)");
  hoverLine.setAttribute("stroke-width", "1");
  hoverLine.setAttribute("stroke-dasharray", "3 3");
  hoverLine.style.display = "none";
  g.appendChild(hoverLine);

  const hoverDot = document.createElementNS(ns, "circle");
  hoverDot.setAttribute("r", "4");
  hoverDot.setAttribute("fill", "var(--accent)");
  hoverDot.setAttribute("stroke", "var(--surface)");
  hoverDot.setAttribute("stroke-width", "2");
  hoverDot.style.display = "none";
  g.appendChild(hoverDot);

  const tooltip = document.createElement("div");
  tooltip.className =
    "pointer-events-none hidden absolute top-2 right-2 rounded px-2 py-1 text-xs font-mono tnum bp-floating";
  container.style.position = "relative";
  container.appendChild(svg);
  container.appendChild(tooltip);

  const primary = series.find((s) => s.isPrimary) || series[0];
  const overlay = document.createElementNS(ns, "rect");
  overlay.setAttribute("width", String(plotW));
  overlay.setAttribute("height", String(plotH));
  overlay.setAttribute("fill", "transparent");
  overlay.addEventListener("mousemove", (e) => {
    const rect = overlay.getBoundingClientRect();
    const relX = ((e.clientX - rect.left) / rect.width) * plotW;
    const approxN = Math.round(x.invert(relX));
    let nearest = primary.points[0];
    let bestDiff = Infinity;
    for (const pt of primary.points) {
      const diff = Math.abs(pt.n - approxN);
      if (diff < bestDiff) {
        bestDiff = diff;
        nearest = pt;
      }
    }
    if (!nearest) return;
    const cx = x(nearest.n);
    const cy = y(nearest.value);
    hoverLine.setAttribute("x1", String(cx));
    hoverLine.setAttribute("x2", String(cx));
    hoverLine.style.display = "";
    hoverDot.setAttribute("cx", String(cx));
    hoverDot.setAttribute("cy", String(cy));
    hoverDot.style.display = "";
    tooltip.textContent = `n=${nearest.n}  ${primary.label}=${formatCompact(nearest.value)}`;
    tooltip.classList.remove("hidden");
  });
  overlay.addEventListener("mouseleave", () => {
    hoverLine.style.display = "none";
    hoverDot.style.display = "none";
    tooltip.classList.add("hidden");
  });
  g.appendChild(overlay);
}

function formatCompact(v) {
  if (Math.abs(v) >= 1_000_000) return `${(v / 1_000_000).toFixed(1)}M`;
  if (Math.abs(v) >= 1_000) return `${(v / 1_000).toFixed(1)}k`;
  return Number.isInteger(v) ? String(v) : v.toFixed(1);
}
