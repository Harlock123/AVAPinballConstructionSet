using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AVAPinballConstructionSet.Engine;

/// <summary>
/// The screen and the table. The table is 420 by 728, x to the right and y down the screen (down the slope): its
/// frame is a wall round the sides with a rounded top, and down the right the shooter lane, closed at the bottom by
/// the plunger. The bottom of the table left of the lane is open: the drain.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double TableW = 420, TableH = 728;
    /// <summary>Where the table sits on the screen at the normal size.</summary>
    public const double TableLeft = 14, TableTop = 20;
    public const double BallR = 7;
    /// <summary>The shooter lane: its inside wall, the gate at its top, and the plunger's face.</summary>
    public const double LaneX = 392, LaneTop = 214, PlungerY = 708;
    public const double Corner = 110;
    public static readonly Vec BallStart = new((LaneX + TableW) / 2, PlungerY - BallR - 0.5);

    public static bool InTable(Vec p) => p.X >= 0 && p.Y >= 0 && p.X <= TableW && p.Y <= TableH;

    /// <summary>The frame's shapes: the sides, the rounded top, the shooter lane's wall and its one-way gate, and the plunger.</summary>
    public static List<Shape> Frame()
    {
        double w = TableW, h = TableH, c = Corner;
        var gate = Shape.Line(new Vec(w, LaneTop - 34), new Vec(LaneX, LaneTop));
        gate.GateSide = (gate.B - gate.A).Perp.Unit is var n && n.Y < 0 ? n : -n;
        return
        [
            Shape.Line(new Vec(0, h + 40), new Vec(0, c)),
            Shape.Bow(new Vec(c, c), c, Math.PI, Math.PI / 2),
            Shape.Line(new Vec(c, 0), new Vec(w - c, 0)),
            Shape.Bow(new Vec(w - c, c), c, 1.5 * Math.PI, Math.PI / 2),
            Shape.Line(new Vec(w, c), new Vec(w, h + 40)),
            Shape.Line(new Vec(LaneX, h + 40), new Vec(LaneX, LaneTop)),
            gate,
            Shape.Line(new Vec(LaneX, PlungerY), new Vec(w, PlungerY), Surface.Plunger),
        ];
    }
}

/// <summary>Every part in the parts box, and the three kinds of wall drawn on the table.</summary>
public enum PartKind
{
    FlipperLeft, FlipperRight, Bumper, SlingLeft, SlingRight, Post, Rollover, Lane, DropTarget, Standup, Spinner,
    Magnet, Kickout, Eater, Wall, Shape, Curve,
}

/// <summary>A part on the table. Walls keep their corners in <see cref="Points"/>; everything else stands at X, Y, turned by Angle.</summary>
public sealed class Part
{
    public int Id { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter<PartKind>))]
    public PartKind Kind { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Turned clockwise this many degrees from how it comes out of the box.</summary>
    public double Angle { get; set; }
    [JsonConverter(typeof(ColourJson))]
    public uint Colour { get; set; }
    /// <summary>The points it scores (a spinner: each turn).</summary>
    public int Score { get; set; }
    public List<Vec> Points { get; set; } = [];

    [JsonIgnore] public Vec At { get => new(X, Y); set { X = value.X; Y = value.Y; } }
    [JsonIgnore] public bool IsWall => Parts.IsWall(Kind);
    [JsonIgnore] public double Radians => Angle * Math.PI / 180;

    /// <summary>A point given in the part's own frame (as it comes out of the box), on the table.</summary>
    public Vec Local(double x, double y) => At + new Vec(x, y).Rotate(Radians);

    public Part Copy() => new() { Id = Id, Kind = Kind, X = X, Y = Y, Angle = Angle, Colour = Colour, Score = Score, Points = [.. Points] };
}

/// <summary>What sets a rule off: any one of its parts, or all of them (each lit up in turn until the set is done).</summary>
public enum When { Any, All }

