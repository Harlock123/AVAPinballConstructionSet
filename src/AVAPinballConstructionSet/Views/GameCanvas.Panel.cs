using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAPinballConstructionSet.Engine;

namespace AVAPinballConstructionSet.Views;

public sealed partial class GameCanvas
{
    private const double Px = 452, Pw = 560;
    public static readonly Rect BoxRect = new(Px, 62, Pw, 184);
    public static readonly Rect NameRect = new(466, 640, 330, 38);
    private static readonly Rect Content = new(Px, 382, Pw, 320);
    private static readonly Tool[] Tools = [Tool.Hand, Tool.Wall, Tool.Shape, Tool.Curve, Tool.Paint, Tool.Magnify, Tool.Wire];
    private List<(string Name, string Path)> _saved = [];
    private double _savedAt = -10;

    /// <summary>Where each part sits in the parts box.</summary>
    public static Rect BoxTile(int i) => new(458 + i % 7 * 79, 68 + i / 7 * 89, 74, 84);
    public static Rect ToolRect(int i) => new(458 + i * 69, 254, 64, 54);
    public static Rect PotRect(int i) => new(458 + i * 39, 316, 34, 26);
    public static Rect TabRect(int i) => new(458 + i * 138, 350, 134, 28);
    public static Rect KnobRect(Editor.Knob k) => new(660, 404 + (int)k * 52, 280, 22);
    public static readonly Rect PlayRect = new(Px, 710, 190, 48);

    private static string ToolName(Tool t) => t switch
    {
        Tool.Hand => "HAND", Tool.Wall => "WALL", Tool.Shape => "SHAPE", Tool.Curve => "CURVE", Tool.Paint => "PAINT", Tool.Magnify => "LOOK", _ => "WIRE",
    };

    private static string TabName(Tab t) => t switch { Tab.Part => "PART", Tab.Physics => "PHYSICS", Tab.Rules => "RULES", _ => "TABLES" };

    private void SetKnob(Editor.Knob k, Point at)
    {
        var r = KnobRect(k);
        int v = (int)Math.Round(1 + (at.X - r.X) / r.Width * 9);
        Engine.Editor.Set(k, v);
    }

    // ───────────────────────────── the construction set ─────────────────────────────

    private void DrawEditor(DrawingContext dc)
    {
        var g = Engine;
        var ed = g.Editor;
        DrawHeader(dc, ed.Table.Name);
        // The parts box.
        Panel(dc, BoxRect, 0xFF20263E);
        for (int i = 0; i < Parts.Box.Length; i++)
        {
            var k = Parts.Box[i];
            var r = BoxTile(i);
            bool hover = r.Contains(_mouse);
            dc.FillRectangle(Brush(hover ? 0xFF34406A : 0xFF161A2E), r, 6);
            dc.DrawRectangle(null, Pen(hover ? Yellow : 0xFF3A4270, 1.2), r, 6, 6);
            using (dc.PushClip(r)) DrawIcon(dc, k, new Point(r.X + 37, r.Y + 36), IconScale(k));
            Text(dc, Parts.Short(k), new Point(r.X + 37, r.Y + 74), 15, hover ? Yellow : Dim, center: true, shadow: false);
            var kind = k;
            Hit(r, () => ed.TakeFromBox(kind, ToTable(_mouse)));
        }
        // The tools, and undo.
        for (int i = 0; i < Tools.Length; i++)
        {
            var t = Tools[i];
            var r = ToolRect(i);
            bool on = ed.Tool == t;
            dc.FillRectangle(Brush(on ? 0xFF5A4A1C : r.Contains(_mouse) ? 0xFF323E68 : 0xFF262E50), r, 6);
            dc.DrawRectangle(null, Pen(on ? Yellow : 0xFF5A6AA0, 1.5), r, 6, 6);
            DrawToolIcon(dc, t, new Point(r.X + 32, r.Y + 22), on ? Yellow : Ink);
            Text(dc, ToolName(t), new Point(r.X + 32, r.Y + 45), 15, on ? Yellow : Dim, center: true, shadow: false);
            Text(dc, KeyFor(t), new Point(r.X + 6, r.Y + 9), 13, 0xFF6A78A8, shadow: false);
            var tool = t;
            Hit(r, () => ed.SetTool(tool));
        }
        {
            var r = ToolRect(7);
            bool can = ed.CanUndo;
            dc.FillRectangle(Brush(can && r.Contains(_mouse) ? 0xFF323E68 : 0xFF262E50), r, 6);
            dc.DrawRectangle(null, Pen(can ? 0xFF5A6AA0 : 0xFF2A3050, 1.5), r, 6, 6);
            DrawToolIcon(dc, null, new Point(r.X + 32, r.Y + 22), can ? Ink : 0xFF4A5070);
            Text(dc, "UNDO", new Point(r.X + 32, r.Y + 45), 15, can ? Dim : 0xFF4A5070, center: true, shadow: false);
            if (can) Hit(r, () => ed.Undo());
        }
        // The paint pots.
        for (int i = 0; i < Parts.Palette.Length; i++)
        {
            var r = PotRect(i);
            uint c = Parts.Palette[i];
            dc.FillRectangle(Brush(c), r, 4);
            bool on = ed.Colour == c;
            dc.DrawRectangle(null, Pen(on ? Yellow : 0xFF5A6AA0, on ? 2.5 : 1), r.Inflate(on ? 2 : 0), 5, 5);
            Hit(r, () =>
            {
                ed.Colour = c;
                if (ed.Tool is not (Tool.Paint or Tool.Wall or Tool.Shape or Tool.Curve)) ed.SetTool(Tool.Paint);
                else ed.Heard.Add(Sfx.Click);
            });
        }
        // The tabs, and the panel under them.
        for (int i = 0; i < 4; i++)
        {
            var t = (Tab)i;
            var r = TabRect(i);
            bool on = ed.Tab == t;
            dc.FillRectangle(Brush(on ? 0xFF2A3256 : 0xFF161A2E), new Rect(r.X, r.Y, r.Width, r.Height + (on ? 6 : 0)), 6);
            dc.DrawRectangle(null, Pen(on ? Stripes[i + 1] : 0xFF3A4270, 1.5), r, 6, 6);
            Text(dc, TabName(t), r.Center, 20, on ? Stripes[i + 1] : Dim, center: true);
            Hit(r, () => { ed.Tab = t; ed.Heard.Add(Sfx.Click); });
        }
        Panel(dc, Content, 0xFF2A3256);
        switch (ed.Tab)
        {
            case Tab.Part: DrawPartTab(dc); break;
            case Tab.Physics: DrawPhysicsTab(dc); break;
            case Tab.Rules: DrawRulesTab(dc); break;
            default: DrawTablesTab(dc); break;
        }
        // Play, and the word on the last thing done.
        Button(dc, PlayRect, "PLAY  (ENTER)", g.PlayTable, size: 28, accent: Green);
        string line = ed.Message ?? Hint();
        double lineSize = 20;
        while (TextWidth(line, lineSize) > Px + Pw - 656 && lineSize > 12) lineSize -= 0.5;
        Text(dc, line, new Point(656, 734), lineSize, ed.Message != null ? Yellow : Dim);
        if (ed.Zoom > 1)
        {
            dc.FillRectangle(Brush(0xC0000000), new Rect(TableRect.X + 8, TableRect.Y + 8, 150, 26), 5);
            Text(dc, $"MAGNIFIED {Editor.Magnification:0.#}X", new Point(TableRect.X + 83, TableRect.Y + 21), 18, Yellow, center: true);
        }
        // A part from the box, in the hand over the panel.
        if (ed.Carried is { } held && ed.CarriedIsNew && !TableRect.Contains(_mouse) && _mouse.X >= 0)
        {
            var (_, z) = View();
            using (dc.PushOpacity(0.85)) DrawIcon(dc, held.Kind, _mouse, z);
        }
    }

