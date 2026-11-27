# Super CS Student

An offline drafting studio for Computer Science coursework covering Automata and Formal Languages, Algorithm Runtime Analysis (DAA), and Data Structures (Trees and Graphs). Built with .NET 8 Blazor Server and a local ES module frontend bundle with no external runtime dependencies.

## Studios and Features

### 1. Automata and Formal Languages (`/automata`)

- **DFA and NFA Studios:** Dedicated tabs for deterministic and nondeterministic finite automata with text constraint synthesis, Kleene star/plus expressions, negation-to-DFA, subset construction, Hopcroft minimization, regular-grammar conversion, live string checking, and step-by-step execution playback.
- **CFG and Derivation:** General Earley parser supporting left recursion, epsilon rules, cyclic grammars, and ambiguity. Displays an animated horizontal step-by-step derivation chain (`S => aSb => aaSbb => aabb`), highlights the active production in `G = (V, Sigma, R, S)`, tracks matched target characters, toggles to an SVG parse tree on demand, and provides a step-by-step **Rejection Walkthrough** explaining why every production rule fails at the dead end for rejected strings. Supports CFG closure operations and conversion to PDA or NFA.
- **Pushdown Automaton (PDA):** Generalized 6-tuple PDA tracer with an interactive LIFO stack visualizer and step evolution strip.
- **Turing Machine:** Schematic macro parser and compiler (`R`, `L`, `R_#`, `L_#`, `S_L`, and custom macros) with live tape visualization and interactive state schema diagrams.
- **Interactive Diagram Canvas:** Classic straight/curved color-coded arrows by default, with a per-arrow `Classic | Blocky` toggle (5px-rounded 90-degree orthogonal routing) and Draw.io-style draggable, pinnable bend-point vertices (`+ Pin`, drag midpoint handles, `Backspace` to remove pins). Includes zoom, fit, reset, and clean SVG/PNG export.

### 2. Algorithm Runtime Analyzer (`/daa`)

- **Visual Blockly Builder and Code Ledger:** Snap loop and statement blocks in a horizontal Blockly workspace paired with a synchronized grid code ledger and KaTeX derivation view.
- **Color-Coded Inner Loops:** Nested loops are highlighted with stacked scope rails synchronized between the Code and Derivation tabs.
- **Exact Symbolic Summation Engine:** Computes exact rational runtime formulas (`T(n)`) using arbitrary-precision rational arithmetic (`BigRational`), symbolic polynomials/logarithms (`SymbolicExpr`), and customizable elementary operation weights (`CostRulesProfile`).
- **Asymptotic and Recurrence Analysis:** Classifies Big-O / Big-Theta bounds, solves divide-and-conquer recurrences (Master Theorem and recursion tree), and plots comparative growth curves with D3.

### 3. Tree and Graph Tracer (`/structures`)

- **Balanced and Unbalanced Trees:** General Binary Tree, BST, AVL, Splay, `(2,4)` Tree, and Red-Black Tree with batch script execution, single `Insert` / `Delete` / `Search`, click-select + `Backspace` node deletion, synchronized pseudocode, and step-linked operation logs.
- **Configurable Rulesets:** Switch between course and textbook conventions (in-order successor vs predecessor deletion, Splay deletion strategies, `(2,4)` split promotion and underflow sibling priority, AVL equal-height tie-breaking, height base `Leaf=1` vs `Leaf=0`, and optional external sentinel leaves).
- **Red-Black Optional Compare View:** Starts full-width by default with a toolbar toggle (`Compare: Off | (2,4) Tree | Splay Tree`) that renders a lockstep side-by-side `(2,4)` isomorphism or Splay tree executing the exact same operation sequence.
- **Recursive-Walk Traversal Animations:** Floating `Traverse` menu across Binary, BST, AVL, Splay, and Red-Black trees animating Pre-order, In-order, Post-order, Level-order (BFS), and Euler Tour along actual tree edges.
- **Graph Algorithms and Editing:** Step through BFS (queue and level-by-level), DFS (recursive and iterative stack with edge classification), Dijkstra shortest path, Prim-Jarnik MST, and Kruskal MST alongside four synchronized representations (Edge List, Adjacency List, Adjacency Matrix, Incidence Matrix). Supports Draw.io-style hover perimeter ports to drag-connect edges, double-click canvas to add vertices, per-arrow `Classic | Blocky` and pinned waypoints, and `Backspace` deletion.