/// <summary>What a rule does: score points, light parts (worth more the next time they're hit), raise the bonus multiplier, give an extra ball, or a second ball.</summary>
public enum Outcome { Points, Light, Bonus, ExtraBall, Multiball }

/// <summary>A wire from some parts to what they do: "all three drop targets down: light the kick-out hole for 5,000".</summary>
public sealed class Rule
{
    [JsonConverter(typeof(JsonStringEnumConverter<When>))]
    public When When { get; set; }
    public List<int> Sources { get; set; } = [];
    [JsonConverter(typeof(JsonStringEnumConverter<Outcome>))]
    public Outcome Effect { get; set; }
    public List<int> Targets { get; set; } = [];
    public int Amount { get; set; }

    public Rule Copy() => new() { When = When, Sources = [.. Sources], Effect = Effect, Targets = [.. Targets], Amount = Amount };
}

/// <summary>The four knobs, each 1 to 10: how steep the table is, how fast time runs, how bouncy it is, and how hard bumpers and slingshots kick.</summary>
public sealed class Physics
{
    public const int Min = 1, Max = 10;
    public int Gravity { get; set; } = 5;
    public int Speed { get; set; } = 5;
    public int Elasticity { get; set; } = 5;
    public int Kick { get; set; } = 5;

    /// <summary>Down the table, in table units a second squared.</summary>
    [JsonIgnore] public double G => 300 + Gravity * 100;
    [JsonIgnore] public double TimeScale => 0.55 + Speed * 0.09;
    /// <summary>How much of its speed into a wall the ball keeps.</summary>
    [JsonIgnore] public double Bounce => 0.15 + Elasticity * 0.07;
    [JsonIgnore] public double KickSpeed => 300 + Kick * 90;

    public Physics Copy() => (Physics)MemberwiseClone();

    public void Clamp()
    {
        Gravity = Math.Clamp(Gravity, Min, Max);
        Speed = Math.Clamp(Speed, Min, Max);
        Elasticity = Math.Clamp(Elasticity, Min, Max);
        Kick = Math.Clamp(Kick, Min, Max);
    }
}

/// <summary>A whole table: its name, its colour, its parts and walls, its rules and its physics.</summary>
public sealed class Table
{
    public const int MaxParts = 400, MaxBalls = 5;
    public static readonly int[] ExtraBallSteps = [0, 50000, 100000, 150000, 250000, 500000];

    public string Name { get; set; } = "BLANK TABLE";
    [JsonConverter(typeof(ColourJson))]
    public uint Playfield { get; set; } = 0xFF14204A;
    public Physics Physics { get; set; } = new();
    /// <summary>Balls a game, each player.</summary>
    public int Balls { get; set; } = 3;
    /// <summary>A score that wins an extra ball, once a game (0: none).</summary>
    public int ExtraBallAt { get; set; } = 100000;
    public List<Part> Parts { get; set; } = [];
    public List<Rule> Rules { get; set; } = [];

    public Table Copy() => new()
    {
        Name = Name, Playfield = Playfield, Physics = Physics.Copy(), Balls = Balls, ExtraBallAt = ExtraBallAt,
        Parts = Parts.Select(p => p.Copy()).ToList(), Rules = Rules.Select(r => r.Copy()).ToList(),
    };

    public int NextId() => Parts.Count == 0 ? 1 : Parts.Max(p => p.Id) + 1;
    public int IndexOf(int id) => Parts.FindIndex(p => p.Id == id);
    public Part? Find(int id) => Parts.FirstOrDefault(p => p.Id == id);

    /// <summary>Takes a part off the table, and out of every rule; a rule left with nothing to set it off goes too.</summary>
    public void Remove(int index)
    {
        int id = Parts[index].Id;
        Parts.RemoveAt(index);
        foreach (var r in Rules)
        {
            r.Sources.Remove(id);
            r.Targets.Remove(id);
        }
        Rules.RemoveAll(r => r.Sources.Count == 0 || r.Effect == Outcome.Light && r.Targets.Count == 0);
    }

