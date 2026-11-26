import * as Blockly from "blockly";

const workspaces = new WeakMap();
const observers = new WeakMap();
let registered = false;

class DesktopZelosConstantProvider extends Blockly.zelos.ConstantProvider {
  constructor() {
    super();
    this.CORNER_RADIUS = 6;
    this.SMALL_PADDING = 4;
    this.MEDIUM_PADDING = 6;
    this.MEDIUM_LARGE_PADDING = 8;
    this.LARGE_PADDING = 10;
    this.MIN_BLOCK_HEIGHT = 28;
    this.EMPTY_STATEMENT_INPUT_HEIGHT = 24;
  }
}

class DesktopZelosRenderer extends Blockly.zelos.Renderer {
  constructor(name) {
    super(name);
  }
  makeConstants_() {
    return new DesktopZelosConstantProvider();
  }
}

function defineThemes() {
  const lightTheme = Blockly.Theme.defineTheme("blueprintLight", {
    base: Blockly.Themes.Zelos,
    componentStyles: {
      workspaceBackgroundColour: "#f8f9fb",
      toolboxBackgroundColour: "#ffffff",
      toolboxForegroundColour: "#111318",
      flyoutBackgroundColour: "#ffffff",
      flyoutForegroundColour: "#111318",
      flyoutOpacity: 0.97,
      scrollbarColour: "#cbd2dc",
      insertionMarkerColour: "#0e6e85",
      insertionMarkerOpacity: 0.35
    },
    fontStyle: {
      family: '"Plus Jakarta Sans", sans-serif',
      weight: "500",
      size: 11
    }
  });

  const darkTheme = Blockly.Theme.defineTheme("blueprintDark", {
    base: Blockly.Themes.Zelos,
    componentStyles: {
      workspaceBackgroundColour: "#0d1016",
      toolboxBackgroundColour: "#11141b",
      toolboxForegroundColour: "#f3f5f9",
      flyoutBackgroundColour: "#181c26",
      flyoutForegroundColour: "#f3f5f9",
      flyoutOpacity: 0.98,
      scrollbarColour: "#374151",
      insertionMarkerColour: "#38bdf8",
      insertionMarkerOpacity: 0.45
    },
    fontStyle: {
      family: '"Plus Jakarta Sans", sans-serif',
      weight: "500",
      size: 11
    }
  });

  return { lightTheme, darkTheme };
}

