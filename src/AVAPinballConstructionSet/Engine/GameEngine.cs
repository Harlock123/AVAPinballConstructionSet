namespace AVAPinballConstructionSet.Engine;

public enum GameState
{
    /// <summary>The title, over the first sample table.</summary>
    Title,
    /// <summary>The demonstration: the computer plays the sample tables in turn.</summary>
    Attract,
    /// <summary>The construction set.</summary>
    Editing,
    Playing,
    Paused,
    GameOver,
    EnterInitials,
    HighScores,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Flipper, Bumper, Sling, Rollover, Drop, Standup, Spinner, Kickout, Catch, Eaten, Magnet, Rubber, Thud,
    Pull, Launch, Drain, Nudge, Danger, Tilt, ExtraBall, Multiball, Lit, Reset, BonusCount, Coin,
    Click, Pick, Place, Delete, Paint, Wire, Undo, Refused,
    Start, GameOver, HighScore, TitleTune, MusicStop,
}

/// <summary>
/// The whole program with no UI: the title and its demonstration, the construction set, a game on the table built,
/// the high scores. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed 60 Hz steps,
/// so it plays the same at any frame rate. The mouse comes in through the <see cref="Editor"/>.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 20;
    public const double AttractSeconds = 90;
    public const double HighScoreSeconds = 10;
    public const double OverSeconds = 3.5;

    private double _accumulator, _idle, _saveWait;
    private bool _startPrev, _tunePending = true;
    private int _attract, _seed = 1;
    private readonly Queue<int> _pending = new();
    /// <summary>Where the high scores lead: back to the construction set after a game, to the title after the demonstration.</summary>
    private GameState _after = GameState.Editing;

    public GameEngine(string? folder = null)
    {
        Folder = folder ?? HighScoreTable.DefaultFolder;
        Editor = new Editor(TableFile.Load(WorkingPath) ?? Samples.Make(Samples.Names[0]));
        Editor.Changed = false;
        HighScores = HighScoreTable.Load(Folder, Editor.Table.Name);
        Demo(0);
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public double StateTime { get; private set; }
    public bool Silent { get; set; }
    /// <summary>The user's folder: high scores, the table being worked on, and the saved tables.</summary>
    public string Folder { get; }
    public string TablesFolder => Path.Combine(Folder, "tables");
    public string WorkingPath => Path.Combine(Folder, "working" + TableFile.Extension);
    public Editor Editor { get; }
    public World World { get; private set; } = new();
    public Pilot Pilot { get; } = new();
    /// <summary>The high scores of the table being played (or last played).</summary>
    public HighScoreTable HighScores { get; private set; }
    public InitialsEntry? Initials { get; private set; }
    /// <summary>The player whose initials are being entered.</summary>
    public int EntryPlayer { get; private set; } = -1;
    public int NewEntryIndex { get; private set; } = -1;
    /// <summary>The name of the table being typed in the tables panel (null: not renaming).</summary>
    public string? Renaming { get; set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        Editor.Tick(dt);
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed) OpenEditor();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Sim.Step(World, Pilot.Decide(World));
                Hear();
                if (startPressed) OpenEditor();
                else if (World.Over || StateTime > AttractSeconds) ShowHighScores(-1, GameState.Title);
                break;
            case GameState.Editing:
                if (startPressed) { PlayTable(); break; }
                AutoSave(dt);
                break;
            case GameState.Playing:
                if (startPressed) Sim.AddPlayer(World);
                Sim.Step(World, c);
                Hear();
                if (World.Over)
                {
                    State = GameState.GameOver;
                    StateTime = 0;
                }
                break;
            case GameState.GameOver:
                if (StateTime > OverSeconds) NextInitials();
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, World.Scores[EntryPlayer]);
                    HighScores.Save();
                    Initials = null;
                    NewEntryIndex = place;
                    NextInitials(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) { if (_after == GameState.Editing) PlayTable(); else OpenEditor(); }
                else if (StateTime > HighScoreSeconds) { if (_after == GameState.Editing) OpenEditor(); else ReturnToTitle(); }
                break;
        }
        foreach (var s in Editor.Heard) Play(s);
        Editor.Heard.Clear();
    }

    /// <summary>The sounds of the game.</summary>
    private void Hear()
    {
        foreach (var s in World.Heard) Play(s);
        World.Heard.Clear();
    }

    /// <summary>The table being built is kept on disk as it changes, so nothing is lost when the program closes.</summary>
    private void AutoSave(double dt)
    {
        _saveWait -= dt;
        var e = Editor;
        if (!e.Changed || _saveWait > 0 || e.Carried != null || e.Corner != null || e.From != null) return;
        e.Changed = false;
        _saveWait = 1;
        try
        {
            TableFile.Save(e.Table, WorkingPath);
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            // A read-only home folder just means the table isn't kept.
        }
    }

    /// <summary>Each player who made the table's high scores, in turn, enters initials; then the table.</summary>
    private void NextInitials(int place = -1)
    {
        if (State == GameState.GameOver)
        {
            _pending.Clear();
            for (int p = 0; p < World.Players; p++) _pending.Enqueue(p);
            NewEntryIndex = -1;
        }
        while (_pending.Count > 0)
        {
            int p = _pending.Dequeue();
            if (!HighScores.Qualifies(World.Scores[p])) continue;
            EntryPlayer = p;
            State = GameState.EnterInitials;
            StateTime = 0;
            Initials = new InitialsEntry(HighScores.LastInitials);
            Play(Sfx.HighScore);
            return;
        }
        EntryPlayer = -1;
        ShowHighScores(place, GameState.Editing);
    }

    private void ShowHighScores(int place, GameState after)
    {
        if (State == GameState.Attract) HighScores = HighScoreTable.Load(Folder, World.Table.Name);
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
        _after = after;
    }

    /// <summary>The computer at the flippers, on one of the sample tables.</summary>
    private void Demo(int which)
    {
        World = new World();
        Sim.Start(World, Samples.Make(Samples.Names[which % Samples.Names.Length]), 1, ++_seed);
        Pilot.Reset(_seed);
    }

    private void StartAttract()
    {
        Demo(_attract++);
        State = GameState.Attract;
        StateTime = 0;
        Play(Sfx.MusicStop);
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Demo(0);
    }

    public void OpenEditor()
    {
        if (State is GameState.Title or GameState.Attract) Play(Sfx.MusicStop);
        State = GameState.Editing;
        StateTime = 0;
        Renaming = null;
    }

    /// <summary>A game on the table in the construction set.</summary>
    public void PlayTable()
    {
        Editor.Cancel();
        Renaming = null;
        World = new World();
        Sim.Start(World, Editor.Table, 1, ++_seed);
        HighScores = HighScoreTable.Load(Folder, Editor.Table.Name);
        NewEntryIndex = -1;
        State = GameState.Playing;
        StateTime = 0;
        Play(Sfx.MusicStop);
        Play(Sfx.Start);
    }

    public void Pause()
    {
        if (State == GameState.Playing) State = GameState.Paused;
    }

    public void Resume()
    {
        if (State == GameState.Paused) State = GameState.Playing;
    }

    // ───────────────────────────── tables on disk ─────────────────────────────

    public void OpenSample(string name)
    {
        Editor.Open(Samples.Make(name));
        Editor.Say($"{Editor.Table.Name} IS READY TO PLAY OR CHANGE");
    }

    public void OpenBlank()
    {
        Editor.Open(Samples.Blank());
        Editor.Say("A BLANK TABLE: DRAG PARTS ON FROM THE BOX");
    }

    /// <summary>Saves the table into the tables folder, under its name; returns the file, or null if it couldn't be written.</summary>
    public string? SaveTable()
    {
        string path = Path.Combine(TablesFolder, TableFile.Slug(Editor.Table.Name) + TableFile.Extension);
        try
        {
            TableFile.Save(Editor.Table, path);
            Editor.Say($"SAVED AS {Path.GetFileName(path).ToUpperInvariant()}");
            Play(Sfx.Place);
            return path;
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            Editor.Say("COULDN'T SAVE THE TABLE");
            Play(Sfx.Refused);
            return null;
        }
    }

    public bool OpenFile(string path)
    {
        if (TableFile.Load(path) is not { } t)
        {
            Editor.Say("THAT ISN'T A TABLE");
            Play(Sfx.Refused);
            return false;
        }
        Editor.Open(t);
        Editor.Say($"{t.Name} LOADED");
        return true;
    }

    public List<(string Name, string Path)> SavedTables() => TableFile.List(TablesFolder);

    public void BeginRename() => Renaming = Editor.Table.Name;

    public void FinishRename()
    {
        if (Renaming is { } n) Editor.Rename(n);
        Renaming = null;
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);
}
