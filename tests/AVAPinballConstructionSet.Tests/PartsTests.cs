using AVAPinballConstructionSet.Engine;

namespace AVAPinballConstructionSet.Tests;

/// <summary>Every part in the box: what it does to the ball and what it scores.</summary>
public class PartsTests
{
    [Fact]
    public void A_pop_bumper_kicks_the_ball_away_scores_and_flashes()
    {
        var w = Lab.World(Lab.Make(PartKind.Bumper, 200, 300));
        var b = Lab.Ball(w, 200, 250);
        Lab.Run(w, 0.3);
        Assert.Equal(100, w.Score);
        Assert.Contains(Sfx.Bumper, w.Heard);
        Assert.True(w.States[0].Flash > 0);
        Assert.True(b.V.Y < -500, "thrown back up");
        Assert.Single(w.Pops);
    }

    [Fact]
    public void A_kicker_kicks_off_its_long_face_and_scores_but_its_back_is_dead_rubber()
    {
        // The left kicker's face runs from its top corner down to its bottom right: it faces up and right.
        var w = Lab.World(Lab.Table(Lab.Make(PartKind.SlingLeft, 200, 400)), p => p.Gravity = 1);
        var b = Lab.Ball(w, 236, 392, -500, 0);
        Lab.Run(w, 0.15);
        Assert.Equal(10, w.Score);
        Assert.Contains(Sfx.Sling, w.Heard);
        Assert.True(b.V.X > 0.8 * w.Table.Physics.KickSpeed * 0.5 && b.V.Length >= w.Table.Physics.KickSpeed * 0.8);
        var back = Lab.World(Lab.Table(Lab.Make(PartKind.SlingLeft, 200, 400)), p => p.Gravity = 1);
        var c = Lab.Ball(back, 160, 400, 500, 0);
        Lab.Run(back, 0.15);
        Assert.Equal(0, back.Score);
        Assert.True(c.V.X < 0, "bounced off the back");
        Assert.True(c.V.Length < 500);
    }

    [Fact]
    public void The_right_kicker_is_the_left_one_mirrored()
    {
        var w = Lab.World(Lab.Table(Lab.Make(PartKind.SlingRight, 200, 400)), p => p.Gravity = 1);
        var b = Lab.Ball(w, 164, 392, 500, 0);
        Lab.Run(w, 0.15);
        Assert.Equal(10, w.Score);
        Assert.True(b.V.X < 0);
    }

    [Fact]
    public void A_post_is_lively_rubber_and_scores_a_little_for_a_hard_hit()
    {
        var w = Lab.World(Lab.Make(PartKind.Post, 200, 300));
        var b = Lab.Ball(w, 200, 200, 0, 800);
        Lab.Run(w, 0.15);
        Assert.Equal(10, w.Score);
        Assert.True(b.V.Y < -400);
    }

    [Fact]
    public void A_rollover_scores_once_as_the_ball_rolls_over_it_without_turning_it()
    {
        var w = Lab.World(Lab.Make(PartKind.Rollover, 200, 300));
        var b = Lab.Ball(w, 200, 240, 0, 300);
        Lab.Run(w, 0.4);
        Assert.Equal(500, w.Score);
        Assert.Equal(200, b.P.X, 6);
        Assert.Contains(Sfx.Rollover, w.Heard);
        Assert.Equal(100, w.Bonus);
        // Back over it, the other way: again.
        b.P = new Vec(200, 360);
        b.V = new Vec(0, -500);
        Lab.Run(w, 0.15);
        Assert.Equal(1000, w.Score);
    }

    [Fact]
    public void A_fast_ball_over_a_rollover_still_counts()
    {
        var w = Lab.World(Lab.Table(Lab.Make(PartKind.Rollover, 200, 300)), p => p.Gravity = 1);
        Lab.Ball(w, 200, 100, 0, Sim.MaxSpeed);
        Lab.Run(w, 0.1);
        Assert.Equal(500, w.Score);
    }

