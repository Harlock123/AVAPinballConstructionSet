using AVAPinballConstructionSet.Engine;

namespace AVAPinballConstructionSet.Tests;

/// <summary>The rules: parts wired to points, lights, the bonus multiplier, extra balls and multiball.</summary>
public class RulesTests
{
    private static Rule Wire(Table t, When when, IEnumerable<int> sources, Outcome what, int amount = 0, params int[] targets)
    {
        var r = new Rule { When = when, Sources = [.. sources], Effect = what, Amount = amount, Targets = [.. targets] };
        t.Rules.Add(r);
        return r;
    }

    private static int[] Ids(Table t, PartKind k) => t.Parts.Where(p => p.Kind == k).Select(p => p.Id).ToArray();

    [Fact]
    public void Any_one_part_hit_scores_the_rule_points()
    {
        var t = Lab.Table(Lab.Make(PartKind.Bumper, 120, 300), Lab.Make(PartKind.Bumper, 280, 300));
        Wire(t, When.Any, Ids(t, PartKind.Bumper), Outcome.Points, 2500);
        var w = Lab.World(t);
        w.Balls.Clear();
        Sim.Trigger(w, 1);
        Assert.Equal(100 + 2500, w.Score);
        Sim.Trigger(w, 0);
        Assert.Equal(2 * 2600, w.Score);
    }

    [Fact]
    public void All_the_drop_targets_down_light_the_kick_out_hole_for_5000_and_the_bank_comes_back_up()
    {
        var t = Lab.Table(Lab.Make(PartKind.DropTarget, 150, 300), Lab.Make(PartKind.DropTarget, 180, 300), Lab.Make(PartKind.DropTarget, 210, 300),
            Lab.Make(PartKind.Kickout, 300, 200));
        int hole = Ids(t, PartKind.Kickout)[0];
        Wire(t, When.All, Ids(t, PartKind.DropTarget), Outcome.Light, 5000, hole);
        var w = Lab.World(t, p => p.Gravity = 1);
        for (int i = 0; i < 3; i++)
        {
            Assert.False(w.States[3].Lit);
            var b = Lab.Ball(w, 150 + i * 30, 400, 0, -900);
            Lab.Run(w, 0.2);
            w.Balls.Remove(b);
            Lab.Ball(w, 60, 60);
            // Collected, and showing it, until the set is done.
            if (i < 2) Assert.True(w.States[i].Collected && w.States[i].Down);
        }
        Assert.True(w.States[3].Lit);
        Assert.Equal(5000, w.States[3].LitValue);
        Assert.All(w.States.Take(3), s => Assert.False(s.Down || s.Collected));
        Assert.Equal(1500, w.Score);
        // Into the lit hole: its 1,000 and the 5,000.
        Lab.Ball(w, 300, 150);
        Lab.Run(w, 0.6);
        Assert.Equal(1500 + 1000 + 5000, w.Score);
        Assert.False(w.States[3].Lit);
    }

    [Fact]
    public void The_lanes_all_made_raise_the_bonus_multiplier_and_the_bonus_is_counted_multiplied_at_the_end_of_the_ball()
    {
        var t = Lab.Table(Lab.Make(PartKind.Lane, 150, 100), Lab.Make(PartKind.Lane, 200, 100), Lab.Make(PartKind.Lane, 250, 100));
        Wire(t, When.All, Ids(t, PartKind.Lane), Outcome.Bonus);
        var w = Lab.World(t);
        Lab.Ball(w, 60, 60);
        for (int round = 0; round < 2; round++)
            for (int i = 0; i < 3; i++)
                Sim.Trigger(w, i);
        Assert.Equal(3, w.Multiplier);
        Assert.Equal(600, w.Bonus);
        int before = w.Score;
        w.Balls.Clear();
        Sim.Step(w, default);
        Assert.Equal(BallPhase.Bonus, w.Phase);
        Assert.Equal(before + 600 * 3, w.Score);
        Assert.Contains(Sfx.BonusCount, w.Heard);
    }

    [Fact]
    public void The_multiplier_and_the_bonus_stop_at_their_tops()
    {
        var t = Lab.Table(Lab.Make(PartKind.Rollover, 150, 100));
        Wire(t, When.Any, Ids(t, PartKind.Rollover), Outcome.Bonus);
        var w = Lab.World(t);
        for (int i = 0; i < 500; i++) Sim.Trigger(w, 0);
        Assert.Equal(Sim.MaxMultiplier, w.Multiplier);
        Assert.Equal(Sim.MaxBonus, w.Bonus);
    }

