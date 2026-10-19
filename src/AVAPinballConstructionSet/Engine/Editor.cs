namespace AVAPinballConstructionSet.Engine;

/// <summary>
/// The tools: the hand picks parts up, moves them and puts them back in the box; the wall draws a straight wall
/// with a drag; the shape draws a solid shape a corner at a click; the curve draws a curved wall (drag its ends,
/// then bend it); the paint brush colours whatever it touches; the magnifier looks closer; the wire connects parts
/// into rules.
/// </summary>
public enum Tool { Hand, Wall, Shape, Curve, Paint, Magnify, Wire }

/// <summary>The panel under the parts box: the part in hand, the physics knobs, the rules, and the tables on disk.</summary>
public enum Tab { Part, Physics, Rules, Tables }

/// <summary>
/// The construction set with no UI: a table and everything done to it. The mouse comes in as presses, moves and
/// releases at points on the table (in table units); each change is kept so it can be undone.
/// </summary>
public sealed class Editor
{
    public const int UndoDepth = 60;
    public const double Magnification = 2.5;
    /// <summary>How near to a wall's corner a press takes hold of the corner, and a new wall's end snaps to one.</summary>
    public const double Snap = 8;

    private readonly List<Table> _undo = [];
    private Vec _grab, _pressAt;
    private bool _moved;

    public Editor(Table? table = null) => Table = table ?? Samples.Blank();

    public Table Table { get; private set; }
    public Tool Tool { get; private set; } = Tool.Hand;
    public Tab Tab { get; set; } = Tab.Part;
    public uint Colour { get; set; } = Parts.Palette[0];
    /// <summary>The part last picked up or put down (-1: none).</summary>
    public int Selected { get; private set; } = -1;
    /// <summary>A part in the hand, following the mouse: one fresh from the box, or one lifted off the table.</summary>
    public Part? Carried { get; private set; }
    public bool CarriedIsNew { get; private set; }
    /// <summary>A wall's corner being dragged: which wall, which corner.</summary>
    public (int Part, int Corner)? Corner { get; private set; }
    /// <summary>A wall or a curve being drawn: from, to.</summary>
    public Vec? From { get; private set; }
    public Vec To { get; private set; }
    /// <summary>A curve whose ends are down, waiting to be bent by the mouse and fixed with a click.</summary>
    public (Vec A, Vec B)? CurveEnds { get; private set; }
    public Vec Bend { get; private set; }
    /// <summary>A shape's corners so far.</summary>
    public List<Vec> ShapePoints { get; } = [];
    /// <summary>The rule being wired, and whether clicks now choose what it lights rather than what sets it off.</summary>
    public Rule? Draft { get; private set; }
    public bool PickingTargets { get; set; }
    /// <summary>The magnifier: how much bigger, and the point at the middle of the view.</summary>
    public double Zoom { get; private set; } = 1;
    public Vec ZoomCentre { get; private set; } = new(Field.TableW / 2, Field.TableH / 2);
    public bool CanUndo => _undo.Count > 0;
    /// <summary>Changed since it was last saved (the engine keeps the working table on disk).</summary>
    public bool Changed { get; set; }
    public List<Sfx> Heard { get; } = [];
    public string? Message { get; private set; }
    public double MessageTime { get; private set; }

    public Part? SelectedPart => Selected >= 0 && Selected < Table.Parts.Count ? Table.Parts[Selected] : null;

    public void Tick(double dt)
    {
        if (MessageTime > 0 && (MessageTime -= dt) <= 0) Message = null;
    }

    public void Say(string text, double seconds = 2.5)
    {
        Message = text;
        MessageTime = seconds;
    }

    /// <summary>A whole new table to work on (a sample, a file, a blank): what was there can be had back with undo.</summary>
    public void Open(Table t)
    {
        Remember();
        Table = t.Copy();
        Selected = -1;
        Cancel();
        Changed = true;
    }