    /// <summary>Puts right anything a hand-edited file got wrong: parts off the table, scores out of range, rules to parts that aren't there.</summary>
    public void Tidy()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "UNTITLED" : new string(Name.ToUpperInvariant().Where(c => c < 128 && !char.IsControl(c)).Take(18).ToArray());
        Physics ??= new Physics();
        Physics.Clamp();
        Balls = Math.Clamp(Balls, 1, MaxBalls);
        ExtraBallAt = Math.Clamp(ExtraBallAt, 0, 10_000_000);
        Parts ??= [];
        Rules ??= [];
        Parts.RemoveAll(p => p == null || !Enum.IsDefined(p.Kind) || p.IsWall && (p.Points == null || p.Points.Count < Parts_MinPoints(p.Kind)));
        if (Parts.Count > MaxParts) Parts.RemoveRange(MaxParts, Parts.Count - MaxParts);
        var seen = new HashSet<int>();
        int next = Parts.Count == 0 ? 1 : Math.Max(1, Parts.Max(p => p.Id) + 1);
        foreach (var p in Parts)
        {
            if (p.Id <= 0 || !seen.Add(p.Id)) { p.Id = next++; seen.Add(p.Id); }
            p.X = Math.Clamp(double.IsFinite(p.X) ? p.X : 0, 0, Field.TableW);
            p.Y = Math.Clamp(double.IsFinite(p.Y) ? p.Y : 0, 0, Field.TableH);
            p.Angle = double.IsFinite(p.Angle) ? (p.Angle % 360 + 360) % 360 : 0;
            p.Score = Math.Clamp(p.Score, 0, 1_000_000);
            p.Colour |= 0xFF000000;
            p.Points = (p.Points ?? []).Select(v => new Vec(Math.Clamp(double.IsFinite(v.X) ? v.X : 0, 0, Field.TableW), Math.Clamp(double.IsFinite(v.Y) ? v.Y : 0, 0, Field.TableH))).Take(64).ToList();
        }
        foreach (var r in Rules.Where(r => r != null))
        {
            r.Sources = (r.Sources ?? []).Where(seen.Contains).Distinct().ToList();
            r.Targets = (r.Targets ?? []).Where(seen.Contains).Distinct().ToList();
            r.Amount = Math.Clamp(r.Amount, 0, 1_000_000);
        }
        Rules.RemoveAll(r => r == null || !Enum.IsDefined(r.Effect) || r.Sources.Count == 0 || r.Effect == Outcome.Light && r.Targets.Count == 0);
        Playfield |= 0xFF000000;
    }

    private static int Parts_MinPoints(PartKind k) => k switch { PartKind.Shape => 3, PartKind.Curve => 3, _ => 2 };
}

/// <summary>What each kind of part is called, scores and looks like out of the box, and how big it is.</summary>
public static class Parts
{
    /// <summary>The parts box, in its order.</summary>
    public static readonly PartKind[] Box =
    [
        PartKind.FlipperLeft, PartKind.FlipperRight, PartKind.Bumper, PartKind.SlingLeft, PartKind.SlingRight, PartKind.Post, PartKind.Rollover,
        PartKind.Lane, PartKind.DropTarget, PartKind.Standup, PartKind.Spinner, PartKind.Magnet, PartKind.Kickout, PartKind.Eater,
    ];

    /// <summary>The paint pots.</summary>
    public static readonly uint[] Palette =
    [
        0xFFF04838, 0xFFFF9A2A, 0xFFFFD83A, 0xFF5AD05A, 0xFF34C8D8, 0xFF4A7AF0, 0xFFA060E8, 0xFFF070B8, 0xFFF4F0E4, 0xFF8A94A8,
        0xFF14204A, 0xFF0E3A2A, 0xFF3A1430, 0xFF101010,
    ];

