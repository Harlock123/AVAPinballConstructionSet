using Avalonia.Input;
using AVAPinballConstructionSet.Engine;

namespace AVAPinballConstructionSet.Tests;

/// <summary>The game round the table: balls and players, the tilt, high scores, the title and demonstration, and the keys.</summary>
public class GameTests
{
    /// <summary>Loses every ball in play, and waits out the bonus.</summary>
    private static void Lose(World w)
    {
        w.Balls.Clear();
        w.Waiting = 0;
        Sim.Step(w, default);
        Lab.Run(w, Sim.BonusSeconds + 0.1);
    }

    // ───────────────────────────── balls and players ─────────────────────────────

    [Fact]
    public void A_game_is_three_balls_each_and_then_it_is_over()
    {
        var w = new World();
        Sim.Start(w, Samples.Make("LUCKY STAR"));
        Assert.Equal((1, 0), (w.BallNo, w.Player));
        Assert.True(World.OnPlunger(w.Balls.Single()));
        Lose(w);
        Assert.Equal(2, w.BallNo);
        Lose(w);
        Assert.Equal(3, w.BallNo);
        Assert.False(w.Over);
        Lose(w);
        Assert.True(w.Over);
        Assert.Contains(Sfx.GameOver, w.Heard);
    }

    [Fact]
    public void Players_join_on_the_first_ball_and_take_turns_ball_by_ball()
    {
        var w = new World();
        var t = Samples.Make("TARGET RANGE");
        t.Balls = 2;
        Sim.Start(w, t);
        Assert.True(Sim.AddPlayer(w));
        Assert.True(Sim.AddPlayer(w));
        Assert.Equal(3, w.Players);
        Assert.Contains(Sfx.Coin, w.Heard);
        var order = new List<(int Ball, int Player)>();
        while (!w.Over)
        {
            order.Add((w.BallNo, w.Player));
            Sim.Award(w, 1000 * (w.Player + 1), null);
            Lose(w);
        }
        Assert.Equal([(1, 0), (1, 1), (1, 2), (2, 0), (2, 1), (2, 2)], order);
        Assert.Equal([2000, 4000, 6000, 0], w.Scores);
    }

    [Fact]
    public void No_one_can_join_once_the_first_ball_is_over_or_when_four_are_playing()
    {
        var w = new World();
        Sim.Start(w, Samples.Make("TARGET RANGE"));
        for (int i = 0; i < 5; i++) Sim.AddPlayer(w);
        Assert.Equal(World.MaxPlayers, w.Players);
        var v = new World();
        Sim.Start(v, Samples.Make("TARGET RANGE"));
        Lose(v);
        Assert.False(Sim.AddPlayer(v));
        Assert.Equal(1, v.Players);
    }

    [Fact]
    public void Passing_the_tables_mark_wins_an_extra_ball_once_a_game()
    {
        var w = new World();
        var t = Samples.Make("LUCKY STAR");
        t.ExtraBallAt = 50000;
        Sim.Start(w, t);
        Sim.Award(w, 49990, null);
        Assert.Equal(0, w.ExtraBalls[0]);
        Sim.Award(w, 20, null);
        Assert.Equal(1, w.ExtraBalls[0]);
        Assert.Contains(Sfx.ExtraBall, w.Heard);
        Sim.Award(w, 100000, null);
        Assert.Equal(1, w.ExtraBalls[0]);
        Lose(w);
        Assert.Equal(1, w.BallNo);
        Lose(w);
        Assert.Equal(2, w.BallNo);
    }

    [Fact]
    public void Each_ball_starts_the_table_afresh_lights_out_targets_up_bonus_nothing()
    {
        var w = new World();
        Sim.Start(w, Samples.Make("TARGET RANGE"));
        foreach (var s in w.States) { s.Lit = true; s.Down = true; s.Collected = true; }
        w.Bonus = 900;
        w.Multiplier = 3;
        Lose(w);
        Assert.All(w.States, s => Assert.False(s.Lit || s.Down || s.Collected));
        Assert.Equal((0, 1), (w.Bonus, w.Multiplier));
    }

    // ───────────────────────────── the tilt ─────────────────────────────