    public void SetTool(Tool t)
    {
        Cancel();
        Tool = t;
        if (t == Tool.Wire) Tab = Tab.Rules;
        Heard.Add(Sfx.Click);
    }

    /// <summary>Puts down whatever is half done: a shape's corners, a curve waiting to be bent, a part in the hand.</summary>
    public void Cancel()
    {
        if (ShapePoints.Count >= 3 && Tool == Tool.Shape) FinishShape();
        ShapePoints.Clear();
        From = null;
        CurveEnds = null;
        Carried = null;
        Corner = null;
    }

    private void Remember()
    {
        _undo.Add(Table.Copy());
        if (_undo.Count > UndoDepth) _undo.RemoveAt(0);
    }

    public bool Undo()
    {
        ShapePoints.Clear();
        Cancel();
        if (_undo.Count == 0) { Heard.Add(Sfx.Refused); return false; }
        Table = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        Selected = -1;
        Draft = null;
        Changed = true;
        Heard.Add(Sfx.Undo);
        return true;
    }

    private static Vec Clamp(Vec p) => new(Math.Clamp(p.X, 0, Field.TableW), Math.Clamp(p.Y, 0, Field.TableH));

    /// <summary>A point near a wall's corner snaps to it, so walls join up.</summary>
    public Vec SnapTo(Vec p)
    {
        foreach (var part in Table.Parts)
            if (part.IsWall)
                foreach (var c in part.Points)
                    if (Vec.Distance(c, p) < Snap / Zoom) return c;
        return Clamp(new Vec(Math.Round(p.X), Math.Round(p.Y)));
    }

    // ───────────────────────────── parts ─────────────────────────────

    /// <summary>The part under a point, the one on top first (-1: none).</summary>
    public int HitTest(Vec at)
    {
        int best = -1;
        double bestD = double.MaxValue;
        for (int i = Table.Parts.Count - 1; i >= 0; i--)
        {
            var p = Table.Parts[i];
            double d = Distance(p, at);
            double reach = p.IsWall ? 6 : Parts.Reach(p.Kind);
            if (d < reach / Math.Sqrt(Zoom) + 0.5 && d < bestD - 1e-9) { best = i; bestD = d; }
        }
        return best;
    }

    /// <summary>How far a point is from a part (0: on it).</summary>
    public static double Distance(Part p, Vec at)
    {
        switch (p.Kind)
        {
            case PartKind.Wall:
            {
                double d = double.MaxValue;
                for (int k = 1; k < p.Points.Count; k++) d = Math.Min(d, Collide.SegmentDistance(p.Points[k - 1], p.Points[k], at));
                return d;
            }
            case PartKind.Shape:
            {
                if (Collide.Inside(p.Points, at)) return 0;
                double d = double.MaxValue;
                for (int k = 0; k < p.Points.Count; k++) d = Math.Min(d, Collide.SegmentDistance(p.Points[k], p.Points[(k + 1) % p.Points.Count], at));
                return d;
            }
            case PartKind.Curve:
            {
                var shapes = new List<Shape>();
                Sim.AddShapes(shapes, p, 0);
                return shapes.Count == 0 ? double.MaxValue : Math.Max(0, Collide.Nearest(shapes[0], at).D);
            }
            case PartKind.FlipperLeft or PartKind.FlipperRight:
            {
                var tip = p.At + Vec.Polar(Sim.Rest(p).Down, Parts.FlipperLength);
                return Math.Max(0, Collide.SegmentDistance(p.At, tip, at) - 2);
            }
            case PartKind.SlingLeft or PartKind.SlingRight:
            {
                var c = Parts.Sling(p.Kind).Select(v => p.Local(v.X, v.Y)).ToList();
                return Collide.Inside(c, at) ? 0 : Math.Max(0, Vec.Distance(p.At, at) - 10);
            }
            default:
                return Vec.Distance(p.At, at);
        }
    }

