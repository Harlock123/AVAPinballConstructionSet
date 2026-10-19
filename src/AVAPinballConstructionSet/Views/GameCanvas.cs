using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using AVAPinballConstructionSet.Engine;

namespace AVAPinballConstructionSet.Views;

/// <summary>
/// The screen: a fixed 1024×768 picture scaled to fit the window: the table down the left, and down the right the
/// parts box, the tools and the panels while building, or the backglass with the scores in play. Runs the engine
/// from a 60 Hz timer, and turns the mouse and the keys into the engine's calls.
/// </summary>
public sealed partial class GameCanvas : Control
{
    public const double ScreenWidth = 1024;
    public const double ScreenHeight = 768;

    private readonly DispatcherTimer? _timer;
    private DateTime _last = DateTime.Now;
    private bool _padPausePrev, _padBackPrev, _padUpPrev, _padDownPrev, _padLeftPrev, _padRightPrev, _padAcceptPrev, _padDonePrev;
    private double _clock;
    private readonly List<(Rect R, Action Do)> _hits = [];
    private Editor.Knob? _knob;
    private bool _pressing;
    private Point _mouse = new(-1, -1);

    public GameEngine Engine { get; }
    public SoundManager Sound { get; }
    public InputHandler Input { get; } = new();

    public GameCanvas(bool silent = false) : this(new GameEngine(), new SoundManager(silent), startTimer: true) { }

    /// <summary>For tests: an engine of your own, and no timer (call <see cref="Tick"/>).</summary>
    public GameCanvas(GameEngine engine, SoundManager sound, bool startTimer)
    {
        Engine = engine;
        Sound = sound;
        Engine.Sound += Sound.Play;
        Focusable = true;
        ClipToBounds = true;
        if (startTimer)
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _timer.Tick += (_, _) => Tick();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer?.Start();
        Focus();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
        Sound.Dispose();
        Input.Dispose();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        Input.Clear();
    }

    /// <summary>One frame: read the controls, run the game, redraw.</summary>
    public void Tick()
    {
        var now = DateTime.Now;
        double dt = Math.Min(0.1, (now - _last).TotalSeconds);
        _last = now;
        _clock += dt;
        Input.Update();
        HandlePad();
        Engine.Update(dt, Input.Player, Input.Start && Engine.Renaming == null);
        InvalidateVisual();
    }

    /// <summary>For tests: moves the drawing clock (blinking lamps, the spinning flaps) without running the game.</summary>
    public void Advance(double dt)
    {
        _clock += dt;
        InvalidateVisual();
    }

    private void HandlePad()
    {
        bool Edge(bool now, ref bool prev) { bool e = now && !prev; prev = now; return e; }
        if (Edge(Input.PadPause, ref _padPausePrev))
        {
            if (Engine.State == GameState.Playing && Engine.World.BallNo > 1) Engine.Pause();
            else if (Engine.State == GameState.Paused) Engine.Resume();
        }
        if (Edge(Input.PadBack, ref _padBackPrev))
        {
            if (Engine.State == GameState.Paused) Engine.OpenEditor();
            else if (Engine.State == GameState.Playing) Engine.Pause();
        }
        if (Engine.State == GameState.EnterInitials && Engine.Initials is { } entry)
        {
            if (Edge(Input.PadUp, ref _padUpPrev)) entry.Cycle(1);
            if (Edge(Input.PadDown, ref _padDownPrev)) entry.Cycle(-1);
            if (Edge(Input.PadLeft, ref _padLeftPrev)) entry.Back();
            if (Edge(Input.PadRight, ref _padRightPrev)) entry.Move(1);
            if (Edge(Input.PadAccept, ref _padAcceptPrev)) entry.Advance();
            if (Edge(Input.PadDone, ref _padDonePrev)) entry.Finish();
        }
    }

    // ───────────────────────────── keys ─────────────────────────────

    private static readonly Dictionary<Key, Tool> ToolKeys = new()
    {
        [Key.H] = Tool.Hand, [Key.W] = Tool.Wall, [Key.S] = Tool.Shape, [Key.C] = Tool.Curve, [Key.P] = Tool.Paint,
        [Key.G] = Tool.Magnify, [Key.L] = Tool.Wire,
    };