function registerBlocksOnce() {
  if (registered) return;
  registered = true;

  Blockly.blockRendering.register("desktop_zelos", DesktopZelosRenderer);

  Blockly.defineBlocksWithJsonArray([
    {
      type: "daa_function",
      message0: "function %1 ( %2 ) %3 %4",
      args0: [
        { type: "field_input", name: "NAME", text: "algorithm" },
        { type: "field_input", name: "PARAMS", text: "array[], n" },
        { type: "input_dummy" },
        { type: "input_statement", name: "BODY" }
      ],
      colour: "#2d5a6b",
      tooltip: "Algorithm function entry block"
    },
    {
      type: "daa_declare",
      message0: "declare %1",
      args0: [{ type: "field_input", name: "VARS", text: "i, sum" }],
      previousStatement: null,
      nextStatement: null,
      colour: "#3b6e8c",
      tooltip: "Uninitialized variable declaration"
    },
    {
      type: "daa_assign",
      message0: "%1 %2 %3",
      args0: [
        { type: "field_input", name: "TARGET", text: "sum" },
        {
          type: "field_dropdown",
          name: "OP",
          options: [
            ["=", "="],
            ["+=", "+="],
            ["-=", "-="],
            ["*=", "*="],
            ["/=", "/="]
          ]
        },
        { type: "input_value", name: "VALUE" }
      ],
      inputsInline: true,
      previousStatement: null,
      nextStatement: null,
      colour: "#3b6e8c",
      tooltip: "Assignment or compound assignment"
    },
    {
      type: "daa_unary",
      message0: "%1 %2",
      args0: [
        { type: "field_input", name: "VAR", text: "c" },
        {
          type: "field_dropdown",
          name: "OP",
          options: [
            ["++", "++"],
            ["--", "--"]
          ]
        }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: "#3b6e8c",
      tooltip: "Unary increment or decrement"
    },
    {
      type: "daa_for",
      message0: "for %1 = %2 ; %3 %4 %5 ; %6 %7 %8 %9 %10",
      args0: [
        { type: "field_input", name: "VAR", text: "i" },
        { type: "field_input", name: "START", text: "1" },
        { type: "field_label_serializable", name: "VAR_MIRROR", text: "i" },
        {
          type: "field_dropdown",
          name: "CMP",
          options: [
            ["<=", "<="],
            ["<", "<"],
            [">=", ">="],
            [">", ">"]
          ]
        },
        { type: "field_input", name: "BOUND", text: "n" },
        { type: "field_label_serializable", name: "VAR_STEP", text: "i" },
        {
          type: "field_dropdown",
          name: "STEP_OP",
          options: [
            ["++", "++"],
            ["--", "--"],
            ["+=", "+="],
            ["-=", "-="],
            ["*=", "*="],
            ["/=", "/="]
          ]
        },
        { type: "field_input", name: "STEP_VAL", text: "1" },
        { type: "input_dummy" },
        { type: "input_statement", name: "BODY" }
      ],
      inputsInline: true,
      previousStatement: null,
      nextStatement: null,
      colour: "#1b6c80",
      tooltip: "For loop with configurable start, bound, and step"
    },
    {
      type: "daa_while",
      message0: "while %1 (iters: %2 ) %3 %4",
      args0: [
        { type: "input_value", name: "COND" },
        { type: "field_input", name: "ITERS", text: "n" },
        { type: "input_dummy" },
        { type: "input_statement", name: "BODY" }
      ],
      inputsInline: true,
      previousStatement: null,
      nextStatement: null,
      colour: "#1b6c80",
      tooltip: "While loop"
    },
    {
      type: "daa_if",
      message0: "if %1 %2 %3 else %4 %5",
      args0: [
        { type: "input_value", name: "COND" },
        { type: "input_dummy" },
        { type: "input_statement", name: "THEN" },
        { type: "input_dummy" },
        { type: "input_statement", name: "ELSE" }
      ],
      inputsInline: true,
      previousStatement: null,
      nextStatement: null,
      colour: "#2c7a5b",
      tooltip: "Conditional branch (leave else empty for single-branch if)"
    },
    {
      type: "daa_return",
      message0: "return %1",
      args0: [{ type: "input_value", name: "VALUE" }],
      inputsInline: true,
      previousStatement: null,
      nextStatement: null,
      colour: "#525866",
      tooltip: "Return statement"
    },
    {
      type: "daa_break",
      message0: "break",
      previousStatement: null,
      nextStatement: null,
      colour: "#8c4a32",
      tooltip: "Early loop exit (affects best vs worst case)"
    },
    {
      type: "daa_call",
      message0: "call %1 ( %2 )",
      args0: [
        { type: "field_input", name: "FUNC", text: "print" },
        { type: "field_input", name: "ARGS", text: "c" }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: "#525866",
      tooltip: "Non-recursive method call"
    },
    {
      type: "daa_recursive_call",
      message0: "recurse %1 times with subproblem %2",
      args0: [
        { type: "field_number", name: "BRANCHES", value: 1, min: 1, max: 16 },
        { type: "field_input", name: "ARG", text: "n-1" }
      ],
      previousStatement: null,
      nextStatement: null,
      colour: "#7c4d8a",
      tooltip: "Recursive self-call for recurrence analysis"
    },
    {
      type: "daa_binary",
      message0: "%1 %2 %3",
      args0: [
        { type: "input_value", name: "LEFT" },
        {
          type: "field_dropdown",
          name: "OP",
          options: [
            ["+", "+"],
            ["-", "-"],
            ["*", "*"],
            ["/", "/"],
            ["<", "<"],
            ["<=", "<="],
            [">", ">"],
            [">=", ">="],
            ["==", "=="],
            ["!=", "!="],
            ["&&", "&&"],
            ["||", "||"]
          ]
        },
        { type: "input_value", name: "RIGHT" }
      ],
      inputsInline: true,
      output: null,
      colour: "#9a6b24",
      tooltip: "Binary arithmetic, comparison, or logical expression"
    },
    {
      type: "daa_array_access",
      message0: "%1 [ %2 ]",
      args0: [
        { type: "field_input", name: "ARR", text: "array" },
        { type: "field_input", name: "IDX", text: "i" }
      ],
      output: null,
      colour: "#9a6b24",
      tooltip: "Array element access"
    },
    {
      type: "daa_var",
      message0: "%1",
      args0: [{ type: "field_input", name: "NAME", text: "sum" }],
      output: null,
      colour: "#4a6572",
      tooltip: "Variable reference"
    },
    {
      type: "daa_num",
      message0: "%1",
      args0: [{ type: "field_input", name: "VAL", text: "0" }],
      output: null,
      colour: "#4a6572",
      tooltip: "Numeric or symbolic literal"
    }
  ]);
}

const TOOLBOX = {
  kind: "categoryToolbox",
  contents: [
    {
      kind: "category",
      name: "Loops",
      colour: "#1b6c80",
      contents: [
        { kind: "block", type: "daa_for" },
        { kind: "block", type: "daa_while" },
        { kind: "block", type: "daa_break" }
      ]
    },
    {
      kind: "category",
      name: "Branches",
      colour: "#2c7a5b",
      contents: [
        { kind: "block", type: "daa_if" }
      ]
    },
    {
      kind: "category",
      name: "Variables",
      colour: "#3b6e8c",
      contents: [
        { kind: "block", type: "daa_declare" },
        { kind: "block", type: "daa_assign" },
        { kind: "block", type: "daa_unary" },
        { kind: "block", type: "daa_var" },
        { kind: "block", type: "daa_num" }
      ]
    },
    {
      kind: "category",
      name: "Expressions",
      colour: "#9a6b24",
      contents: [
        { kind: "block", type: "daa_binary" },
        { kind: "block", type: "daa_array_access" }
      ]
    },
    {
      kind: "category",
      name: "Functions",
      colour: "#525866",
      contents: [
        { kind: "block", type: "daa_function" },
        { kind: "block", type: "daa_call" },
        { kind: "block", type: "daa_return" },
        { kind: "block", type: "daa_recursive_call" }
      ]
    }
  ]
};

function serializeExpr(block) {
  if (!block) return null;
  switch (block.type) {
    case "daa_binary":
      return {
        kind: "binary",
        op: block.getFieldValue("OP"),
        left: serializeExpr(block.getInputTargetBlock("LEFT")),
        right: serializeExpr(block.getInputTargetBlock("RIGHT"))
      };
    case "daa_array_access":
      return {
        kind: "arrayAccess",
        arrayName: block.getFieldValue("ARR") || "array",
        indexExpr: block.getFieldValue("IDX") || "i"
      };
    case "daa_var":
      return { kind: "variable", name: block.getFieldValue("NAME") || "x" };
    case "daa_num":
      return { kind: "literal", value: block.getFieldValue("VAL") || "0" };
    default:
      return null;
  }
}

function serializeStatements(firstBlock) {
  const list = [];
  let cur = firstBlock;
  while (cur) {
    const stmt = serializeStmt(cur);
    if (stmt) list.push(stmt);
    cur = cur.getNextBlock();
  }
  return list;
}

function serializeStmt(block) {
  if (!block) return null;
  const id = block.id;
  switch (block.type) {
    case "daa_declare":
      return { kind: "declare", id, names: (block.getFieldValue("VARS") || "").split(",").map((s) => s.trim()).filter(Boolean) };
    case "daa_assign":
      return {
        kind: "assign",
        id,
        target: block.getFieldValue("TARGET") || "x",
        op: block.getFieldValue("OP") || "=",
        value: serializeExpr(block.getInputTargetBlock("VALUE"))
      };
    case "daa_unary":
      return {
        kind: "unary",
        id,
        variable: block.getFieldValue("VAR") || "c",
        op: block.getFieldValue("OP") || "++"
      };
    case "daa_for": {
      const v = block.getFieldValue("VAR") || "i";
      return {
        kind: "for",
        id,
        iterator: v,
        startExpr: block.getFieldValue("START") || "1",
        comparator: block.getFieldValue("CMP") || "<=",
        boundExpr: block.getFieldValue("BOUND") || "n",
        stepOp: block.getFieldValue("STEP_OP") || "++",
        stepValue: block.getFieldValue("STEP_VAL") || "1",
        body: serializeStatements(block.getInputTargetBlock("BODY"))
      };
    }
    case "daa_while":
      return {
        kind: "while",
        id,
        condition: serializeExpr(block.getInputTargetBlock("COND")),
        iterationsExpr: block.getFieldValue("ITERS") || "n",
        body: serializeStatements(block.getInputTargetBlock("BODY"))
      };
    case "daa_if":
      return {
        kind: "if",
        id,
        condition: serializeExpr(block.getInputTargetBlock("COND")),
        thenBody: serializeStatements(block.getInputTargetBlock("THEN")),
        elseBody: serializeStatements(block.getInputTargetBlock("ELSE"))
      };
    case "daa_return":
      return { kind: "return", id, value: serializeExpr(block.getInputTargetBlock("VALUE")) };
    case "daa_break":
      return { kind: "break", id };
    case "daa_call":
      return {
        kind: "call",
        id,
        functionName: block.getFieldValue("FUNC") || "print",
        args: block.getFieldValue("ARGS") || ""
      };
    case "daa_recursive_call":
      return {
        kind: "recursiveCall",
        id,
        branches: Number(block.getFieldValue("BRANCHES") || 1),
        subproblem: block.getFieldValue("ARG") || "n-1"
      };
    default:
      return null;
  }
}

export function extractAst(container) {
  const ws = workspaces.get(container);
  if (!ws) return null;
  const topBlocks = ws.getTopBlocks(true);
  const fnBlock = topBlocks.find((b) => b.type === "daa_function");
  if (fnBlock) {
    return {
      name: fnBlock.getFieldValue("NAME") || "algorithm",
      parameters: (fnBlock.getFieldValue("PARAMS") || "").split(",").map((s) => s.trim()).filter(Boolean),
      body: serializeStatements(fnBlock.getInputTargetBlock("BODY"))
    };
  }
  const statements = [];
  for (const b of topBlocks) {
    statements.push(...serializeStatements(b));
  }
  return { name: "algorithm", parameters: ["n"], body: statements };
}

export function mountBlockly(container, dotNetRef, initialStateJson, isDark) {
  if (!container) return;
  registerBlocksOnce();

  const themes = defineThemes();
  if (workspaces.has(container)) {
    workspaces.get(container).dispose();
    workspaces.delete(container);
  }

  const ws = Blockly.inject(container, {
    renderer: "desktop_zelos",
    theme: isDark ? themes.darkTheme : themes.lightTheme,
    toolbox: TOOLBOX,
    grid: { spacing: 24, length: 2, colour: isDark ? "#262629" : "#dce2e8", snap: true },
    zoom: { controls: true, wheel: true, startScale: 0.78, maxScale: 1.6, minScale: 0.45 },
    trashcan: true,
    move: { scrollbars: true, drag: true, wheel: true }
  });

  workspaces.set(container, ws);

  if (typeof ResizeObserver !== "undefined") {
    const ro = new ResizeObserver(() => {
      Blockly.svgResize(ws);
    });
    ro.observe(container);
    observers.set(container, ro);
  }

  if (initialStateJson) {
    loadWorkspaceState(container, initialStateJson);
  }

  let debounceTimer = null;
  ws.addChangeListener((event) => {
    if (event.isUiEvent) return;
    // Keep mirrored loop variable labels synced
    if (event.type === Blockly.Events.BLOCK_CHANGE && event.name === "VAR") {
      const blk = ws.getBlockById(event.blockId);
      if (blk && blk.type === "daa_for") {
        const v = blk.getFieldValue("VAR") || "i";
        blk.setFieldValue(v, "VAR_MIRROR");
        blk.setFieldValue(v, "VAR_STEP");
      }
    }
    clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => {
      const ast = extractAst(container);
      if (dotNetRef && ast) {
        dotNetRef.invokeMethodAsync("OnWorkspaceChanged", JSON.stringify(ast));
      }
    }, 120);
  });

  return extractAst(container);
}

export function loadWorkspaceState(container, stateJson) {
  const ws = workspaces.get(container);
  if (!ws || !stateJson) return;
  try {
    const parsed = typeof stateJson === "string" ? JSON.parse(stateJson) : stateJson;
    Blockly.Events.disable();
    ws.clear();
    Blockly.serialization.workspaces.load(parsed, ws);
    Blockly.Events.enable();
    ws.getToolbox()?.clearSelection();
    ws.scrollCenter();
  } catch {
    Blockly.Events.enable();
  }
}

export function setBlocklyTheme(container, isDark) {
  const ws = workspaces.get(container);
  if (!ws) return;
  const themes = defineThemes();
  ws.setTheme(isDark ? themes.darkTheme : themes.lightTheme);
}

export function highlightBlock(container, blockId) {
  const ws = workspaces.get(container);
  if (!ws) return;
  ws.highlightBlock(blockId || null);
}

export function disposeBlockly(container) {
  const ro = observers.get(container);
  if (ro) {
    ro.disconnect();
    observers.delete(container);
  }
  const ws = workspaces.get(container);
  if (ws) {
    ws.dispose();
    workspaces.delete(container);
  }
}
