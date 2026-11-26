using SuperCsStudent.Core.Workspace;

namespace SuperCsStudent.Web.Services;

public sealed record QuickCommandItem(
    string Id,
    string Title,
    string Subtitle,
    string Category,
    string Route,
    string? PresetKey = null);

public sealed class AppStateService
{
    public string Theme { get; private set; } = "light";
    public bool IsDark => Theme == "dark";
    public string EmptyStringSymbol { get; set; } = "e";
    public string BlankTapeSymbol { get; set; } = "#";
    public bool SnapMotion { get; set; } = false;
    public bool SidebarCollapsed { get; set; } = false;
    public bool CommandPaletteOpen { get; set; } = false;
    public bool SettingsOpen { get; set; } = false;
    public string? PendingPresetKey { get; set; }

    public List<WorkspaceDocument> RecentWorkspaces { get; } = new();

    public event Action? OnChange;

    public void SetTheme(string theme)
    {
        Theme = theme == "dark" ? "dark" : "light";
        Notify();
    }

    public void ToggleSidebar()
    {
        SidebarCollapsed = !SidebarCollapsed;
        Notify();
    }

    public void SetCommandPalette(bool open)
    {
        CommandPaletteOpen = open;
        Notify();
    }

    public void SetSettingsOpen(bool open)
    {
        SettingsOpen = open;
        Notify();
    }

    public void RecordRecent(WorkspaceDocument doc)
    {
        RecentWorkspaces.RemoveAll(w => w.Id == doc.Id);
        RecentWorkspaces.Insert(0, doc);
        if (RecentWorkspaces.Count > 8)
            RecentWorkspaces.RemoveAt(RecentWorkspaces.Count - 1);
        Notify();
    }

    public void Notify() => OnChange?.Invoke();

    public IReadOnlyList<QuickCommandItem> GetQuickCommands() =>
    [
        new("nav-home", "Studio Hub", "Overview and quick presets", "Navigate", "/"),
        new("nav-automata", "Automata & Formal Languages", "DFA, NFA, CFG, PDA, Turing Machine studio", "Navigate", "/automata"),
        new("nav-daa", "Algorithm Runtime Analyzer", "Block builder, cost toggles, exact T(n) & Big-O", "Navigate", "/daa"),
        new("nav-structures", "Tree & Graph Tracer", "Binary, BST, AVL, Splay, (2,4), Red-Black & Graphs", "Navigate", "/structures"),

        new("auto-palindrome", "TM Schema: Palindromes (w = w^R)", "Machine-schema diagram + macro & micro trace", "Automata", "/automata", "tm-palindrome"),
        new("auto-anbn-pda", "PDA: a^n b^n", "Empty-stack + final-state 5-column trace", "Automata", "/automata", "pda-anbn"),
        new("auto-contains-aba", "DFA + TM: Contains 'aba'", "Regex -> NFA -> Min DFA -> Decidable TM", "Automata", "/automata", "dfa-contains-aba"),
        new("auto-expr-cfg", "CFG Parse Tree: Arithmetic E/T/F", "Leftmost derivation and parse tree diagram", "Automata", "/automata", "cfg-arith"),

        new("daa-nested3", "3-Level Nested Dependent Loop", "T(n) = (2/3)n^3 + 4n^2 + (22/3)n + 3", "Runtime", "/daa", "nested-3"),
        new("daa-log", "Logarithmic Step Loop (i *= 2)", "T(n) = 4 log_2 n + 3", "Runtime", "/daa", "log-base2"),
        new("daa-findsmall", "Find Minimum with Branch", "Best vs Worst Case exact T(n) = 6n - 2", "Runtime", "/daa", "find-small"),
        new("daa-recur", "Divide & Conquer Recurrence", "T(n) = 2T(n/2) + n -> Master Theorem & Unrolling", "Runtime", "/daa", "recur-nlogn"),

        new("struct-splay", "Splay Tree Deletion Variants", "Compare BST-delete vs Splay Max-Left / Min-Right", "Structures", "/structures", "splay-delete"),
        new("struct-24", "(2,4) Tree Overflow & Fusion", "Toggle 3rd-key (k3) vs 2nd-key (k2) split rules", "Structures", "/structures", "tree24-split"),
        new("struct-rb", "Red-Black Tree + (2,4) Isomorphism", "Trinode restructuring and double-black fixes", "Structures", "/structures", "rb-demo"),
        new("struct-dijkstra", "Weighted Graph: Dijkstra & MST", "Cloud relaxation, Prim-Jarnik, Kruskal + 4 reps", "Structures", "/structures", "graph-airport")
    ];
}