    public const double FlipperLength = 58, FlipperBase = 8.5, FlipperTip = 4.5;
    public const double BumperR = 17, PostR = 5, RolloverR = 8, KickoutR = 10, EaterR = 12, MagnetR = 46, TargetHalf = 13, SpinnerHalf = 14;

    public static bool IsWall(PartKind k) => k is PartKind.Wall or PartKind.Shape or PartKind.Curve;
    public static bool IsFlipper(PartKind k) => k is PartKind.FlipperLeft or PartKind.FlipperRight;

    public static string Name(PartKind k) => k switch
    {
        PartKind.FlipperLeft => "LEFT FLIPPER", PartKind.FlipperRight => "RIGHT FLIPPER", PartKind.Bumper => "POP BUMPER",
        PartKind.SlingLeft => "LEFT KICKER", PartKind.SlingRight => "RIGHT KICKER", PartKind.Post => "POST", PartKind.Rollover => "ROLLOVER",
        PartKind.Lane => "ROLLOVER LANE", PartKind.DropTarget => "DROP TARGET", PartKind.Standup => "STANDUP TARGET", PartKind.Spinner => "SPINNER",
        PartKind.Magnet => "MAGNET", PartKind.Kickout => "KICK-OUT HOLE", PartKind.Eater => "BALL EATER", PartKind.Wall => "WALL",
        PartKind.Shape => "SHAPE", _ => "CURVED WALL",
    };

    /// <summary>A short name, for the parts box and the rules.</summary>
    public static string Short(PartKind k) => k switch
    {
        PartKind.FlipperLeft => "FLIPPER", PartKind.FlipperRight => "FLIPPER", PartKind.Bumper => "BUMPER", PartKind.SlingLeft => "KICKER",
        PartKind.SlingRight => "KICKER", PartKind.Post => "POST", PartKind.Rollover => "ROLLOVER", PartKind.Lane => "LANE",
        PartKind.DropTarget => "DROP", PartKind.Standup => "TARGET", PartKind.Spinner => "SPINNER", PartKind.Magnet => "MAGNET",
        PartKind.Kickout => "KICK-OUT", PartKind.Eater => "EATER", PartKind.Wall => "WALL", PartKind.Shape => "SHAPE", _ => "CURVE",
    };

    public static int DefaultScore(PartKind k) => k switch
    {
        PartKind.Bumper => 100, PartKind.SlingLeft or PartKind.SlingRight => 10, PartKind.Post => 10, PartKind.Rollover => 500,
        PartKind.Lane => 500, PartKind.DropTarget => 500, PartKind.Standup => 250, PartKind.Spinner => 100, PartKind.Magnet => 250,
        PartKind.Kickout => 1000, PartKind.Eater => 0, _ => 0,
    };

    public static uint DefaultColour(PartKind k) => k switch
    {
        PartKind.FlipperLeft or PartKind.FlipperRight => 0xFFF4F0E4, PartKind.Bumper => 0xFFF04838, PartKind.SlingLeft or PartKind.SlingRight => 0xFFFFD83A,
        PartKind.Post => 0xFFF4F0E4, PartKind.Rollover => 0xFF5AD05A, PartKind.Lane => 0xFF34C8D8, PartKind.DropTarget => 0xFFFF9A2A,
        PartKind.Standup => 0xFFF070B8, PartKind.Spinner => 0xFF34C8D8, PartKind.Magnet => 0xFFA060E8, PartKind.Kickout => 0xFFFFD83A,
        PartKind.Eater => 0xFF8A94A8, _ => 0xFFF4F0E4,
    };

    /// <summary>A new part of a kind, as it comes out of the box, at a point.</summary>
    public static Part Make(PartKind k, Vec at, int id = 0) => new() { Id = id, Kind = k, X = at.X, Y = at.Y, Colour = DefaultColour(k), Score = DefaultScore(k) };

    /// <summary>Whether triggering the part adds to the bonus counted at the end of the ball.</summary>
    public static bool Bonus(PartKind k) => k is PartKind.Rollover or PartKind.Lane or PartKind.DropTarget or PartKind.Standup or PartKind.Kickout;

