namespace AVAPinballConstructionSet.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
}

/// <summary>
/// The controls in play: the two flipper buttons, the plunger (held to pull it back, let go to shoot), and a nudge
/// of the table, left or right (x) or up the table (y = -1, as y runs down the screen).
/// </summary>
public readonly record struct Controls(bool Left = false, bool Right = false, bool Plunge = false, int NudgeX = 0, int NudgeY = 0);

/// <summary>Where a game is: balls in play, the bonus being counted after one's lost, or the game over.</summary>
public enum BallPhase { Live, Bonus, Over }

public sealed class Ball
{
    public Vec P, V;
    /// <summary>The kick-out hole holding it (-1: none).</summary>
    public int Held = -1;
    /// <summary>How long it has hardly moved; a ball fed in by multiball waits this long on the plunger before it's shot.</summary>
    public double Still, Auto = -1;
    public bool Gone;
    public Ball Copy() => (Ball)MemberwiseClone();
}

/// <summary>How a part is in play: lit, down, collected for a rule, flashing from a hit, spinning, holding the ball, swung up.</summary>
public sealed class PartState
{
    public bool Lit, Down, Collected, Inside;
    public int LitValue;
    public double Flash, Cooldown, Timer, Rest;
    /// <summary>A spinner's turns and how fast it's turning; a flipper's angle and how fast it's swinging.</summary>
    public double Spin, SpinRate, Angle, Omega;
    public PartState Copy() => (PartState)MemberwiseClone();
}

/// <summary>A score floating up from where it was made.</summary>
public record struct Pop(Vec At, int Points, double T);

/// <summary>
/// Everything in a game of pinball that moves or changes, kept together so it can be copied whole: the table being
/// played, its parts' states, the balls, the players and their scores, the bonus, the tilt.
/// </summary>
public sealed class World
{
    public const int MaxPlayers = 4, MaxBallsInPlay = 3;

    public Table Table = new();
    public PartState[] States = [];
    /// <summary>What the ball can hit that never moves: built from the table once, and shared by copies.</summary>
    public List<Shape> Statics = [];
    public List<Ball> Balls = [];
    public int Players = 1, Player, BallNo = 1;
    public int[] Scores = new int[MaxPlayers], ExtraBalls = new int[MaxPlayers];
    public bool[] ExtraGiven = new bool[MaxPlayers];
    public int Bonus, Multiplier = 1;
    public double TiltMeter;
    public bool Tilted;
    public BallPhase Phase;
    public double PhaseTime;
    /// <summary>How far back the plunger is pulled (0 to 1).</summary>
    public double Pull;
    public bool LeftHeld, RightHeld;
    public int NudgeXPrev, NudgeYPrev;
    /// <summary>Balls owed by multiball, to be fed into the shooter lane.</summary>
    public int Waiting;
    public double DropReset;
    public double Clock, Shake;
    public Vec ShakeDir;
    public Dice Dice = new(1);
    public List<Sfx> Heard = [];
    public string? Message;
    public double MessageTime;
    public List<Pop> Pops = [];
    /// <summary>How many times the ball has hit something, for the attract mode's patience and the tests.</summary>
    public int Hits;

    public int Score => Scores[Player];
    public bool Over => Phase == BallPhase.Over;

    public World Copy() => new()
    {
        Table = Table.Copy(), States = States.Select(s => s.Copy()).ToArray(), Statics = Statics,
        Balls = Balls.Select(b => b.Copy()).ToList(), Players = Players, Player = Player, BallNo = BallNo,
        Scores = (int[])Scores.Clone(), ExtraBalls = (int[])ExtraBalls.Clone(), ExtraGiven = (bool[])ExtraGiven.Clone(),
        Bonus = Bonus, Multiplier = Multiplier, TiltMeter = TiltMeter, Tilted = Tilted, Phase = Phase, PhaseTime = PhaseTime,
        Pull = Pull, LeftHeld = LeftHeld, RightHeld = RightHeld, NudgeXPrev = NudgeXPrev, NudgeYPrev = NudgeYPrev,
        Waiting = Waiting, DropReset = DropReset, Clock = Clock, Shake = Shake, ShakeDir = ShakeDir, Dice = Dice,
        Message = Message, MessageTime = MessageTime, Pops = [.. Pops], Hits = Hits,
    };

    /// <summary>A ball sitting on the plunger, ready to be shot.</summary>
    public static bool OnPlunger(Ball b) => b.Held < 0 && b.P.X > Field.LaneX && b.P.Y > Field.PlungerY - 40;
}