    [Fact]
    public void A_nudge_shoves_the_ball_and_too_many_too_quickly_tilt_the_table()
    {
        var w = Lab.World(Lab.Table(Lab.Make(PartKind.FlipperLeft, 128, 652), Lab.Make(PartKind.Bumper, 200, 200)));
        var b = Lab.Ball(w, 200, 400);
        Sim.Step(w, new Controls(NudgeX: 1));
        Assert.True(b.V.X > 100, "shoved right");
        Assert.Contains(Sfx.Nudge, w.Heard);
        // Holding the key is one nudge.
        Lab.Run(w, 0.2, new Controls(NudgeX: 1));
        Assert.Equal(1, w.TiltMeter, 0.2);
        Sim.Step(w, default);
        Sim.Step(w, new Controls(NudgeY: -1));
        Assert.Equal("DANGER", w.Message);
        Assert.Contains(Sfx.Danger, w.Heard);
        Sim.Step(w, default);
        Sim.Step(w, new Controls(NudgeX: -1));
        Assert.True(w.Tilted);
        Assert.Contains(Sfx.Tilt, w.Heard);
        // Tilted: the flippers are dead and nothing scores.
        Lab.Run(w, 0.2, new Controls(Left: true));
        Assert.False(w.LeftHeld);
        Assert.Equal(Sim.Rest(w.Table.Parts[0]).Down, w.States[0].Angle, 6);
        Sim.Trigger(w, 1);
        Assert.Equal(0, w.Score);
    }

    [Fact]
    public void Nudges_spaced_out_never_tilt_and_a_tilt_loses_the_bonus_but_the_next_ball_is_fine()
    {
        var w = new World();
        Sim.Start(w, Samples.Make("LUCKY STAR"));
        w.Balls[0].P = new Vec(200, 300);
        for (int i = 0; i < 6; i++)
        {
            Sim.Step(w, new Controls(NudgeX: 1));
            Lab.Run(w, 2.5);
            w.Balls[0].P = new Vec(200, 300);
            w.Balls[0].V = default;
        }
        Assert.False(w.Tilted);
        for (int i = 0; i < 3; i++) { Sim.Step(w, new Controls(NudgeX: 1)); Sim.Step(w, default); }
        Assert.True(w.Tilted);
        w.Bonus = 5000;
        int score = w.Score;
        Lose(w);
        Assert.Equal(score, w.Scores[0]);
        Assert.False(w.Tilted);
        Assert.Equal(0, w.TiltMeter);
    }

    // ───────────────────────────── the engine ─────────────────────────────

    [Fact]
    public void The_title_runs_the_demonstration_then_the_high_scores_then_the_title()
    {
        var g = Lab.Engine();
        Assert.Equal(GameState.Title, g.State);
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
        g.Tick(default, start: true);
        Assert.Equal(GameState.Editing, g.State);
    }

    [Fact]
    public void In_the_demonstration_the_computer_plays_and_scores()
    {
        var g = Lab.Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        g.Idle(60 * 40);
        Assert.Equal(GameState.Attract, g.State);
        Assert.True(g.World.Score > 0);
        Assert.True(g.Pilot.Flips > 0);
    }

    [Fact]
    public void From_the_construction_set_enter_plays_the_table_and_escape_goes_back_after_a_pause()
    {
        var g = Lab.Engine();
        g.Tick(default, start: true);
        g.Tick(default);
        g.OpenSample("COMET RUN");
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal("COMET RUN", g.World.Table.Name);
        // Enter again on the first ball: a second player.
        g.Tick(default);
        g.Tick(default, start: true);
        Assert.Equal(2, g.World.Players);
        g.Pause();
        Assert.Equal(GameState.Paused, g.State);
        var at = g.World.Balls[0].P;
        g.Run(30, new Controls(Plunge: true));
        Assert.Equal(at, g.World.Balls[0].P);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
        g.Pause();
        g.OpenEditor();
        Assert.Equal(GameState.Editing, g.State);
    }

    [Fact]
    public void A_game_played_on_a_table_does_not_change_the_table_being_built()
    {
        var g = Lab.Engine();
        g.OpenEditor();
        g.OpenSample("TARGET RANGE");
        g.PlayTable();
        g.World.Table.Parts.Clear();
        Assert.NotEmpty(g.Editor.Table.Parts);
    }

