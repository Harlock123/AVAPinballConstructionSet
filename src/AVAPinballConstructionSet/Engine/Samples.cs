namespace AVAPinballConstructionSet.Engine;

/// <summary>
/// The tables that come in the box: five to play and pull apart, each showing off some of the parts and the rules,
/// and a blank one to start from.
/// </summary>
public static class Samples
{
    public static readonly string[] Names = ["LUCKY STAR", "TARGET RANGE", "MAGNET MINE", "COMET RUN", "BUMPER CITY"];

    public static Table Make(string name) => name switch
    {
        "LUCKY STAR" => LuckyStar(),
        "TARGET RANGE" => TargetRange(),
        "MAGNET MINE" => MagnetMine(),
        "COMET RUN" => CometRun(),
        "BUMPER CITY" => BumperCity(),
        _ => Blank(),
    };

    public static Table Blank() => new() { Name = "BLANK TABLE" };

    public static IEnumerable<Table> All() => Names.Select(Make);

    // ───────────────────────────── building ─────────────────────────────

    private static int Add(Table t, PartKind k, double x, double y, double angle = 0, int? score = null, uint? colour = null)
    {
        var p = Parts.Make(k, new Vec(x, y), t.NextId());
        p.Angle = (angle % 360 + 360) % 360;
        if (score is { } s) p.Score = s;
        if (colour is { } c) p.Colour = c;
        t.Parts.Add(p);
        return p.Id;
    }

    private static int Wall(Table t, uint colour, params Vec[] pts)
    {
        var p = new Part { Id = t.NextId(), Kind = PartKind.Wall, Colour = colour, Points = [.. pts] };
        p.At = pts[0];
        t.Parts.Add(p);
        return p.Id;
    }

    private static int Shape(Table t, uint colour, params Vec[] pts)
    {
        var p = new Part { Id = t.NextId(), Kind = PartKind.Shape, Colour = colour, Points = [.. pts] };
        p.At = pts[0];
        t.Parts.Add(p);
        return p.Id;
    }

    private static int Curve(Table t, uint colour, Vec a, Vec m, Vec b)
    {
        var p = new Part { Id = t.NextId(), Kind = PartKind.Curve, Colour = colour, Points = [a, m, b] };
        p.At = m;
        t.Parts.Add(p);
        return p.Id;
    }

    private static void Rule(Table t, When when, IEnumerable<int> sources, Outcome effect, int amount = 0, params int[] targets) =>
        t.Rules.Add(new Rule { When = when, Sources = [.. sources], Effect = effect, Amount = amount, Targets = [.. targets] });

    private static Vec V(double x, double y) => new(x, y);

    /// <summary>The bottom of a table: two flippers, the walls that lead the inlanes down to them, the outlanes, and two kickers.</summary>
    private static void Lower(Table t, uint walls, uint kickers, uint flippers = 0xFFF4F0E4)
    {
        Add(t, PartKind.FlipperLeft, 128, 652, colour: flippers);
        Add(t, PartKind.FlipperRight, 264, 652, colour: flippers);
        Wall(t, walls, V(26, 500), V(26, 598), V(119, 643));
        Wall(t, walls, V(366, 500), V(366, 598), V(273, 643));
        // Deflectors over the outlanes, so a ball running down the side is turned into the inlane.
        Wall(t, walls, V(0, 410), V(30, 462));
        Wall(t, walls, V(Field.LaneX, 410), V(362, 462));
        Add(t, PartKind.SlingLeft, 84, 560, colour: kickers);
        Add(t, PartKind.SlingRight, 308, 560, colour: kickers);
        Add(t, PartKind.Post, 26, 494);
        Add(t, PartKind.Post, 366, 494);
    }

    // ───────────────────────────── the tables ─────────────────────────────

