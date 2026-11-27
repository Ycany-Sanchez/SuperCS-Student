# Super CS Student

An offline web studio for Computer Science coursework covering Automata and Formal Languages, Algorithm Runtime Analysis, and Data Structures. Built with .NET 8 Blazor Server.

## What It Includes

### Automata and Formal Languages (`/automata`)

- **DFA and NFA:** Build automata from rules or language constraints, run subset construction and minimization, convert to regular grammars, and test strings step by step.
- **CFG and Derivation:** Parse context-free grammars, step through leftmost derivations and parse trees, inspect step-by-step explanations when an input is rejected, and convert grammars to PDAs or NFAs.
- **Pushdown Automata (PDA):** Trace 6-tuple PDAs with a live LIFO stack view.
- **Turing Machines:** Author and run schematic Turing machines with a live tape trace and interactive state diagram.

### Algorithm Runtime Analyzer (`/daa`)

- **Visual Block Builder:** Construct algorithms using Blockly blocks alongside synchronized code and derivation views.
- **Symbolic Runtime Analysis:** Compute exact runtime expressions, customize operation cost rules, solve recurrences, and compare asymptotic growth curves.

### Tree and Graph Tracer (`/structures`)

- **Trees:** Interactive tracing for Binary Trees, BST, AVL, Splay, (2,4), and Red-Black trees, including configurable textbook rulesets, animated traversals, and side-by-side comparison views for Red-Black trees.
- **Graphs:** Step through BFS, DFS, Dijkstra, Prim-Jarnik, and Kruskal with live graph representations and interactive canvas editing.

### General Controls

- Unified step playback (play/pause, step controls, speed, and instant snap mode).
- Interactive SVG canvases with pan, zoom, layout editing, and SVG/PNG export.
- Command palette (`Cmd+K` / `Ctrl+K`), light/dark themes, and local session history with JSON backup.

## Project Structure

```text
SuperCsStudent.sln
├── src/
│   ├── SuperCsStudent.Core/        # Shared diagram, layout, math, and tracing primitives
│   ├── SuperCsStudent.Algorithms/  # AST, symbolic summation, and recurrence analysis
│   ├── SuperCsStudent.Automata/    # DFA, NFA, CFG, PDA, and Turing machine engines
│   ├── SuperCsStudent.Structures/  # Tree and graph data structures and tracers
│   └── SuperCsStudent.Web/         # Blazor Server application and frontend assets
└── tests/
    ├── SuperCsStudent.Algorithms.Tests/
    ├── SuperCsStudent.Automata.Tests/
    ├── SuperCsStudent.Structures.Tests/
    └── web-scripts/
```

## Tech Stack

- .NET 8, ASP.NET Core Blazor Server
- Tailwind CSS v4, esbuild
- Blockly, GSAP, KaTeX, D3
- xUnit and Node test runner

## Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Node.js 20+](https://nodejs.org/) (optional; required only when rebuilding frontend CSS/JS assets or running JS tests)

### Run Locally

```bash
git clone https://github.com/Ycany-Sanchez/SuperCS-Student.git
cd SuperCS-Student
dotnet run --project src/SuperCsStudent.Web/SuperCsStudent.Web.csproj
```

Then open `http://localhost:5164` in your browser.

### Rebuild Frontend Assets (Optional)

```bash
npm --prefix src/SuperCsStudent.Web install
npm --prefix src/SuperCsStudent.Web run build:assets
```

### Run Tests

```bash
dotnet test SuperCsStudent.sln
node --test tests/web-scripts/orthoRouter.test.mjs
```