    /// <summary>A new part straight onto the table.</summary>
    public Part Place(PartKind kind, Vec at)
    {
        Remember();
        var p = Parts.Make(kind, Clamp(at), Table.NextId());
        Table.Parts.Add(p);
        Selected = Table.Parts.Count - 1;
        Changed = true;
        Heard.Add(Sfx.Place);
        Tab = Tab.Part;
        return p;
    }

    /// <summary>A part taken from the box into the hand, to be put down where the mouse lets go.</summary>
    public void TakeFromBox(PartKind kind, Vec at)
    {
        Cancel();
        if (Tool != Tool.Hand) Tool = Tool.Hand;
        Carried = Parts.Make(kind, at);
        CarriedIsNew = true;
        _grab = default;
        Heard.Add(Sfx.Pick);
    }

    public void Move(int index, Vec to)
    {
        var p = Table.Parts[index];
        var d = Clamp(to) - p.At;
        Shift(p, d);
        Changed = true;
    }

    private static void Shift(Part p, Vec d)
    {
        p.At += d;
        for (int k = 0; k < p.Points.Count; k++) p.Points[k] = Clamp(p.Points[k] + d);
    }

    public void Delete(int index) => Delete(index, true);

    private void Delete(int index, bool remember)
    {
        if (index < 0 || index >= Table.Parts.Count) return;
        if (remember) Remember();
        Table.Remove(index);
        if (Draft != null)
        {
            var ids = Table.Parts.Select(q => q.Id).ToHashSet();
            Draft.Sources.RemoveAll(id => !ids.Contains(id));
            Draft.Targets.RemoveAll(id => !ids.Contains(id));
        }
        Selected = -1;
        Changed = true;
        Heard.Add(Sfx.Delete);
    }

    public void DeleteSelected()
    {
        if (Selected >= 0) Delete(Selected);
        else Heard.Add(Sfx.Refused);
    }

    /// <summary>Turns the part in hand (or chosen) by some degrees; walls turn about their middle.</summary>
    public void Rotate(int index, double degrees)
    {
        if (index < 0 || index >= Table.Parts.Count) return;
        Remember();
        var p = Table.Parts[index];
        if (p.IsWall)
        {
            var c = new Vec(p.Points.Average(v => v.X), p.Points.Average(v => v.Y));
            for (int k = 0; k < p.Points.Count; k++) p.Points[k] = Clamp(c + (p.Points[k] - c).Rotate(degrees * Math.PI / 180));
            p.At = p.Points[0];
        }
        else p.Angle = ((p.Angle + degrees) % 360 + 360) % 360;
        Changed = true;
        Heard.Add(Sfx.Click);
    }

    public static readonly int[] ScoreSteps = [0, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 25000, 50000];

    /// <summary>A part's score one step up or down the scale.</summary>
    public void StepScore(int index, int by)
    {
        if (index < 0 || index >= Table.Parts.Count) return;
        var p = Table.Parts[index];
        SetScore(index, Step(ScoreSteps, p.Score, by));
    }

    public void SetScore(int index, int score)
    {
        Remember();
        Table.Parts[index].Score = Math.Clamp(score, 0, 1_000_000);
        Changed = true;
        Heard.Add(Sfx.Click);
    }

    /// <summary>The next value along a scale from the one nearest a value.</summary>
    public static int Step(int[] scale, int value, int by)
    {
        if (by > 0)
        {
            int i = Array.FindIndex(scale, v => v > value);
            return i < 0 ? scale[^1] : scale[Math.Min(scale.Length - 1, i + by - 1)];
        }
        if (by < 0)
        {
            int i = Array.FindLastIndex(scale, v => v < value);
            return i < 0 ? scale[0] : scale[Math.Max(0, i + by + 1)];
        }
        return value;
    }

    public void Paint(int index, uint colour)
    {
        Remember();
        if (index < 0) Table.Playfield = colour;
        else Table.Parts[index].Colour = colour;
        Changed = true;
        Heard.Add(Sfx.Paint);
    }

