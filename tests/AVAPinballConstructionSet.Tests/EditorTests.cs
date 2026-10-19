using AVAPinballConstructionSet.Engine;

namespace AVAPinballConstructionSet.Tests;

/// <summary>The construction set: parts from the box, the hand, the walls, the paint, the magnifier, the wires, the knobs and undo.</summary>
public class EditorTests
{
    private static Editor Blank() => new(Samples.Blank());

    private static void Drag(Editor ed, Vec from, Vec to, bool overBox = false)
    {
        ed.Press(from);
        ed.Drag((from + to) / 2);
        ed.Drag(to);
        ed.Release(to, overBox);
    }

    [Fact]
    public void A_part_dragged_from_the_box_lands_where_it_is_let_go()
    {
        var ed = Blank();
        ed.TakeFromBox(PartKind.Bumper, new Vec(-50, 100));
        Assert.True(ed.CarriedIsNew);
        ed.Drag(new Vec(150, 220));
        ed.Release(new Vec(160, 240));
        var p = Assert.Single(ed.Table.Parts);
        Assert.Equal(PartKind.Bumper, p.Kind);
        Assert.Equal(new Vec(160, 240), p.At);
        Assert.Equal(100, p.Score);
        Assert.Equal(0, ed.Selected);
        Assert.Contains(Sfx.Place, ed.Heard);
        Assert.Null(ed.Carried);
    }

    [Fact]
    public void A_part_from_the_box_let_go_off_the_table_goes_back_in_the_box()
    {
        var ed = Blank();
        ed.TakeFromBox(PartKind.Spinner, new Vec(100, 100));
        ed.Release(new Vec(100, 100), overBox: true);
        Assert.Empty(ed.Table.Parts);
        Assert.False(ed.CanUndo);
    }

    [Fact]
    public void The_hand_picks_a_part_up_and_moves_it_and_back_in_the_box_it_is_thrown_away()
    {
        var ed = Blank();
        ed.Place(PartKind.Kickout, new Vec(100, 100));
        ed.Place(PartKind.Bumper, new Vec(200, 300));
        // Taking hold off-centre: it keeps its place under the hand.
        Drag(ed, new Vec(205, 303), new Vec(255, 403));
        Assert.Equal(new Vec(250, 400), ed.Table.Parts[1].At);
        Assert.Equal(1, ed.Selected);
        Drag(ed, new Vec(250, 400), new Vec(600, 400), overBox: true);
        Assert.Single(ed.Table.Parts);
        Assert.Contains(Sfx.Delete, ed.Heard);
        Assert.True(ed.Undo());
        Assert.Equal(2, ed.Table.Parts.Count);
        Assert.Equal(new Vec(250, 400), ed.Table.Parts[1].At);
        Assert.True(ed.Undo());
        Assert.Equal(new Vec(200, 300), ed.Table.Parts[1].At);
    }

    [Fact]
    public void A_click_without_a_move_chooses_a_part_without_changing_anything()
    {
        var ed = Blank();
        ed.Place(PartKind.Post, new Vec(100, 100));
        ed.Place(PartKind.Bumper, new Vec(300, 100));
        ed.Press(new Vec(101, 99));
        ed.Release(new Vec(101, 99));
        Assert.Equal(0, ed.Selected);
        Assert.Equal(new Vec(100, 100), ed.Table.Parts[0].At);
        ed.Undo();
        Assert.Single(ed.Table.Parts);
        // Clicking empty table chooses nothing.
        ed.Press(new Vec(200, 500));
        Assert.Equal(-1, ed.Selected);
    }

    [Fact]
    public void Parts_are_found_by_where_they_are_drawn()
    {
        var ed = Blank();
        ed.Place(PartKind.FlipperLeft, new Vec(128, 652));
        ed.DrawWall(new Vec(20, 100), new Vec(120, 200));
        ed.DrawShape([new Vec(200, 200), new Vec(300, 200), new Vec(250, 280)]);
        ed.DrawCurve(new Vec(50, 400), new Vec(100, 350), new Vec(150, 400));
        // The flipper's tip, a wall's middle, inside the shape, and on the curve.
        Assert.Equal(0, ed.HitTest(new Vec(128, 652) + Vec.Polar(Math.PI / 6, 50)));
        Assert.Equal(1, ed.HitTest(new Vec(72, 150)));
        Assert.Equal(2, ed.HitTest(new Vec(250, 230)));
        Assert.Equal(3, ed.HitTest(new Vec(100, 352)));
        Assert.Equal(-1, ed.HitTest(new Vec(350, 600)));
    }

