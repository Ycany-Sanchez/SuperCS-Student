import * as svg from "./svgVisualizer.js";
import * as blockly from "./blocklyInterop.js";
import * as charts from "./chartInterop.js";
import * as math from "./mathInterop.js";
import * as storage from "./storageInterop.js";

window.SuperCs = {
  svg,
  blockly,
  charts,
  math,
  storage
};

export { svg, blockly, charts, math, storage };
