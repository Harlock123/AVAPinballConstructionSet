using AVAPinballConstructionSet.Engine;

namespace AVAPinballConstructionSet.Tests;

/// <summary>A test bench: tables of a part or two, balls put exactly where a test wants them, and time run on.</summary>
internal static class Lab
{
    public static string TempFolder() => Path.Combine(Path.GetTempPath(), "avapinball-tests-" + Guid.NewGuid().ToString("N"));

    public static GameEngine Engine() => new(TempFolder()) { Silent = true };

    public static Part Make(PartKind k, double x, double y, double angle = 0)
    {
        var p = Parts.Make(k, new Vec(x, y));
        p.Angle = angle;
        return p;
    }

    public static Part Wall(params (double X, double Y)[] pts) => Walls(PartKind.Wall, pts);

    public static Part Walls(PartKind kind, params (double X, double Y)[] pts)
    {
        var p = new Part { Kind = kind, Colour = 0xFFF4F0E4, Points = pts.Select(v => new Vec(v.X, v.Y)).ToList() };
        p.At = p.Points[0];
        return p;
    }

    public static Table Table(params Part[] parts)
    {
        var t = new Table { Name = "LAB" };
        foreach (var p in parts)
        {
            p.Id = t.NextId();
            t.Parts.Add(p);
        }
        return t;
    }

    /// <summary>A game on a table, with no ball yet: the test puts its own in.</summary>
    public static World World(Table t, Action<Physics>? physics = null)
    {
        physics?.Invoke(t.Physics);
        var w = new World();
        Sim.Start(w, t, 1, 1);
        w.Balls.Clear();
        return w;
    }

    public static World World(params Part[] parts) => World(Table(parts));

    public static Ball Ball(World w, double x, double y, double vx = 0, double vy = 0)
    {
        var b = new Ball { P = new Vec(x, y), V = new Vec(vx, vy) };
        w.Balls.Add(b);
        return b;
    }

    public static void Run(World w, double seconds, Controls c = default, Action? each = null)
    {
        for (int i = 0; i < (int)Math.Round(seconds * 60); i++)
        {
            Sim.Step(w, c);
            each?.Invoke();
        }
    }

    public static int Index(World w, PartKind k) => w.Table.Parts.FindIndex(p => p.Kind == k);
}