    [Fact]
    public void A_lane_guides_the_ball_between_its_walls_over_its_rollover()
    {
        var w = Lab.World(Lab.Make(PartKind.Lane, 200, 300));
        var b = Lab.Ball(w, 200, 200);
        Lab.Run(w, 0.6);
        Assert.Equal(500, w.Score);
        // Coming down on a guide's end, it's turned aside.
        var w2 = Lab.World(Lab.Make(PartKind.Lane, 200, 300));
        var c = Lab.Ball(w2, 219, 200);
        Lab.Run(w2, 0.6);
        Assert.True(c.P.X > 216 + Field.BallR - 1, "turned outside the lane");
        Assert.Equal(0, w2.Score);
    }

    [Fact]
    public void A_drop_target_drops_when_hit_lets_the_ball_through_after_and_the_bank_comes_back_up()
    {
        var w = Lab.World(Lab.Table(Lab.Make(PartKind.DropTarget, 200, 300), Lab.Make(PartKind.DropTarget, 260, 300)), p => p.Gravity = 1);
        var b = Lab.Ball(w, 200, 400, 0, -900);
        Lab.Run(w, 0.2);
        Assert.True(w.States[0].Down);
        Assert.Equal(500, w.Score);
        Assert.True(b.V.Y > 0, "bounced off it as it fell");
        Assert.Contains(Sfx.Drop, w.Heard);
        // Down, it's no longer in the way.
        b.P = new Vec(200, 400);
        b.V = new Vec(0, -900);
        Lab.Run(w, 0.3);
        Assert.True(b.P.Y < 280);
        Assert.Equal(500, w.Score);
        // The other one down too: the whole bank comes back up a moment later.
        var c = Lab.Ball(w, 260, 400, 0, -900);
        Lab.Run(w, 0.2);
        Assert.True(w.States[1].Down);
        Lab.Run(w, 1.4);
        Assert.False(w.States[0].Down);
        Assert.False(w.States[1].Down);
        Assert.Contains(Sfx.Reset, w.Heard);
        _ = c;
    }

    [Fact]
    public void A_standup_target_scores_every_hit_and_stays_up()
    {
        var w = Lab.World(Lab.Table(Lab.Make(PartKind.Standup, 200, 300)), p => p.Gravity = 1);
        for (int k = 1; k <= 2; k++)
        {
            var b = Lab.Ball(w, 200, 400, 0, -900);
            Lab.Run(w, 0.25);
            Assert.True(b.V.Y > 0);
            Assert.False(w.States[0].Down);
            Assert.Equal(250 * k, w.Score);
            w.Balls.Remove(b);
            Lab.Ball(w, 50, 50);
        }
    }

    [Fact]
    public void A_target_turned_on_its_side_faces_the_ball_from_the_side()
    {
        var w = Lab.World(Lab.Table(Lab.Make(PartKind.Standup, 300, 300, 90)), p => p.Gravity = 1);
        var b = Lab.Ball(w, 200, 300, 900, 0);
        Lab.Run(w, 0.2);
        Assert.Equal(250, w.Score);
        Assert.True(b.V.X < 0);
    }

    [Fact]
    public void A_spinner_spins_as_the_ball_goes_through_and_scores_every_turn()
    {
        var w = Lab.World(Lab.Table(Lab.Make(PartKind.Spinner, 200, 300)), p => p.Gravity = 1);
        Lab.Ball(w, 200, 400, 0, -1500);
        Lab.Run(w, 0.1);
        double rate = Math.Abs(w.States[0].SpinRate);
        Assert.True(rate > 5);
        // The ball away, it turns down.
        w.Balls[0].P = new Vec(60, 60);
        w.Balls[0].V = default;
        Lab.Run(w, 1);
        Assert.True(Math.Abs(w.States[0].SpinRate) < rate - 3);
        Lab.Run(w, 4);
        Assert.Equal(0, w.States[0].SpinRate);
        Assert.True(w.Score >= 300, $"scored {w.Score}");
        Assert.Equal(0, w.Score % 100);
        Assert.Contains(Sfx.Spinner, w.Heard);
    }