    /// <summary>The flipper's resting and raised angles, in radians, before it's turned.</summary>
    public static (double Down, double Up) FlipperAngles(PartKind k) =>
        k == PartKind.FlipperLeft ? (30 * Math.PI / 180, -28 * Math.PI / 180) : (150 * Math.PI / 180, 208 * Math.PI / 180);

    /// <summary>A slingshot's three corners, in its own frame: the third side (from the first to the last) is the kicking face.</summary>
    public static Vec[] Sling(PartKind k)
    {
        Vec[] left = [new(-12, -30), new(-12, 26), new(14, 38)];
        return k == PartKind.SlingLeft ? left : left.Select(v => new Vec(-v.X, v.Y)).ToArray();
    }

    /// <summary>A rough size, for picking parts up off the table.</summary>
    public static double Reach(PartKind k) => k switch
    {
        PartKind.Bumper => BumperR + 2, PartKind.Post => PostR + 4, PartKind.SlingLeft or PartKind.SlingRight => 24, PartKind.Lane => 20,
        PartKind.DropTarget or PartKind.Standup => 14, PartKind.Spinner => 15, PartKind.Magnet => 16, PartKind.Kickout => KickoutR + 4,
        PartKind.Eater => EaterR + 3, PartKind.Rollover => RolloverR + 4, _ => 10,
    };
}

/// <summary>A colour in a table file: "#RRGGBB".</summary>
public sealed class ColourJson : JsonConverter<uint>
{
    public override uint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number) return reader.GetUInt32() | 0xFF000000;
        var s = (reader.GetString() ?? "").TrimStart('#');
        return uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint v) ? v | 0xFF000000 : 0xFFF4F0E4;
    }

    public override void Write(Utf8JsonWriter writer, uint value, JsonSerializerOptions options) =>
        writer.WriteStringValue("#" + (value & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture));
}

/// <summary>
/// Tables on disk: a small JSON file of their own ("format": "AVAPinballConstructionSet table"), easy to read and
/// to share. Saved tables live in the "tables" folder of the user's application-data folder.
/// </summary>
public static class TableFile
{
    public const string Format = "AVAPinballConstructionSet table";
    public const int Version = 1;
    public const string Extension = ".pinball.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    private sealed class Envelope
    {
        public string Format { get; set; } = TableFile.Format;
        public int Version { get; set; } = TableFile.Version;
        public Table? Table { get; set; }
    }

    public static string ToJson(Table t) => JsonSerializer.Serialize(new Envelope { Table = t }, Options);

    public static Table? FromJson(string json)
    {
        try
        {
            var e = JsonSerializer.Deserialize<Envelope>(json, Options);
            if (e?.Format != Format || e.Version > Version || e.Table is not { } t) return null;
            t.Tidy();
            return t;
        }
        catch (Exception x) when (x is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return null;
        }
    }

    public static void Save(Table t, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        // Write then swap, so a crash mid-write can't leave half a table.
        string temp = path + ".tmp";
        File.WriteAllText(temp, ToJson(t));
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>A saved table, or null if the file isn't one.</summary>
    public static Table? Load(string path)
    {
        try
        {
            return FromJson(File.ReadAllText(path));
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>A file name from a table's name: "BUMPER CITY" is bumper-city.pinball.json.</summary>
    public static string Slug(string name)
    {
        var s = new string(name.ToLowerInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (s.Contains("--")) s = s.Replace("--", "-");
        return s.Length == 0 ? "untitled" : s;
    }

    /// <summary>The tables saved in a folder, by name.</summary>
    public static List<(string Name, string Path)> List(string folder)
    {
        var list = new List<(string, string)>();
        try
        {
            if (!Directory.Exists(folder)) return list;
            foreach (var f in Directory.GetFiles(folder, "*" + Extension).Order())
                if (Load(f) is { } t) list.Add((t.Name, f));
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
        }
        return list;
    }
}