    [Fact]
    public void An_extra_ball_rule_gives_the_player_another_ball_before_the_next_one_comes_round()
    {
        var t = Lab.Table(Lab.Make(PartKind.Standup, 150, 300));
        Wire(t, When.Any, Ids(t, PartKind.Standup), Outcome.ExtraBall);
        var w = new World();
        Sim.Start(w, t, 2);
        Sim.Trigger(w, 0);
        Assert.Equal(1, w.ExtraBalls[0]);
        w.Balls.Clear();
        Lab.Run(w, Sim.BonusSeconds + 0.2);
        Assert.Equal(0, w.Player);
        Assert.Equal(1, w.BallNo);
        Assert.Equal("SHOOT AGAIN", w.Message);
        Assert.Equal(0, w.ExtraBalls[0]);
        w.Balls.Clear();
        Lab.Run(w, Sim.BonusSeconds + 0.2);
        Assert.Equal(1, w.Player);
    }

    [Fact]
    public void Multiball_feeds_a_second_ball_into_the_shooter_lane_and_shoots_it_and_the_ball_ends_only_when_both_are_gone()
    {
        var t = Lab.Table(Lab.Make(PartKind.Standup, 150, 300), Lab.Wall((100, 200), (300, 200)));
        Wire(t, When.Any, Ids(t, PartKind.Standup), Outcome.Multiball);
        var w = new World();
        Sim.Start(w, t);
        w.Balls[0].P = new Vec(200, 150);
        Sim.Trigger(w, 0);
        Assert.Equal(1, w.Waiting);
        Lab.Run(w, 0.1);
        Assert.Equal(2, w.Balls.Count);
        Assert.True(World.OnPlunger(w.Balls[1]));
        Lab.Run(w, 1.2);
        Assert.True(w.Balls.Count == 2 && !World.OnPlunger(w.Balls[1]), "shot by itself");
        Assert.Contains(Sfx.Multiball, w.Heard);
        // No more than three balls at once, however often it's set off.
        for (int i = 0; i < 6; i++) Sim.Trigger(w, 0);
        Assert.Equal(World.MaxBallsInPlay, w.Balls.Count + w.Waiting);
        w.Waiting = 0;
        w.Balls.RemoveAt(0);
        Sim.Step(w, default);
        Assert.Equal(BallPhase.Live, w.Phase);
        w.Balls.Clear();
        Sim.Step(w, default);
        Assert.Equal(BallPhase.Bonus, w.Phase);
    }

    [Fact]
    public void A_rule_needing_all_its_parts_counts_each_only_once()
    {
        var t = Lab.Table(Lab.Make(PartKind.Rollover, 150, 100), Lab.Make(PartKind.Rollover, 250, 100));
        Wire(t, When.All, Ids(t, PartKind.Rollover), Outcome.Points, 10000);
        var w = Lab.World(t);
        Sim.Trigger(w, 0);
        Sim.Trigger(w, 0);
        Sim.Trigger(w, 0);
        Assert.Equal(1500, w.Score);
        Sim.Trigger(w, 1);
        Assert.Equal(2000 + 10000, w.Score);
    }

    [Fact]
    public void Taking_a_part_off_the_table_takes_it_out_of_its_rules_and_a_rule_with_nothing_left_goes()
    {
        var t = Lab.Table(Lab.Make(PartKind.Rollover, 150, 100), Lab.Make(PartKind.Rollover, 250, 100), Lab.Make(PartKind.Kickout, 200, 300));
        var ids = Ids(t, PartKind.Rollover);
        Wire(t, When.All, ids, Outcome.Light, 5000, Ids(t, PartKind.Kickout));
        Wire(t, When.Any, [ids[0]], Outcome.Points, 100);
        t.Remove(0);
        Assert.Single(t.Rules);
        Assert.Equal([ids[1]], t.Rules[0].Sources);
        t.Remove(t.IndexOf(Ids(t, PartKind.Kickout)[0]));
        Assert.Empty(t.Rules);
    }

    [Fact]
    public void A_tilted_table_scores_nothing_and_its_rules_do_nothing()
    {
        var t = Lab.Table(Lab.Make(PartKind.Rollover, 150, 100));
        Wire(t, When.Any, Ids(t, PartKind.Rollover), Outcome.Bonus);
        var w = Lab.World(t);
        w.Tilted = true;
        Sim.Trigger(w, 0);
        Assert.Equal(0, w.Score);
        Assert.Equal(1, w.Multiplier);
    }

    [Fact]
    public void Rules_read_as_a_sentence()
    {
        var t = Samples.Make("TARGET RANGE");
        Assert.Equal("ALL 5 DROPS: LIGHT KICK-OUT 5,000", Sim.Describe(t, t.Rules[0]));
        Assert.Equal("BOTH LANES: BONUS X+1", Sim.Describe(t, t.Rules[1]));
        Assert.Equal("SPINNER: 50 POINTS", Sim.Describe(t, t.Rules[2]));
        var r = new Rule { When = When.Any, Sources = t.Parts.Where(p => p.Kind == PartKind.Bumper).Select(p => p.Id).ToList(), Effect = Outcome.Multiball };
        Assert.Equal("ANY BUMPERS: MULTIBALL", Sim.Describe(t, r));
    }
}
