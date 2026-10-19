namespace AVAPinballConstructionSet.Engine;

/// <summary>
/// The rules of pinball, one 60th of a second at a time. Each step is cut into small steps, so a flipper never
/// turns more than a few units in one; in each, the ball moves to the first thing in its way (so however fast it
/// goes it can't pass through a wall or a flipper), bounces off it and goes on with the rest of the step.
/// </summary>
public static class Sim
{
    /// <summary>A flipper's swing up and its fall back, in radians a second.</summary>
    public const double FlipUp = 26, FlipDown = 16;
    public const double MaxSpeed = 3200;
    /// <summary>Each nudge adds one to the tilt meter, which runs down by itself: at the warning it says DANGER, at the limit it tilts.</summary>
    public const double TiltLimit = 2.5, TiltWarning = 1.5, TiltDecay = 0.5, NudgePush = 150;
    public const double BonusSeconds = 2.2, HoldSeconds = 1.0, MagnetSeconds = 1.4, MagnetRest = 2.5;
    public const double SearchSeconds = 4;
    public const int MaxBonus = 40000, MaxMultiplier = 5;
    /// <summary>The longest a flipper's tip may move in one small step.</summary>
    private const double FlipStepLength = 3;

    // ───────────────────────────── a new game ─────────────────────────────

    /// <summary>A new game on a table, for some players: the first ball on the plunger.</summary>
    public static void Start(World w, Table t, int players = 1, int seed = 1)
    {
        w.Table = t.Copy();
        w.Statics = Build(w.Table);
        w.States = w.Table.Parts.Select(_ => new PartState()).ToArray();
        w.Players = Math.Clamp(players, 1, World.MaxPlayers);
        w.Player = 0;
        w.BallNo = 1;
        w.Scores = new int[World.MaxPlayers];
        w.ExtraBalls = new int[World.MaxPlayers];
        w.ExtraGiven = new bool[World.MaxPlayers];
        w.Dice = new Dice(seed);
        w.Clock = 0;
        NewBall(w);
    }

    /// <summary>Another player joins, while the first player is still on the first ball.</summary>
    public static bool AddPlayer(World w)
    {
        if (w.Players >= World.MaxPlayers || w.BallNo != 1 || w.Player != 0 || w.Phase != BallPhase.Live) return false;
        w.Players++;
        Say(w, $"PLAYER {w.Players} IN", 1.5);
        w.Heard.Add(Sfx.Coin);
        return true;
    }

    private static void NewBall(World w)
    {
        for (int i = 0; i < w.States.Length; i++)
        {
            var (rest, _) = Rest(w.Table.Parts[i]);
            w.States[i] = new PartState { Angle = rest };
        }
        w.Balls = [new Ball { P = Field.BallStart }];
        w.Bonus = 0;
        w.Multiplier = 1;
        w.TiltMeter = 0;
        w.Tilted = false;
        w.Pull = 0;
        w.Waiting = 0;
        w.Phase = BallPhase.Live;
        w.PhaseTime = 0;
    }

    /// <summary>What the ball can hit that never moves: the frame, and every part but the flippers.</summary>
    public static List<Shape> Build(Table t)
    {
        var list = Field.Frame();
        for (int i = 0; i < t.Parts.Count; i++) AddShapes(list, t.Parts[i], i);
        return list;
    }

