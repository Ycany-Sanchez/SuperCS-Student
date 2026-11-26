export function getItem(key) {
  try {
    return window.localStorage.getItem(key);
  } catch {
    return null;
  }
}

export function setItem(key, value) {
  try {
    window.localStorage.setItem(key, value);
    return true;
  } catch {
    return false;
  }
}

export function removeItem(key) {
  try {
    window.localStorage.removeItem(key);
  } catch {
    // ignore storage errors
  }
}

export function applyTheme(theme) {
  const resolved = theme === "dark" ? "dark" : "light";
  document.documentElement.setAttribute("data-theme", resolved);
  setItem("scs_theme", resolved);
  return resolved;
}

export function getInitialTheme() {
  const saved = getItem("scs_theme");
  if (saved === "dark" || saved === "light") {
    document.documentElement.setAttribute("data-theme", saved);
    return saved;
  }
  document.documentElement.setAttribute("data-theme", "light");
  return "light";
}

export function downloadTextFile(filename, content, mimeType = "text/plain;charset=utf-8") {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

export function registerGlobalShortcuts(dotNetRef) {
  const handler = (e) => {
    if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
      e.preventDefault();
      dotNetRef.invokeMethodAsync("OnCommandPaletteShortcut");
    }
  };
  window.addEventListener("keydown", handler);
  return {
    dispose: () => window.removeEventListener("keydown", handler)
  };
}