    [Fact]
    public void Game_over_asks_each_player_with_a_high_score_for_initials_in_turn_and_the_tables_keep_their_own_scores()
    {
        var g = Lab.Engine();
        g.OpenEditor();
        g.OpenSample("MAGNET MINE");
        g.PlayTable();
        Sim.AddPlayer(g.World);
        Sim.AddPlayer(g.World);
        g.World.Scores[0] = 70000;
        g.World.Scores[1] = 100;
        g.World.Scores[2] = 99000;
        while (g.State == GameState.Playing)
        {
            g.World.Balls.Clear();
            g.Tick(default);
        }
        Assert.Equal(GameState.GameOver, g.State);
        g.Run((int)(60 * GameEngine.OverSeconds) + 5);
        Assert.Equal(GameState.EnterInitials, g.State);
        Assert.Equal(0, g.EntryPlayer);
        foreach (char ch in "ONE") g.Initials!.Type(ch);
        g.Tick(default);
        Assert.Equal(GameState.EnterInitials, g.State);
        Assert.Equal(2, g.EntryPlayer);
        Assert.Equal("ONE", g.Initials!.Initials);
        foreach (char ch in "TRE") g.Initials!.Type(ch);
        g.Tick(default);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal(new HighScoreEntry("TRE", 99000), g.HighScores.Entries[0]);
        Assert.Equal(new HighScoreEntry("ONE", 70000), g.HighScores.Entries[1]);
        Assert.Equal(1, g.NewEntryIndex >= 0 ? 1 : 0);
        // Kept on disk for that table; another table has its own.
        Assert.Equal(99000, HighScoreTable.Load(g.Folder, "MAGNET MINE").TopScore);
        Assert.Equal(60000, HighScoreTable.Load(g.Folder, "BUMPER CITY").TopScore);
        // Enter plays again; left alone, it's back to the construction set.
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
    }

    [Fact]
    public void After_the_high_scores_the_construction_set_comes_back_by_itself()
    {
        var g = Lab.Engine();
        g.OpenEditor();
        g.PlayTable();
        while (g.State == GameState.Playing) { g.World.Balls.Clear(); g.Tick(default); }
        g.Run((int)(60 * (GameEngine.OverSeconds + GameEngine.HighScoreSeconds)) + 10);
        Assert.Equal(GameState.Editing, g.State);
    }

    [Fact]
    public void The_table_being_built_is_kept_on_disk_and_is_there_next_time()
    {
        string folder = Lab.TempFolder();
        var g = new GameEngine(folder) { Silent = true };
        g.OpenEditor();
        g.OpenSample("BUMPER CITY");
        g.Editor.Place(PartKind.Magnet, new Vec(100, 400));
        g.Run(70);
        Assert.True(File.Exists(g.WorkingPath));
        var again = new GameEngine(folder) { Silent = true };
        Assert.Equal("BUMPER CITY", again.Editor.Table.Name);
        Assert.Contains(again.Editor.Table.Parts, p => p.Kind == PartKind.Magnet);
        // A fresh start opens the first sample.
        Assert.Equal(Samples.Names[0], Lab.Engine().Editor.Table.Name);
    }

    [Fact]
    public void The_engine_hears_the_game_and_the_construction_set_unless_it_is_silent()
    {
        var g = Lab.Engine();
        g.Silent = false;
        var heard = new List<Sfx>();
        g.Sound += heard.Add;
        g.Tick(default);
        Assert.Contains(Sfx.TitleTune, heard);
        g.OpenEditor();
        g.Editor.Place(PartKind.Bumper, new Vec(100, 100));
        g.Tick(default);
        Assert.Contains(Sfx.Place, heard);
        g.PlayTable();
        Assert.Contains(Sfx.Start, heard);
        g.Run(60, new Controls(Plunge: true));
        g.Tick(default);
        Assert.Contains(Sfx.Launch, heard);
    }

