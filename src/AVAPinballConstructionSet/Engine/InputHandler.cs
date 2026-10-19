using Avalonia.Input;

namespace AVAPinballConstructionSet.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls in play: the flippers, the plunger and the nudge. (The mouse,
/// which builds the table, goes straight to the engine's editor.)
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    /// <summary>Let go of everything (the window lost the focus).</summary>
    public void Clear() => _held.Clear();

    public void Update() => _pad.Update();

    /// <summary>
    /// Left Shift or Z flips the left flipper, Right Shift or / the right; Space or Down pulls the plunger back (let
    /// go to shoot); Left and Right nudge the table that way, and Up nudges it up the table (y runs down the screen,
    /// so that's -1). On a gamepad: the shoulder buttons or triggers flip, A or down on the D-pad plunges, and the
    /// D-pad's left, right and up (or X) nudge.
    /// </summary>
    public Controls Player
    {
        get
        {
            var pad = _pad;
            bool left = Any(Key.LeftShift, Key.Z) || pad.LeftBumper || pad.LeftTrigger > 0.4f;
            bool right = Any(Key.RightShift, Key.OemQuestion, Key.Divide) || pad.RightBumper || pad.RightTrigger > 0.4f;
            bool plunge = Any(Key.Space, Key.Down) || pad.ButtonA || pad.DPadDown || pad.LeftStickY > 0.6f;
            int nx = (Any(Key.Right) || pad.DPadRight ? 1 : 0) - (Any(Key.Left) || pad.DPadLeft ? 1 : 0);
            int ny = Any(Key.Up) || pad.DPadUp || pad.ButtonX ? -1 : 0;
            return new Controls(left, right, plunge, nx, ny);
        }
    }

    /// <summary>Enter.</summary>
    public bool Start => Any(Key.Enter) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