    public Part DrawWall(Vec a, Vec b)
    {
        Remember();
        var p = new Part { Id = Table.NextId(), Kind = PartKind.Wall, Colour = Colour, Points = [Clamp(a), Clamp(b)] };
        p.At = p.Points[0];
        Table.Parts.Add(p);
        Selected = Table.Parts.Count - 1;
        Changed = true;
        Heard.Add(Sfx.Place);
        return p;
    }

    public Part DrawShape(IEnumerable<Vec> corners)
    {
        Remember();
        var p = new Part { Id = Table.NextId(), Kind = PartKind.Shape, Colour = Colour, Points = corners.Select(Clamp).ToList() };
        p.At = p.Points[0];
        Table.Parts.Add(p);
        Selected = Table.Parts.Count - 1;
        Changed = true;
        Heard.Add(Sfx.Place);
        return p;
    }

    public Part DrawCurve(Vec a, Vec bend, Vec b)
    {
        Remember();
        var p = new Part { Id = Table.NextId(), Kind = PartKind.Curve, Colour = Colour, Points = [Clamp(a), Clamp(bend), Clamp(b)] };
        p.At = p.Points[1];
        Table.Parts.Add(p);
        Selected = Table.Parts.Count - 1;
        Changed = true;
        Heard.Add(Sfx.Place);
        return p;
    }

    private void FinishShape()
    {
        var corners = ShapePoints.ToList();
        ShapePoints.Clear();
        if (corners.Count >= 3) DrawShape(corners);
    }

    // ───────────────────────────── the mouse ─────────────────────────────

    /// <summary>The left button goes down on the table.</summary>
    public void Press(Vec at)
    {
        at = Clamp(at);
        _pressAt = at;
        _moved = false;
        switch (Tool)
        {
            case Tool.Hand:
            {
                if (CornerAt(at) is { } c)
                {
                    Remember();
                    Corner = c;
                    Selected = c.Part;
                    Heard.Add(Sfx.Pick);
                    return;
                }
                int hit = HitTest(at);
                Selected = hit;
                if (hit < 0) return;
                Remember();
                Carried = Table.Parts[hit];
                CarriedIsNew = false;
                _grab = at - Carried.At;
                Tab = Tab.Part;
                Heard.Add(Sfx.Pick);
                return;
            }
            case Tool.Wall:
                From = SnapTo(at);
                To = From.Value;
                return;
            case Tool.Curve:
                if (CurveEnds is { } ends)
                {
                    DrawCurve(ends.A, at, ends.B);
                    CurveEnds = null;
                    return;
                }
                From = SnapTo(at);
                To = From.Value;
                return;
            case Tool.Shape:
            {
                var p = SnapTo(at);
                if (ShapePoints.Count >= 3 && Vec.Distance(p, ShapePoints[0]) < Snap / Zoom * 1.5) { FinishShape(); return; }
                if (ShapePoints.Count > 0 && Vec.Distance(p, ShapePoints[^1]) < 2) return;
                ShapePoints.Add(p);
                Heard.Add(Sfx.Click);
                if (ShapePoints.Count >= 24) FinishShape();
                return;
            }
            case Tool.Paint:
                Paint(HitTest(at), Colour);
                return;
            case Tool.Magnify:
                Magnify(at);
                return;
            case Tool.Wire:
                WireClick(HitTest(at));
                return;
        }
    }

    /// <summary>The magnifier: a closer look round a point, or back to the whole table.</summary>
    public void Magnify(Vec at)
    {
        if (Zoom > 1) Zoom = 1;
        else
        {
            Zoom = Magnification;
            ZoomCentre = Clamp(at);
        }
        Heard.Add(Sfx.Click);
    }

