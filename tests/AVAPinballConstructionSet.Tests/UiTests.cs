using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using AVAPinballConstructionSet.Engine;
using AVAPinballConstructionSet.Views;

namespace AVAPinballConstructionSet.Tests;

/// <summary>The real window, driven by the real mouse and keys (headless): what a person's clicks and key presses do.</summary>
public sealed class UiTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open()
    {
        var g = new GameEngine(Lab.TempFolder()) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        canvas.Focus();
        Draw(w, canvas);
        return (w, canvas, g);
    }

    private static void Draw(Window w, GameCanvas c)
    {
        c.InvalidateVisual();
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
        Assert.NotNull(w.CaptureRenderedFrame());
    }

    private static void Key(Window w, Key k, RawInputModifiers mods = RawInputModifiers.None)
    {
        w.KeyPress(k, mods, PhysicalKey.None, null);
        w.KeyRelease(k, mods, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Click(Window w, Point at, MouseButton button = MouseButton.Left)
    {
        w.MouseMove(at);
        w.MouseDown(at, button);
        w.MouseUp(at, button);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The construction set, open on a sample.</summary>
    private static (Window W, GameCanvas Canvas, GameEngine G) Building(string sample = "LUCKY STAR")
    {
        var (w, c, g) = Open();
        // Enter held through a step of the game: the title opens the construction set.
        w.KeyPress(Avalonia.Input.Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(c.Input.Start);
        g.Tick(c.Input.Player, c.Input.Start);
        w.KeyRelease(Avalonia.Input.Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        g.Tick(c.Input.Player, c.Input.Start);
        Assert.Equal(GameState.Editing, g.State);
        g.OpenSample(sample);
        Draw(w, c);
        return (w, c, g);
    }

    [AvaloniaFact]
    public void A_part_dragged_from_the_box_with_the_mouse_lands_on_the_table_and_dragged_back_is_gone()
    {
        var (w, c, g) = Building();
        int n = g.Editor.Table.Parts.Count;
        var tile = GameCanvas.BoxTile(Array.IndexOf(Parts.Box, PartKind.Magnet)).Center;
        var drop = c.FromTable(new Vec(200, 420));
        w.MouseMove(tile);
        w.MouseDown(tile, MouseButton.Left);
        w.MouseMove(new Point(600, 500));
        Draw(w, c);
        w.MouseMove(drop);
        w.MouseUp(drop, MouseButton.Left);
        Assert.Equal(n + 1, g.Editor.Table.Parts.Count);
        var magnet = g.Editor.Table.Parts[^1];
        Assert.Equal(PartKind.Magnet, magnet.Kind);
        Assert.Equal(200, magnet.X, 0);
        // Picked up with the hand and dropped on the parts box: thrown away.
        w.MouseMove(drop);
        w.MouseDown(drop, MouseButton.Left);
        w.MouseMove(tile);
        w.MouseUp(tile, MouseButton.Left);
        Assert.Equal(n, g.Editor.Table.Parts.Count);
        // Ctrl+Z brings it back.
        Key(w, Avalonia.Input.Key.Z, RawInputModifiers.Control);
        Assert.Equal(n + 1, g.Editor.Table.Parts.Count);
    }

    [AvaloniaFact]
    public void The_tool_keys_choose_tools_and_the_tool_buttons_too()
    {
        var (w, c, g) = Building();
        Key(w, Avalonia.Input.Key.W);
        Assert.Equal(Tool.Wall, g.Editor.Tool);
        Key(w, Avalonia.Input.Key.S);
        Assert.Equal(Tool.Shape, g.Editor.Tool);
        Key(w, Avalonia.Input.Key.L);
        Assert.Equal(Tool.Wire, g.Editor.Tool);
        Assert.Equal(Tab.Rules, g.Editor.Tab);
        Draw(w, c);
        Click(w, GameCanvas.ToolRect(3).Center);
        Assert.Equal(Tool.Curve, g.Editor.Tool);
        Key(w, Avalonia.Input.Key.H);
        Assert.Equal(Tool.Hand, g.Editor.Tool);
        // Tab steps through the panels.
        var tab = g.Editor.Tab;
        Key(w, Avalonia.Input.Key.Tab);
        Assert.NotEqual(tab, g.Editor.Tab);
    }

    [AvaloniaFact]
    public void Drawing_a_wall_with_the_mouse()
    {
        var (w, c, g) = Building();
        Key(w, Avalonia.Input.Key.W);
        int n = g.Editor.Table.Parts.Count;
        var a = c.FromTable(new Vec(150, 380));
        var b = c.FromTable(new Vec(250, 360));
        w.MouseMove(a);
        w.MouseDown(a, MouseButton.Left);
        w.MouseMove(b);
        w.MouseUp(b, MouseButton.Left);
        Assert.Equal(n + 1, g.Editor.Table.Parts.Count);
        Assert.Equal(PartKind.Wall, g.Editor.Table.Parts[^1].Kind);
    }

    [AvaloniaFact]
    public void A_chosen_part_turns_with_r_scores_more_with_plus_and_goes_with_delete()
    {
        var (w, c, g) = Building();
        int bumper = g.Editor.Table.Parts.FindIndex(p => p.Kind == PartKind.Bumper);
        Click(w, c.FromTable(g.Editor.Table.Parts[bumper].At));
        Assert.Equal(bumper, g.Editor.Selected);
        Key(w, Avalonia.Input.Key.R);
        Assert.Equal(15, g.Editor.Table.Parts[bumper].Angle);
        Key(w, Avalonia.Input.Key.OemPlus);
        Assert.Equal(250, g.Editor.Table.Parts[bumper].Score);
        Draw(w, c);
        int n = g.Editor.Table.Parts.Count;
        Key(w, Avalonia.Input.Key.Delete);
        Assert.Equal(n - 1, g.Editor.Table.Parts.Count);
    }

    [AvaloniaFact]
    public void The_physics_knob_follows_the_mouse_along_its_track()
    {
        var (w, c, g) = Building();
        Click(w, GameCanvas.TabRect((int)Tab.Physics).Center);
        Draw(w, c);
        var track = GameCanvas.KnobRect(Editor.Knob.Elasticity);
        w.MouseMove(new Point(track.X + 2, track.Center.Y));
        w.MouseDown(new Point(track.X + 2, track.Center.Y), MouseButton.Left);
        Assert.Equal(1, g.Editor.Table.Physics.Elasticity);
        w.MouseMove(new Point(track.Right, track.Center.Y));
        w.MouseUp(new Point(track.Right, track.Center.Y), MouseButton.Left);
        Assert.Equal(10, g.Editor.Table.Physics.Elasticity);
    }

    [AvaloniaFact]
    public void Renaming_the_table_by_typing_and_saving_it()
    {
        var (w, c, g) = Building("COMET RUN");
        Click(w, GameCanvas.TabRect((int)Tab.Tables).Center);
        Draw(w, c);
        Click(w, GameCanvas.NameRect.Center);
        Assert.NotNull(g.Renaming);
        for (int i = 0; i < 20; i++) Key(w, Avalonia.Input.Key.Back);
        foreach (var k in new[] { Avalonia.Input.Key.M, Avalonia.Input.Key.Y, Avalonia.Input.Key.Space, Avalonia.Input.Key.R, Avalonia.Input.Key.U, Avalonia.Input.Key.N })
            Key(w, k);
        Key(w, Avalonia.Input.Key.Enter);
        Assert.Null(g.Renaming);
        Assert.Equal("MY RUN", g.Editor.Table.Name);
        Assert.Equal(GameState.Editing, g.State);
        Draw(w, c);
        g.SaveTable();
        Assert.True(File.Exists(Path.Combine(g.TablesFolder, "my-run" + TableFile.Extension)));
    }

    [AvaloniaFact]
    public void Play_flippers_plunger_pause_and_back_to_the_construction_set_with_the_keys()
    {
        var (w, c, g) = Building();
        Click(w, GameCanvas.PlayRect.Center);
        Assert.Equal(GameState.Playing, g.State);
        w.KeyPress(Avalonia.Input.Key.Z, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(c.Input.Player.Left);
        w.KeyRelease(Avalonia.Input.Key.Z, RawInputModifiers.None, PhysicalKey.None, null);
        w.KeyPress(Avalonia.Input.Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(c.Input.Player.Plunge);
        for (int i = 0; i < 40; i++) g.Tick(c.Input.Player);
        Assert.True(g.World.Pull > 0.5);
        w.KeyRelease(Avalonia.Input.Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
        Dispatcher.UIThread.RunJobs();
        g.Tick(c.Input.Player);
        Assert.True(g.World.Balls[0].V.Y < -800);
        Key(w, Avalonia.Input.Key.P);
        Assert.Equal(GameState.Paused, g.State);
        Draw(w, c);
        Key(w, Avalonia.Input.Key.Escape);
        Assert.Equal(GameState.Editing, g.State);
    }

    [AvaloniaFact]
    public void M_mutes_and_unmutes()
    {
        var (w, c, _) = Building();
        Key(w, Avalonia.Input.Key.M);
        Assert.True(c.Sound.IsMuted);
        Key(w, Avalonia.Input.Key.M);
        Assert.False(c.Sound.IsMuted);
    }

    [AvaloniaFact]
    public void Initials_are_typed_on_the_keyboard()
    {
        var (w, c, g) = Building();
        g.PlayTable();
        g.World.Scores[0] = 77777;
        while (g.State == GameState.Playing) { g.World.Balls.Clear(); g.Tick(default); }
        g.Run((int)(60 * GameEngine.OverSeconds) + 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        Draw(w, c);
        foreach (var k in new[] { Avalonia.Input.Key.L, Avalonia.Input.Key.E, Avalonia.Input.Key.W }) Key(w, k);
        g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("LEW", g.HighScores.Entries[0].Initials);
        Draw(w, c);
        Key(w, Avalonia.Input.Key.Escape);
        Assert.Equal(GameState.Editing, g.State);
    }

    [AvaloniaFact]
    public void The_mouse_wheel_magnifies_where_it_points_and_the_view_maps_points_both_ways()
    {
        var (w, c, g) = Building();
        var at = c.FromTable(new Vec(120, 600));
        w.MouseWheel(at, new Vector(0, 1));
        Dispatcher.UIThread.RunJobs();
        Assert.True(g.Editor.Zoom > 1);
        var p = new Vec(130, 610);
        var back = c.ToTable(c.FromTable(p));
        Assert.Equal(p.X, back.X, 6);
        Assert.Equal(p.Y, back.Y, 6);
        w.MouseWheel(at, new Vector(0, -1));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, g.Editor.Zoom);
    }

    [AvaloniaFact]
    public void Every_screen_draws()
    {
        var (w, c, g) = Open();
        foreach (var tab in Enum.GetValues<Tab>())
        {
            g.OpenEditor();
            g.Editor.Tab = tab;
            Draw(w, c);
        }
        g.Editor.NewRule();
        Draw(w, c);
        g.PlayTable();
        Draw(w, c);
        g.Pause();
        Draw(w, c);
        g.ReturnToTitle();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        Draw(w, c);
    }
}
