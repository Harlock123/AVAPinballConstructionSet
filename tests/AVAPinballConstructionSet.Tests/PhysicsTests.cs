using AVAPinballConstructionSet.Engine;

namespace AVAPinballConstructionSet.Tests;

public class PhysicsTests
{
    // ───────────────────────────── the geometry ─────────────────────────────

    [Fact]
    public void A_moving_ball_touches_a_line_a_circle_and_an_arc_where_it_should()
    {
        double t = 1;
        var n = default(Vec);
        // Straight down at a flat line 100 below: it touches when its edge (7 up from its centre) reaches it.
        Assert.True(Collide.Sweep(Shape.Line(new Vec(0, 100), new Vec(200, 100)), new Vec(100, 0), new Vec(0, 200), 7, ref t, ref n));
        Assert.Equal(93 / 200.0, t, 6);
        Assert.Equal(new Vec(0, -1), n);
        // A circle of 10 straight ahead.
        t = 1;
        Assert.True(Collide.Sweep(Shape.Ring(new Vec(100, 100), 10), new Vec(0, 100), new Vec(200, 0), 7, ref t, ref n));
        Assert.Equal(83 / 200.0, t, 6);
        Assert.Equal(-1, n.X, 6);
        // Inside an arc (the top of the table's rounded corner), moving out into it.
        var arc = Shape.Bow(new Vec(100, 100), 100, Math.PI, Math.PI / 2);
        t = 1;
        Assert.True(Collide.Sweep(arc, new Vec(100 - 60, 100 - 60), new Vec(-100, -100), 7, ref t, ref n));
        var hit = new Vec(40, 40) + new Vec(-100, -100) * t;
        Assert.Equal(93, (hit - new Vec(100, 100)).Length, 3);
        Assert.True(n.X > 0 && n.Y > 0);
        // Moving away, or past the end of a line: no touch.
        t = 1;
        Assert.False(Collide.Sweep(Shape.Line(new Vec(0, 100), new Vec(200, 100)), new Vec(100, 0), new Vec(0, -200), 7, ref t, ref n));
        Assert.False(Collide.Sweep(Shape.Line(new Vec(0, 100), new Vec(50, 100)), new Vec(100, 0), new Vec(0, 200), 7, ref t, ref n));
    }

    [Fact]
    public void Three_points_make_the_arc_through_them_and_three_in_a_line_make_none()
    {
        var a = Collide.Arc(new Vec(0, 0), new Vec(50, -50), new Vec(100, 0))!.Value;
        Assert.Equal(new Vec(50, 0).X, a.Centre.X, 6);
        Assert.Equal(50, a.R, 6);
        Assert.Equal(Math.PI, a.Sweep, 6);
        Assert.True(Collide.OnArc(-Math.PI / 2, a.Start, a.Sweep));
        Assert.False(Collide.OnArc(Math.PI / 2, a.Start, a.Sweep));
        Assert.Null(Collide.Arc(new Vec(0, 0), new Vec(50, 0), new Vec(100, 0)));
    }

    // ───────────────────────────── never through a wall ─────────────────────────────

    [Fact]
    public void A_ball_fired_round_a_closed_shape_at_top_speed_never_gets_out()
    {
        foreach (int angle in new[] { 7, 31, 58, 83, 122, 167, 211, 263, 301, 344 })
        {
            var box = Lab.Walls(PartKind.Shape, (60, 100), (340, 100), (340, 500), (60, 500));
            var w = Lab.World(Lab.Table(box), p => { p.Elasticity = 10; p.Gravity = 1; });
            var v = Vec.Polar(angle * Math.PI / 180, Sim.MaxSpeed);
            var b = Lab.Ball(w, 200, 300, v.X, v.Y);
            Lab.Run(w, 3, each: () =>
            {
                Assert.InRange(b.P.X, 60 + Field.BallR - 0.01, 340 - Field.BallR + 0.01);
                Assert.InRange(b.P.Y, 100 + Field.BallR - 0.01, 500 - Field.BallR + 0.01);
            });
            Assert.True(w.Hits > 3);
        }
    }

    [Fact]
    public void A_ball_inside_a_ring_of_two_curved_walls_never_gets_out()
    {
        var top = Lab.Walls(PartKind.Curve, (100, 300), (200, 200), (300, 300));
        var bottom = Lab.Walls(PartKind.Curve, (100, 300), (200, 400), (300, 300));
        foreach (int angle in new[] { 0, 45, 100, 190, 260, 333 })
        {
            var w = Lab.World(Lab.Table(top.Copy(), bottom.Copy()), p => p.Elasticity = 10);
            var v = Vec.Polar(angle * Math.PI / 180, Sim.MaxSpeed);
            var b = Lab.Ball(w, 200, 300, v.X, v.Y);
            Lab.Run(w, 2, each: () => Assert.True(Vec.Distance(b.P, new Vec(200, 300)) <= 100 - Field.BallR + 0.01));
        }
    }