    public static void AddShapes(List<Shape> list, Part p, int i)
    {
        switch (p.Kind)
        {
            case PartKind.Bumper: list.Add(Shape.Ring(p.At, Parts.BumperR, Surface.Bumper, i)); break;
            case PartKind.Post: list.Add(Shape.Ring(p.At, Parts.PostR, Surface.Rubber, i)); break;
            case PartKind.SlingLeft or PartKind.SlingRight:
            {
                var c = Parts.Sling(p.Kind).Select(v => p.Local(v.X, v.Y)).ToArray();
                list.Add(Shape.Line(c[0], c[1], Surface.Rubber, i));
                list.Add(Shape.Line(c[1], c[2], Surface.Rubber, i));
                list.Add(Shape.Line(c[2], c[0], Surface.Sling, i));
                break;
            }
            case PartKind.Lane:
                list.Add(Shape.Line(p.Local(-16, -20), p.Local(-16, 20), Surface.Wall, i));
                list.Add(Shape.Line(p.Local(16, -20), p.Local(16, 20), Surface.Wall, i));
                break;
            case PartKind.DropTarget or PartKind.Standup:
                list.Add(Shape.Line(p.Local(-Parts.TargetHalf, 0), p.Local(Parts.TargetHalf, 0), Surface.Target, i));
                break;
            case PartKind.Wall:
                for (int k = 1; k < p.Points.Count; k++) list.Add(Shape.Line(p.Points[k - 1], p.Points[k], Surface.Wall, i));
                break;
            case PartKind.Shape:
                for (int k = 0; k < p.Points.Count; k++) list.Add(Shape.Line(p.Points[k], p.Points[(k + 1) % p.Points.Count], Surface.Wall, i));
                break;
            case PartKind.Curve when p.Points.Count >= 3:
                if (Collide.Arc(p.Points[0], p.Points[1], p.Points[2]) is { } a) list.Add(Shape.Bow(a.Centre, a.R, a.Start, a.Sweep, Surface.Wall, i));
                else list.Add(Shape.Line(p.Points[0], p.Points[2], Surface.Wall, i));
                break;
        }
    }

    /// <summary>A flipper's resting and raised angles on the table.</summary>
    public static (double Down, double Up) Rest(Part p)
    {
        if (!Parts.IsFlipper(p.Kind)) return (0, 0);
        var (rest, up) = Parts.FlipperAngles(p.Kind);
        return (rest + p.Radians, up + p.Radians);
    }

    /// <summary>A flipper at an angle: its pivot, its tip, and the two sides between them.</summary>
    public static void FlipperShapes(List<Shape> list, Part p, int i, double angle, double omega)
    {
        var c1 = p.At;
        var u = Vec.Polar(angle);
        var c2 = c1 + u * Parts.FlipperLength;
        double r1 = Parts.FlipperBase, r2 = Parts.FlipperTip, k = (r1 - r2) / Parts.FlipperLength, s = Math.Sqrt(1 - k * k);
        void Add(Shape sh)
        {
            sh.Surface = Surface.Flipper;
            sh.Part = i;
            sh.Pivot = c1;
            sh.Omega = omega;
            list.Add(sh);
        }
        Add(Shape.Ring(c1, r1));
        Add(Shape.Ring(c2, r2));
        foreach (int side in new[] { 1, -1 })
        {
            var n = u * k + u.Perp * (s * side);
            Add(Shape.Line(c1 + n * r1, c2 + n * r2));
        }
    }

    // ───────────────────────────── a step ─────────────────────────────

    public static void Step(World w, Controls c)
    {
        double dt = Field.Step;
        var t = w.Table;
        double h = dt * t.Physics.TimeScale;
        w.Clock += dt;
        if (w.MessageTime > 0 && (w.MessageTime -= dt) <= 0) w.Message = null;
        if (w.Shake > 0) w.Shake = Math.Max(0, w.Shake - dt);
        for (int i = w.Pops.Count - 1; i >= 0; i--)
        {
            var pop = w.Pops[i];
            pop.T += dt;
            if (pop.T > 1.2) w.Pops.RemoveAt(i);
            else w.Pops[i] = pop;
        }
        w.TiltMeter = Math.Max(0, w.TiltMeter - TiltDecay * dt);
        for (int i = 0; i < w.States.Length; i++)
        {
            var s = w.States[i];
            s.Flash = Math.Max(0, s.Flash - dt);
            s.Cooldown = Math.Max(0, s.Cooldown - h);
            s.Rest = Math.Max(0, s.Rest - h);
            // A magnet lets go when its time is up, and rests before it can grab again.
            if (t.Parts[i].Kind == PartKind.Magnet && s.Timer > 0 && (s.Timer -= h) <= 0)
            {
                s.Timer = 0;
                s.Rest = MagnetRest;
            }
        }
        switch (w.Phase)
        {
            case BallPhase.Over:
                return;
            case BallPhase.Bonus:
                w.PhaseTime += dt;
                Flippers(w, false, false);
                Move(w, h);
                if (w.PhaseTime > BonusSeconds) NextBall(w);
                return;
        }
        w.PhaseTime += dt;
        Handle(w, c, dt);
        Feed(w, dt);
        Holes(w, h);
        Move(w, h);
        Spinners(w, h);
        DropTargets(w, dt);
        Drain(w, dt);
    }