    /// <summary>The mouse moves (with the button down or not).</summary>
    public void Drag(Vec at)
    {
        at = Clamp(at);
        if (Vec.Distance(at, _pressAt) > 1) _moved = true;
        if (Carried != null)
        {
            if (CarriedIsNew) Carried.At = at;
            else
            {
                Shift(Carried, Clamp(at - _grab) - Carried.At);
                Changed = true;
            }
            return;
        }
        if (Corner is { } c)
        {
            var p = Table.Parts[c.Part];
            p.Points[c.Corner] = at;
            if (c.Corner == 0) p.At = at;
            if (p.Kind == PartKind.Curve && c.Corner == 1) p.At = at;
            Changed = true;
            return;
        }
        if (From != null) To = Tool == Tool.Wall ? SnapTo(at) : at;
        else if (CurveEnds != null) Bend = at;
    }

    /// <summary>The left button comes up: over the table, or over the parts box (where a part in hand is put away).</summary>
    public void Release(Vec at, bool overBox = false)
    {
        at = Clamp(at);
        if (Carried is { } part)
        {
            Carried = null;
            if (CarriedIsNew)
            {
                if (!overBox) Place(part.Kind, at);
                else Heard.Add(Sfx.Click);
                return;
            }
            int index = Table.Parts.IndexOf(part);
            if (overBox)
            {
                // Back in the box: it's gone. (The press already kept the table as it was, for undo.)
                Delete(index, false);
                return;
            }
            if (!_moved) _undo.RemoveAt(_undo.Count - 1);
            else Heard.Add(Sfx.Place);
            return;
        }
        if (Corner != null)
        {
            if (!_moved) _undo.RemoveAt(_undo.Count - 1);
            else Heard.Add(Sfx.Place);
            Corner = null;
            return;
        }
        if (From is { } from)
        {
            var to = Tool == Tool.Wall ? SnapTo(at) : at;
            From = null;
            if (Vec.Distance(from, to) < 6) return;
            if (Tool == Tool.Wall) DrawWall(from, to);
            else
            {
                CurveEnds = (from, to);
                Bend = (from + to) / 2;
                Heard.Add(Sfx.Click);
            }
        }
    }

    /// <summary>The right button: finishes a shape, straightens a waiting curve, or lets go of what's in hand.</summary>
    public void RightClick()
    {
        if (Tool == Tool.Shape && ShapePoints.Count >= 3) { FinishShape(); return; }
        if (CurveEnds is { } ends)
        {
            DrawCurve(ends.A, (ends.A + ends.B) / 2, ends.B);
            CurveEnds = null;
            return;
        }
        if (Zoom > 1 && Tool == Tool.Magnify) { Zoom = 1; return; }
        Cancel();
    }

    /// <summary>A wall's corner under a point, if any (the chosen wall's first).</summary>
    public (int Part, int Corner)? CornerAt(Vec at)
    {
        (int, int)? best = null;
        double bestD = Snap / Zoom;
        for (int i = 0; i < Table.Parts.Count; i++)
        {
            var p = Table.Parts[i];
            if (!p.IsWall) continue;
            for (int k = 0; k < p.Points.Count; k++)
            {
                double d = Vec.Distance(p.Points[k], at) - (i == Selected ? 1 : 0);
                if (d < bestD) { bestD = d; best = (i, k); }
            }
        }
        return best;
    }

    // ───────────────────────────── the rules ─────────────────────────────

    /// <summary>A new rule, waiting for its parts to be chosen.</summary>
    public void NewRule()
    {
        Cancel();
        Tool = Tool.Wire;
        Tab = Tab.Rules;
        Draft = new Rule { When = When.All, Effect = Outcome.Points, Amount = 5000 };
        PickingTargets = false;
        Heard.Add(Sfx.Click);
    }

    /// <summary>A part clicked with the wire: in or out of the rule's parts (or its targets).</summary>
    public void WireClick(int index)
    {
        if (Draft == null) NewRule();
        if (index < 0 || Table.Parts[index].IsWall || Parts.IsFlipper(Table.Parts[index].Kind)) { Heard.Add(Sfx.Refused); return; }
        int id = Table.Parts[index].Id;
        var list = PickingTargets ? Draft!.Targets : Draft!.Sources;
        if (!list.Remove(id)) list.Add(id);
        Heard.Add(Sfx.Wire);
    }