    [Fact]
    public void A_ball_fired_at_a_thin_wall_from_every_side_never_comes_through()
    {
        var a = new Vec(100, 200);
        var e = new Vec(300, 400);
        var u = (e - a).Unit;
        foreach (int side in new[] { 1, -1 })
        for (int k = 0; k < 24; k++)
        {
            var w = Lab.World(Lab.Wall((a.X, a.Y), (e.X, e.Y)));
            var aim = a + u * (90 + k * 4);
            var from = aim + u.Perp * (60 * side) + u * ((k % 5 - 2) * 20);
            var v = (aim - from).Unit * Sim.MaxSpeed;
            var b = Lab.Ball(w, from.X, from.Y, v.X, v.Y);
            Lab.Run(w, 0.06, each: () =>
            {
                double along = (b.P - a).Dot(u);
                if (along > 0 && along < (e - a).Length) Assert.True((b.P - a).Dot(u.Perp) * side > 0, $"through the wall from side {side}, shot {k}");
            });
        }
    }

    [Fact]
    public void A_ball_fired_down_onto_a_flipper_at_top_speed_bounces_off_it_whether_it_is_down_or_swinging_up()
    {
        foreach (bool held in new[] { false, true })
        foreach (double x in new[] { 140.0, 152, 165, 176 })
        {
            var w = Lab.World(Lab.Make(PartKind.FlipperLeft, 128, 652));
            var b = Lab.Ball(w, x, 560, 0, Sim.MaxSpeed);
            var flipper = w.Table.Parts[0];
            bool bounced = false;
            Lab.Run(w, 0.25, new Controls(Left: held), () =>
            {
                bounced |= b.V.Y < 0 || Math.Abs(b.V.X) > 500;
                // Above the bat's middle line, wherever the bat is now.
                double angle = w.States[0].Angle;
                var d = (b.P - flipper.At).Rotate(-angle);
                if (d.X > 0 && d.X < Parts.FlipperLength) Assert.True(d.Y < 0, $"through the flipper at x {x}, held {held}");
            });
            Assert.True(bounced, "it bounced");
        }
    }

    [Fact]
    public void A_flipper_bats_a_resting_ball_up_the_table()
    {
        var w = Lab.World(Lab.Make(PartKind.FlipperLeft, 128, 652));
        var b = Lab.Ball(w, 166, 655);
        Lab.Run(w, 0.15);
        Assert.InRange(b.P.Y, 600, 700);
        double before = b.P.Y;
        Lab.Run(w, 0.05, new Controls(Left: true));
        Assert.True(b.V.Y < -600, $"launched at {b.V.Y:0}");
        Assert.Contains(Sfx.Flipper, w.Heard);
        Lab.Run(w, 0.3, new Controls(Left: true));
        Assert.True(b.P.Y < before - 150, "up the table");
    }

    [Fact]
    public void The_right_flipper_swings_the_other_way_and_bats_up_too()
    {
        var w = Lab.World(Lab.Make(PartKind.FlipperRight, 264, 652));
        var b = Lab.Ball(w, 226, 655);
        Lab.Run(w, 0.15);
        Lab.Run(w, 0.05, new Controls(Right: true));
        Assert.True(b.V.Y < -600);
        Assert.True(w.States[0].Angle > Sim.Rest(w.Table.Parts[0]).Down);
    }

    // ───────────────────────────── the knobs ─────────────────────────────

    [Fact]
    public void A_ball_falls_as_the_gravity_knob_says_and_faster_the_steeper_the_table()
    {
        double Fall(int gravity)
        {
            var w = Lab.World(Lab.Table(), p => p.Gravity = gravity);
            var b = Lab.Ball(w, 200, 100);
            Lab.Run(w, 0.5);
            return b.P.Y - 100;
        }
        double low = Fall(2), mid = Fall(5), high = Fall(9);
        Assert.True(low < mid && mid < high);
        Assert.Equal(0.5 * 800 * 0.25, mid, 2.5);
        Assert.Equal(0.5 * (300 + 9 * 100) * 0.25, high, 3.5);
    }

    [Fact]
    public void The_speed_knob_runs_time_faster_or_slower()
    {
        double Fall(int speed)
        {
            var w = Lab.World(Lab.Table(), p => p.Speed = speed);
            var b = Lab.Ball(w, 200, 100);
            Lab.Run(w, 0.4);
            return b.P.Y - 100;
        }
        Assert.True(Fall(2) < Fall(5));
        Assert.True(Fall(5) < Fall(10));
    }