    /// <summary>The flippers, the plunger and the nudge.</summary>
    private static void Handle(World w, Controls c, double dt)
    {
        bool left = c.Left && !w.Tilted, right = c.Right && !w.Tilted;
        if (left && !w.LeftHeld || right && !w.RightHeld) w.Heard.Add(Sfx.Flipper);
        Flippers(w, left, right);
        if (c.Plunge)
        {
            if (w.Pull == 0) w.Heard.Add(Sfx.Pull);
            w.Pull = Math.Min(1, w.Pull + dt / 0.9);
        }
        else if (w.Pull > 0)
        {
            bool shot = false;
            foreach (var b in w.Balls)
                if (World.OnPlunger(b))
                {
                    b.V = new Vec(0, -LaunchSpeed(w, w.Pull));
                    b.Auto = -1;
                    shot = true;
                }
            if (shot) w.Heard.Add(Sfx.Launch);
            w.Pull = 0;
        }
        int nx = Math.Sign(c.NudgeX), ny = Math.Sign(c.NudgeY);
        if (nx != 0 && nx != w.NudgeXPrev || ny != 0 && ny != w.NudgeYPrev) Nudge(w, new Vec(nx, ny).Unit);
        w.NudgeXPrev = nx;
        w.NudgeYPrev = ny;
    }

    private static void Flippers(World w, bool left, bool right)
    {
        w.LeftHeld = left;
        w.RightHeld = right;
    }

    /// <summary>How fast the plunger shoots the ball, pulled back so far: fast enough at any slope to make the top when pulled right back.</summary>
    public static double LaunchSpeed(World w, double pull) => (450 + 1550 * Math.Clamp(pull, 0, 1)) * Math.Sqrt(w.Table.Physics.G / 800);

    /// <summary>A shove: every ball moves a little, the table shakes, and too many too quickly is a tilt.</summary>
    public static void Nudge(World w, Vec dir)
    {
        if (w.Tilted) return;
        foreach (var b in w.Balls)
            if (b.Held < 0) b.V += dir * NudgePush;
        w.Shake = 0.2;
        w.ShakeDir = dir;
        w.Heard.Add(Sfx.Nudge);
        w.TiltMeter += 1;
        if (w.TiltMeter >= TiltLimit)
        {
            w.Tilted = true;
            w.LeftHeld = w.RightHeld = false;
            Say(w, "TILT", 3);
            w.Heard.Add(Sfx.Tilt);
        }
        else if (w.TiltMeter >= TiltWarning)
        {
            Say(w, "DANGER", 1.5);
            w.Heard.Add(Sfx.Danger);
        }
    }

    /// <summary>Multiball: each ball owed is fed onto the plunger, and shot by itself if no one shoots it.</summary>
    private static void Feed(World w, double dt)
    {
        foreach (var b in w.Balls)
        {
            if (b.Auto < 0) continue;
            b.Auto -= dt;
            if (b.Auto < 0 && World.OnPlunger(b))
            {
                b.V = new Vec(0, -LaunchSpeed(w, 0.85 + w.Dice.Next() * 0.15));
                w.Heard.Add(Sfx.Launch);
            }
        }
        if (w.Waiting > 0 && !w.Balls.Any(World.OnPlunger))
        {
            w.Waiting--;
            w.Balls.Add(new Ball { P = Field.BallStart, Auto = 0.8 });
        }
    }