    public void SetEffect(Outcome e)
    {
        if (Draft == null) return;
        Draft.Effect = e;
        PickingTargets = e == Outcome.Light;
        Draft.Amount = e switch { Outcome.Points or Outcome.Light => Draft.Amount == 0 ? 5000 : Draft.Amount, _ => 0 };
        Heard.Add(Sfx.Click);
    }

    public void ToggleWhen()
    {
        if (Draft == null) return;
        Draft.When = Draft.When == When.All ? When.Any : When.All;
        Heard.Add(Sfx.Click);
    }

    public static readonly int[] AmountSteps = [500, 1000, 2000, 2500, 3000, 5000, 7500, 10000, 15000, 20000, 25000, 50000, 100000];

    public void StepAmount(int by)
    {
        if (Draft == null || Draft.Effect is not (Outcome.Points or Outcome.Light)) return;
        Draft.Amount = Step(AmountSteps, Draft.Amount, by);
        Heard.Add(Sfx.Click);
    }

    /// <summary>The rule is done: onto the table, if it's whole.</summary>
    public bool FinishRule()
    {
        if (Draft is not { } r) return false;
        if (r.Sources.Count == 0 || r.Effect == Outcome.Light && r.Targets.Count == 0)
        {
            Say(r.Sources.Count == 0 ? "CLICK THE PARTS THAT SET IT OFF" : "CLICK THE PARTS TO LIGHT");
            Heard.Add(Sfx.Refused);
            return false;
        }
        Remember();
        Table.Rules.Add(r);
        Draft = null;
        PickingTargets = false;
        Changed = true;
        Heard.Add(Sfx.Place);
        return true;
    }

    public void CancelRule()
    {
        Draft = null;
        PickingTargets = false;
        Heard.Add(Sfx.Click);
    }

    public void DeleteRule(int index)
    {
        if (index < 0 || index >= Table.Rules.Count) return;
        Remember();
        Table.Rules.RemoveAt(index);
        Changed = true;
        Heard.Add(Sfx.Delete);
    }

    // ───────────────────────────── the knobs ─────────────────────────────

    public enum Knob { Gravity, Speed, Elasticity, Kick }

    public int Get(Knob k) => k switch
    {
        Knob.Gravity => Table.Physics.Gravity, Knob.Speed => Table.Physics.Speed, Knob.Elasticity => Table.Physics.Elasticity, _ => Table.Physics.Kick,
    };

    public void Set(Knob k, int value)
    {
        value = Math.Clamp(value, Physics.Min, Physics.Max);
        if (Get(k) == value) return;
        Remember();
        switch (k)
        {
            case Knob.Gravity: Table.Physics.Gravity = value; break;
            case Knob.Speed: Table.Physics.Speed = value; break;
            case Knob.Elasticity: Table.Physics.Elasticity = value; break;
            default: Table.Physics.Kick = value; break;
        }
        Changed = true;
        Heard.Add(Sfx.Click);
    }

    public void StepBalls(int by)
    {
        int b = Math.Clamp(Table.Balls + by, 1, Table.MaxBalls);
        if (b == Table.Balls) return;
        Remember();
        Table.Balls = b;
        Changed = true;
        Heard.Add(Sfx.Click);
    }

    public void StepExtraBall(int by)
    {
        int v = Step(Table.ExtraBallSteps, Table.ExtraBallAt, by);
        if (v == Table.ExtraBallAt) return;
        Remember();
        Table.ExtraBallAt = v;
        Changed = true;
        Heard.Add(Sfx.Click);
    }

    public void Rename(string name)
    {
        name = new string(name.ToUpperInvariant().Where(c => c is >= 'A' and <= 'Z' or >= '0' and <= '9' or ' ' or '-' or '!' or '.').Take(18).ToArray()).Trim();
        if (name.Length == 0 || name == Table.Name) return;
        Remember();
        Table.Name = name;
        Changed = true;
    }
}