    [Fact]
    public void A_copied_world_goes_on_just_as_the_original()
    {
        var a = new World();
        Sim.Start(a, Samples.Make("BUMPER CITY"), 1, 9);
        var p = new Pilot(4);
        for (int i = 0; i < 60 * 8; i++) Sim.Step(a, p.Decide(a));
        var b = a.Copy();
        var pa = new Pilot(11);
        var pb = new Pilot(11);
        for (int i = 0; i < 60 * 15; i++)
        {
            Sim.Step(a, pa.Decide(a));
            Sim.Step(b, pb.Decide(b));
        }
        Assert.Equal(a.Scores, b.Scores);
        Assert.Equal(a.Balls.Select(x => x.P), b.Balls.Select(x => x.P));
        Assert.Equal(a.BallNo, b.BallNo);
        // And the copy is its own: changing it leaves the original alone.
        b.Scores[0] = -1;
        b.States[0].Lit = !a.States[0].Lit;
        b.Table.Parts.Clear();
        Assert.NotEqual(-1, a.Scores[0]);
        Assert.NotEqual(b.States[0].Lit, a.States[0].Lit);
        Assert.NotEmpty(a.Table.Parts);
    }

    // ───────────────────────────── the keys ─────────────────────────────

    [Fact]
    public void A_shift_let_go_as_caps_lock_drops_its_flipper()
    {
        // With shift:both_capslock_cancel, X11 names a Shift's release CapsLock; the physical key is still the Shift.
        using var input = new InputHandler();
        input.OnKeyDown(Key.LeftShift, PhysicalKey.ShiftLeft);
        input.OnKeyDown(Key.RightShift, PhysicalKey.ShiftRight);
        Assert.Equal(new Controls(Left: true, Right: true), input.Player);
        input.OnKeyUp(Key.CapsLock, PhysicalKey.ShiftRight);
        Assert.Equal(new Controls(Left: true), input.Player);
        input.OnKeyUp(Key.CapsLock, PhysicalKey.ShiftLeft);
        Assert.Equal(default, input.Player);
        input.OnKeyDown(Key.Z, PhysicalKey.Z);
        Assert.Equal(new Controls(Left: true), input.Player);
        input.OnKeyUp(Key.Z, PhysicalKey.Z);
        Assert.Equal(default, input.Player);
    }