    /// <summary>The first table: three top lanes for the bonus multiplier, three pop bumpers, a kick-out hole lit by two targets.</summary>
    private static Table LuckyStar()
    {
        var t = new Table { Name = "LUCKY STAR", Playfield = 0xFF14204A, ExtraBallAt = 80000 };
        Lower(t, 0xFFF4F0E4, 0xFFFFD83A);
        int[] lanes = [Add(t, PartKind.Lane, 150, 92), Add(t, PartKind.Lane, 200, 92), Add(t, PartKind.Lane, 250, 92)];
        Add(t, PartKind.Bumper, 158, 196, colour: 0xFFF04838);
        Add(t, PartKind.Bumper, 242, 196, colour: 0xFFF04838);
        Add(t, PartKind.Bumper, 200, 262, colour: 0xFFFF9A2A);
        int hole = Add(t, PartKind.Kickout, 46, 300, 50, colour: 0xFFFFD83A);
        int[] targets = [Add(t, PartKind.Standup, 382, 300, 90, colour: 0xFFF070B8), Add(t, PartKind.Standup, 382, 340, 90, colour: 0xFFF070B8)];
        Add(t, PartKind.Rollover, 120, 420, colour: 0xFF5AD05A);
        Add(t, PartKind.Rollover, 272, 420, colour: 0xFF5AD05A);
        Add(t, PartKind.Spinner, 18, 210, colour: 0xFF34C8D8);
        Wall(t, 0xFF34C8D8, V(80, 150), V(110, 128));
        Wall(t, 0xFF34C8D8, V(320, 150), V(290, 128));
        Rule(t, When.All, lanes, Outcome.Bonus);
        Rule(t, When.All, targets, Outcome.Light, 5000, hole);
        return t;
    }

    /// <summary>A bank of five drop targets: knock them all down and the kick-out hole is lit for 5,000; a spinner up the left.</summary>
    private static Table TargetRange()
    {
        var t = new Table { Name = "TARGET RANGE", Playfield = 0xFF0E3A2A, ExtraBallAt = 100000 };
        t.Physics.Kick = 6;
        Lower(t, 0xFFF4F0E4, 0xFFFF9A2A);
        var drops = new List<int>();
        for (int i = 0; i < 5; i++) drops.Add(Add(t, PartKind.DropTarget, 136 + i * 30, 330, colour: 0xFFFF9A2A));
        int hole = Add(t, PartKind.Kickout, 196, 150, 155, colour: 0xFFFFD83A);
        Add(t, PartKind.Bumper, 120, 200, colour: 0xFF5AD05A);
        Add(t, PartKind.Bumper, 272, 200, colour: 0xFF5AD05A);
        int[] lanes = [Add(t, PartKind.Lane, 170, 76), Add(t, PartKind.Lane, 230, 76)];
        int spin = Add(t, PartKind.Spinner, 18, 300, score: 200, colour: 0xFF34C8D8);
        Add(t, PartKind.Standup, 380, 260, 90, colour: 0xFFF070B8);
        Shape(t, 0xFF2A7A5A, V(150, 420), V(242, 420), V(196, 452));
        Rule(t, When.All, drops, Outcome.Light, 5000, hole);
        Rule(t, When.All, lanes, Outcome.Bonus);
        Rule(t, When.Any, [spin], Outcome.Points, 50);
        return t;
    }

    /// <summary>Two magnets that grab the ball, a ball eater in the middle worth a gamble, and three targets for an extra ball.</summary>
    private static Table MagnetMine()
    {
        var t = new Table { Name = "MAGNET MINE", Playfield = 0xFF3A1430, ExtraBallAt = 120000 };
        Lower(t, 0xFFF4F0E4, 0xFFA060E8, 0xFFFFD83A);
        Add(t, PartKind.Magnet, 110, 300, colour: 0xFFA060E8);
        Add(t, PartKind.Magnet, 282, 300, colour: 0xFFA060E8);
        Add(t, PartKind.Eater, 196, 392, score: 2500);
        Add(t, PartKind.Post, 174, 372);
        Add(t, PartKind.Post, 218, 372);
        int[] targets = [Add(t, PartKind.Standup, 156, 140, colour: 0xFF5AD05A), Add(t, PartKind.Standup, 196, 132, colour: 0xFF5AD05A), Add(t, PartKind.Standup, 236, 140, colour: 0xFF5AD05A)];
        Add(t, PartKind.Bumper, 150, 220, colour: 0xFFF070B8);
        Add(t, PartKind.Bumper, 242, 220, colour: 0xFFF070B8);
        int s1 = Add(t, PartKind.Spinner, 18, 260, colour: 0xFF34C8D8);
        int r1 = Add(t, PartKind.Rollover, 82, 440), r2 = Add(t, PartKind.Rollover, 310, 440);
        Rule(t, When.All, targets, Outcome.ExtraBall);
        Rule(t, When.All, [r1, r2], Outcome.Light, 2500, s1);
        return t;
    }