    private string Hint()
    {
        var ed = Engine.Editor;
        if (ed.Carried != null) return "LET GO ON THE TABLE TO PUT IT THERE, OR IN THE BOX TO THROW IT AWAY";
        return ed.Tool switch
        {
            Tool.Hand => "DRAG PARTS FROM THE BOX ONTO THE TABLE; DRAG THEM BACK TO THROW AWAY",
            Tool.Wall => "DRAG ACROSS THE TABLE TO DRAW A WALL; ENDS SNAP TOGETHER",
            Tool.Shape => ed.ShapePoints.Count >= 3 ? "CLICK THE FIRST CORNER (OR RIGHT-CLICK) TO CLOSE IT" : "CLICK CORNER BY CORNER TO DRAW A SOLID SHAPE",
            Tool.Curve => ed.CurveEnds != null ? "MOVE TO BEND THE CURVE, THEN CLICK" : "DRAG FROM ONE END OF THE CURVE TO THE OTHER",
            Tool.Paint => "CLICK A PART, A WALL OR THE TABLE TO PAINT IT",
            Tool.Magnify => ed.Zoom > 1 ? "CLICK TO SEE THE WHOLE TABLE AGAIN" : "CLICK WHERE YOU WANT A CLOSER LOOK",
            _ => ed.Draft == null ? "CLICK A PART TO START A NEW RULE" : ed.PickingTargets ? "CLICK THE PARTS IT LIGHTS" : "CLICK THE PARTS THAT SET IT OFF",
        };
    }

    private void DrawHeader(DrawingContext dc, string name)
    {
        var r = new Rect(Px, 10, Pw, 46);
        Panel(dc, r, 0xFF101428);
        for (int i = 0; i < Stripes.Length; i++) dc.FillRectangle(Brush(Stripes[i]), new Rect(Px + 10 + i * 9, 18, 6, 30), 2);
        Text(dc, "PINBALL CONSTRUCTION SET", new Point(Px + 62, 33), 34, White);
        Text(dc, name, new Point(Px + Pw - 12, 33), 24, Yellow, right: true);
    }