    [Fact]
    public void A_dropped_ball_bounces_back_about_as_high_as_the_elasticity_says_and_never_higher_than_it_fell()
    {
        double previous = 0;
        foreach (int elasticity in new[] { 2, 5, 9 })
        {
            var w = Lab.World(Lab.Table(Lab.Wall((40, 600), (360, 600))), p => p.Elasticity = elasticity);
            var b = Lab.Ball(w, 200, 300);
            double fell = 600 - Field.BallR - 300, top = double.MaxValue;
            int stage = 0;
            Lab.Run(w, 2.5, each: () =>
            {
                // Falling, then rising from the first bounce (the top of that rise is what counts), then done.
                if (stage == 0 && b.V.Y < 0) stage = 1;
                if (stage == 1 && b.P.Y < top) top = b.P.Y;
                if (stage == 1 && b.V.Y > 0) stage = 2;
            });
            double rose = 600 - Field.BallR - top, e = w.Table.Physics.Bounce;
            Assert.True(rose < fell, "no energy from nowhere");
            Assert.InRange(rose, e * e * fell * 0.85, e * e * fell * 1.15);
            Assert.True(rose > previous);
            previous = rose;
        }
    }

    [Fact]
    public void Off_walls_alone_a_ball_never_gains_energy()
    {
        var box = Lab.Walls(PartKind.Shape, (60, 100), (340, 160), (300, 520), (80, 480));
        var w = Lab.World(Lab.Table(box, Lab.Make(PartKind.Post, 200, 300)), p => p.Elasticity = 10);
        var b = Lab.Ball(w, 200, 200, 900, 400);
        double g = w.Table.Physics.G;
        double Energy() => b.V.LengthSquared / 2 + g * (1000 - b.P.Y);
        double start = Energy();
        Lab.Run(w, 6, each: () => Assert.True(Energy() <= start * 1.01));
        Assert.True(Energy() < start);
    }

    [Fact]
    public void A_bumper_kicks_as_hard_as_the_kick_knob_says()
    {
        double Kick(int kick)
        {
            var w = Lab.World(Lab.Table(Lab.Make(PartKind.Bumper, 200, 300)), p => p.Kick = kick);
            var b = Lab.Ball(w, 200, 250);
            for (int i = 0; i < 60 && w.States[0].Flash == 0; i++) Sim.Step(w, default);
            Assert.Equal(100, w.Score);
            return b.V.Length;
        }
        Assert.True(Kick(2) < Kick(5) && Kick(5) < Kick(9));
        Assert.True(Kick(5) >= new Physics().KickSpeed * 0.9);
    }

    [Fact]
    public void Two_balls_bounce_off_each_other()
    {
        var w = Lab.World(Lab.Table(), p => p.Gravity = 1);
        var a = Lab.Ball(w, 150, 300, 400, 0);
        var b = Lab.Ball(w, 250, 300, -400, 0);
        Lab.Run(w, 0.25, each: () => Assert.True(Vec.Distance(a.P, b.P) > 2 * Field.BallR - 1));
        Assert.True(a.V.X < 0 && b.V.X > 0);
    }

    [Fact]
    public void The_plunger_shoots_the_ball_up_the_lane_and_round_into_the_table()
    {
        var w = new World();
        Sim.Start(w, Samples.Blank());
        var b = w.Balls[0];
        Lab.Run(w, 0.5);
        Assert.True(World.OnPlunger(b));
        Lab.Run(w, 1.0, new Controls(Plunge: true));
        Assert.Equal(1, w.Pull);
        Lab.Run(w, 1.0 / 60);
        Assert.Contains(Sfx.Launch, w.Heard);
        Assert.True(b.V.Y < -1500);
        bool inTable = false;
        Lab.Run(w, 1.5, each: () => inTable |= w.Balls.Count > 0 && w.Balls[0].P.X < Field.LaneX - 20);
        Assert.True(inTable, "round the top and into the table");
    }

    [Fact]
    public void A_gentle_pull_does_not_reach_the_top_and_the_ball_comes_back_to_the_plunger()
    {
        var w = new World();
        Sim.Start(w, Samples.Blank());
        var b = w.Balls[0];
        Lab.Run(w, 0.05, new Controls(Plunge: true));
        Lab.Run(w, 3);
        Assert.True(World.OnPlunger(b));
    }

    [Fact]
    public void A_ball_coming_back_down_the_shooter_lane_is_stopped_by_the_gate()
    {
        var w = Lab.World(Lab.Table());
        var b = Lab.Ball(w, 406, 120, 0, 300);
        Lab.Run(w, 1.5);
        Assert.True(b.P.X < Field.LaneX || b.P.Y < Field.LaneTop, "turned out of the lane at its top");
    }

    [Fact]
    public void A_ball_that_has_come_to_rest_where_nothing_moves_it_is_kicked_free()
    {
        var w = Lab.World(Lab.Wall((150, 400), (250, 400)));
        var b = Lab.Ball(w, 200, 392);
        Lab.Run(w, Sim.SearchSeconds + 0.5);
        Assert.True(b.P.Y < 380 || b.V.Length > 50);
    }
}
