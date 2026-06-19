using System.Runtime.InteropServices;

partial class MainForm : Form
{
    // ── Controls ──────────────────────────────────────────────────────────
    MenuStrip          menuStrip  = null!;
    GraphCanvas        canvas     = null!;
    ComboBox           graphCombo = null!;
    CheckBox           toutCheck  = null!;
    CheckBox           distCheck  = null!;
    NumericUpDown      meshInput  = null!;
    NumericUpDown      nInput     = null!;
    ToolStripLabel[]   pLabels    = new ToolStripLabel[4];
    NumericUpDown[]    pInputs    = new NumericUpDown[4];

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
        RestoreSettings();

        canvas.ClientSizeChanged += (_, _) => TriggerCompute();
        canvas.MouseClick += OnCanvasClick;
        FormClosing += (_, _) => SaveSettings();
    }

    static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GraphAlgo", "settings.txt");

    void SaveSettings() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllLines(SettingsPath, [
                $"{Width} {Height}",
                $"{activeGraph}",
                $"{(toutCheck.Checked?1:0)} {(distCheck.Checked?1:0)} {(int)meshInput.Value}",
                string.Join(" ", pInputs.Select(s => (int)s.Value)),
            ]);
        } catch { }
    }

    void RestoreSettings() {
        // Lire le fichier AVANT LoadGraph qui écrase les valeurs sauvegardées
        string[]? L = null;
        try { L = File.ReadAllLines(SettingsPath); } catch { }

        int graphIdx = 0;
        if (L != null && L.Length >= 2) {
            var p0 = L[0].Split(' ');
            if (p0.Length == 2 && int.TryParse(p0[0], out int w) && int.TryParse(p0[1], out int h))
                Size = new Size(Math.Max(w, MinimumSize.Width), Math.Max(h, MinimumSize.Height));
            if (int.TryParse(L[1], out int g) && g >= 0 && g < graphs.Count)
                graphIdx = g;
        }

        LoadGraph(graphIdx);   // remet les sliders à leurs défauts (et sauvegarde)

        if (L == null) return;
        try {
            // coches + maillage
            if (L.Length >= 3) {
                var p2 = L[2].Split(' ');
                if (p2.Length == 3) {
                    toutCheck.Checked = p2[0] == "1";
                    distCheck.Checked = p2[1] == "1";
                    meshInput.Value  = Math.Clamp(int.Parse(p2[2]), (int)meshInput.Minimum, (int)meshInput.Maximum);
                }
            }
            // sliders a b c d
            if (L.Length >= 4) {
                var p3 = L[3].Split(' ');
                for (int i = 0; i < pInputs.Length && i < p3.Length; i++)
                    if (int.TryParse(p3[i], out int v))
                        pInputs[i].Value = Math.Clamp(v, (int)pInputs[i].Minimum, (int)pInputs[i].Maximum);
            }
        } catch { }
    }

    // ── Layout ────────────────────────────────────────────────────────────
    void BuildLayout()
    {
        // menu
        menuStrip = new MenuStrip { BackColor = AppConfig.Background, ForeColor = AppConfig.Text };
        graphCombo = new ComboBox {
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = AppConfig.CanvasBack, ForeColor = AppConfig.Text,
            Font = new Font("Segoe UI", 9f), Width = 360,
        };
        foreach (var g in graphs)
            graphCombo.Items.Add(g.Name);
        graphCombo.SelectedIndex = 0;
        graphCombo.SelectedIndexChanged += (_, _) => {
            if (graphCombo.SelectedIndex != activeGraph)
                LoadGraph(graphCombo.SelectedIndex);
        };
        menuStrip.Items.Add(new ToolStripControlHost(graphCombo));

        nInput = new NumericUpDown {
            Minimum = 10, Maximum = 100000, Value = 532,
            Width = 80, BackColor = AppConfig.CanvasBack, ForeColor = AppConfig.Text,
            Font = new Font("Segoe UI", 9f),
        };
        nInput.ValueChanged += (_, _) => TriggerCompute();
        menuStrip.Items.Add(new ToolStripLabel("N") { ForeColor = AppConfig.Text, Alignment = ToolStripItemAlignment.Right, Margin = new Padding(8, 0, 2, 0) });
        menuStrip.Items.Add(new ToolStripControlHost(nInput) { Alignment = ToolStripItemAlignment.Right });

        toutCheck = new CheckBox {
            Text = "Tout", ForeColor = AppConfig.Text, BackColor = AppConfig.Background,
            AutoSize = true, Padding = new Padding(6, 0, 0, 0)
        };
        toutCheck.CheckedChanged += (_, _) => TriggerCompute();
        menuStrip.Items.Add(new ToolStripControlHost(toutCheck));

        distCheck = new CheckBox {
            Text = "distance", ForeColor = AppConfig.Text, BackColor = AppConfig.Background,
            AutoSize = true, Padding = new Padding(6, 0, 0, 0)
        };
        distCheck.CheckedChanged += (_, _) => TriggerCompute();
        menuStrip.Items.Add(new ToolStripControlHost(distCheck));

        menuStrip.Items.Add(new ToolStripLabel("maillage") { ForeColor = AppConfig.Text, Margin = new Padding(8, 0, 2, 0) });
        meshInput = new NumericUpDown {
            Minimum = 1, Maximum = 10, Value = 1,
            Width = 65, BackColor = AppConfig.CanvasBack, ForeColor = AppConfig.Text,
            Font = new Font("Segoe UI", 9f),
        };
        meshInput.ValueChanged += (_, _) => TriggerCompute();
        menuStrip.Items.Add(new ToolStripControlHost(meshInput));

        for (int i = 0; i < 4; i++) {
            pLabels[i] = new ToolStripLabel("–") { ForeColor = AppConfig.Text, Margin = new Padding(8, 0, 2, 0) };
            menuStrip.Items.Add(pLabels[i]);
            pInputs[i] = new NumericUpDown {
                Minimum = 1, Maximum = 4, Value = 1,
                Width = 65, BackColor = AppConfig.CanvasBack, ForeColor = AppConfig.Text,
                Font = new Font("Segoe UI", 9f),
            };
            pInputs[i].ValueChanged += (_, _) => TriggerCompute();
            menuStrip.Items.Add(new ToolStripControlHost(pInputs[i]));
        }

        // canvas
        canvas = new GraphCanvas { Dock = DockStyle.Fill, BackColor = AppConfig.CanvasBack };
        canvas.Paint += CanvasPaint;

        Controls.Add(canvas);
        Controls.Add(menuStrip);
        MainMenuStrip = menuStrip;
    }

    // ── Graph switching ───────────────────────────────────────────────────
    void LoadGraph(int idx)
    {
        activeGraph = idx;
        var g = graphs[idx];

        if (graphCombo.SelectedIndex != idx)
            graphCombo.SelectedIndex = idx;

        for (int i = 0; i < g.Params.Length; i++) {
            var p = g.Params[i];
            pInputs[i].Minimum = p.Min;
            pInputs[i].Maximum = p.Max;
            pInputs[i].Value   = p.Default;
            pLabels[i].Text    = p.Name;
        }
        SaveSettings();
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
            int cellN = (int)nInput.Value;
            Task.Run(() => {
                var allPts = new PointF[total][];
                var opts   = new ParallelOptions { CancellationToken = token,
                                                   MaxDegreeOfParallelism = Environment.ProcessorCount };
                try {
                    Parallel.For(0, total, opts, i => allPts[i] = g.Compute(combos[i], cellW, cellH, cellN));
                } catch (OperationCanceledException) { return; }
                if (token.IsCancellationRequested) return;
                RenderGrid(allPts, cols, rows, iw, ih, token);
            }, token);
        } else {
            gridCombos = null;
            var g    = graphs[activeGraph];
            var vals = pInputs.Take(g.Params.Length).Select(s => (int)s.Value).ToArray();
            bool showDist  = distCheck.Checked;
            int  meshLevel = (int)meshInput.Value;
            int  n         = (int)nInput.Value;
            Task.Run(() => {
                if (token.IsCancellationRequested) return;
                var pts = g.Compute(vals, iw, ih, n);
                PointF[][]? meshPts = null;
                if (meshLevel > 1) {
                    meshPts = new PointF[meshLevel - 1][];
                    for (int k = 1; k < meshLevel; k++)
                        meshPts[k - 1] = g.Compute(vals, iw, ih, n, (double)k / meshLevel);
                }
                if (!token.IsCancellationRequested)
                    RenderCanvas(pts, iw, ih, token, showDist, meshPts);
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
            pInputs[i].Value = combo[i];                    // dernier TriggerCompute gagne ✓
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