    /// <summary>Kick-out holes holding a ball: when their time's up they kick it out the way they face.</summary>
    private static void Holes(World w, double h)
    {
        foreach (var b in w.Balls)
        {
            if (b.Held < 0) continue;
            var st = w.States[b.Held];
            st.Timer -= h;
            if (st.Timer > 0) continue;
            var p = w.Table.Parts[b.Held];
            var dir = new Vec(0, -1).Rotate(p.Radians + (w.Dice.Next() - 0.5) * 0.12);
            b.P = p.At + dir * (Parts.KickoutR + 2);
            b.V = dir * (620 * Math.Sqrt(w.Table.Physics.G / 800));
            b.Held = -1;
            st.Rest = 0.5;
            w.Heard.Add(Sfx.Kickout);
        }
    }

    /// <summary>The balls and the flippers through one step, in small steps.</summary>
    private static void Move(World w, double h)
    {
        var parts = w.Table.Parts;
        double vmax = w.Balls.Count == 0 ? 0 : w.Balls.Max(b => b.V.Length);
        int n = (int)Math.Clamp(Math.Ceiling(Math.Max(vmax * h / (Field.BallR * 2), FlipUp * (Parts.FlipperLength + Parts.FlipperTip) * h / FlipStepLength)), 4, 64);
        double dh = h / n;
        var flippers = new List<Shape>();
        for (int k = 0; k < n; k++)
        {
            flippers.Clear();
            for (int i = 0; i < parts.Count; i++)
            {
                if (!Parts.IsFlipper(parts[i].Kind)) continue;
                var st = w.States[i];
                var (rest, up) = Rest(parts[i]);
                bool held = parts[i].Kind == PartKind.FlipperLeft ? w.LeftHeld : w.RightHeld;
                double target = held ? up : rest, diff = target - st.Angle, step = (held ? FlipUp : FlipDown) * dh;
                if (Math.Abs(diff) < 1e-12) st.Omega = 0;
                else if (Math.Abs(diff) <= step) { st.Omega = diff / dh; st.Angle = target; }
                else { st.Omega = Math.Sign(diff) * (held ? FlipUp : FlipDown); st.Angle += st.Omega * dh; }
                FlipperShapes(flippers, parts[i], i, st.Angle, st.Omega);
            }
            foreach (var b in w.Balls)
                if (b.Held < 0 && !b.Gone) Substep(w, b, flippers, dh);
            Balls(w);
        }
        w.Balls.RemoveAll(b => b.Gone);
    }

    private static bool Active(World w, in Shape s) => s.Part < 0 || !w.States[s.Part].Down;

    private static void Substep(World w, Ball b, List<Shape> flippers, double h)
    {
        var statics = w.Statics;
        double r = Field.BallR;
        // Out of anything that has moved into the ball (a flipper swinging up into it).
        for (int i = 0; i < flippers.Count; i++) Unstick(w, b, flippers[i]);
        for (int i = 0; i < statics.Count; i++)
            if (Active(w, statics[i])) Unstick(w, b, statics[i]);
        Pull(w, b, h);
        b.V = new Vec(b.V.X, b.V.Y + w.Table.Physics.G * h);
        if (b.V.Length > MaxSpeed) b.V = b.V.Unit * MaxSpeed;
        var from = b.P;
        double left = 1;
        for (int iter = 0; iter < 8 && left > 1e-9; iter++)
        {
            var d = b.V * (h * left);
            double t = 1;
            var n = default(Vec);
            int hit = -1;
            bool flip = false;
            for (int i = 0; i < statics.Count; i++)
                if (Active(w, statics[i]) && Collide.Sweep(statics[i], b.P, d, r, ref t, ref n)) hit = i;
            for (int i = 0; i < flippers.Count; i++)
                if (Collide.Sweep(flippers[i], b.P, d, r, ref t, ref n)) { hit = i; flip = true; }
            if (hit < 0)
            {
                b.P += d;
                break;
            }
            b.P += d * t + n * 1e-4;
            Respond(w, b, flip ? flippers[hit] : statics[hit], n);
            left *= 1 - t;
        }
        Sensors(w, b, from, b.P);
    }

