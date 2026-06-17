using System.Runtime.InteropServices;

partial class MainForm : Form
{
    // ── Controls ──────────────────────────────────────────────────────────
    MenuStrip         menuStrip = null!;
    Panel             sliderRow = null!;
    GraphCanvas       canvas    = null!;
    ToolStripMenuItem graphMenu = null!;
    CheckBox          toutCheck = null!;
    ToolStripLabel    timeLbl   = null!;
    Label[]           pLabels   = new Label[4];
    TrackBar[]        pSliders  = new TrackBar[4];

    // ── State ─────────────────────────────────────────────────────────────
    readonly List<Graph> graphs = Enumerable.Range(0, 16)
        .Select(i => new HarmoniqueGraph(i))
        .Where(g => g.IsYSymNotX && !g.IsXSinCos)
        .Select(g => (Graph)g).ToList();
    int activeGraph;
    CancellationTokenSource? cts;

    // grille "Tout" — mémorisée pour le clic
    int            gridCols, gridRows;
    List<int[]>?   gridCombos;

    // ── DWM ───────────────────────────────────────────────────────────────
    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int val, int sz);

    protected override CreateParams CreateParams {
        get { var cp = base.CreateParams; cp.ExStyle |= 0x02000000; return cp; }
    }

    protected override void OnHandleCreated(EventArgs e) {
        base.OnHandleCreated(e);
        int v = 1; DwmSetWindowAttribute(Handle, 20, ref v, 4);
        v = 2;     DwmSetWindowAttribute(Handle, 33, ref v, 4);
    }

    public MainForm()
    {
        Text        = "GraphAlgo";
        Size        = new Size(1100, 750);
        MinimumSize = new Size(700, 450);
        BackColor   = AppConfig.Background;

        BuildLayout();
        LoadGraph(0);

        canvas.ClientSizeChanged += (_, _) => TriggerCompute();
        canvas.MouseClick += OnCanvasClick;
    }

    // ── Layout ────────────────────────────────────────────────────────────
    void BuildLayout()
    {
        // menu
        menuStrip = new MenuStrip { BackColor = AppConfig.Background, ForeColor = AppConfig.Text };
        graphMenu = new ToolStripMenuItem("Graphes");
        for (int i = 0; i < graphs.Count; i++) {
            int idx = i;
            graphMenu.DropDownItems.Add(
                new ToolStripMenuItem(graphs[i].Name, null, (_, _) => LoadGraph(idx)));
        }
        menuStrip.Items.Add(graphMenu);

        timeLbl = new ToolStripLabel { ForeColor = AppConfig.Text, Alignment = ToolStripItemAlignment.Right };
        menuStrip.Items.Add(timeLbl);

        toutCheck = new CheckBox {
            Text = "Tout", ForeColor = AppConfig.Text, BackColor = AppConfig.Background,
            AutoSize = true, Padding = new Padding(6, 0, 0, 0)
        };
        toutCheck.CheckedChanged += (_, _) => TriggerCompute();
        menuStrip.Items.Add(new ToolStripControlHost(toutCheck));

        // slider row — TableLayoutPanel 2 lignes × 4 colonnes
        // ligne 0 : labels uniquement  /  ligne 1 : sliders verticaux (haut = max)
        sliderRow = new Panel { Dock = DockStyle.Top, Height = 90, BackColor = AppConfig.SliderBack };
        var table = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 4,
            BackColor = AppConfig.SliderBack, Padding = new Padding(0)
        };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));   // ligne 0 : labels
        table.RowStyles.Add(new RowStyle(SizeType.Percent,  100f));  // ligne 1 : sliders
        for (int i = 0; i < 4; i++)
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

        for (int i = 0; i < 4; i++) {
            int idx = i;
            pLabels[i] = new Label {
                Text = "–", ForeColor = AppConfig.Text, BackColor = AppConfig.SliderBack,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 9f)
            };
            pSliders[i] = new TrackBar {
                Minimum = 1, Maximum = 4, Value = 1,
                SmallChange = 1, LargeChange = 1,
                TickFrequency = 1, TickStyle = TickStyle.BottomRight,
                BackColor = AppConfig.SliderBack,
                Dock = DockStyle.Fill
            };
            pSliders[i].ValueChanged += (_, _) => {
                if (idx < graphs[activeGraph].Params.Length)
                    pLabels[idx].Text = $"{graphs[activeGraph].Params[idx].Name} = {pSliders[idx].Value}";
                TriggerCompute();
            };
            table.Controls.Add(pLabels[i],  i, 0);   // ligne 0
            table.Controls.Add(pSliders[i], i, 1);   // ligne 1
        }
        sliderRow.Controls.Add(table);

        // canvas
        canvas = new GraphCanvas { Dock = DockStyle.Fill, BackColor = AppConfig.CanvasBack };
        canvas.Paint += CanvasPaint;

        Controls.Add(canvas);
        Controls.Add(sliderRow);
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;
    }

    // ── Graph switching ───────────────────────────────────────────────────
    void LoadGraph(int idx)
    {
        activeGraph = idx;
        var g = graphs[idx];

        foreach (ToolStripMenuItem item in graphMenu.DropDownItems)
            item.Checked = false;
        ((ToolStripMenuItem)graphMenu.DropDownItems[idx]).Checked = true;

        for (int i = 0; i < g.Params.Length; i++) {
            var p = g.Params[i];
            pSliders[i].Minimum = p.Min;
            pSliders[i].Maximum = p.Max;
            pSliders[i].Value   = p.Default;
            pLabels[i].Text     = $"{p.Name} = {p.Default}";
        }
        TriggerCompute();
    }

    // ── Computation ───────────────────────────────────────────────────────
    void TriggerCompute()
    {
        cts?.Cancel();
        cts = new CancellationTokenSource();
        var token = cts.Token;
        int iw = canvas.ClientSize.Width;
        int ih = canvas.ClientSize.Height;
        if (iw <= 0 || ih <= 0) return;

        if (toutCheck.Checked) {
            var g = graphs[activeGraph];
            var combos = EnumerateCombinations(g);
            int total = combos.Count;
            int cols  = (int)Math.Ceiling(Math.Sqrt(total));
            int rows  = (int)Math.Ceiling((double)total / cols);
            int cellW = iw / cols;
            int cellH = ih / rows;
            if (cellW <= 0 || cellH <= 0) return;
            gridCols = cols; gridRows = rows; gridCombos = combos;
            int cellN = Math.Max(50, Math.Max(cellW, cellH) * 2);   // points adaptés à la taille
            Task.Run(() => {
                var allPts = new PointF[total][];
                var opts   = new ParallelOptions { CancellationToken = token,
                                                   MaxDegreeOfParallelism = Environment.ProcessorCount };
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try {
                    Parallel.For(0, total, opts, i => allPts[i] = g.Compute(combos[i], cellW, cellH, cellN));
                } catch (OperationCanceledException) { return; }
                sw.Stop();
                if (token.IsCancellationRequested) return;
                Invoke(() => timeLbl.Text = $"calcul : {sw.ElapsedMilliseconds} ms | N={cellN} | {g.FormulaTemplate}");
                RenderGrid(allPts, cols, rows, iw, ih, token);
            }, token);
        } else {
            gridCombos = null;
            var g    = graphs[activeGraph];
            var vals = pSliders.Take(g.Params.Length).Select(s => s.Value).ToArray();
            Task.Run(() => {
                if (token.IsCancellationRequested) return;
                var sw  = System.Diagnostics.Stopwatch.StartNew();
                var pts = g.Compute(vals, iw, ih);
                sw.Stop();
                if (!token.IsCancellationRequested) {
                    Invoke(() => timeLbl.Text = $"calcul : {sw.ElapsedMilliseconds} ms | N={pts.Length} | {g.Formula(vals)}");
                    RenderCanvas(pts, iw, ih, token);
                }
            }, token);
        }
    }

    void OnCanvasClick(object? sender, MouseEventArgs e)
    {
        if (gridCombos == null) return;
        int cellW = canvas.ClientSize.Width  / gridCols;
        int cellH = canvas.ClientSize.Height / gridRows;
        if (cellW <= 0 || cellH <= 0) return;

        int idx = (e.Y / cellH) * gridCols + (e.X / cellW);
        if (idx < 0 || idx >= gridCombos.Count) return;

        var combo = gridCombos[idx];
        var g     = graphs[activeGraph];

        toutCheck.Checked = false;                          // décoche → TriggerCompute (annulé ensuite)
        for (int i = 0; i < g.Params.Length && i < combo.Length; i++)
            pSliders[i].Value = combo[i];                   // dernier TriggerCompute gagne ✓
    }

    static List<int[]> EnumerateCombinations(Graph g)
    {
        var result  = new List<int[]>();
        var current = new int[g.Params.Length];
        void Rec(int depth) {
            if (depth == g.Params.Length) {
                if (!g.IsDegenerate(current)) result.Add(current.ToArray());
                return;
            }
            var p = g.Params[depth];
            for (int v = p.Min; v <= p.Max; v++) { current[depth] = v; Rec(depth + 1); }
        }
        Rec(0);
        return result;
    }
}

// ── Double-buffered canvas panel ──────────────────────────────────────────
class GraphCanvas : Panel
{
    public GraphCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint            |
                 ControlStyles.OptimizedDoubleBuffer, true);
    }
}
