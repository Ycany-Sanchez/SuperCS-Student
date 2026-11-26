import katex from "katex";

export function renderMath(element, latex, displayMode = false) {
  if (!element) return;
  try {
    katex.render(latex || "", element, {
      displayMode: !!displayMode,
      throwOnError: false,
      strict: false,
      trust: false
    });
  } catch {
    element.textContent = latex || "";
  }
}