    /// <summary>A ball found inside a shape is put back on its surface, and bounced if it's moving into it.</summary>
    private static void Unstick(World w, Ball b, in Shape s)
    {
        var (_, n, d) = Collide.Nearest(s, b.P);
        if (d >= Field.BallR || !Collide.GateStops(s, b.P)) return;
        b.P += n * (Field.BallR - d + 1e-4);
        Respond(w, b, s, n);
    }

    /// <summary>The ball meets a surface: it bounces (off a moving flipper, as the flipper sees it), and the part does what it does.</summary>
    private static void Respond(World w, Ball b, in Shape s, Vec n)
    {
        var q = b.P - n * Field.BallR;
        var vs = s.Velocity(q);
        var rel = b.V - vs;
        double vn = rel.Dot(n);
        if (vn >= 0) return;
        double bounce = w.Table.Physics.Bounce;
        double e = s.Surface switch
        {
            Surface.Rubber => Math.Min(0.92, bounce + 0.2),
            Surface.Flipper => bounce * 0.75,
            Surface.Target => bounce * 0.8,
            Surface.Plunger => 0.15,
            _ => bounce,
        };
        // A ball rolling along a surface (or coming to rest on one) doesn't bounce on it, nor lose speed along it.
        bool rolling = -vn < 30;
        if (rolling) e = 0;
        var tangent = rel - n * vn;
        rel = (rolling ? tangent : tangent * 0.99) - n * (vn * e);
        b.V = rel + vs;
        if (s.Part >= 0) Struck(w, b, s, n, -vn);
        else if (-vn > 450) w.Heard.Add(Sfx.Thud);
    }

    /// <summary>What a part does when the ball strikes it this hard.</summary>
    private static void Struck(World w, Ball b, in Shape s, Vec n, double speed)
    {
        var st = w.States[s.Part];
        var part = w.Table.Parts[s.Part];
        w.Hits++;
        switch (s.Surface)
        {
            case Surface.Bumper when st.Cooldown <= 0:
                st.Cooldown = 0.06;
                if (w.Tilted) break;
                Kick(w, b, n, 1);
                w.Heard.Add(Sfx.Bumper);
                Trigger(w, s.Part);
                break;
            case Surface.Sling when st.Cooldown <= 0 && speed > 60:
                st.Cooldown = 0.08;
                if (w.Tilted) break;
                Kick(w, b, n, 0.85);
                w.Heard.Add(Sfx.Sling);
                Trigger(w, s.Part);
                break;
            case Surface.Target when st.Cooldown <= 0 && speed > 20:
                st.Cooldown = 0.15;
                if (part.Kind == PartKind.DropTarget)
                {
                    st.Down = true;
                    w.Heard.Add(Sfx.Drop);
                }
                else w.Heard.Add(Sfx.Standup);
                Trigger(w, s.Part);
                break;
            case Surface.Rubber when part.Kind == PartKind.Post && speed > 80 && st.Cooldown <= 0:
                st.Cooldown = 0.1;
                w.Heard.Add(Sfx.Rubber);
                Trigger(w, s.Part);
                break;
            case Surface.Wall or Surface.Rubber when speed > 450:
                w.Heard.Add(Sfx.Thud);
                break;
        }
    }

    /// <summary>A pop bumper or a slingshot throws the ball off at its kick speed at least.</summary>
    private static void Kick(World w, Ball b, Vec n, double k)
    {
        double kick = w.Table.Physics.KickSpeed * k, out_ = b.V.Dot(n);
        if (out_ < kick) b.V += n * (kick - out_);
    }

