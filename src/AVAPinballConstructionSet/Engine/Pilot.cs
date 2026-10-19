namespace AVAPinballConstructionSet.Engine;

/// <summary>
/// The computer at the flippers, for the demonstration and the tests. It pulls the plunger back a different way
/// each ball, and flips when a ball (where it will be in a moment) is over a flipper's bat and not flying away up
/// the table: a short flip, then a rest, so it doesn't hold the ball for ever.
/// </summary>
public sealed class Pilot
{
    public const double FlipSeconds = 0.2, RestSeconds = 0.12;

    private double _plungeFor = -1, _plunged, _leftHold, _rightHold, _leftRest, _rightRest;
    private Dice _dice;

    public Pilot(int seed = 7) => _dice = new Dice(seed);

    /// <summary>How many times it has flipped, for the probe.</summary>
    public int Flips { get; private set; }

    public void Reset(int? seed = null)
    {
        if (seed is { } s) _dice = new Dice(s);
        _plungeFor = -1;
        _plunged = _leftHold = _rightHold = _leftRest = _rightRest = 0;
    }

    public Controls Decide(World w)
    {
        if (w.Phase != BallPhase.Live) return default;
        double dt = Field.Step;
        bool plunge = false;
        if (w.Balls.Any(b => World.OnPlunger(b) && b.V.Length < 5 && b.Auto < 0))
        {
            if (_plungeFor < 0) _plungeFor = 0.6 + _dice.Next() * 0.35;
            _plunged += dt;
            plunge = _plunged < _plungeFor;
            if (!plunge) { _plungeFor = -1; _plunged = 0; }
        }
        else { _plungeFor = -1; _plunged = 0; }
        bool wantLeft = false, wantRight = false;
        foreach (var b in w.Balls)
        {
            if (b.Held >= 0) continue;
            foreach (var f in w.Table.Parts)
            {
                if (!Parts.IsFlipper(f.Kind) || !Over(f, b)) continue;
                if (f.Kind == PartKind.FlipperLeft) wantLeft = true;
                else wantRight = true;
            }
        }
        bool left = Flip(wantLeft, ref _leftHold, ref _leftRest, dt), right = Flip(wantRight, ref _rightHold, ref _rightRest, dt);
        return new Controls(left, right, plunge);
    }

    private bool Flip(bool want, ref double hold, ref double rest, double dt)
    {
        if (hold > 0)
        {
            hold -= dt;
            if (hold <= 0) rest = RestSeconds;
            return hold > 0;
        }
        rest -= dt;
        if (!want || rest > 0) return false;
        hold = FlipSeconds;
        Flips++;
        return true;
    }

    /// <summary>A ball over a flipper's bat, coming down onto it or rolling along it.</summary>
    public static bool Over(Part f, Ball b)
    {
        var (rest, _) = Sim.Rest(f);
        var p = b.P + b.V * 0.03;
        var d = (p - f.At).Rotate(-rest);
        // Along the bat, and on the side it swings to.
        double up = f.Kind == PartKind.FlipperLeft ? -d.Y : d.Y;
        return d.X > 6 && d.X < Parts.FlipperLength + 8 && up > -4 && up < 30 && b.V.Y > -120;
    }
}
