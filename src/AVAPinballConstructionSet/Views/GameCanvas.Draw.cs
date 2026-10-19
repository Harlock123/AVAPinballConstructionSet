using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAPinballConstructionSet.Engine;

namespace AVAPinballConstructionSet.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Dictionary<(uint, double, int), Pen> DashCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAPinballConstructionSet/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        return p;
    }

    /// <summary>A dashed pen; the dashes march along as <paramref name="phase"/> goes up.</summary>
    private static Pen Dash(uint argb, double width, double phase)
    {
        int step = (int)(phase * 8) % 8;
        if (!DashCache.TryGetValue((argb, width, step), out var p))
            DashCache[(argb, width, step)] = p = new Pen(Brush(argb), width, new DashStyle([3, 2.5], step * 0.6875), PenLineCap.Flat);
        return p;
    }

    private const uint White = 0xFFF4F0E4, Yellow = 0xFFFFD83A, Red = 0xFFF04838, Dim = 0xFF8A94B8, Cyan = 0xFF34C8D8, Green = 0xFF5AD05A, Orange = 0xFFFF9A2A;
    private const uint Ink = 0xFFE8E4D8, PanelBack = 0xFF1A1E34, PanelEdge = 0xFF3A4270, Cabinet = 0xFF0C0A16;
    /// <summary>The 80s stripes: red, orange, yellow, green, blue.</summary>
    private static readonly uint[] Stripes = [0xFFF04838, 0xFFFF9A2A, 0xFFFFD83A, 0xFF5AD05A, 0xFF4A7AF0];

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + size / 14, at.Y - ft.Height / 2 + size / 14));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static double TextWidth(string text, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brushes.White).Width;

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        if (pts.Length < 2) return;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static void Polyline(DrawingContext dc, Pen pen, IReadOnlyList<Point> pts)
    {
        if (pts.Count < 2) return;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], false);
            for (int i = 1; i < pts.Count; i++) c.LineTo(pts[i]);
            c.EndFigure(false);
        }
        dc.DrawGeometry(null, pen, g);
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private static uint Blend(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return 0xFF000000 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static uint Shade(uint c, double k)
    {
        uint Ch(int shift) => (uint)Math.Clamp(Math.Round(((c >> shift) & 0xFF) * k), 0, 255);
        return (c & 0xFF000000) | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    /// <summary>How light a colour is (0 to 1).</summary>
    private static double Light(uint c) => (((c >> 16) & 0xFF) * 0.3 + ((c >> 8) & 0xFF) * 0.59 + (c & 0xFF) * 0.11) / 255;

    private static Point P(Vec v) => new(v.X, v.Y);

    /// <summary>A place on the screen that answers a click.</summary>
    private void Hit(Rect r, Action a) => _hits.Add((r, a));

    /// <summary>A button: its face, its label, and what it does when clicked.</summary>
    private void Button(DrawingContext dc, Rect r, string label, Action act, bool on = false, double size = 20, bool enabled = true, uint? accent = null)
    {
        bool hover = r.Contains(_mouse) && enabled;
        uint face = on ? 0xFF5A4A1C : !enabled ? 0xFF181C2C : hover ? 0xFF323E68 : 0xFF262E50;
        dc.FillRectangle(Brush(face), r, 5);
        dc.DrawRectangle(null, Pen(on ? Yellow : accent ?? (enabled ? 0xFF5A6AA0 : 0xFF2A3050), 1.5), r, 5, 5);
        Text(dc, label, new Point(r.X + r.Width / 2, r.Y + r.Height / 2), size, on ? Yellow : enabled ? accent ?? Ink : 0xFF4A5070, center: true);
        if (enabled) Hit(r, act);
    }

    private static void Panel(DrawingContext dc, Rect r, uint back = PanelBack)
    {
        dc.FillRectangle(Brush(back), r, 8);
        dc.DrawRectangle(null, Pen(PanelEdge, 1.5), r, 8, 8);
    }

    private bool Blink(double rate = 3) => (int)(_clock * rate) % 2 == 0;

    // ───────────────────────────── the screen ─────────────────────────────

    private void DrawScreen(DrawingContext dc)
    {
        _hits.Clear();
        var g = Engine;
        dc.FillRectangle(Brush(Cabinet), new Rect(0, 0, Field.Width, Field.Height));
        // The cabinet round the table: wood, with a bright rail.
        var rail = TableRect.Inflate(9);
        dc.FillRectangle(Brush(0xFF3A2414), rail, 10);
        dc.DrawRectangle(null, Pen(0xFF6A4424, 2), rail, 10, 10);
        dc.DrawRectangle(null, Pen(0xFF1A0E06, 2), TableRect.Inflate(2), 4, 4);
        switch (g.State)
        {
            case GameState.Title:
                DrawTable(dc, g.World.Table, g.World, false);
                DrawTitle(dc);
                break;
            case GameState.Editing:
                DrawTable(dc, g.Editor.Table, null, true);
                DrawEditor(dc);
                break;
            case GameState.EnterInitials:
                DrawTable(dc, g.World.Table, g.World, false);
                DrawInitials(dc);
                break;
            case GameState.HighScores:
                DrawTable(dc, g.World.Table, g.World, false);
                DrawHighScores(dc);
                break;
            default:
                DrawTable(dc, g.World.Table, g.World, false);
                DrawBackglass(dc);
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0xC0000000), TableRect);
            Text(dc, "PAUSED", new Point(TableRect.Center.X, 320), 72, Yellow, center: true);
            Text(dc, "ANY KEY TO PLAY ON", new Point(TableRect.Center.X, 380), 26, White, center: true);
            Text(dc, "ESC: BACK TO THE CONSTRUCTION SET", new Point(TableRect.Center.X, 410), 22, Dim, center: true);
        }
        if (g.State == GameState.GameOver)
        {
            dc.FillRectangle(Brush(0x90000000), TableRect);
            Text(dc, "GAME OVER", new Point(TableRect.Center.X, 330), 72, Blink(4) ? Yellow : White, center: true);
        }
    }

    // ───────────────────────────── the table ─────────────────────────────

    /// <summary>
    /// The table: its playfield, its frame and plunger, its parts and walls, the balls and the scores floating up;
    /// while building, the rules' wires, the part in hand and whatever is being drawn.
    /// </summary>
    private void DrawTable(DrawingContext dc, Table t, World? w, bool editing)
    {
        using var clip = dc.PushClip(TableRect);
        var (o, z) = View();
        var shake = w is { Shake: > 0 } ? w.ShakeDir * (4 * w.Shake / 0.2) : default;
        var m = Matrix.CreateTranslation(-o.X + shake.X, -o.Y + shake.Y) * Matrix.CreateScale(z, z) * Matrix.CreateTranslation(TableRect.X, TableRect.Y);
        using var tf = dc.PushTransform(m);
        DrawPlayfield(dc, t, editing);
        var ed = Engine.Editor;
        var order = Enumerable.Range(0, t.Parts.Count).OrderBy(i => Layer(t.Parts[i].Kind)).ToList();
        foreach (int i in order)
        {
            if (editing && ed.Carried == t.Parts[i] && ed.CarriedIsNew) continue;
            DrawPart(dc, t.Parts[i], w?.States.ElementAtOrDefault(i), editing && ed.Selected == i);
        }
        DrawFrame(dc, w);
        if (w != null)
        {
            foreach (var b in w.Balls) DrawBall(dc, b.P, b.Held >= 0);
            foreach (var pop in w.Pops)
                Text(dc, pop.Points.ToString("N0", CultureInfo.InvariantCulture), new Point(pop.At.X, pop.At.Y - 16 - pop.T * 30), 18, Alpha(Yellow, 1.3 - pop.T), center: true);
            if (w.Message is { } msg && Engine.State is GameState.Playing or GameState.Attract)
            {
                double size = msg.Length > 14 ? 34 : 48;
                dc.FillRectangle(Brush(0xA0000000), new Rect(20, 470, Field.LaneX - 40, size + 14), 8);
                Text(dc, msg, new Point(Field.LaneX / 2, 477 + size / 2), size, msg == "TILT" ? Red : Yellow, center: true);
            }
        }
        if (editing) DrawEditing(dc, t);
    }

    /// <summary>Which parts are drawn over which: walls at the bottom, flippers on top.</summary>
    private static int Layer(PartKind k) => k switch
    {
        PartKind.Shape => 0, PartKind.Magnet => 1, PartKind.Curve or PartKind.Wall => 2, PartKind.Rollover or PartKind.Lane or PartKind.Kickout or PartKind.Eater => 3,
        PartKind.FlipperLeft or PartKind.FlipperRight => 6, PartKind.Bumper => 5, _ => 4,
    };

    private void DrawPlayfield(DrawingContext dc, Table t, bool editing)
    {
        uint field = t.Playfield;
        // A gentle light from the top of the table.
        for (int i = 0; i < 14; i++)
            dc.FillRectangle(Brush(Blend(Shade(field, 1.25), Shade(field, 0.8), i / 13.0)), new Rect(0, i * 52, Field.TableW, 53));
        bool light = Light(field) > 0.55;
        uint dot = light ? Alpha(0xFF000000, 0.18) : Alpha(0xFFFFFFFF, 0.13);
        if (editing)
            for (double y = 20; y < Field.TableH; y += 20)
            for (double x = 20; x < Field.LaneX; x += 20)
                dc.FillRectangle(Brush(dot), new Rect(x - 0.6, y - 0.6, 1.2, 1.2));
        // The drain: an arrow in the middle of the bottom, and the table's name in the apron.
        uint apron = Alpha(light ? 0xFF000000 : 0xFFFFFFFF, 0.16);
        Poly(dc, apron, null, 0, new Point(196 - 10, 700), new Point(196 + 10, 700), new Point(196, 714));
        Text(dc, t.Name, new Point(196, 722), 16, apron, center: true, shadow: false);
    }

    /// <summary>The frame: the sides and the rounded top, the shooter lane and its gate, the plunger pulled back.</summary>
    private void DrawFrame(DrawingContext dc, World? w)
    {
        double W = Field.TableW, H = Field.TableH, c = Field.Corner;
        // Outside the rounded corners: the cabinet's wood.
        foreach (var (cx, a0) in new[] { (c, Math.PI), (W - c, 1.5 * Math.PI) })
        {
            var pts = new List<Point> { new(cx == c ? 0 : W, 0) };
            for (int i = 0; i <= 16; i++) pts.Add(P(new Vec(cx, c) + Vec.Polar(a0 + Math.PI / 2 * i / 16, c)));
            Poly(dc, 0xFF3A2414, null, 0, pts.ToArray());
        }
        uint rail = 0xFFD8D0BC;
        var arcs = new List<Point>();
        for (int i = 0; i <= 24; i++) arcs.Add(P(new Vec(c, c) + Vec.Polar(Math.PI + Math.PI / 2 * i / 24, c)));
        for (int i = 0; i <= 24; i++) arcs.Add(P(new Vec(W - c, c) + Vec.Polar(1.5 * Math.PI + Math.PI / 2 * i / 24, c)));
        Polyline(dc, Pen(rail, 5), [new Point(1.5, H), new Point(1.5, c), .. arcs.Select(p => new Point(Math.Clamp(p.X, 1.5, W - 1.5), Math.Max(1.5, p.Y))), new Point(W - 1.5, c), new Point(W - 1.5, H)]);
        // The shooter lane.
        dc.FillRectangle(Brush(0x40000000), new Rect(Field.LaneX, Field.LaneTop, W - Field.LaneX, H - Field.LaneTop));
        dc.DrawLine(Pen(rail, 4), new Point(Field.LaneX, H), new Point(Field.LaneX, Field.LaneTop));
        dc.DrawLine(Pen(0xFF8A8270, 2.5), new Point(W, Field.LaneTop - 34), new Point(Field.LaneX, Field.LaneTop));
        // The plunger: a knob on a spring, pulled back.
        double pull = w?.Pull ?? 0, top = Field.PlungerY + pull * 18, x = (Field.LaneX + W) / 2;
        var spring = new List<Point>();
        for (int i = 0; i <= 10; i++) spring.Add(new Point(x + (i % 2 == 0 ? -6 : 6), top + 4 + (H - top - 4) * i / 10));
        Polyline(dc, Pen(0xFFA0A0B0, 1.5), spring);
        dc.FillRectangle(Brush(0xFFB0283A), new Rect(Field.LaneX + 3, top - 1, W - Field.LaneX - 6, 6), 2);
        dc.FillRectangle(Brush(0xFFF06878), new Rect(Field.LaneX + 4, top - 1, W - Field.LaneX - 8, 1.5));
        if (pull > 0)
        {
            dc.FillRectangle(Brush(0xFF202430), new Rect(Field.LaneX - 14, 560, 8, 120), 3);
            dc.FillRectangle(Brush(pull > 0.95 ? Red : Yellow), new Rect(Field.LaneX - 13, 679 - 118 * pull, 6, 118 * pull), 2);
        }
    }

    private static void DrawBall(DrawingContext dc, Vec at, bool held)
    {
        double r = Field.BallR;
        dc.FillEllipse(at.X + 2, at.Y + 3, r, 0x60000000);
        dc.FillEllipse(at.X, at.Y, r, held ? 0xFF8A8E9A : 0xFF7A7E8C);
        dc.FillEllipse(at.X - 0.6, at.Y - 0.8, r - 1.2, 0xFFC4C8D4);
        dc.FillEllipse(at.X - 1.4, at.Y - 1.8, r - 3.2, 0xFFE0E4EE);
        dc.FillEllipse(at.X - 2.6, at.Y - 2.8, 1.9, 0xFFFFFFFF);
    }

    // ───────────────────────────── the parts ─────────────────────────────

    /// <summary>The ring round a part that's lit, blinking; the lamp that's on when it's been collected for a rule.</summary>
    private bool LitNow(PartState? st) => st is { Lit: true } && Blink(5);

    private static uint Lamp(uint colour, bool on) => on ? Blend(colour, 0xFFFFFFFF, 0.45) : Shade(colour, 0.42);

    /// <summary>A part where it stands, as the state it's in shows it (null: as it comes out of the box).</summary>
    private void DrawPart(DrawingContext dc, Part p, PartState? st, bool selected, bool icon = false)
    {
        uint col = p.Colour;
        double flash = st?.Flash ?? 0;
        bool lit = LitNow(st), on = st is { Collected: true } || lit;
        if (st is { Lit: true }) dc.FillEllipse(p.X, p.Y, 24, Alpha(Yellow, lit ? 0.32 : 0.12));
        switch (p.Kind)
        {
            case PartKind.FlipperLeft or PartKind.FlipperRight:
            {
                double angle = st?.Angle ?? Sim.Rest(p).Down;
                var outline = FlipperOutline(p.At, angle);
                Poly(dc, 0x50000000, null, 0, outline.Select(q => new Point(q.X + 2, q.Y + 3)).ToArray());
                Poly(dc, col, Shade(col, 0.55), 2, outline);
                var tipLine = p.At + Vec.Polar(angle, Parts.FlipperLength - 4);
                dc.DrawLine(Pen(Alpha(0xFFFFFFFF, 0.35), 1.5), P(p.At + Vec.Polar(angle - 1.2, 4)), P(tipLine + Vec.Polar(angle - 1.4, 2)));
                dc.FillEllipse(p.X, p.Y, 3, Shade(col, 0.45));
                break;
            }
            case PartKind.Bumper:
            {
                double R = Parts.BumperR;
                if (flash > 0) dc.FillEllipse(p.X, p.Y, R + 10, Alpha(Blend(col, 0xFFFFFFFF, 0.5), flash * 1.6));
                dc.FillEllipse(p.X + 2, p.Y + 3, R, 0x60000000);
                dc.FillEllipse(p.X, p.Y, R, Shade(col, 0.55));
                dc.DrawEllipse(null, Pen(0xFFF8F4EC, 2.5), new Point(p.X, p.Y), R - 1.5, R - 1.5);
                uint cap = flash > 0 ? Blend(col, 0xFFFFFFFF, 0.7) : col;
                dc.FillEllipse(p.X, p.Y - 1, R - 5, cap);
                Star(dc, new Point(p.X, p.Y - 1), 6, 2.6, flash > 0 ? col : 0xFFFFFFFF);
                dc.FillEllipse(p.X - 4, p.Y - 6, 2.2, Alpha(0xFFFFFFFF, 0.7));
                break;
            }
            case PartKind.Post:
                dc.FillEllipse(p.X + 1.5, p.Y + 2, Parts.PostR, 0x60000000);
                dc.FillEllipse(p.X, p.Y, Parts.PostR, flash > 0 ? 0xFFFFFFFF : 0xFFE8E4DA);
                dc.FillEllipse(p.X, p.Y, 2.4, col == 0xFFF4F0E4 ? 0xFFF04838 : col);
                break;
            case PartKind.SlingLeft or PartKind.SlingRight:
            {
                var c = Parts.Sling(p.Kind).Select(v => P(p.Local(v.X, v.Y))).ToArray();
                Poly(dc, 0x50000000, null, 0, c.Select(q => new Point(q.X + 2, q.Y + 3)).ToArray());
                Poly(dc, Shade(col, 0.8), Shade(col, 0.5), 1.5, c);
                // A lamp in the middle, and the white rubber along the kicking face.
                var mid = new Point((c[0].X + c[1].X + c[2].X) / 3, (c[0].Y + c[1].Y + c[2].Y) / 3);
                Poly(dc, flash > 0 ? 0xFFFFFFFF : Blend(col, 0xFFFFFFFF, 0.35), null, 0, Lerp(c[0], mid, 0.45), Lerp(c[1], mid, 0.45), Lerp(c[2], mid, 0.45));
                dc.DrawLine(Pen(flash > 0 ? Yellow : 0xFFF8F4EC, flash > 0 ? 4.5 : 3.5), c[0], c[2]);
                dc.DrawLine(Pen(0xFFF8F4EC, 2), c[0], c[1]);
                dc.DrawLine(Pen(0xFFF8F4EC, 2), c[1], c[2]);
                foreach (var q in c) dc.FillEllipse(q.X, q.Y, 3, 0xFFE8E4DA);
                break;
            }
            case PartKind.Rollover:
            {
                uint lamp = Lamp(col, on || flash > 0);
                Poly(dc, lamp, Shade(col, 0.3), 1.5, P(p.Local(0, -9)), P(p.Local(7, 0)), P(p.Local(0, 9)), P(p.Local(-7, 0)));
                dc.FillEllipse(p.X, p.Y, 2.4, on ? 0xFFFFFFFF : Shade(col, 0.8));
                break;
            }
            case PartKind.Lane:
            {
                foreach (int side in new[] { -1, 1 })
                {
                    dc.DrawLine(Pen(0x50000000, 5), P(p.Local(16 * side + 2, -20 + 3)), P(p.Local(16 * side + 2, 20 + 3)));
                    dc.DrawLine(Pen(col, 5), P(p.Local(16 * side, -20)), P(p.Local(16 * side, 20)));
                    dc.DrawLine(Pen(Blend(col, 0xFFFFFFFF, 0.6), 1.2), P(p.Local(16 * side - 1, -19)), P(p.Local(16 * side - 1, 19)));
                }
                uint lamp = Lamp(col, on || flash > 0);
                dc.FillEllipse(p.Local(0, 6).X, p.Local(0, 6).Y, 5.5, lamp);
                Poly(dc, lamp, null, 0, P(p.Local(-5, -6)), P(p.Local(0, -13)), P(p.Local(5, -6)));
                break;
            }
            case PartKind.DropTarget:
            {
                double hw = Parts.TargetHalf;
                if (st is { Down: true })
                {
                    Poly(dc, 0x60000000, Shade(col, 0.5), 1, P(p.Local(-hw, -2)), P(p.Local(hw, -2)), P(p.Local(hw, 2)), P(p.Local(-hw, 2)));
                    break;
                }
                Poly(dc, 0x50000000, null, 0, P(p.Local(-hw + 2, -1)), P(p.Local(hw + 2, -1)), P(p.Local(hw + 2, 7)), P(p.Local(-hw + 2, 7)));
                uint face = flash > 0 ? 0xFFFFFFFF : on ? Blend(col, 0xFFFFFFFF, 0.3) : col;
                Poly(dc, face, Shade(col, 0.5), 1.2, P(p.Local(-hw, -3.5)), P(p.Local(hw, -3.5)), P(p.Local(hw, 3.5)), P(p.Local(-hw, 3.5)));
                dc.DrawLine(Pen(Alpha(0xFFFFFFFF, 0.7), 1.2), P(p.Local(-hw + 1.5, -2.5)), P(p.Local(hw - 1.5, -2.5)));
                break;
            }
            case PartKind.Standup:
            {
                double hw = Parts.TargetHalf;
                Poly(dc, 0x50000000, null, 0, P(p.Local(-hw + 2, -1)), P(p.Local(hw + 2, -1)), P(p.Local(hw + 2, 6)), P(p.Local(-hw + 2, 6)));
                Poly(dc, Shade(col, 0.7), Shade(col, 0.4), 1.2, P(p.Local(-hw, -3)), P(p.Local(hw, -3)), P(p.Local(hw, 3)), P(p.Local(-hw, 3)));
                var face = p.Local(0, -1);
                dc.FillEllipse(face.X, face.Y, 5.5, flash > 0 ? Yellow : 0xFFF8F4EC);
                dc.FillEllipse(face.X, face.Y, 3, on ? 0xFFFFFFFF : col);
                break;
            }
            case PartKind.Spinner:
            {
                double hw = Parts.SpinnerHalf, spin = st?.Spin ?? 0.15;
                double thick = 1 + 5 * Math.Abs(Math.Cos(spin * Math.PI * 2));
                dc.DrawLine(Pen(0xFFA0A0B0, 1.2), P(p.Local(-hw - 2, 0)), P(p.Local(hw + 2, 0)));
                Poly(dc, flash > 0 ? 0xFFFFFFFF : col, Shade(col, 0.5), 1, P(p.Local(-hw + 2, -thick)), P(p.Local(hw - 2, -thick)), P(p.Local(hw - 2, thick)), P(p.Local(-hw + 2, thick)));
                foreach (int side in new[] { -1, 1 })
                {
                    var q = p.Local(side * (hw + 2), 0);
                    dc.FillEllipse(q.X, q.Y, 2.8, 0xFFE8E4DA);
                }
                break;
            }
            case PartKind.Magnet:
            {
                double active = st?.Timer ?? 0;
                if (!icon)
                {
                    var fieldPen = Dash(Alpha(col, active > 0 ? 0.8 : 0.35), 1, _clock * (active > 0 ? 3 : 0.4));
                    dc.DrawEllipse(null, fieldPen, new Point(p.X, p.Y), Parts.MagnetR, Parts.MagnetR);
                    if (active > 0)
                        for (int k = 0; k < 3; k++)
                        {
                            double r = Parts.MagnetR * (1 - (_clock * 1.5 + k / 3.0) % 1);
                            dc.DrawEllipse(null, Pen(Alpha(col, 0.5 * r / Parts.MagnetR), 1.5), new Point(p.X, p.Y), r, r);
                        }
                }
                // A horseshoe magnet.
                var arc = new List<Point>();
                for (int i = 0; i <= 12; i++) arc.Add(P(p.Local(Math.Cos(Math.PI * i / 12) * 7, 2 + Math.Sin(Math.PI * i / 12) * 7)));
                Polyline(dc, Pen(Shade(col, 0.5), 6.5), arc);
                Polyline(dc, Pen(flash > 0 || active > 0 ? Blend(col, 0xFFFFFFFF, 0.4) : col, 4.5), arc);
                foreach (int side in new[] { -1, 1 })
                    dc.DrawLine(Pen(0xFFE8E4DA, 4.5), P(p.Local(side * 7, 2)), P(p.Local(side * 7, -5)));
                break;
            }
            case PartKind.Kickout:
            {
                double R = Parts.KickoutR;
                dc.FillEllipse(p.X, p.Y, R + 3.5, Shade(col, 0.6));
                dc.FillEllipse(p.X, p.Y, R + 1.5, col);
                dc.FillEllipse(p.X, p.Y, R - 1, 0xFF06060A);
                dc.FillEllipse(p.X - 2, p.Y - 2, R - 5, 0xFF16161E);
                // The arrow lamp: which way it kicks the ball out.
                uint lamp = Lamp(Yellow, on || flash > 0);
                Poly(dc, lamp, Shade(col, 0.4), 1, P(p.Local(-6, -R - 6)), P(p.Local(0, -R - 15)), P(p.Local(6, -R - 6)));
                break;
            }
            case PartKind.Eater:
            {
                double R = Parts.EaterR;
                var teeth = new List<Point>();
                for (int i = 0; i < 24; i++) teeth.Add(P(p.At + Vec.Polar(i * Math.PI / 12 + _clock * (icon ? 0 : 0.6), i % 2 == 0 ? R + 5 : R + 1)));
                Poly(dc, flash > 0 ? Red : col, Shade(col, 0.4), 1.2, teeth.ToArray());
                dc.FillEllipse(p.X, p.Y, R, 0xFF000000);
                dc.FillEllipse(p.X - 4, p.Y - 3, 2, Red);
                dc.FillEllipse(p.X + 4, p.Y - 3, 2, Red);
                break;
            }
            case PartKind.Wall:
            {
                var pts = p.Points.Select(P).ToList();
                Polyline(dc, Pen(0x50000000, 5), pts.Select(q => new Point(q.X + 2, q.Y + 3)).ToList());
                Polyline(dc, Pen(Shade(col, 0.6), 6), pts);
                Polyline(dc, Pen(col, 4), pts);
                Polyline(dc, Pen(Alpha(0xFFFFFFFF, 0.45), 1.2), pts);
                break;
            }
            case PartKind.Shape:
            {
                var pts = p.Points.Select(P).ToArray();
                Poly(dc, 0x50000000, null, 0, pts.Select(q => new Point(q.X + 2, q.Y + 3)).ToArray());
                Poly(dc, col, Blend(col, 0xFFFFFFFF, 0.45), 2.5, pts);
                break;
            }
            case PartKind.Curve:
            {
                var pts = CurvePoints(p.Points[0], p.Points[1], p.Points[2]);
                Polyline(dc, Pen(0x50000000, 5), pts.Select(q => new Point(q.X + 2, q.Y + 3)).ToList());
                Polyline(dc, Pen(Shade(col, 0.6), 6), pts);
                Polyline(dc, Pen(col, 4), pts);
                Polyline(dc, Pen(Alpha(0xFFFFFFFF, 0.45), 1.2), pts);
                break;
            }
        }
        if (selected) DrawSelected(dc, p);
    }

    private static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    private static void Star(DrawingContext dc, Point c, double outer, double inner, uint colour)
    {
        var pts = new Point[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5, r = i % 2 == 0 ? outer : inner;
            pts[i] = new Point(c.X + Math.Cos(a) * r, c.Y + Math.Sin(a) * r);
        }
        Poly(dc, colour, null, 0, pts);
    }

    /// <summary>A flipper's outline: round at the pivot, tapering to a round tip.</summary>
    public static Point[] FlipperOutline(Vec pivot, double angle)
    {
        double r1 = Parts.FlipperBase, r2 = Parts.FlipperTip, L = Parts.FlipperLength;
        double th = Math.Acos((r1 - r2) / L);
        var tip = pivot + Vec.Polar(angle, L);
        var pts = new List<Point>();
        for (int i = 0; i <= 8; i++) pts.Add(P(tip + Vec.Polar(angle + th - 2 * th * i / 8, r2)));
        for (int i = 0; i <= 14; i++) pts.Add(P(pivot + Vec.Polar(angle - th - (2 * Math.PI - 2 * th) * i / 14, r1)));
        return pts.ToArray();
    }

    /// <summary>A curved wall's line: the arc from a through m to b (straight if they're in a line).</summary>
    public static List<Point> CurvePoints(Vec a, Vec m, Vec b)
    {
        if (Collide.Arc(a, m, b) is not { } arc) return [P(a), P(b)];
        int n = Math.Clamp((int)(arc.R * arc.Sweep / 6), 6, 60);
        var pts = new List<Point>();
        for (int i = 0; i <= n; i++) pts.Add(P(arc.Centre + Vec.Polar(arc.Start + arc.Sweep * i / n, arc.R)));
        return pts;
    }

    /// <summary>The part in hand: marching ants round it, and its corners if it's a wall.</summary>
    private void DrawSelected(DrawingContext dc, Part p)
    {
        var ants = Dash(0xFFFFFFFF, 1.2, _clock * 2);
        if (p.IsWall)
        {
            foreach (var c in p.Points)
            {
                dc.FillRectangle(Brush(0xFF000000), new Rect(c.X - 3.5, c.Y - 3.5, 7, 7));
                dc.DrawRectangle(null, Pen(Yellow, 1.2), new Rect(c.X - 3, c.Y - 3, 6, 6));
            }
            return;
        }
        double r = Parts.IsFlipper(p.Kind) ? 10 : Parts.Reach(p.Kind) + 3;
        if (Parts.IsFlipper(p.Kind))
        {
            var mid = p.At + Vec.Polar(Sim.Rest(p).Down, Parts.FlipperLength / 2);
            dc.DrawEllipse(null, ants, P(mid), Parts.FlipperLength / 2 + 8, Parts.FlipperLength / 2 + 8);
            return;
        }
        dc.DrawEllipse(null, ants, new Point(p.X, p.Y), r, r);
    }

    /// <summary>A part as an icon, drawn at a point at a size (for the parts box and the panel).</summary>
    private void DrawIcon(DrawingContext dc, PartKind k, Point at, double scale, uint? colour = null, double angle = 0)
    {
        var offset = k switch
        {
            PartKind.FlipperLeft => new Vec(-24, -8),
            PartKind.FlipperRight => new Vec(24, -8),
            PartKind.SlingLeft => new Vec(-1, -2),
            PartKind.SlingRight => new Vec(1, -2),
            _ => default,
        };
        var p = Parts.Make(k, offset);
        p.Angle = angle;
        if (colour is { } c) p.Colour = c;
        using (dc.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(at.X, at.Y)))
            DrawPart(dc, p, null, false, icon: true);
    }

    private static double IconScale(PartKind k) => k switch
    {
        PartKind.FlipperLeft or PartKind.FlipperRight => 0.82, PartKind.Bumper => 1.25, PartKind.SlingLeft or PartKind.SlingRight => 0.78,
        PartKind.Post => 2.4, PartKind.Rollover => 1.9, PartKind.Lane => 0.95, PartKind.DropTarget or PartKind.Standup => 1.55, PartKind.Spinner => 1.35,
        PartKind.Magnet => 2.0, PartKind.Kickout => 1.25, PartKind.Eater => 1.25, _ => 1,
    };

    // ───────────────────────────── building ─────────────────────────────

    private static readonly uint[] RuleColours = [0xFF5AD05A, 0xFFFF9A2A, 0xFF34C8D8, 0xFFF070B8, 0xFFFFD83A, 0xFFA060E8, 0xFFF04838];

    private static uint RuleColour(int i) => RuleColours[i % RuleColours.Length];

    /// <summary>Over the table while building: the rules' wires, what's being drawn, and the part in hand.</summary>
    private void DrawEditing(DrawingContext dc, Table t)
    {
        var ed = Engine.Editor;
        if (ed.Tab == Tab.Rules || ed.Tool == Tool.Wire)
        {
            for (int i = 0; i < t.Rules.Count; i++) DrawRule(dc, t, t.Rules[i], RuleColour(i), false, i);
            if (ed.Draft is { } d) DrawRule(dc, t, d, 0xFFFFFFFF, true, t.Rules.Count);
        }
        var mouse = ToTable(_mouse);
        bool overTable = TableRect.Contains(_mouse);
        if (ed.Tool == Tool.Wire && overTable && ed.HitTest(mouse) is >= 0 and var hover && !t.Parts[hover].IsWall)
        {
            var hp = t.Parts[hover];
            double r = Parts.Reach(hp.Kind) + 6;
            dc.DrawEllipse(null, Pen(ed.PickingTargets ? Yellow : Green, 1.5), new Point(hp.X, hp.Y), r, r);
        }
        uint ink = ed.Colour;
        if (ed.From is { } from)
        {
            if (ed.Tool == Tool.Wall) DrawPart(dc, new Part { Kind = PartKind.Wall, Colour = ink, Points = [from, ed.To] }, null, false);
            else dc.DrawLine(Dash(ink, 2, _clock * 2), P(from), P(ed.To));
            dc.FillEllipse(from.X, from.Y, 3, Yellow);
        }
        if (ed.CurveEnds is { } ends)
        {
            var bend = overTable ? mouse : ed.Bend;
            DrawPart(dc, new Part { Kind = PartKind.Curve, Colour = ink, Points = [ends.A, bend, ends.B] }, null, false);
            dc.FillEllipse(ends.A.X, ends.A.Y, 3, Yellow);
            dc.FillEllipse(ends.B.X, ends.B.Y, 3, Yellow);
            dc.FillEllipse(bend.X, bend.Y, 2.5, White);
        }
        if (ed.ShapePoints.Count > 0)
        {
            var pts = ed.ShapePoints.Select(P).ToList();
            if (pts.Count >= 3) Poly(dc, Alpha(ink, 0.45), null, 0, pts.ToArray());
            if (overTable) pts.Add(P(ed.SnapTo(mouse)));
            Polyline(dc, Pen(ink, 2.5), pts);
            foreach (var q in ed.ShapePoints) dc.FillEllipse(q.X, q.Y, 2.5, White);
            var first = ed.ShapePoints[0];
            dc.DrawEllipse(null, Pen(Yellow, 1.5), P(first), 6, 6);
        }
        if (ed.Carried is { } c && ed.CarriedIsNew && overTable)
            using (dc.PushOpacity(0.8))
                DrawPart(dc, c, null, false);
        if (ed.Zoom <= 1 && ed.Tool == Tool.Magnify && overTable)
        {
            var (vw, vh) = (Field.TableW / Editor.Magnification, Field.TableH / Editor.Magnification);
            dc.DrawRectangle(null, Dash(White, 1.2, _clock), new Rect(mouse.X - vw / 2, mouse.Y - vh / 2, vw, vh));
        }
    }

    /// <summary>A rule's wires: a ring round each part that sets it off, and a line from them to what it lights (or to a badge saying what it does).</summary>
    private void DrawRule(DrawingContext dc, Table t, Rule r, uint colour, bool draft, int index)
    {
        var sources = r.Sources.Select(t.Find).Where(p => p != null).Select(p => p!).ToList();
        var targets = r.Targets.Select(t.Find).Where(p => p != null).Select(p => p!).ToList();
        uint src = draft ? Green : colour, dst = draft ? Yellow : colour;
        var pen = draft ? Dash(src, 2, _clock * 3) : Pen(Alpha(colour, 0.9), 1.8);
        foreach (var s in sources)
        {
            double rr = Parts.Reach(s.Kind) + 4;
            dc.DrawEllipse(null, Pen(src, 2), new Point(s.X, s.Y), rr, rr);
            if (r.When == When.All && sources.Count > 1) dc.FillEllipse(s.X + rr * 0.7, s.Y - rr * 0.7, 3, src);
        }
        if (r.Effect == Outcome.Light)
        {
            foreach (var tg in targets)
            {
                double rr = Parts.Reach(tg.Kind) + 6;
                dc.DrawEllipse(null, Dash(dst, 2, _clock * 2), new Point(tg.X, tg.Y), rr, rr);
                foreach (var s in sources) Wire(dc, s.At, tg.At, Parts.Reach(s.Kind) + 4, rr, pen, dst);
            }
            return;
        }
        if (sources.Count == 0) return;
        // Each rule's badge over its parts, rules on the same parts side by side.
        var c = new Vec(sources.Average(s => s.X), sources.Average(s => s.Y) - 30);
        int same = t.Rules.Take(index).Count(o => o.Effect != Outcome.Light && o.Sources.Count > 0 && Vec.Distance(Centre(t, o), c) < 20);
        c += new Vec((same % 2 == 0 ? 1 : -1) * ((same + 1) / 2) * 28, 0);
        string badge = r.Effect switch
        {
            Outcome.Points => r.Amount >= 1000 ? $"+{r.Amount / 1000.0:0.#}K" : $"+{r.Amount}",
            Outcome.Bonus => "2X", Outcome.ExtraBall => "EB", _ => "MB",
        };
        foreach (var s in sources) Wire(dc, s.At, c, Parts.Reach(s.Kind) + 4, 12, pen, src);
        dc.FillEllipse(c.X, c.Y, 12, 0xE0101420);
        dc.DrawEllipse(null, Pen(src, 2), P(c), 12, 12);
        Text(dc, badge, new Point(c.X, c.Y), badge.Length > 3 ? 13 : 16, src, center: true, shadow: false);
    }

    /// <summary>Where a rule's badge would sit before it's moved aside: over the middle of its parts.</summary>
    private static Vec Centre(Table t, Rule r)
    {
        var ps = r.Sources.Select(t.Find).Where(p => p != null).Select(p => p!).ToList();
        return ps.Count == 0 ? default : new Vec(ps.Average(p => p.X), ps.Average(p => p.Y) - 30);
    }

    /// <summary>A wire bowed a little from one part to another, with an arrowhead at the end.</summary>
    private static void Wire(DrawingContext dc, Vec a, Vec b, double ra, double rb, Pen pen, uint colour)
    {
        var d = b - a;
        double l = d.Length;
        if (l < ra + rb + 2) return;
        var u = d / l;
        var from = a + u * ra;
        var to = b - u * rb;
        var mid = (from + to) / 2 + u.Perp * (l * 0.12);
        var pts = new List<Point>();
        for (int i = 0; i <= 16; i++)
        {
            double t = i / 16.0;
            pts.Add(P(from * ((1 - t) * (1 - t)) + mid * (2 * t * (1 - t)) + to * (t * t)));
        }
        Polyline(dc, pen, pts);
        var dir = (to - mid).Unit;
        Poly(dc, colour, null, 0, P(to), P(to - dir * 7 + dir.Perp * 3.5), P(to - dir * 7 - dir.Perp * 3.5));
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}
