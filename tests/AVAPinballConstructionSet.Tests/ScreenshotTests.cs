using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAPinballConstructionSet.Engine;
using AVAPinballConstructionSet.Views;

namespace AVAPinballConstructionSet.Tests;

/// <summary>
/// Draws the program in a real (headless) window at the moments worth showing, driving it with the mouse where a
/// person would. Set PINBALL_SHOTS to a folder to save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(Path.Combine(Path.GetTempPath(), "avapinball-ui-" + Guid.NewGuid().ToString("N"))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Draws a frame, so the buttons are where the clicks will look for them.</summary>
    private static void Draw(Window w, GameCanvas c)
    {
        c.InvalidateVisual();
        Pump();
        Assert.NotNull(w.CaptureRenderedFrame());
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("PINBALL_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAPinballConstructionSet-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>The construction set open on a sample table.</summary>
    private static (Window W, GameCanvas Canvas, GameEngine G) Building(string sample)
    {
        var (w, c, g) = Open();
        g.Tick(default, start: true);
        Assert.Equal(GameState.Editing, g.State);
        g.OpenSample(sample);
        g.Run(200);
        Draw(w, c);
        return (w, c, g);
    }

    private static Point At(GameCanvas c, double x, double y) => c.FromTable(new Vec(x, y));

    /// <summary>Plays a table with the computer at the flippers until a moment comes.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<World, bool> moment, int maxSeconds = 900)
    {
        for (int t = 0; t < 60 * maxSeconds && g.State == GameState.Playing; t++)
        {
            if (moment(g.World)) return true;
            g.Tick(g.Pilot.Decide(g.World));
            if (t % 3 == 0) c.Advance(0.05);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title()
    {
        var (w, c, g) = Open();
        g.Idle(40);
        c.Advance(2.05);
        Assert.Equal(GameState.Title, g.State);
        Shot(w, c, "title");
    }

    [AvaloniaFact]
    public void The_construction_set_with_a_part_on_its_way_from_the_box()
    {
        var (w, c, g) = Building("LUCKY STAR");
        // Pick up a bumper and look at it; then drag a spinner out of the box.
        c.Click(At(c, 200, 262));
        Assert.Equal(PartKind.Bumper, g.Editor.SelectedPart?.Kind);
        Draw(w, c);
        int spinner = Array.IndexOf(Parts.Box, PartKind.Spinner);
        c.Press(GameCanvas.BoxTile(spinner).Center);
        Assert.NotNull(g.Editor.Carried);
        c.PointTo(At(c, 300, 360));
        c.PointTo(At(c, 312, 368));
        c.Advance(0.4);
        Shot(w, c, "construction-set");
        c.Lift(At(c, 312, 368));
        Assert.Contains(g.Editor.Table.Parts, p => p.Kind == PartKind.Spinner && Math.Abs(p.X - 312) < 1);
    }

    [AvaloniaFact]
    public void Wiring_a_rule()
    {
        var (w, c, g) = Building("TARGET RANGE");
        c.Click(GameCanvas.TabRect((int)Tab.Rules).Center);
        Draw(w, c);
        var ed = g.Editor;
        ed.NewRule();
        // The two bumpers, both hit: light the kick-out hole and the standup for 7,500.
        foreach (var p in ed.Table.Parts.Where(p => p.Kind == PartKind.Bumper).ToList()) c.Click(At(c, p.X, p.Y));
        ed.SetEffect(Outcome.Light);
        foreach (var p in ed.Table.Parts.Where(p => p.Kind is PartKind.Standup).ToList()) c.Click(At(c, p.X, p.Y));
        ed.StepAmount(1);
        ed.StepAmount(1);
        Assert.Equal(2, ed.Draft!.Sources.Count);
        Assert.Single(ed.Draft.Targets);
        c.PointTo(At(c, 380, 262));
        c.Advance(0.3);
        Shot(w, c, "wiring");
    }

    [AvaloniaFact]
    public void The_physics_knobs_and_a_shape_being_drawn()
    {
        var (w, c, g) = Building("COMET RUN");
        c.Click(GameCanvas.TabRect((int)Tab.Physics).Center);
        Draw(w, c);
        c.Click(GameCanvas.KnobRect(Editor.Knob.Gravity).Center + new Point(56, 0));
        c.Click(new Point(GameCanvas.KnobRect(Editor.Knob.Kick).X + 4, GameCanvas.KnobRect(Editor.Knob.Kick).Center.Y));
        Assert.Equal(7, g.Editor.Table.Physics.Gravity);
        Assert.Equal(1, g.Editor.Table.Physics.Kick);
        // A paint pot, then the shape tool: a little island, corner by corner.
        c.Click(GameCanvas.PotRect(1).Center);
        c.Click(GameCanvas.ToolRect(2).Center);
        Assert.Equal(Tool.Shape, g.Editor.Tool);
        foreach (var (x, y) in new[] { (150.0, 420.0), (196.0, 396.0), (244.0, 420.0), (232.0, 452.0) }) c.Click(At(c, x, y));
        c.PointTo(At(c, 170, 458));
        Shot(w, c, "physics");
    }

    [AvaloniaFact]
    public void Close_work_with_the_magnifier()
    {
        var (w, c, g) = Building("LUCKY STAR");
        c.Click(GameCanvas.ToolRect(5).Center);
        c.Click(At(c, 196, 600));
        Assert.True(g.Editor.Zoom > 1);
        Draw(w, c);
        // A curved wall above the left flipper, drawn magnified.
        c.Click(GameCanvas.ToolRect(3).Center);
        c.Click(GameCanvas.PotRect(4).Center);
        c.Press(At(c, 150, 560));
        c.PointTo(At(c, 200, 560));
        c.Lift(At(c, 242, 560));
        c.PointTo(At(c, 196, 540));
        Assert.NotNull(g.Editor.CurveEnds);
        Shot(w, c, "magnifier");
    }

    [AvaloniaFact]
    public void Playing_a_sample_table()
    {
        var (w, c, g) = Building("LUCKY STAR");
        c.Click(GameCanvas.PlayRect.Center);
        Assert.Equal(GameState.Playing, g.State);
        g.Tick(default, start: true);
        g.Tick(default);
        Assert.Equal(2, g.World.Players);
        Assert.True(PlayUntil(g, c, w => w.Scores[0] > 12000 && w.Balls.Count == 1 && w.Balls[0].P.Y is > 420 and < 600 && w.Balls[0].P.X < 380 && w.States.Any(s => s.Collected)));
        Shot(w, c, "playing");
    }

    [AvaloniaFact]
    public void Multiball()
    {
        var (w, c, g) = Building("BUMPER CITY");
        g.PlayTable();
        // Well into a game, the lanes and the holes made (as the rule asks), and a second ball comes in.
        Assert.True(PlayUntil(g, c, w => w.Scores[0] > 25000 && w.Balls.Count == 1 && w.Balls[0].P.Y < 300));
        Sim.Fire(g.World, g.World.Table.Rules.Single(r => r.Effect == Outcome.Multiball), new Vec(196, 74));
        Assert.True(PlayUntil(g, c, w => w.Balls.Count(b => b.P.Y < 560 && b.P.X < Field.LaneX && b.Held < 0) >= 2 && w.Pops.Count >= 2));
        Shot(w, c, "multiball");
    }

    [AvaloniaFact]
    public void The_high_scores()
    {
        var (w, c, g) = Building("TARGET RANGE");
        g.PlayTable();
        g.World.Scores[0] = 91250;
        g.World.Balls.Clear();
        for (int i = 0; i < 60 * 30 && g.State == GameState.Playing; i++)
        {
            g.World.Balls.Clear();
            g.Tick(default);
        }
        g.Run((int)(60 * GameEngine.OverSeconds) + 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "AVA") g.Initials!.Type(ch);
        g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        c.Advance(0.1);
        Shot(w, c, "high-scores");
    }
}
