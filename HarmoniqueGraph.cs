class HarmoniqueGraph : Graph
{
    readonly int variant;   // bits 0-3 : 0=cos 1=sin pour chaque position de la formule

    public HarmoniqueGraph(int variant) { this.variant = variant; }

    // x impaire (bits 0,1 différents) ET y paire (bits 2,3 identiques)
    public bool IsYSymNotX => ((variant >> 0) & 1) != ((variant >> 1) & 1)
                           && ((variant >> 2) & 1) == ((variant >> 3) & 1);

    // sin·cos exclus : x ne doit pas commencer par sin
    public bool IsXSinCos  => ((variant >> 0) & 1) == 1 && ((variant >> 1) & 1) == 0;

    public override bool IsDegenerate(int[] v) {
        bool xSq   = ((variant >> 0) & 1) == ((variant >> 1) & 1) && v[0] == v[1];
        bool ySq   = ((variant >> 2) & 1) == ((variant >> 3) & 1) && v[2] == v[3];
        bool yXSym = (v[2] + v[3]) % 2 == 1;
        // c et d interchangeables (bit2==bit3) → garder seulement c ≤ d
        if (xSq || ySq || yXSym || v[2] > v[3]) return true;

        // exclure si y descend en dessous de -0.8
        int bit2 = (variant >> 2) & 1, bit3 = (variant >> 3) & 1;
        int c = v[2], d = v[3];
        for (int t = 0; t < 1_000; t++)
            if (T(bit2, c * t) * T(bit3, d * t) < -0.8) return true;
        return false;
    }

    public override string Name => FormulaTemplate;

    public override GraphParam[] Params => [
        new("a", 1, 4, 1), new("b", 1, 4, 2),
        new("c", 1, 4, 3), new("d", 1, 4, 4),
    ];

    static double T(int bit, double x) => bit == 0 ? Math.Cos(x) : Math.Sin(x);
    static string Fn(int bit)          => bit == 0 ? "cos" : "sin";

    public override string Formula(int[] v) =>
        $"x={Fn((variant>>0)&1)}({v[0]}t)·{Fn((variant>>1)&1)}({v[1]}t)   " +
        $"y={Fn((variant>>2)&1)}({v[2]}t)·{Fn((variant>>3)&1)}({v[3]}t)";

    public override string FormulaTemplate =>
        $"x={Fn((variant>>0)&1)}(a·t)·{Fn((variant>>1)&1)}(b·t)   " +
        $"y={Fn((variant>>2)&1)}(c·t)·{Fn((variant>>3)&1)}(d·t)";

    public override PointF[] Compute(int[] v, float width, float height, int n = 0, double offset = 0.0)
    {
        double a = v[0], b = v[1], c = v[2], d = v[3];
        int N = n > 0 ? n : 1_000;
        var pts = new PointF[N];
        double cx = width  * 0.5, cy = height * 0.5;
        double rx = width  * 0.45, ry = height * 0.45;

        for (int i = 0; i < N; i++) {
            double t = i + offset;
            pts[i] = new PointF(
                (float)(cx + T((variant >> 0) & 1, a * t) * T((variant >> 1) & 1, b * t) * rx),
                (float)(cy - T((variant >> 2) & 1, c * t) * T((variant >> 3) & 1, d * t) * ry));
        }
        return pts;
    }
}
