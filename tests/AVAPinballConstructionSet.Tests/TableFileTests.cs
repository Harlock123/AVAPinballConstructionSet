using AVAPinballConstructionSet.Engine;

namespace AVAPinballConstructionSet.Tests;

/// <summary>Tables to and from their files.</summary>
public class TableFileTests
{
    /// <summary>A table with one of every part (turned, painted, scored), every kind of wall, every kind of rule and the knobs turned.</summary>
    private static Table Everything()
    {
        var t = new Table { Name = "EVERYTHING", Playfield = 0xFF3A1430, Balls = 4, ExtraBallAt = 250000 };
        t.Physics = new Physics { Gravity = 2, Speed = 9, Elasticity = 7, Kick = 3 };
        int i = 0;
        foreach (var k in Parts.Box)
        {
            var p = Parts.Make(k, new Vec(40 + i % 6 * 60, 80 + i / 6 * 90), t.NextId());
            p.Angle = i * 15;
            p.Colour = Parts.Palette[i % Parts.Palette.Length];
            p.Score = 123 * i;
            t.Parts.Add(p);
            i++;
        }
        t.Parts.Add(new Part { Id = t.NextId(), Kind = PartKind.Wall, Colour = 0xFF5AD05A, Points = [new(10, 400), new(80, 450), new(120, 430)], X = 10, Y = 400 });
        t.Parts.Add(new Part { Id = t.NextId(), Kind = PartKind.Shape, Colour = 0xFFFF9A2A, Points = [new(200, 400), new(260, 400), new(230, 450.5)], X = 200, Y = 400 });
        t.Parts.Add(new Part { Id = t.NextId(), Kind = PartKind.Curve, Colour = 0xFF34C8D8, Points = [new(100, 500), new(150, 470.25), new(200, 500)], X = 150, Y = 470.25 });
        int n = 0;
        foreach (var what in Enum.GetValues<Outcome>())
            t.Rules.Add(new Rule { When = n++ % 2 == 0 ? When.All : When.Any, Sources = [t.Parts[n].Id, t.Parts[n + 1].Id], Effect = what, Amount = 1000 * n, Targets = what == Outcome.Light ? [t.Parts[12].Id] : [] });
        return t;
    }