    [Fact]
    public void The_wall_tool_draws_a_wall_with_a_drag_and_its_ends_snap_to_walls_already_there()
    {
        var ed = Blank();
        ed.SetTool(Tool.Wall);
        Drag(ed, new Vec(50, 100), new Vec(150, 150));
        var a = Assert.Single(ed.Table.Parts);
        Assert.Equal(PartKind.Wall, a.Kind);
        Assert.Equal([new Vec(50, 100), new Vec(150, 150)], a.Points);
        Assert.Equal(ed.Colour, a.Colour);
        Drag(ed, new Vec(154, 147), new Vec(250, 300));
        Assert.Equal(new Vec(150, 150), ed.Table.Parts[1].Points[0]);
        // Too short to be a wall: nothing.
        Drag(ed, new Vec(300, 300), new Vec(302, 301));
        Assert.Equal(2, ed.Table.Parts.Count);
    }

    [Fact]
    public void The_shape_tool_draws_a_solid_shape_corner_by_corner_closing_on_the_first()
    {
        var ed = Blank();
        ed.SetTool(Tool.Shape);
        foreach (var p in new[] { new Vec(100, 100), new Vec(200, 100), new Vec(150, 180) }) { ed.Press(p); ed.Release(p); }
        Assert.Equal(3, ed.ShapePoints.Count);
        Assert.Empty(ed.Table.Parts);
        ed.Press(new Vec(102, 101));
        var s = Assert.Single(ed.Table.Parts);
        Assert.Equal(PartKind.Shape, s.Kind);
        Assert.Equal(3, s.Points.Count);
        Assert.Empty(ed.ShapePoints);
        // Or with the right button.
        foreach (var p in new[] { new Vec(250, 300), new Vec(300, 300), new Vec(280, 360), new Vec(240, 350) }) { ed.Press(p); ed.Release(p); }
        ed.RightClick();
        Assert.Equal(4, ed.Table.Parts[1].Points.Count);
    }

    [Fact]
    public void The_curve_tool_drags_the_ends_then_bends_the_curve_with_the_mouse_and_a_click()
    {
        var ed = Blank();
        ed.SetTool(Tool.Curve);
        Drag(ed, new Vec(100, 300), new Vec(300, 300));
        Assert.Equal((new Vec(100, 300), new Vec(300, 300)), ed.CurveEnds);
        ed.Drag(new Vec(200, 220));
        Assert.Equal(new Vec(200, 220), ed.Bend);
        ed.Press(new Vec(200, 220));
        var c = Assert.Single(ed.Table.Parts);
        Assert.Equal(PartKind.Curve, c.Kind);
        Assert.Equal([new Vec(100, 300), new Vec(200, 220), new Vec(300, 300)], c.Points);
        Assert.Null(ed.CurveEnds);
        // It's an arc to the ball.
        var shapes = Sim.Build(ed.Table);
        Assert.Contains(shapes, s => s.Kind == ShapeKind.Arc && s.Part == 0);
    }

    [Fact]
    public void A_wall_corner_can_be_dragged_to_reshape_it()
    {
        var ed = Blank();
        ed.DrawWall(new Vec(100, 100), new Vec(200, 100));
        ed.SetTool(Tool.Hand);
        Drag(ed, new Vec(199, 101), new Vec(220, 160));
        Assert.Equal(new Vec(220, 160), ed.Table.Parts[0].Points[1]);
        Assert.Equal(new Vec(100, 100), ed.Table.Parts[0].Points[0]);
        ed.Undo();
        Assert.Equal(new Vec(200, 100), ed.Table.Parts[0].Points[1]);
    }

    [Fact]
    public void A_whole_wall_moves_with_the_hand()
    {
        var ed = Blank();
        ed.DrawShape([new Vec(100, 100), new Vec(200, 100), new Vec(150, 200)]);
        ed.SetTool(Tool.Hand);
        Drag(ed, new Vec(150, 130), new Vec(170, 160));
        Assert.Equal([new Vec(120, 130), new Vec(220, 130), new Vec(170, 230)], ed.Table.Parts[0].Points);
    }