    [Fact]
    public void The_keys()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Key.Z);
        Assert.Equal(new Controls(Left: true), input.Player);
        input.OnKeyUp(Key.Z);
        input.OnKeyDown(Key.LeftShift);
        input.OnKeyDown(Key.RightShift);
        Assert.Equal(new Controls(Left: true, Right: true), input.Player);
        input.Clear();
        input.OnKeyDown(Key.OemQuestion);
        input.OnKeyDown(Key.Space);
        Assert.Equal(new Controls(Right: true, Plunge: true), input.Player);
        input.Clear();
        input.OnKeyDown(Key.Down);
        Assert.True(input.Player.Plunge);
        input.Clear();
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.NudgeX);
        input.OnKeyDown(Key.Right);
        Assert.Equal(0, input.Player.NudgeX);
        input.Clear();
        input.OnKeyDown(Key.Right);
        input.OnKeyDown(Key.Up);
        Assert.Equal((1, -1), (input.Player.NudgeX, input.Player.NudgeY));
        input.Clear();
        input.OnKeyDown(Key.Enter);
        Assert.True(input.Start);
        input.Clear();
        Assert.Equal(default, input.Player);
    }

    [Fact]
    public void Up_through_the_keys_nudges_the_ball_up_the_screen_and_z_through_the_keys_swings_the_left_flipper_up()
    {
        using var input = new InputHandler();
        var w = Lab.World(Lab.Table(Lab.Make(PartKind.FlipperLeft, 128, 652)), p => p.Gravity = 1);
        var b = Lab.Ball(w, 200, 300);
        input.OnKeyDown(Key.Up);
        Sim.Step(w, input.Player);
        Assert.True(b.V.Y < -100, "up the screen is -y");
        input.Clear();
        input.OnKeyDown(Key.Z);
        double rest = w.States[0].Angle;
        Sim.Step(w, input.Player);
        Assert.True(w.States[0].Angle < rest, "the left flipper's tip swings up the screen");
        var tip = w.Table.Parts[0].At + Vec.Polar(w.States[0].Angle, Parts.FlipperLength);
        Assert.True(tip.Y < w.Table.Parts[0].At.Y + Parts.FlipperLength * 0.5);
    }

    [Fact]
    public void Typing_initials_and_dialling_them()
    {
        var e = new InitialsEntry("AB");
        Assert.Equal("ABA", e.Initials);
        e.Cycle(-1);
        Assert.Equal(' ', e.Initials[0]);
        e.Type('z');
        e.Type('!');
        e.Type('9');
        e.Type('.');
        Assert.Equal("Z9.", e.Initials);
        Assert.True(e.IsDone);
    }

    [Fact]
    public void A_high_score_table_keeps_the_top_ten_and_survives_a_broken_file()
    {
        string folder = Lab.TempFolder();
        var t = HighScoreTable.Load(folder, "LUCKY STAR");
        Assert.Equal(10, t.Entries.Count);
        Assert.False(t.Qualifies(100));
        Assert.True(t.Qualifies(30000));
        Assert.Equal(0, t.Insert("WOW", 999999));
        Assert.Equal(-1, t.Insert("NO", 1));
        t.Save();
        var again = HighScoreTable.Load(folder, "LUCKY STAR");
        Assert.Equal(new HighScoreEntry("WOW", 999999), again.Entries[0]);
        Assert.Equal("WOW", again.LastInitials);
        Assert.Equal(10, again.Entries.Count);
        File.WriteAllText(Path.Combine(folder, "scores-comet-run.txt"), "garbage\n|||\nAAA|notanumber");
        Assert.Empty(HighScoreTable.Load(folder, "COMET RUN").Entries);
    }

    // ───────────────────────────── the sample tables ─────────────────────────────

    [Fact]
    public void Every_sample_table_plays_with_the_ball_always_inside_the_table_and_scores()
    {
        foreach (var name in Samples.Names)
        {
            var w = new World();
            Sim.Start(w, Samples.Make(name), 1, 3);
            var p = new Pilot(3);
            for (int i = 0; i < 60 * 90 && !w.Over; i++)
            {
                Sim.Step(w, p.Decide(w));
                foreach (var b in w.Balls)
                {
                    Assert.InRange(b.P.X, Field.BallR - 0.5, Field.TableW - Field.BallR + 0.5);
                    Assert.True(b.P.Y > -1 && b.P.Y < Field.TableH + 20, $"{name}: ball at {b.P}");
                    if (b.P.X > Field.LaneX + 1 && b.P.Y > Field.LaneTop + 10) Assert.True(b.P.X > Field.LaneX + Field.BallR - 0.5, $"{name}: in the lane wall at {b.P}");
                }
            }
            Assert.True(w.Score > 1000, $"{name} scored {w.Score}");
        }
    }

    [Fact]
    public void The_computer_at_the_flippers_does_far_better_than_no_one()
    {
        int with = 0, without = 0;
        foreach (var name in Samples.Names)
        for (int seed = 1; seed <= 2; seed++)
        {
            foreach (bool flips in new[] { true, false })
            {
                var w = new World();
                Sim.Start(w, Samples.Make(name), 1, seed);
                var p = new Pilot(seed);
                for (int i = 0; i < 60 * 60 * 6 && !w.Over; i++)
                {
                    var c = p.Decide(w);
                    Sim.Step(w, flips ? c : c with { Left = false, Right = false });
                }
                if (flips) with += w.Score;
                else without += w.Score;
            }
        }
        Assert.True(with > without * 3, $"{with} against {without}");
    }

    [Fact]
    public void The_computer_flips_when_a_ball_comes_down_onto_a_flipper_and_not_when_it_is_far_away()
    {
        var w = Lab.World(Lab.Make(PartKind.FlipperLeft, 128, 652), Lab.Make(PartKind.FlipperRight, 264, 652));
        var b = Lab.Ball(w, 160, 640, 0, 200);
        var p = new Pilot();
        var c = p.Decide(w);
        Assert.True(c.Left);
        Assert.False(c.Right);
        b.P = new Vec(200, 200);
        var q = new Pilot();
        Assert.Equal(default, q.Decide(w));
    }

    [Fact]
    public void The_computer_pulls_the_plunger_back_and_lets_go()
    {
        var w = new World();
        Sim.Start(w, Samples.Make("LUCKY STAR"));
        var p = new Pilot();
        Lab.Run(w, 0.2);
        bool pulled = false;
        for (int i = 0; i < 120; i++)
        {
            var c = p.Decide(w);
            pulled |= c.Plunge;
            Sim.Step(w, c);
        }
        Assert.True(pulled);
        Assert.False(World.OnPlunger(w.Balls[0]));
    }
}