    [Fact]
    public void A_magnet_grabs_the_ball_for_a_moment_then_lets_it_go()
    {
        var w = Lab.World(Lab.Make(PartKind.Magnet, 200, 300));
        var b = Lab.Ball(w, 225, 280);
        Lab.Run(w, 1.0);
        Assert.True(Vec.Distance(b.P, new Vec(200, 300)) < 12, "held at the magnet");
        Assert.Equal(250, w.Score);
        Assert.Contains(Sfx.Magnet, w.Heard);
        Lab.Run(w, 1.2);
        Assert.True(b.P.Y > 360, "let go");
        Assert.True(w.States[0].Rest > 0);
    }

    [Fact]
    public void A_kick_out_hole_catches_the_ball_holds_it_then_kicks_it_out_the_way_it_faces()
    {
        var w = Lab.World(Lab.Make(PartKind.Kickout, 200, 300, 90));
        var b = Lab.Ball(w, 200, 240);
        Lab.Run(w, 0.45);
        Assert.Equal(0, b.Held);
        Assert.Equal(new Vec(200, 300), b.P);
        Assert.Equal(1000, w.Score);
        Assert.Contains(Sfx.Catch, w.Heard);
        Lab.Run(w, 0.5);
        Assert.Equal(0, b.Held);
        Lab.Run(w, 0.6);
        Assert.Equal(-1, b.Held);
        Assert.Contains(Sfx.Kickout, w.Heard);
        Assert.True(b.P.X > 215, "kicked out to the right, as it faces");
    }

    [Fact]
    public void A_ball_eater_swallows_the_ball_and_the_ball_is_over()
    {
        var t = Lab.Table(Lab.Make(PartKind.Eater, 200, 400));
        t.Parts[0].Score = 2500;
        var w = Lab.World(t);
        Lab.Ball(w, 200, 300);
        Lab.Run(w, 0.5);
        Assert.Empty(w.Balls);
        Assert.Equal(2500 + 0, w.Score);
        Assert.Contains(Sfx.Eaten, w.Heard);
        Assert.Equal(BallPhase.Bonus, w.Phase);
    }

    [Fact]
    public void A_lit_part_scores_its_lit_value_on_top_of_its_own_and_goes_out()
    {
        var w = Lab.World(Lab.Make(PartKind.Bumper, 200, 300));
        w.States[0].Lit = true;
        w.States[0].LitValue = 5000;
        Lab.Ball(w, 200, 250);
        Lab.Run(w, 0.3);
        Assert.Equal(5100, w.Score);
        Assert.False(w.States[0].Lit);
        Assert.Contains(Sfx.Lit, w.Heard);
    }

    [Fact]
    public void Every_part_scores_what_the_construction_set_says()
    {
        var t = Lab.Table(Lab.Make(PartKind.Bumper, 200, 300));
        t.Parts[0].Score = 2500;
        var w = Lab.World(t);
        Lab.Ball(w, 200, 250);
        Lab.Run(w, 0.3);
        Assert.Equal(2500, w.Score);
    }

    [Fact]
    public void Each_kind_out_of_the_box_has_its_name_score_colour_and_icon_size()
    {
        foreach (var k in Enum.GetValues<PartKind>())
        {
            Assert.False(string.IsNullOrEmpty(Parts.Name(k)));
            Assert.False(string.IsNullOrEmpty(Parts.Short(k)));
            Assert.True(Parts.DefaultScore(k) >= 0);
            Assert.True(Parts.DefaultColour(k) >> 24 == 0xFF);
        }
        Assert.Equal(14, Parts.Box.Length);
        Assert.DoesNotContain(PartKind.Wall, Parts.Box);
    }
}
