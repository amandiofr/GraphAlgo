using System.Drawing.Drawing2D;
using System.Diagnostics;

partial class MainForm
{
    Bitmap? canvasBmp;

    void RenderCanvas(PointF[] pts, int w, int h, CancellationToken token, bool showDist = false)
    {
        if (w <= 0 || h <= 0 || token.IsCancellationRequested) return;

        var bmp = new Bitmap(w, h);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(AppConfig.CanvasBack);

        if (pts.Length >= 2) {
            using var pen = new Pen(AppConfig.CurveColor, 1.2f);
            const int Chunk = 8192;
            for (int i = 0; i + 1 < pts.Length; i += Chunk - 1) {
                if (token.IsCancellationRequested) { bmp.Dispose(); return; }
                int len = Math.Min(Chunk, pts.Length - i);
                if (len < 2) break;
                g.DrawLines(pen, pts[i..(i + len)]);
            }
        }

        if (showDist && pts.Length >= 2) {
            float refX = pts[0].X, refY = pts[0].Y;
            var dists = new float[pts.Length];
            float maxD = 0f;
            for (int i = 0; i < pts.Length; i++) {
                float dx = pts[i].X - refX, dy = pts[i].Y - refY;
                dists[i] = (float)Math.Sqrt(dx * dx + dy * dy);
                if (dists[i] > maxD) maxD = dists[i];
            }
            if (maxD > 0.001f) {
                var distPts = new PointF[pts.Length];
                float xScale = (w - 1f) / (pts.Length - 1);
                float yScale  = (h - 4f) / maxD;
                for (int i = 0; i < pts.Length; i++)
                    distPts[i] = new PointF(i * xScale, h - 2f - dists[i] * yScale);
                using var distPen = new Pen(Color.FromArgb(255, 160, 40), 1f);
                const int Chunk = 8192;
                for (int i = 0; i + 1 < distPts.Length; i += Chunk - 1) {
                    if (token.IsCancellationRequested) { bmp.Dispose(); return; }
                    int len = Math.Min(Chunk, distPts.Length - i);
                    if (len < 2) break;
                    g.DrawLines(distPen, distPts[i..(i + len)]);
                }
            }
        }

        if (token.IsCancellationRequested) { bmp.Dispose(); return; }
        canvas.Invoke(() => {
            var old = canvasBmp; canvasBmp = bmp; old?.Dispose();
            canvas.Invalidate();
        });
    }

    void RenderGrid(PointF[][] allPts, int cols, int rows, int w, int h, CancellationToken token)
    {
        if (w <= 0 || h <= 0 || token.IsCancellationRequested) return;

        int cellW = w / cols;
        int cellH = h / rows;

        var sw  = Stopwatch.StartNew();
        var bmp = new Bitmap(w, h);
        using var g   = Graphics.FromImage(bmp);
        using var pen = new Pen(AppConfig.CurveColor, 0.8f);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(AppConfig.CanvasBack);

        for (int i = 0; i < allPts.Length; i++) {
            if (token.IsCancellationRequested) { bmp.Dispose(); return; }

            int col = i % cols, row = i / cols;
            float ox = col * cellW, oy = row * cellH;

            g.ResetTransform();
            g.ResetClip();
            g.SetClip(new RectangleF(ox, oy, cellW, cellH));
            g.TranslateTransform(ox, oy);

            var pts = allPts[i];
            if (pts.Length >= 2) g.DrawLines(pen, pts);
        }
        g.ResetTransform();
        g.ResetClip();
        sw.Stop();

        if (token.IsCancellationRequested) { bmp.Dispose(); return; }
        long renderMs = sw.ElapsedMilliseconds;
        canvas.Invoke(() => {
            var old = canvasBmp; canvasBmp = bmp; old?.Dispose();
            canvas.Invalidate();
        });
        Invoke(() => {
            string t  = timeLbl.Text ?? "";
            int    ri = t.LastIndexOf(" | rendu");
            timeLbl.Text = (ri >= 0 ? t[..ri] : t) + $" | rendu : {renderMs} ms";
        });
    }

    void CanvasPaint(object? sender, PaintEventArgs e)
    {
        e.Graphics.Clear(AppConfig.CanvasBack);
        if (canvasBmp is { } bmp)
            e.Graphics.DrawImageUnscaled(bmp, 0, 0);
    }
}