    [Fact]
    public void Every_part_wall_rule_and_knob_comes_back_from_a_file_just_as_it_went()
    {
        var t = Everything();
        string path = Path.Combine(Lab.TempFolder(), "everything" + TableFile.Extension);
        TableFile.Save(t, path);
        var back = TableFile.Load(path);
        Assert.NotNull(back);
        Assert.Equal(TableFile.ToJson(t), TableFile.ToJson(back));
        Assert.Equal(t.Parts.Count, back.Parts.Count);
        foreach (var (a, b) in t.Parts.Zip(back.Parts))
        {
            Assert.Equal(a.Kind, b.Kind);
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.At, b.At);
            Assert.Equal(a.Angle, b.Angle);
            Assert.Equal(a.Colour, b.Colour);
            Assert.Equal(a.Score, b.Score);
            Assert.Equal(a.Points, b.Points);
        }
        Assert.Equal(Enum.GetValues<PartKind>().Length, back.Parts.Select(p => p.Kind).Distinct().Count());
        Assert.Equal(5, back.Rules.Count);
        Assert.Equal(9, back.Physics.Speed);
        Assert.Equal(4, back.Balls);
        Assert.Equal(0xFF3A1430, back.Playfield);
    }

    [Fact]
    public void A_table_file_is_readable_json_with_its_format_named_and_colours_as_hex()
    {
        string json = TableFile.ToJson(Samples.Make("LUCKY STAR"));
        Assert.Contains("\"format\": \"AVAPinballConstructionSet table\"", json);
        Assert.Contains("\"kind\": \"FlipperLeft\"", json);
        Assert.Contains("\"colour\": \"#F04838\"", json);
        Assert.Contains("\"when\": \"All\"", json);
    }

    [Fact]
    public void Every_sample_table_round_trips_and_plays_the_same_after()
    {
        foreach (var t in Samples.All().Append(Samples.Blank()))
        {
            var back = TableFile.FromJson(TableFile.ToJson(t));
            Assert.NotNull(back);
            Assert.Equal(TableFile.ToJson(t), TableFile.ToJson(back));
            var a = new World();
            var b = new World();
            Sim.Start(a, t, 1, 5);
            Sim.Start(b, back, 1, 5);
            var pa = new Pilot(3);
            var pb = new Pilot(3);
            for (int i = 0; i < 60 * 20; i++)
            {
                Sim.Step(a, pa.Decide(a));
                Sim.Step(b, pb.Decide(b));
            }
            Assert.Equal(a.Scores[0], b.Scores[0]);
            Assert.Equal(a.Balls.Select(x => x.P), b.Balls.Select(x => x.P));
        }
    }

    [Fact]
    public void A_file_that_is_not_a_table_or_is_broken_loads_as_nothing()
    {
        string folder = Lab.TempFolder();
        Directory.CreateDirectory(folder);
        string junk = Path.Combine(folder, "junk" + TableFile.Extension);
        File.WriteAllText(junk, "{ not json");
        Assert.Null(TableFile.Load(junk));
        Assert.Null(TableFile.FromJson("{\"format\": \"something else\", \"version\": 1, \"table\": {}}"));
        Assert.Null(TableFile.FromJson("{\"format\": \"AVAPinballConstructionSet table\", \"version\": 99, \"table\": {}}"));
        Assert.Null(TableFile.Load(Path.Combine(folder, "missing.json")));
    }

    [Fact]
    public void A_hand_edited_file_is_put_right_parts_kept_on_the_table_and_rules_to_missing_parts_dropped()
    {
        string json = """
        {
          "format": "AVAPinballConstructionSet table", "version": 1,
          "table": {
            "name": "my table", "balls": 40, "extraBallAt": -5,
            "physics": { "gravity": 99, "speed": 0, "elasticity": 5, "kick": 5 },
            "parts": [
              { "id": 7, "kind": "Bumper", "x": 5000, "y": -20, "angle": 400, "colour": "#ff0000", "score": 100 },
              { "id": 7, "kind": "Rollover", "x": 100, "y": 100, "colour": "nonsense" },
              { "id": 9, "kind": "Wall", "points": [ { "x": 1, "y": 1 } ] }
            ],
            "rules": [
              { "when": "All", "sources": [7, 30], "effect": "Points", "amount": 100 },
              { "when": "Any", "sources": [123], "effect": "Bonus" },
              { "when": "Any", "sources": [7], "effect": "Light", "targets": [55] }
            ]
          }
        }
        """;
        var t = TableFile.FromJson(json);
        Assert.NotNull(t);
        Assert.Equal("MY TABLE", t.Name);
        Assert.Equal(Table.MaxBalls, t.Balls);
        Assert.Equal(0, t.ExtraBallAt);
        Assert.Equal(10, t.Physics.Gravity);
        Assert.Equal(1, t.Physics.Speed);
        Assert.Equal(2, t.Parts.Count);
        Assert.Equal(new Vec(Field.TableW, 0), t.Parts[0].At);
        Assert.Equal(40, t.Parts[0].Angle);
        Assert.Equal(0xFFFF0000, t.Parts[0].Colour);
        Assert.NotEqual(t.Parts[0].Id, t.Parts[1].Id);
        var r = Assert.Single(t.Rules);
        Assert.Equal([7], r.Sources);
    }

    [Fact]
    public void Saved_tables_are_listed_by_name_and_a_name_makes_a_tidy_file_name()
    {
        string folder = Lab.TempFolder();
        foreach (var name in new[] { "COMET RUN", "BUMPER CITY" })
            TableFile.Save(Samples.Make(name), Path.Combine(folder, TableFile.Slug(name) + TableFile.Extension));
        File.WriteAllText(Path.Combine(folder, "broken" + TableFile.Extension), "nope");
        var list = TableFile.List(folder);
        Assert.Equal(["BUMPER CITY", "COMET RUN"], list.Select(x => x.Name));
        Assert.Equal("bumper-city", TableFile.Slug("BUMPER CITY"));
        Assert.Equal("my-table-2", TableFile.Slug("  My  Table #2! "));
        Assert.Equal("untitled", TableFile.Slug("!!"));
        Assert.Empty(TableFile.List(Path.Combine(folder, "none")));
    }

    [Fact]
    public void The_engine_saves_the_table_under_its_name_and_opens_it_again()
    {
        var g = Lab.Engine();
        g.OpenSample("MAGNET MINE");
        g.Editor.Rename("deep mine");
        var path = g.SaveTable();
        Assert.NotNull(path);
        Assert.Equal(Path.Combine(g.TablesFolder, "deep-mine" + TableFile.Extension), path);
        g.OpenBlank();
        Assert.Empty(g.Editor.Table.Parts);
        var saved = Assert.Single(g.SavedTables());
        Assert.Equal("DEEP MINE", saved.Name);
        Assert.True(g.OpenFile(saved.Path));
        Assert.Equal("DEEP MINE", g.Editor.Table.Name);
        Assert.Equal(Samples.Make("MAGNET MINE").Parts.Count, g.Editor.Table.Parts.Count);
        Assert.False(g.OpenFile(Path.Combine(g.TablesFolder, "nothing.json")));
    }
}