### Shared Studio Controls

- **Unified Playback Bar:** Step backward/forward (`Left` / `Right`), play/pause (`Space`), timeline scrubber, playback speed (`0.25x` to `4x`), and `Snap` mode to disable motion.
- **Navigation and Persistence:** Command palette (`Cmd+K` / `Ctrl+K`), Light and Dark themes, collapsible inspector (`Full canvas`), and local session history with JSON backup/import.

## Solution Structure

```text
SuperCsStudent.sln
├── src/
│   ├── SuperCsStudent.Core/        # Shared diagram models, tidy-tree & graph layouts, BigRational, TraceTimeline
│   ├── SuperCsStudent.Algorithms/  # AST, symbolic summation engine, iterative & recurrence analyzers, cost profiles
│   ├── SuperCsStudent.Automata/    # DFA, NFA, regex/constraint synthesis, CFG Earley parser, PDA, TM schemas
│   ├── SuperCsStudent.Structures/  # Binary, BST, AVL, Splay, (2,4), Red-Black, traversal walker, Graph algorithms
│   └── SuperCsStudent.Web/         # Blazor Server UI, Tailwind v4 styles, esbuild JS modules (SVG, Blockly, D3, KaTeX)
└── tests/
    ├── SuperCsStudent.Algorithms.Tests/  # xUnit tests for symbolic engine, cost rules, iterative & recurrence analysis
    ├── SuperCsStudent.Automata.Tests/    # xUnit tests for DFA/NFA, synthesis, CFG derivations/rejections, PDA, TM
    ├── SuperCsStudent.Structures.Tests/  # xUnit tests for all trees, traversals, graphs, and presets
    └── web-scripts/                      # Node test suite for the SVG orthogonal & waypoint arrow router
```

## Tech Stack

- **Backend and UI:** .NET 8, ASP.NET Core Blazor Server, C#
- **Styling and Bundling:** Tailwind CSS v4 (`@tailwindcss/cli`), esbuild
- **Client Libraries:** Blockly (`@blockly/continuous-toolbox`), GSAP, KaTeX, D3 (`d3-scale`, `d3-shape`)
- **Testing:** xUnit (.NET) and Node built-in test runner (`node --test`)

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/) (needed only when modifying CSS or JS assets, or running JS router unit tests)

## Getting Started

1. **Clone the repository:**

   ```bash
   git clone https://github.com/Ycany-Sanchez/SuperCS-Student.git
   cd SuperCS-Student
   ```

2. **Install frontend dependencies and build web assets** (required if editing files under `src/SuperCsStudent.Web/Scripts/` or `Styles/`):

   ```bash
   npm --prefix src/SuperCsStudent.Web install
   npm --prefix src/SuperCsStudent.Web run build:assets
   ```

3. **Run the application:**

   ```bash
   dotnet run --project src/SuperCsStudent.Web/SuperCsStudent.Web.csproj
   ```

   Open `http://localhost:5164` (or `https://localhost:7050`) in your browser.

### Frontend Asset Scripts

Run from the repository root:

- `npm --prefix src/SuperCsStudent.Web run build:css` -- compile minified Tailwind CSS into `wwwroot/css/app.css`
- `npm --prefix src/SuperCsStudent.Web run build:js` -- bundle ES modules with esbuild into `wwwroot/js/main.js`
- `npm --prefix src/SuperCsStudent.Web run copy:katex` -- copy KaTeX stylesheet and fonts into `wwwroot/vendor/katex/`
- `npm --prefix src/SuperCsStudent.Web run build:assets` -- run all three asset tasks

## Running Tests

Run the full .NET test suite (Algorithms, Automata, and Structures):

```bash
dotnet test SuperCsStudent.sln
```

Run the JavaScript arrow router tests:

```bash
node --test tests/web-scripts/orthoRouter.test.mjs
```