    [Fact]
    public void The_paint_brush_colours_a_part_a_wall_or_the_table_itself()
    {
        var ed = Blank();
        ed.Place(PartKind.Bumper, new Vec(200, 300));
        ed.DrawWall(new Vec(20, 100), new Vec(120, 200));
        ed.SetTool(Tool.Paint);
        ed.Colour = Parts.Palette[4];
        ed.Press(new Vec(200, 300));
        ed.Press(new Vec(70, 150));
        ed.Press(new Vec(300, 600));
        Assert.Equal(Parts.Palette[4], ed.Table.Parts[0].Colour);
        Assert.Equal(Parts.Palette[4], ed.Table.Parts[1].Colour);
        Assert.Equal(Parts.Palette[4], ed.Table.Playfield);
        Assert.Contains(Sfx.Paint, ed.Heard);
    }

    [Fact]
    public void A_part_turns_and_its_score_steps_up_and_down_its_scale()
    {
        var ed = Blank();
        ed.Place(PartKind.Rollover, new Vec(200, 300));
        ed.Rotate(0, 15);
        ed.Rotate(0, -30);
        Assert.Equal(345, ed.Table.Parts[0].Angle);
        Assert.Equal(500, ed.Table.Parts[0].Score);
        ed.StepScore(0, 1);
        Assert.Equal(1000, ed.Table.Parts[0].Score);
        ed.StepScore(0, -1);
        ed.StepScore(0, -1);
        Assert.Equal(250, ed.Table.Parts[0].Score);
        ed.SetScore(0, 777);
        ed.StepScore(0, 1);
        Assert.Equal(1000, ed.Table.Parts[0].Score);
        for (int i = 0; i < 30; i++) ed.StepScore(0, 1);
        Assert.Equal(Editor.ScoreSteps[^1], ed.Table.Parts[0].Score);
    }

    [Fact]
    public void A_turned_wall_turns_about_its_middle()
    {
        var ed = Blank();
        ed.DrawWall(new Vec(100, 200), new Vec(200, 200));
        ed.Rotate(0, 90);
        Assert.Equal(150, ed.Table.Parts[0].Points[0].X, 6);
        Assert.Equal(150, ed.Table.Parts[0].Points[0].Y, 6);
        Assert.Equal(250, ed.Table.Parts[0].Points[1].Y, 6);
    }

    [Fact]
    public void The_magnifier_looks_closer_at_a_point_and_back_out()
    {
        var ed = Blank();
        ed.SetTool(Tool.Magnify);
        ed.Press(new Vec(100, 600));
        Assert.Equal(Editor.Magnification, ed.Zoom);
        Assert.Equal(new Vec(100, 600), ed.ZoomCentre);
        ed.Press(new Vec(100, 600));
        Assert.Equal(1, ed.Zoom);
        ed.Magnify(new Vec(10, 10));
        Assert.Equal(Editor.Magnification, ed.Zoom);
        ed.RightClick();
        Assert.Equal(1, ed.Zoom);
    }

    [Fact]
    public void Wiring_a_rule_chooses_what_sets_it_off_what_it_does_and_what_it_lights()
    {
        var ed = new Editor(Samples.Make("TARGET RANGE"));
        int rules = ed.Table.Rules.Count;
        ed.NewRule();
        Assert.Equal(Tool.Wire, ed.Tool);
        Assert.Equal(Tab.Rules, ed.Tab);
        var bumpers = ed.Table.Parts.Where(p => p.Kind == PartKind.Bumper).ToList();
        foreach (var b in bumpers) ed.Press(b.At);
        Assert.Equal(bumpers.Select(b => b.Id), ed.Draft!.Sources);
        // A wall can't be wired; a part clicked twice is out again.
        ed.Press(ed.Table.Parts.First(p => p.IsWall).Points[0]);
        Assert.Contains(Sfx.Refused, ed.Heard);
        ed.Press(bumpers[0].At);
        ed.Press(bumpers[0].At);
        Assert.Equal(2, ed.Draft.Sources.Count);
        ed.SetEffect(Outcome.Light);
        Assert.True(ed.PickingTargets);
        // Not finished until it has something to light.
        Assert.False(ed.FinishRule());
        var standup = ed.Table.Parts.First(p => p.Kind == PartKind.Standup);
        ed.Press(standup.At);
        ed.StepAmount(1);
        ed.ToggleWhen();
        Assert.Equal(When.Any, ed.Draft.When);
        Assert.True(ed.FinishRule());
        Assert.Equal(rules + 1, ed.Table.Rules.Count);
        var r = ed.Table.Rules[^1];
        Assert.Equal(Outcome.Light, r.Effect);
        Assert.Equal([standup.Id], r.Targets);
        Assert.Equal(7500, r.Amount);
        Assert.Null(ed.Draft);
        ed.DeleteRule(rules);
        Assert.Equal(rules, ed.Table.Rules.Count);
        ed.Undo();
        Assert.Equal(rules + 1, ed.Table.Rules.Count);
    }