    /// <summary>Curved walls round the sides, and three targets that light the kick-out hole: catch the ball there, lit, for a second ball.</summary>
    private static Table CometRun()
    {
        var t = new Table { Name = "COMET RUN", Playfield = 0xFF101830, ExtraBallAt = 150000 };
        t.Physics.Elasticity = 6;
        Lower(t, 0xFFF4F0E4, 0xFF34C8D8);
        Curve(t, 0xFF34C8D8, V(70, 450), V(56, 320), V(110, 200));
        Curve(t, 0xFF34C8D8, V(322, 450), V(336, 320), V(282, 200));
        int spin = Add(t, PartKind.Spinner, 30, 380, colour: 0xFFFFD83A);
        int[] targets = [Add(t, PartKind.Standup, 150, 250, -20, colour: 0xFFFF9A2A), Add(t, PartKind.Standup, 196, 238, colour: 0xFFFF9A2A), Add(t, PartKind.Standup, 242, 250, 20, colour: 0xFFFF9A2A)];
        int hole = Add(t, PartKind.Kickout, 196, 330, 180, colour: 0xFFFFD83A);
        Add(t, PartKind.Bumper, 160, 140, colour: 0xFF4A7AF0);
        Add(t, PartKind.Bumper, 232, 140, colour: 0xFF4A7AF0);
        Add(t, PartKind.Rollover, 196, 60);
        Add(t, PartKind.Post, 196, 420);
        Rule(t, When.All, targets, Outcome.Light, 10000, hole);
        Rule(t, When.All, [.. targets, hole], Outcome.Multiball);
        Rule(t, When.Any, [spin], Outcome.Points, 100);
        return t;
    }

    /// <summary>Pop bumpers everywhere; the three top lanes and both kick-out holes start a second ball.</summary>
    private static Table BumperCity()
    {
        var t = new Table { Name = "BUMPER CITY", Playfield = 0xFF14204A, ExtraBallAt = 250000 };
        t.Physics.Kick = 7;
        Lower(t, 0xFFF4F0E4, 0xFFF04838);
        uint[] colours = [0xFFF04838, 0xFFFF9A2A, 0xFFFFD83A, 0xFF5AD05A, 0xFF34C8D8, 0xFFA060E8, 0xFFF070B8];
        (double X, double Y)[] at = [(196, 160), (146, 210), (246, 210), (196, 260), (106, 300), (286, 300), (196, 350)];
        for (int i = 0; i < at.Length; i++) Add(t, PartKind.Bumper, at[i].X, at[i].Y, colour: colours[i]);
        int a = Add(t, PartKind.Kickout, 50, 200, 60, colour: 0xFFFFD83A), b = Add(t, PartKind.Kickout, 346, 200, -60, colour: 0xFFFFD83A);
        int[] lanes = [Add(t, PartKind.Lane, 164, 74), Add(t, PartKind.Lane, 196, 74), Add(t, PartKind.Lane, 228, 74)];
        Rule(t, When.All, [a, b, .. lanes], Outcome.Multiball);
        Rule(t, When.All, lanes, Outcome.Bonus);
        Rule(t, When.All, lanes, Outcome.Points, 10000);
        return t;
    }
}