    private void DrawToolIcon(DrawingContext dc, Tool? t, Point c, uint col)
    {
        double x = c.X, y = c.Y;
        var pen = Pen(col, 2.2);
        switch (t)
        {
            case Tool.Hand:
                dc.FillRectangle(Brush(col), new Rect(x - 7, y - 2, 14, 12), 4);
                for (int i = 0; i < 4; i++) dc.FillRectangle(Brush(col), new Rect(x - 7 + i * 3.8, y - 12 + (i is 1 or 2 ? -2 : 1), 3, 12), 1.5);
                dc.DrawLine(Pen(col, 3), new Point(x - 7, y + 3), new Point(x - 12, y - 3));
                break;
            case Tool.Wall:
                dc.DrawLine(Pen(col, 4), new Point(x - 14, y + 9), new Point(x + 14, y - 9));
                dc.FillEllipse(x - 14, y + 9, 3, Yellow);
                dc.FillEllipse(x + 14, y - 9, 3, Yellow);
                break;
            case Tool.Shape:
                Poly(dc, Alpha(col, 0.6), col, 2, new Point(x - 14, y + 10), new Point(x - 8, y - 10), new Point(x + 12, y - 6), new Point(x + 14, y + 10));
                break;
            case Tool.Curve:
                Polyline(dc, Pen(col, 4), Enumerable.Range(0, 13).Select(i => new Point(x - 14 + i * 28 / 12.0, y + 8 - Math.Sin(Math.PI * i / 12) * 18)).ToList());
                break;
            case Tool.Paint:
                dc.FillRectangle(Brush(col), new Rect(x - 3, y - 13, 6, 14), 2);
                Poly(dc, Engine.Editor.Colour, col, 1.5, new Point(x - 7, y + 1), new Point(x + 7, y + 1), new Point(x + 4, y + 12), new Point(x - 4, y + 12));
                break;
            case Tool.Magnify:
                dc.DrawEllipse(null, Pen(col, 3), new Point(x - 3, y - 3), 8, 8);
                dc.DrawLine(Pen(col, 4), new Point(x + 3, y + 3), new Point(x + 11, y + 11));
                break;
            case Tool.Wire:
                dc.FillEllipse(x - 11, y + 7, 4, Green);
                dc.FillEllipse(x + 11, y - 7, 4, Yellow);
                Polyline(dc, pen, [new Point(x - 11, y + 7), new Point(x - 4, y - 6), new Point(x + 3, y + 6), new Point(x + 11, y - 7)]);
                break;
            default:
            {
                var arc = Enumerable.Range(0, 11).Select(i => new Point(x + Math.Cos(Math.PI * (1.1 + 1.3 * i / 10)) * -10, y + 4 + Math.Sin(Math.PI * (1.1 + 1.3 * i / 10)) * 10)).ToList();
                Polyline(dc, Pen(col, 3), arc);
                var end = arc[^1];
                Poly(dc, col, null, 0, new Point(end.X - 6, end.Y - 2), new Point(end.X + 5, end.Y - 6), new Point(end.X + 1, end.Y + 6));
                break;
            }
        }
    }