    [Fact]
    public void A_rule_with_nothing_to_set_it_off_is_refused_and_a_cancelled_one_leaves_no_trace()
    {
        var ed = Blank();
        ed.NewRule();
        Assert.False(ed.FinishRule());
        Assert.NotNull(ed.Message);
        ed.CancelRule();
        Assert.Null(ed.Draft);
        Assert.Empty(ed.Table.Rules);
    }

    [Fact]
    public void The_knobs_balls_and_extra_ball_mark_stay_in_their_ranges_and_undo()
    {
        var ed = Blank();
        ed.Set(Editor.Knob.Gravity, 8);
        ed.Set(Editor.Knob.Speed, 0);
        ed.Set(Editor.Knob.Elasticity, 50);
        ed.Set(Editor.Knob.Kick, 3);
        Assert.Equal((8, 1, 10, 3), (ed.Table.Physics.Gravity, ed.Table.Physics.Speed, ed.Table.Physics.Elasticity, ed.Table.Physics.Kick));
        for (int i = 0; i < 9; i++) ed.StepBalls(1);
        Assert.Equal(Table.MaxBalls, ed.Table.Balls);
        for (int i = 0; i < 9; i++) ed.StepBalls(-1);
        Assert.Equal(1, ed.Table.Balls);
        ed.StepExtraBall(1);
        Assert.Equal(150000, ed.Table.ExtraBallAt);
        for (int i = 0; i < 9; i++) ed.StepExtraBall(-1);
        Assert.Equal(0, ed.Table.ExtraBallAt);
        while (ed.CanUndo) ed.Undo();
        Assert.Equal(5, ed.Table.Physics.Gravity);
        Assert.Equal(3, ed.Table.Balls);
        Assert.Equal(100000, ed.Table.ExtraBallAt);
    }

    [Fact]
    public void A_table_can_be_renamed_and_opening_another_can_be_undone()
    {
        var ed = new Editor(Samples.Make("COMET RUN"));
        ed.Rename("my  comet!");
        Assert.Equal("MY  COMET!", ed.Table.Name);
        ed.Rename("   ");
        Assert.Equal("MY  COMET!", ed.Table.Name);
        ed.Open(Samples.Blank());
        Assert.Empty(ed.Table.Parts);
        ed.Undo();
        Assert.Equal("MY  COMET!", ed.Table.Name);
        Assert.NotEmpty(ed.Table.Parts);
    }

    [Fact]
    public void Undo_with_nothing_to_undo_is_refused()
    {
        var ed = Blank();
        Assert.False(ed.Undo());
        Assert.Contains(Sfx.Refused, ed.Heard);
    }

    [Fact]
    public void Deleting_the_chosen_part_takes_it_out_of_the_rules_too()
    {
        var ed = new Editor(Samples.Make("LUCKY STAR"));
        int hole = ed.Table.Parts.FindIndex(p => p.Kind == PartKind.Kickout);
        int rules = ed.Table.Rules.Count;
        ed.Press(ed.Table.Parts[hole].At);
        ed.Release(ed.Table.Parts[hole].At);
        Assert.Equal(hole, ed.Selected);
        ed.DeleteSelected();
        Assert.Equal(rules - 1, ed.Table.Rules.Count);
        Assert.DoesNotContain(ed.Table.Parts, p => p.Kind == PartKind.Kickout);
    }
}