    /// <summary>The magnets pull the ball in for a moment, then let it go and rest.</summary>
    private static void Pull(World w, Ball b, double h)
    {
        var parts = w.Table.Parts;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Kind != PartKind.Magnet) continue;
            var st = w.States[i];
            if (st.Timer <= 0) continue;
            var d = parts[i].At - b.P;
            double l = d.Length;
            if (l > Parts.MagnetR + 10) continue;
            b.V += d.Unit * (2600 * Math.Min(1, l / 10) * h);
            b.V *= 1 - 5 * h;
        }
    }

    /// <summary>The ball over the parts it doesn't bounce off: rollovers, spinners, kick-out holes, ball eaters and magnets.</summary>
    private static void Sensors(World w, Ball b, Vec from, Vec to)
    {
        var parts = w.Table.Parts;
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            var st = w.States[i];
            switch (p.Kind)
            {
                case PartKind.Rollover or PartKind.Lane:
                {
                    double d = Collide.SegmentDistance(from, to, p.At);
                    if (d < Parts.RolloverR && !st.Inside)
                    {
                        st.Inside = true;
                        w.Heard.Add(Sfx.Rollover);
                        Trigger(w, i);
                    }
                    else if (Vec.Distance(to, p.At) > Parts.RolloverR + 10) st.Inside = false;
                    break;
                }
                case PartKind.Spinner:
                    if (Collide.Crosses(from, to, p.Local(-Parts.SpinnerHalf, 0), p.Local(Parts.SpinnerHalf, 0)))
                    {
                        var across = new Vec(0, -1).Rotate(p.Radians);
                        double speed = Math.Abs(b.V.Dot(across));
                        st.SpinRate = Math.Min(14, Math.Max(st.SpinRate, 1.5 + speed / 90)) * Math.Sign(b.V.Dot(across) + 1e-9);
                    }
                    break;
                case PartKind.Kickout:
                    if (st.Rest <= 0 && b.Held < 0 && Collide.SegmentDistance(from, to, p.At) < Parts.KickoutR && !w.Balls.Any(o => o.Held == i))
                    {
                        b.Held = i;
                        b.P = p.At;
                        b.V = default;
                        st.Timer = HoldSeconds;
                        w.Heard.Add(Sfx.Catch);
                        Trigger(w, i);
                    }
                    break;
                case PartKind.Eater:
                    if (!b.Gone && Collide.SegmentDistance(from, to, p.At) < Parts.EaterR)
                    {
                        b.Gone = true;
                        b.P = p.At;
                        w.Heard.Add(Sfx.Eaten);
                        Trigger(w, i);
                    }
                    break;
                case PartKind.Magnet:
                    if (st.Timer <= 0 && st.Rest <= 0 && Vec.Distance(to, p.At) < Parts.MagnetR)
                    {
                        st.Timer = MagnetSeconds;
                        w.Heard.Add(Sfx.Magnet);
                        Trigger(w, i);
                    }
                    break;
            }
        }
    }

    /// <summary>Balls against each other.</summary>
    private static void Balls(World w)
    {
        var bs = w.Balls;
        for (int i = 0; i < bs.Count; i++)
        for (int j = i + 1; j < bs.Count; j++)
        {
            var a = bs[i];
            var b = bs[j];
            if (a.Held >= 0 || b.Held >= 0 || a.Gone || b.Gone) continue;
            var d = b.P - a.P;
            double l = d.Length;
            if (l >= 2 * Field.BallR || l < 1e-9) continue;
            var n = d / l;
            double push = (2 * Field.BallR - l) / 2;
            a.P -= n * push;
            b.P += n * push;
            double vn = (b.V - a.V).Dot(n);
            if (vn >= 0) continue;
            var j_ = n * (vn * 0.95);
            a.V += j_;
            b.V -= j_;
        }
    }

    /// <summary>Spinners turning down, scoring every turn.</summary>
    private static void Spinners(World w, double h)
    {
        for (int i = 0; i < w.States.Length; i++)
        {
            var st = w.States[i];
            if (st.SpinRate == 0) continue;
            double before = st.Spin;
            st.Spin += st.SpinRate * h;
            double rate = Math.Max(0, Math.Abs(st.SpinRate) - 3.5 * h);
            st.SpinRate = Math.Sign(st.SpinRate) * rate;
            if (Math.Floor(Math.Abs(st.Spin)) != Math.Floor(Math.Abs(before)))
            {
                w.Heard.Add(Sfx.Spinner);
                Trigger(w, i);
            }
        }
    }

    /// <summary>When every drop target on the table is down, a moment later they all come back up.</summary>
    private static void DropTargets(World w, double dt)
    {
        var parts = w.Table.Parts;
        bool any = false, all = true;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].Kind != PartKind.DropTarget) continue;
            any = true;
            all &= w.States[i].Down;
        }
        if (!any || !all) { w.DropReset = 0; return; }
        w.DropReset += dt;
        if (w.DropReset < 1.2) return;
        w.DropReset = 0;
        for (int i = 0; i < parts.Count; i++)
            if (parts[i].Kind == PartKind.DropTarget) { w.States[i].Down = false; w.States[i].Collected = false; }
        w.Heard.Add(Sfx.Reset);
    }

    /// <summary>Balls off the bottom of the table, a ball stuck where nothing will move it, and the end of the ball.</summary>
    private static void Drain(World w, double dt)
    {
        foreach (var b in w.Balls)
        {
            if (b.P.Y > Field.TableH + 12 || !double.IsFinite(b.P.X) || b.P.X < -20 || b.P.X > Field.TableW + 20 || b.P.Y < -40)
            {
                b.Gone = true;
                continue;
            }
            bool still = b.V.Length < 12 && b.Held < 0 && !World.OnPlunger(b) && !w.LeftHeld && !w.RightHeld;
            b.Still = still ? b.Still + dt : 0;
            if (b.Still > SearchSeconds)
            {
                // The ball search: a kick from below, as the machine fires everything to free a stuck ball.
                b.Still = 0;
                b.V = new Vec((w.Dice.Next() - 0.5) * 400, -420);
                w.Heard.Add(Sfx.Kickout);
            }
        }
        int before = w.Balls.Count;
        w.Balls.RemoveAll(b => b.Gone);
        if (w.Balls.Count < before && w.Balls.Count > 0) w.Heard.Add(Sfx.Drain);
        if (w.Balls.Count == 0 && w.Waiting == 0) EndBall(w);
    }

    /// <summary>The ball is lost: the bonus is counted (not after a tilt), then the next ball.</summary>
    private static void EndBall(World w)
    {
        w.Phase = BallPhase.Bonus;
        w.PhaseTime = 0;
        w.LeftHeld = w.RightHeld = false;
        w.Heard.Add(Sfx.Drain);
        if (w.Tilted) return;
        int bonus = w.Bonus * w.Multiplier;
        if (bonus > 0)
        {
            Award(w, bonus, null);
            Say(w, w.Multiplier > 1 ? $"BONUS {w.Bonus:N0} X {w.Multiplier}" : $"BONUS {w.Bonus:N0}", BonusSeconds);
            w.Heard.Add(Sfx.BonusCount);
        }
    }

    private static void NextBall(World w)
    {
        if (w.ExtraBalls[w.Player] > 0)
        {
            w.ExtraBalls[w.Player]--;
            NewBall(w);
            Say(w, "SHOOT AGAIN", 2);
            return;
        }
        w.Player++;
        if (w.Player >= w.Players)
        {
            w.Player = 0;
            w.BallNo++;
        }
        if (w.BallNo > w.Table.Balls)
        {
            w.BallNo = w.Table.Balls;
            w.Player = 0;
            w.Phase = BallPhase.Over;
            w.Balls.Clear();
            w.Heard.Add(Sfx.GameOver);
            return;
        }
        NewBall(w);
        if (w.Players > 1) Say(w, $"PLAYER {w.Player + 1}", 2);
    }

    // ───────────────────────────── scoring and the rules ─────────────────────────────

    public static void Say(World w, string text, double seconds)
    {
        w.Message = text;
        w.MessageTime = seconds;
    }

    /// <summary>Points to the player up (none after a tilt), and the extra ball when they pass the mark.</summary>
    public static void Award(World w, int points, Vec? at)
    {
        if (w.Tilted || points <= 0) return;
        int p = w.Player;
        w.Scores[p] += points;
        if (at is { } a) w.Pops.Add(new Pop(a, points, 0));
        int mark = w.Table.ExtraBallAt;
        if (mark > 0 && !w.ExtraGiven[p] && w.Scores[p] >= mark)
        {
            w.ExtraGiven[p] = true;
            w.ExtraBalls[p]++;
            Say(w, "EXTRA BALL", 2.5);
            w.Heard.Add(Sfx.ExtraBall);
        }
    }

    /// <summary>A part has been hit (or rolled over, or caught the ball): its points, more if it's lit, and the rules it's wired to.</summary>
    public static void Trigger(World w, int index)
    {
        if (w.Tilted) return;
        var p = w.Table.Parts[index];
        var st = w.States[index];
        int points = p.Score;
        if (st.Lit)
        {
            points += st.LitValue;
            st.Lit = false;
            w.Heard.Add(Sfx.Lit);
        }
        Award(w, points, p.At);
        st.Flash = 0.3;
        if (Parts.Bonus(p.Kind)) w.Bonus = Math.Min(MaxBonus, w.Bonus + 100);
        foreach (var r in w.Table.Rules)
        {
            if (!r.Sources.Contains(p.Id)) continue;
            if (r.When == When.Any) { Fire(w, r, p.At); continue; }
            st.Collected = true;
            var idx = r.Sources.Select(w.Table.IndexOf).Where(k => k >= 0).ToList();
            if (!idx.All(k => w.States[k].Collected)) continue;
            foreach (int k in idx)
            {
                w.States[k].Collected = false;
                if (w.States[k].Down) { w.States[k].Down = false; w.Heard.Add(Sfx.Reset); }
            }
            Fire(w, r, p.At);
        }
    }

    /// <summary>A rule goes off.</summary>
    public static void Fire(World w, Rule r, Vec at)
    {
        switch (r.Effect)
        {
            case Outcome.Points:
                Award(w, r.Amount, at + new Vec(0, -14));
                w.Heard.Add(Sfx.Lit);
                break;
            case Outcome.Light:
                foreach (int id in r.Targets)
                    if (w.Table.IndexOf(id) is >= 0 and var k)
                    {
                        w.States[k].Lit = true;
                        w.States[k].LitValue = r.Amount;
                    }
                w.Heard.Add(Sfx.Lit);
                break;
            case Outcome.Bonus:
                if (w.Multiplier < MaxMultiplier) w.Multiplier++;
                Say(w, $"BONUS {w.Multiplier}X", 1.5);
                w.Heard.Add(Sfx.Lit);
                break;
            case Outcome.ExtraBall:
                w.ExtraBalls[w.Player]++;
                Say(w, "EXTRA BALL", 2.5);
                w.Heard.Add(Sfx.ExtraBall);
                break;
            case Outcome.Multiball:
                if (w.Balls.Count(b => !b.Gone) + w.Waiting < World.MaxBallsInPlay)
                {
                    w.Waiting++;
                    Say(w, "MULTIBALL", 2);
                    w.Heard.Add(Sfx.Multiball);
                }
                break;
        }
    }

    /// <summary>A sentence for a rule: "ALL 3 DROPS: LIGHT KICK-OUT 5,000".</summary>
    public static string Describe(Table t, Rule r)
    {
        string Names(List<int> ids)
        {
            var kinds = ids.Select(t.Find).Where(p => p != null).Select(p => Parts.Short(p!.Kind)).ToList();
            if (kinds.Count == 0) return "NOTHING";
            var groups = kinds.GroupBy(k => k).Select(g => g.Count() > 2 || g.Count() == 2 && kinds.Count > 2 ? $"{g.Count()} {g.Key}S" : g.Count() == 2 ? $"{g.Key}S" : g.Key);
            return string.Join(" + ", groups);
        }
        string all = r.Sources.Count == 2 ? "BOTH" : "ALL";
        string when = r.When == When.All && r.Sources.Count > 1 ? $"{all} {Names(r.Sources)}" : r.Sources.Count > 1 ? $"ANY {Names(r.Sources)}" : Names(r.Sources);
        string what = r.Effect switch
        {
            Outcome.Points => $"{r.Amount:N0} POINTS",
            Outcome.Light => $"LIGHT {Names(r.Targets)} {r.Amount:N0}",
            Outcome.Bonus => "BONUS X+1",
            Outcome.ExtraBall => "EXTRA BALL",
            _ => "MULTIBALL",
        };
        return $"{when}: {what}";
    }
}