    /// <summary>The part in hand: what it is, what it scores, which way it faces, its colour; or, with nothing in hand, how to use the tool.</summary>
    private void DrawPartTab(DrawingContext dc)
    {
        var ed = Engine.Editor;
        var t = ed.Table;
        double x = Content.X + 14, y = Content.Y + 18;
        if (ed.SelectedPart is not { } p)
        {
            Text(dc, $"THE {ToolName(ed.Tool)}", new Point(x, y + 4), 30, Yellow);
            string[] lines = ed.Tool switch
            {
                Tool.Hand =>
                [
                    "DRAG A PART FROM THE BOX ONTO THE TABLE.", "PICK PARTS UP TO MOVE THEM; DRAG THEM BACK", "INTO THE BOX TO THROW THEM AWAY.",
                    "CLICK A PART TO SET ITS SCORE AND TURN IT.", "DRAG A WALL'S CORNER TO RESHAPE IT.",
                ],
                Tool.Wall => ["DRAG FROM ONE END OF A WALL TO THE OTHER.", "A NEW WALL'S ENDS SNAP TO THE CORNERS", "OF THE WALLS ALREADY THERE, SO THEY JOIN.", "IT'S DRAWN IN THE PAINT POT'S COLOUR."],
                Tool.Shape => ["CLICK THE CORNERS OF A SOLID SHAPE ONE BY", "ONE. CLICK THE FIRST CORNER AGAIN, OR", "RIGHT-CLICK, OR PRESS ENTER, TO CLOSE IT.", "THE BALL BOUNCES OFF ITS SIDES."],
                Tool.Curve => ["DRAG FROM ONE END OF THE CURVE TO THE", "OTHER, THEN MOVE THE MOUSE TO BEND IT", "AND CLICK. RIGHT-CLICK LEAVES IT STRAIGHT.", "DRAG ITS MIDDLE CORNER LATER TO REBEND IT."],
                Tool.Paint => ["CHOOSE A PAINT POT, THEN CLICK A PART OR", "A WALL TO PAINT IT. CLICK THE TABLE ITSELF", "TO PAINT THE PLAYFIELD."],
                Tool.Magnify => ["CLICK THE TABLE FOR A CLOSER LOOK, FOR", "FINE WORK; EVERY TOOL WORKS MAGNIFIED.", "CLICK AGAIN TO SEE THE WHOLE TABLE.", "THE MOUSE WHEEL DOES THE SAME."],
                _ => ["THE WIRE CONNECTS PARTS INTO RULES:", "SEE THE RULES TAB."],
            };
            for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(x, y + 46 + i * 28), 22, Ink);
            int walls = t.Parts.Count(q => q.IsWall);
            Text(dc, $"ON THE TABLE: {t.Parts.Count - walls} PARTS, {walls} WALLS, {t.Rules.Count} RULES", new Point(x, Content.Bottom - 22), 20, Dim);
            return;
        }
        int index = ed.Selected;
        var frame = new Rect(x, y - 4, 112, 112);
        dc.FillRectangle(Brush(0xFF161A2E), frame, 8);
        dc.DrawRectangle(null, Pen(0xFF3A4270, 1.5), frame, 8, 8);
        if (!p.IsWall)
            using (dc.PushClip(frame)) DrawIcon(dc, p.Kind, frame.Center, IconScale(p.Kind) * 1.25, p.Colour, p.Angle);
        else DrawToolIcon(dc, p.Kind switch { PartKind.Shape => Tool.Shape, PartKind.Curve => Tool.Curve, _ => Tool.Wall }, frame.Center, p.Colour);
        double cx = x + 130;
        Text(dc, Parts.Name(p.Kind), new Point(cx, y + 8), 32, Yellow);
        bool scores = !p.IsWall && !Parts.IsFlipper(p.Kind);
        if (scores)
        {
            Text(dc, p.Kind == PartKind.Spinner ? "SCORES EACH TURN" : "SCORES", new Point(cx, y + 50), 22, Dim);
            Text(dc, p.Score.ToString("N0", CultureInfo.InvariantCulture), new Point(cx + 270, y + 50), 30, White, right: true);
            Button(dc, new Rect(cx + 290, y + 36, 40, 30), "-", () => ed.StepScore(index, -1), size: 26);
            Button(dc, new Rect(cx + 336, y + 36, 40, 30), "+", () => ed.StepScore(index, 1), size: 26);
        }
        else Text(dc, p.IsWall ? $"{p.Points.Count} CORNERS" : "FLIPS WITH ITS BUTTON", new Point(cx, y + 50), 22, Dim);
        Text(dc, p.IsWall ? "TURN IT" : $"TURNED {p.Angle:0} DEGREES", new Point(cx, y + 90), 22, Dim);
        Button(dc, new Rect(cx + 290, y + 76, 40, 30), "<", () => ed.Rotate(index, -15), size: 24);
        Button(dc, new Rect(cx + 336, y + 76, 40, 30), ">", () => ed.Rotate(index, 15), size: 24);
        y += 132;
        Text(dc, "COLOUR", new Point(x, y), 22, Dim);
        dc.FillRectangle(Brush(p.Colour), new Rect(x + 80, y - 12, 60, 24), 4);
        dc.DrawRectangle(null, Pen(0xFF5A6AA0, 1), new Rect(x + 80, y - 12, 60, 24), 4, 4);
        Button(dc, new Rect(x + 152, y - 14, 200, 28), "PAINT IT WITH THE POT", () => ed.Paint(index, ed.Colour), size: 18);
        int rules = t.Rules.Count(r => r.Sources.Contains(p.Id) || r.Targets.Contains(p.Id));
        y += 40;
        Text(dc, rules == 0 ? "NOT WIRED TO ANY RULE" : rules == 1 ? "WIRED TO 1 RULE" : $"WIRED TO {rules} RULES", new Point(x, y), 22, rules > 0 ? Green : Dim);
        Text(dc, PartHelp(p.Kind), new Point(x, y + 32), 19, Ink);
        Button(dc, new Rect(x, Content.Bottom - 48, 260, 36), "PUT IT BACK IN THE BOX", () => ed.Delete(index), size: 20, accent: Red);
        Text(dc, "DEL KEY  R: TURN  +/-: SCORE", new Point(Content.Right - 14, Content.Bottom - 30), 18, 0xFF6A78A8, right: true);
    }

    private static string PartHelp(PartKind k) => k switch
    {
        PartKind.FlipperLeft or PartKind.FlipperRight => "Z / LEFT SHIFT AND / / RIGHT SHIFT FLIP THEM.",
        PartKind.Bumper => "KICKS THE BALL AWAY AS HARD AS THE KICK KNOB SAYS.",
        PartKind.SlingLeft or PartKind.SlingRight => "ITS LONG WHITE SIDE KICKS THE BALL ACROSS.",
        PartKind.Post => "A RUBBER POST: A LIVELY BOUNCE.",
        PartKind.Rollover => "SCORES AS THE BALL ROLLS OVER IT.",
        PartKind.Lane => "A LANE WITH A ROLLOVER IN IT; GOOD IN THREES.",
        PartKind.DropTarget => "DROPS WHEN HIT; UP AGAIN WHEN THE BANK'S DOWN.",
        PartKind.Standup => "A TARGET THAT STAYS UP.",
        PartKind.Spinner => "SPINS AS THE BALL GOES THROUGH, SCORING EACH TURN.",
        PartKind.Magnet => "GRABS THE BALL FOR A MOMENT, THEN LETS GO.",
        PartKind.Kickout => "HOLDS THE BALL, THEN KICKS IT OUT THE WAY IT POINTS.",
        PartKind.Eater => "SWALLOWS THE BALL: IT'S LOST, BUT IT SCORES.",
        _ => "THE BALL BOUNCES OFF IT AS THE ELASTICITY KNOB SAYS.",
    };

    /// <summary>The four knobs, the balls a game and the extra ball's mark.</summary>
    private void DrawPhysicsTab(DrawingContext dc)
    {
        var ed = Engine.Editor;
        var t = ed.Table;
        (Editor.Knob K, string Name, string Help)[] knobs =
        [
            (Editor.Knob.Gravity, "GRAVITY", "HOW STEEP THE TABLE IS"),
            (Editor.Knob.Speed, "SPEED", "HOW FAST THE GAME RUNS"),
            (Editor.Knob.Elasticity, "ELASTICITY", "HOW BOUNCY THE WALLS ARE"),
            (Editor.Knob.Kick, "KICK", "HOW HARD BUMPERS AND KICKERS HIT"),
        ];
        foreach (var (k, name, help) in knobs)
        {
            var r = KnobRect(k);
            int v = ed.Get(k);
            Text(dc, name, new Point(Content.X + 14, r.Y + 4), 28, Stripes[(int)k]);
            Text(dc, help, new Point(Content.X + 14, r.Y + 30), 17, Dim);
            dc.FillRectangle(Brush(0xFF101428), r, 11);
            dc.FillRectangle(Brush(Shade(Stripes[(int)k], 0.7)), new Rect(r.X, r.Y, r.Width * (v - 1) / 9.0 + 11, r.Height), 11);
            for (int i = 0; i < 10; i++) dc.FillRectangle(Brush(0x60000000), new Rect(r.X + 11 + i * (r.Width - 22) / 9 - 0.75, r.Y + 6, 1.5, 10));
            var knob = new Point(r.X + 11 + (v - 1) * (r.Width - 22) / 9, r.Center.Y);
            dc.FillEllipse(knob.X + 1.5, knob.Y + 2, 13, 0x80000000);
            dc.FillEllipse(knob.X, knob.Y, 13, 0xFFE8E4DA);
            dc.FillEllipse(knob.X, knob.Y, 8, Stripes[(int)k]);
            Text(dc, v.ToString(CultureInfo.InvariantCulture), new Point(r.Right + 34, r.Center.Y), 30, White, center: true);
            var kk = k;
            Hit(r.Inflate(new Thickness(14, 8)), () => { _knob = kk; SetKnob(kk, _mouse); });
        }
        double y = Content.Y + 258;
        Text(dc, "BALLS A GAME", new Point(Content.X + 14, y), 24, Ink);
        Button(dc, new Rect(660, y - 15, 36, 30), "-", () => ed.StepBalls(-1), size: 26);
        Text(dc, t.Balls.ToString(CultureInfo.InvariantCulture), new Point(740, y), 30, White, center: true);
        Button(dc, new Rect(784, y - 15, 36, 30), "+", () => ed.StepBalls(1), size: 26);
        y += 40;
        Text(dc, "EXTRA BALL AT", new Point(Content.X + 14, y), 24, Ink);
        Button(dc, new Rect(660, y - 15, 36, 30), "-", () => ed.StepExtraBall(-1), size: 26);
        Text(dc, t.ExtraBallAt == 0 ? "NEVER" : t.ExtraBallAt.ToString("N0", CultureInfo.InvariantCulture), new Point(760, y), 26, White, center: true);
        Button(dc, new Rect(828, y - 15, 36, 30), "+", () => ed.StepExtraBall(1), size: 26);
    }

    /// <summary>The rules on the table, and the one being wired.</summary>
    private void DrawRulesTab(DrawingContext dc)
    {
        var ed = Engine.Editor;
        var t = ed.Table;
        double x = Content.X + 14, y = Content.Y + 18;
        if (ed.Draft is not { } d)
        {
            if (t.Rules.Count == 0)
            {
                Text(dc, "NO RULES YET", new Point(x, y + 4), 30, Yellow);
                string[] lines =
                [
                    "A RULE WIRES PARTS TOGETHER. WHEN ONE OF", "THEM (OR ALL OF THEM) HAS BEEN HIT, IT SCORES", "POINTS, LIGHTS OTHER PARTS FOR MORE POINTS,",
                    "RAISES THE BONUS MULTIPLIER, GIVES AN EXTRA", "BALL, OR STARTS MULTIBALL.",
                ];
                for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(x, y + 46 + i * 28), 22, Ink);
            }
            int shown = Math.Min(t.Rules.Count, 7);
            for (int i = 0; i < shown; i++)
            {
                double ry = y + i * 34;
                dc.FillEllipse(x + 8, ry + 2, 7, RuleColour(i));
                string s = Sim.Describe(t, t.Rules[i]);
                double size = 20;
                while (TextWidth(s, size) > 470 && size > 14) size -= 1;
                Text(dc, s, new Point(x + 24, ry + 2), size, Ink);
                int index = i;
                Button(dc, new Rect(Content.Right - 42, ry - 12, 28, 26), "X", () => ed.DeleteRule(index), size: 18, accent: Red);
            }
            if (t.Rules.Count > shown) Text(dc, $"AND {t.Rules.Count - shown} MORE", new Point(x + 24, y + shown * 34 + 2), 20, Dim);
            Button(dc, new Rect(x, Content.Bottom - 50, 200, 38), "NEW RULE", ed.NewRule, size: 24, accent: Green);
            Text(dc, "THE WIRES SHOW ON THE TABLE", new Point(Content.Right - 14, Content.Bottom - 30), 18, 0xFF6A78A8, right: true);
            return;
        }
        Text(dc, "WIRING A NEW RULE", new Point(x, y + 2), 28, Yellow);
        y += 36;
        Text(dc, $"1. CLICK WHAT SETS IT OFF ({d.Sources.Count})", new Point(x, y), 21, !ed.PickingTargets ? Green : Ink);
        Button(dc, new Rect(Content.Right - 178, y - 14, 164, 28), d.When == When.All ? "WHEN ALL ARE HIT" : "WHEN ANY IS HIT", ed.ToggleWhen, size: 18);
        y += 36;
        Text(dc, "2. WHAT IT DOES", new Point(x, y), 21, Ink);
        y += 26;
        (Outcome E, string Name)[] effects = [(Outcome.Points, "POINTS"), (Outcome.Light, "LIGHT"), (Outcome.Bonus, "BONUS X"), (Outcome.ExtraBall, "EXTRA BALL"), (Outcome.Multiball, "MULTIBALL")];
        for (int i = 0; i < effects.Length; i++)
        {
            var (e, name) = effects[i];
            Button(dc, new Rect(x + i * 106, y - 4, 100, 30), name, () => ed.SetEffect(e), on: d.Effect == e, size: 17);
        }
        y += 44;
        if (d.Effect is Outcome.Points or Outcome.Light)
        {
            Text(dc, d.Effect == Outcome.Light ? "LIT, IT'S WORTH" : "WORTH", new Point(x + 18, y), 21, Ink);
            Button(dc, new Rect(x + 190, y - 14, 34, 28), "-", () => ed.StepAmount(-1), size: 24);
            Text(dc, d.Amount.ToString("N0", CultureInfo.InvariantCulture), new Point(x + 280, y), 26, White, center: true);
            Button(dc, new Rect(x + 336, y - 14, 34, 28), "+", () => ed.StepAmount(1), size: 24);
        }
        y += 38;
        if (d.Effect == Outcome.Light)
        {
            Text(dc, $"3. CLICK WHAT IT LIGHTS ({d.Targets.Count})", new Point(x, y), 21, ed.PickingTargets ? Yellow : Ink);
            Button(dc, new Rect(Content.Right - 178, y - 14, 164, 28), ed.PickingTargets ? "CHOOSING LIGHTS" : "CHOOSING TRIGGERS", () => { ed.PickingTargets = !ed.PickingTargets; ed.Heard.Add(Sfx.Click); }, size: 17);
        }
        y += 38;
        string s2 = Sim.Describe(t, d);
        double size2 = 21;
        while (TextWidth(s2, size2) > Pw - 30 && size2 > 14) size2 -= 1;
        Text(dc, s2, new Point(x, y), size2, Cyan);
        Button(dc, new Rect(x, Content.Bottom - 50, 150, 38), "DONE", () => ed.FinishRule(), size: 24, accent: Green);
        Button(dc, new Rect(x + 162, Content.Bottom - 50, 150, 38), "CANCEL", ed.CancelRule, size: 24);
    }

    /// <summary>The sample tables, the user's own, and saving.</summary>
    private void DrawTablesTab(DrawingContext dc)
    {
        var g = Engine;
        double x = Content.X + 14, y = Content.Y + 18;
        Text(dc, "SAMPLE TABLES", new Point(x, y), 24, Yellow);
        var names = Samples.Names.Append("BLANK TABLE").ToArray();
        for (int i = 0; i < names.Length; i++)
        {
            var r = new Rect(x + i % 2 * 270, y + 18 + i / 2 * 38, 262, 32);
            string n = names[i];
            Button(dc, r, n, () => { if (n == "BLANK TABLE") g.OpenBlank(); else g.OpenSample(n); }, size: 21, on: g.Editor.Table.Name == n);
        }
        y += 140;
        Text(dc, "YOUR TABLES", new Point(x, y), 24, Yellow);
        if (_clock - _savedAt > 2 || _savedAt > _clock)
        {
            _saved = g.SavedTables();
            _savedAt = _clock;
        }
        if (_saved.Count == 0) Text(dc, "NONE SAVED YET", new Point(x, y + 32), 21, Dim);
        for (int i = 0; i < Math.Min(_saved.Count, 6); i++)
        {
            var r = new Rect(x + i % 2 * 270, y + 18 + i / 2 * 36, 262, 30);
            string path = _saved[i].Path;
            Button(dc, r, _saved[i].Name, () => g.OpenFile(path), size: 20);
        }
        bool typing = g.Renaming != null;
        dc.FillRectangle(Brush(typing ? 0xFF101428 : 0xFF161A2E), NameRect, 6);
        dc.DrawRectangle(null, Pen(typing ? Yellow : 0xFF5A6AA0, 1.5), NameRect, 6, 6);
        string shown = typing ? g.Renaming + (Blink(3) ? "_" : " ") : g.Editor.Table.Name;
        Text(dc, "NAME", new Point(NameRect.X + 10, NameRect.Center.Y), 18, Dim);
        Text(dc, shown, new Point(NameRect.X + 64, NameRect.Center.Y), 26, typing ? Yellow : White);
        Hit(NameRect, () => { if (g.Renaming == null) g.BeginRename(); else g.FinishRename(); });
        Button(dc, new Rect(NameRect.Right + 12, NameRect.Y, 184, NameRect.Height), "SAVE TABLE", () => { g.FinishRename(); g.SaveTable(); _savedAt = -10; }, size: 22, accent: Green);
        Text(dc, typing ? "TYPE A NAME, THEN ENTER" : "CLICK THE NAME TO CHANGE IT", new Point(x, Content.Bottom - 10), 16, 0xFF6A78A8);
    }

    // ───────────────────────────── the backglass ─────────────────────────────

    /// <summary>In play: the backglass, with every player's score, the ball, the bonus and the lamps.</summary>
    private void DrawBackglass(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        var r = new Rect(Px, 10, Pw, 748);
        Panel(dc, r, 0xFF101428);
        DrawMarquee(dc, w.Table.Name);
        double y = 222;
        bool canAdd = g.State == GameState.Playing && w.BallNo == 1 && w.Player == 0 && w.Players < World.MaxPlayers;
        for (int i = 0; i < w.Players; i++)
        {
            bool up = i == w.Player && !w.Over;
            var row = new Rect(Px + 16, y + i * 62 - 26, Pw - 32, 54);
            dc.FillRectangle(Brush(up ? 0xFF2A2410 : 0xFF161A2E), row, 6);
            dc.DrawRectangle(null, Pen(up ? Yellow : 0xFF3A4270, 1.5), row, 6, 6);
            Text(dc, $"PLAYER {i + 1}", new Point(row.X + 16, row.Center.Y), 26, up ? Yellow : Dim);
            Digits(dc, w.Scores[i], new Point(row.Right - 16, row.Center.Y), 46, up ? 0xFFFF8A30 : 0xFFA05A20);
            if (up && Blink(2)) Poly(dc, Yellow, null, 0, new Point(row.X - 10, row.Center.Y - 8), new Point(row.X - 2, row.Center.Y), new Point(row.X - 10, row.Center.Y + 8));
        }
        y += Math.Max(1, w.Players) * 62 - 20;
        if (canAdd && g.State == GameState.Playing)
        {
            Text(dc, "PRESS ENTER TO ADD A PLAYER", new Point(r.Center.X, y + 4), 22, Blink(2) ? Green : Dim, center: true);
            y += 28;
        }
        DrawCard(dc, w.Table, new Rect(Px + 16, y + 8, Pw - 32, 506 - y - 8));
        // The lamps.
        y = 520;
        Lamp(dc, new Rect(Px + 16, y, 170, 44), w.Over ? "GAME OVER" : $"BALL {w.BallNo} OF {w.Table.Balls}", !w.Over, Cyan);
        Lamp(dc, new Rect(Px + 195, y, 170, 44), "SHOOT AGAIN", w.ExtraBalls[w.Player] > 0, Green);
        Lamp(dc, new Rect(Px + 374, y, 170, 44), "TILT", w.Tilted, Red);
        y += 54;
        Lamp(dc, new Rect(Px + 16, y, 170, 44), $"BONUS {w.Bonus:N0}", w.Bonus > 0, Yellow);
        Lamp(dc, new Rect(Px + 195, y, 170, 44), $"BONUS {w.Multiplier}X", w.Multiplier > 1, Orange);
        Lamp(dc, new Rect(Px + 374, y, 170, 44), "MULTIBALL", w.Balls.Count + w.Waiting > 1, 0xFFF070B8);
        y += 66;
        var top = g.HighScores.Entries.FirstOrDefault();
        if (top != null) Text(dc, $"HIGH SCORE  {top.Initials}  {top.Score:N0}", new Point(r.Center.X, y), 24, White, center: true);
        y += 34;
        if (g.State == GameState.Attract)
        {
            Text(dc, "DEMONSTRATION", new Point(r.Center.X, y), 30, Blink(1.5) ? Yellow : Orange, center: true);
            Text(dc, "PRESS ENTER TO BUILD A TABLE", new Point(r.Center.X, y + 32), 22, White, center: true);
        }
        else
        {
            Text(dc, "Z / LEFT SHIFT: LEFT FLIPPER    / OR RIGHT SHIFT: RIGHT", new Point(r.Center.X, y), 18, Dim, center: true);
            Text(dc, "SPACE OR DOWN: PULL THE PLUNGER, LET GO TO SHOOT", new Point(r.Center.X, y + 22), 18, Dim, center: true);
            Text(dc, "ARROWS: NUDGE    P: PAUSE    ESC: CONSTRUCTION SET", new Point(r.Center.X, y + 44), 18, Dim, center: true);
        }
    }

    /// <summary>The instruction card: the table's rules, as many as there's room for.</summary>
    private static void DrawCard(DrawingContext dc, Table t, Rect r)
    {
        if (r.Height < 50) return;
        dc.FillRectangle(Brush(0xFFEDE6D2), r, 4);
        dc.DrawRectangle(null, Pen(0xFF8A7A5A, 1.5), r.Deflate(4), 2, 2);
        Text(dc, "HOW TO SCORE", new Point(r.Center.X, r.Y + 20), 22, 0xFF2A2018, center: true, shadow: false);
        var lines = t.Rules.Select(rule => Sim.Describe(t, rule)).ToList();
        if (lines.Count == 0) lines.Add("EVERY PART SCORES ITS OWN POINTS");
        if (t.ExtraBallAt > 0) lines.Add($"EXTRA BALL AT {t.ExtraBallAt:N0}");
        int room = (int)((r.Height - 44) / 24);
        for (int i = 0; i < Math.Min(room, lines.Count); i++)
        {
            double size = 19;
            while (TextWidth(lines[i], size) > r.Width - 44 && size > 13) size -= 0.5;
            dc.FillEllipse(r.X + 20, r.Y + 46 + i * 24, 3, 0xFFB0283A);
            Text(dc, lines[i], new Point(r.X + 32, r.Y + 46 + i * 24), size, 0xFF2A2018, shadow: false);
        }
    }

    /// <summary>The top of the backglass: the program's name over the table's, in lights.</summary>
    private void DrawMarquee(DrawingContext dc, string name)
    {
        var art = new Rect(Px + 16, 24, Pw - 32, 150);
        dc.FillRectangle(Brush(0xFF1A1030), art, 10);
        for (int i = 0; i < Stripes.Length; i++)
            dc.FillRectangle(Brush(Alpha(Stripes[i], 0.85)), new Rect(art.X, art.Y + 96 + i * 8, art.Width, 6));
        // Bulbs round the edge, chasing.
        int n = 34;
        for (int i = 0; i < n; i++)
        {
            double t = i / (double)n, px, py;
            double per = 2 * (art.Width + art.Height), d = t * per;
            if (d < art.Width) { px = art.X + d; py = art.Y; }
            else if (d < art.Width + art.Height) { px = art.Right; py = art.Y + d - art.Width; }
            else if (d < 2 * art.Width + art.Height) { px = art.Right - (d - art.Width - art.Height); py = art.Bottom; }
            else { px = art.X; py = art.Bottom - (d - 2 * art.Width - art.Height); }
            bool on = (i + (int)(_clock * 8)) % 3 == 0;
            dc.FillEllipse(px, py, 4, on ? 0xFFFFF0A0 : 0xFF5A4A20);
        }
        Text(dc, "AVA PINBALL", new Point(art.Center.X, art.Y + 44), 66, White, center: true);
        double size = name.Length > 12 ? 34 : 40, width = TextWidth(name, size);
        var band = new Rect(art.Center.X - width / 2 - 18, art.Y + 92, width + 36, 46);
        dc.FillRectangle(Brush(0xF0100A20), band, 8);
        dc.DrawRectangle(null, Pen(Yellow, 1.5), band, 8, 8);
        Text(dc, name, band.Center, size, Yellow, center: true);
    }

    /// <summary>A score in the backglass's lit digits.</summary>
    private static void Digits(DrawingContext dc, int value, Point right, double size, uint colour)
    {
        string s = value.ToString("N0", CultureInfo.InvariantCulture);
        Text(dc, "8,888,888".Substring(Math.Max(0, 9 - Math.Max(s.Length, 1))), new Point(right.X, right.Y), size, Alpha(colour, 0.12), right: true, shadow: false);
        Text(dc, s, right, size, colour, right: true);
    }

    private static void Lamp(DrawingContext dc, Rect r, string label, bool on, uint colour)
    {
        dc.FillRectangle(Brush(on ? Shade(colour, 0.45) : 0xFF141828), r, 6);
        dc.DrawRectangle(null, Pen(on ? colour : 0xFF2A3050, 1.5), r, 6, 6);
        Text(dc, label, r.Center, 22, on ? Blend(colour, 0xFFFFFFFF, 0.5) : 0xFF4A5070, center: true);
    }

    // ───────────────────────────── the title and the scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        var r = new Rect(Px, 10, Pw, 748);
        Panel(dc, r, 0xFF101428);
        double cx = r.Center.X;
        Text(dc, "A V A", new Point(cx, 48), 30, Dim, center: true);
        string word = "PINBALL";
        for (int i = 0; i < word.Length; i++)
        {
            double tx = cx - (word.Length - 1) * 33 + i * 66, ty = 120 + Math.Sin(_clock * 2.2 + i * 0.6) * 5;
            Text(dc, word[i].ToString(), new Point(tx, ty), 140, Stripes[i % Stripes.Length], center: true);
        }
        for (int i = 0; i < Stripes.Length; i++) dc.FillRectangle(Brush(Stripes[i]), new Rect(Px + 20, 186 + i * 7, Pw - 40, 5));
        Text(dc, "CONSTRUCTION SET", new Point(cx, 250), 62, White, center: true);
        Text(dc, "IN THE STYLE OF BILL BUDGE'S 1983 CLASSIC", new Point(cx, 292), 24, Yellow, center: true);
        // The parts, on parade.
        PartKind[] parade = [PartKind.FlipperLeft, PartKind.Bumper, PartKind.SlingLeft, PartKind.DropTarget, PartKind.Spinner, PartKind.Kickout, PartKind.Magnet, PartKind.FlipperRight];
        for (int i = 0; i < parade.Length; i++)
            DrawIcon(dc, parade[i], new Point(Px + 48 + i * 66, 352 + Math.Sin(_clock * 3 + i) * 3), IconScale(parade[i]) * 0.8);
        string[] lines =
        [
            "BUILD A PINBALL TABLE FROM A BOX OF PARTS:", "FLIPPERS, BUMPERS, KICKERS, TARGETS, SPINNERS,", "MAGNETS AND HOLES. DRAW THE WALLS, PAINT IT,",
            "WIRE UP THE RULES, TURN THE PHYSICS KNOBS -", "THEN PLAY IT, UP TO FOUR OF YOU.",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx, 410 + i * 28), 23, Ink, center: true);
        if (Blink(2)) Text(dc, "PRESS ENTER TO BUILD A TABLE", new Point(cx, 580), 38, Yellow, center: true);
        Text(dc, "MOUSE: BUILD    Z AND /: FLIPPERS    SPACE: PLUNGER", new Point(cx, 640), 19, Dim, center: true);
        Text(dc, "ARROWS: NUDGE    P: PAUSE    M: SOUND    CTRL+S: SCREENSHOT", new Point(cx, 664), 19, Dim, center: true);
        Text(dc, "MUSIC: THE ENTERTAINER, SCOTT JOPLIN (1902)", new Point(cx, 716), 18, 0xFF6A78A8, center: true);
        Text(dc, "AN INDEPENDENT TRIBUTE - NOT AFFILIATED WITH ELECTRONIC ARTS", new Point(cx, 738), 16, 0xFF5A6890, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        var r = new Rect(Px, 10, Pw, 748);
        Panel(dc, r, 0xFF101428);
        DrawMarquee(dc, g.World.Table.Name);
        double cx = r.Center.X;
        Text(dc, "HIGH SCORES", new Point(cx, 238), 52, Yellow, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && Blink(4);
            uint c = mine ? Red : i == 0 ? Yellow : White;
            double y = 296 + i * 38;
            Text(dc, $"{i + 1,2}.", new Point(Px + 110, y), 32, Dim);
            Text(dc, e.Initials, new Point(Px + 180, y), 34, c);
            Text(dc, e.Score.ToString("N0", CultureInfo.InvariantCulture), new Point(Px + 450, y), 34, c, right: true);
        }
        Text(dc, g.State == GameState.HighScores ? "ENTER: PLAY    ESC: CONSTRUCTION SET" : "", new Point(cx, 700), 24, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        var r = new Rect(Px, 10, Pw, 748);
        Panel(dc, r, 0xFF101428);
        DrawMarquee(dc, g.World.Table.Name);
        double cx = r.Center.X;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 260), 58, Yellow, center: true);
        if (g.World.Players > 1) Text(dc, $"PLAYER {g.EntryPlayer + 1}", new Point(cx, 312), 30, Cyan, center: true);
        if (g.EntryPlayer >= 0) Digits(dc, g.World.Scores[g.EntryPlayer], new Point(cx + 150, 370), 56, 0xFFFF8A30);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 440), 32, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && Blink(3);
                Text(dc, s[i].ToString(), new Point(cx - 70 + i * 70, 520), 90, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 94 + i * 70, 566, 48, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 640), 24, White, center: true);
    }
}