    public static string KeyFor(Tool t) => ToolKeys.FirstOrDefault(kv => kv.Value == t).Key.ToString();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var g = Engine;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        if (e.Key == Key.S && ctrl)
        {
            SaveScreenshot(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), $"AVAPinballConstructionSet_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png"));
            return;
        }
        // While entering initials every key belongs to the entry; while renaming the table, to the name.
        if (g.State == GameState.EnterInitials && g.Initials is { } entry)
        {
            InitialsKey(entry, e.Key);
            return;
        }
        if (g.Renaming is { } name)
        {
            RenameKey(name, e.Key);
            return;
        }
        if (e.Key == Key.M)
        {
            Sound.IsMuted = !Sound.IsMuted;
            return;
        }
        switch (g.State)
        {
            case GameState.Title or GameState.Attract:
                if (e.Key == Key.Escape)
                {
                    if (g.State == GameState.Attract) g.ReturnToTitle();
                    else (TopLevel.GetTopLevel(this) as Window)?.Close();
                    return;
                }
                break;
            case GameState.Editing:
                if (EditorKey(e.Key, ctrl, e.KeyModifiers.HasFlag(KeyModifiers.Shift))) return;
                break;
            case GameState.Playing:
                if (e.Key is Key.Escape or Key.P or Key.Pause) { g.Pause(); return; }
                break;
            case GameState.Paused:
                if (e.Key == Key.Escape) g.OpenEditor();
                else g.Resume();
                return;
            case GameState.HighScores or GameState.GameOver:
                if (e.Key == Key.Escape) { g.OpenEditor(); return; }
                break;
        }
        Input.OnKeyDown(e.Key);
    }

    /// <summary>The construction set's keys; true if the key was one of them.</summary>
    private bool EditorKey(Key key, bool ctrl, bool shift)
    {
        var ed = Engine.Editor;
        if (ctrl && key == Key.Z) { ed.Undo(); return true; }
        if (ctrl) return false;
        if (ToolKeys.TryGetValue(key, out var tool)) { ed.SetTool(tool); return true; }
        switch (key)
        {
            case Key.Escape:
                if (ed.Draft != null) ed.CancelRule();
                else if (ed.ShapePoints.Count > 0 || ed.CurveEnds != null || ed.Carried != null) ed.Cancel();
                else if (ed.Zoom > 1) ed.RightClick();
                else Engine.ReturnToTitle();
                return true;
            case Key.Delete or Key.Back:
                ed.DeleteSelected();
                return true;
            case Key.R:
                ed.Rotate(ed.Selected, shift ? -15 : 15);
                return true;
            case Key.OemPlus or Key.Add:
                ed.StepScore(ed.Selected, 1);
                return true;
            case Key.OemMinus or Key.Subtract:
                ed.StepScore(ed.Selected, -1);
                return true;
            case Key.Tab:
                ed.Tab = (Tab)(((int)ed.Tab + (shift ? 3 : 1)) % 4);
                return true;
            case Key.Enter:
                // A shape is finished with Enter; otherwise Enter plays the table (the engine sees it as start).
                if (ed.Tool == Tool.Shape && ed.ShapePoints.Count >= 3) { ed.RightClick(); return true; }
                if (ed.Draft != null) { ed.FinishRule(); return true; }
                return false;
        }
        return false;
    }

    private void RenameKey(string name, Key key)
    {
        switch (key)
        {
            case Key.Enter: Engine.FinishRename(); return;
            case Key.Escape: Engine.Renaming = null; return;
            case Key.Back: if (name.Length > 0) Engine.Renaming = name[..^1]; return;
        }
        char? c = key switch
        {
            >= Key.A and <= Key.Z => (char)('A' + (key - Key.A)),
            >= Key.D0 and <= Key.D9 => (char)('0' + (key - Key.D0)),
            Key.Space => ' ',
            Key.OemMinus => '-',
            _ => null,
        };
        if (c is { } ch && name.Length < 18) Engine.Renaming = name + ch;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        Input.OnKeyUp(e.Key);
        e.Handled = true;
    }

    private static void InitialsKey(InitialsEntry entry, Key key)
    {
        switch (key)
        {
            case Key.Up: entry.Cycle(1); break;
            case Key.Down: entry.Cycle(-1); break;
            case Key.Left or Key.Back: entry.Back(); break;
            case Key.Right: entry.Move(1); break;
            case Key.Space: entry.Advance(); break;
            case Key.Enter: entry.Finish(); break;
            case Key.OemPeriod or Key.Decimal: entry.Type('.'); break;
            case >= Key.A and <= Key.Z: entry.Type((char)('A' + (key - Key.A))); break;
            case >= Key.D0 and <= Key.D9: entry.Type((char)('0' + (key - Key.D0))); break;
            case >= Key.NumPad0 and <= Key.NumPad9: entry.Type((char)('0' + (key - Key.NumPad0))); break;
        }
    }

    // ───────────────────────────── the mouse ─────────────────────────────

    /// <summary>A point in the window to the fixed screen.</summary>
    private Point ToScreen(Point p)
    {
        var (s, ox, oy) = Fit();
        return s <= 0 ? new Point(-1, -1) : new Point((p.X - ox) / s, (p.Y - oy) / s);
    }

    public static readonly Rect TableRect = new(Field.TableLeft, Field.TableTop, Field.TableW, Field.TableH);

    /// <summary>The part of the table in view: its top-left corner and how much it's magnified.</summary>
    public (Vec Origin, double Zoom) View()
    {
        var ed = Engine.Editor;
        if (Engine.State != GameState.Editing || ed.Zoom <= 1) return (default, 1);
        double z = ed.Zoom, w = Field.TableW / z, h = Field.TableH / z;
        double x = Math.Clamp(ed.ZoomCentre.X - w / 2, 0, Field.TableW - w), y = Math.Clamp(ed.ZoomCentre.Y - h / 2, 0, Field.TableH - h);
        return (new Vec(x, y), z);
    }

    /// <summary>A point on the screen to the table.</summary>
    public Vec ToTable(Point at)
    {
        var (o, z) = View();
        return new Vec(o.X + (at.X - TableRect.X) / z, o.Y + (at.Y - TableRect.Y) / z);
    }

    public Point FromTable(Vec p)
    {
        var (o, z) = View();
        return new Point(TableRect.X + (p.X - o.X) * z, TableRect.Y + (p.Y - o.Y) * z);
    }

    /// <summary>The button (or other place that answers a click) under a point, topmost first.</summary>
    private Action? HitAt(Point at)
    {
        for (int i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].R.Contains(at)) return _hits[i].Do;
        return null;
    }

    /// <summary>The left (or right) button down at a point on the screen. For tests too.</summary>
    public void Press(Point at, bool right = false)
    {
        var g = Engine;
        _mouse = at;
        switch (g.State)
        {
            case GameState.Title or GameState.Attract:
                g.OpenEditor();
                return;
            case GameState.HighScores:
                g.OpenEditor();
                return;
            case GameState.Paused:
                g.Resume();
                return;
            case GameState.Editing:
                break;
            default:
                return;
        }
        if (g.Renaming != null && !NameRect.Contains(at)) g.FinishRename();
        var ed = g.Editor;
        if (right)
        {
            if (TableRect.Contains(at)) ed.RightClick();
            return;
        }
        if (HitAt(at) is { } act) { act(); return; }
        if (TableRect.Contains(at))
        {
            ed.Press(ToTable(at));
            _pressing = true;
        }
    }

    /// <summary>The mouse moved to a point on the screen (a button held or not).</summary>
    public void PointTo(Point at)
    {
        _mouse = at;
        if (Engine.State != GameState.Editing) return;
        if (_knob is { } k) { SetKnob(k, at); return; }
        var ed = Engine.Editor;
        if (_pressing || ed.Carried != null || ed.CurveEnds != null) ed.Drag(ToTable(at));
    }

    /// <summary>The left button up at a point on the screen.</summary>
    public void Lift(Point at)
    {
        _mouse = at;
        _knob = null;
        bool was = _pressing;
        _pressing = false;
        if (Engine.State != GameState.Editing) return;
        var ed = Engine.Editor;
        if (was || ed.Carried != null) ed.Release(ToTable(at), overBox: !TableRect.Contains(at));
    }

    /// <summary>A click (down and up) at a point on the screen.</summary>
    public void Click(Point at, bool right = false)
    {
        Press(at, right);
        if (!right) Lift(at);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        PointTo(ToScreen(e.GetPosition(this)));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var props = e.GetCurrentPoint(this).Properties;
        Press(ToScreen(e.GetPosition(this)), right: props.IsRightButtonPressed);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Left) Lift(ToScreen(e.GetPosition(this)));
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _mouse = new Point(-1, -1);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Engine.State != GameState.Editing) return;
        var ed = Engine.Editor;
        var at = ToScreen(e.GetPosition(this));
        if (ed.Selected >= 0 && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) ed.Rotate(ed.Selected, e.Delta.Y > 0 ? -15 : 15);
        else if (TableRect.Contains(at) && (e.Delta.Y > 0) != (ed.Zoom > 1)) ed.Magnify(ToTable(at));
    }

    /// <summary>Saves what's on screen as a PNG.</summary>
    public void SaveScreenshot(string path)
    {
        var size = new PixelSize((int)Math.Max(1, Bounds.Width), (int)Math.Max(1, Bounds.Height));
        using var bitmap = new RenderTargetBitmap(size);
        bitmap.Render(this);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }

    /// <summary>How the fixed screen fits the window: scale and letterbox offsets.</summary>
    private (double Scale, double X, double Y) Fit()
    {
        double s = Math.Min(Bounds.Width / ScreenWidth, Bounds.Height / ScreenHeight);
        return (s, (Bounds.Width - ScreenWidth * s) / 2, (Bounds.Height - ScreenHeight * s) / 2);
    }

    public override void Render(DrawingContext dc)
    {
        dc.FillRectangle(Brushes.Black, new Rect(Bounds.Size));
        var (s, ox, oy) = Fit();
        if (s <= 0) return;
        using (dc.PushTransform(Matrix.CreateScale(s, s) * Matrix.CreateTranslation(ox, oy)))
        using (dc.PushClip(new Rect(0, 0, ScreenWidth, ScreenHeight)))
            DrawScreen(dc);
    }
}
