# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers using the construction set, playing
and the controls; this file covers how the code is put together and the rules that keep it working.

## What this is

A pinball construction set in the style of Bill Budge's 1983 Pinball Construction Set, in C# on .NET 10 and
Avalonia 12, drawn through Avalonia's `DrawingContext`. Build a table from a parts box (flippers, bumpers, kickers, targets, spinners,
magnets, holes, walls), wire parts into rules, tune the physics, save it, and play it with up to four players. One
self-contained executable per platform (Windows, macOS and Linux, x64 and ARM). The graphics are drawn in code and
the sounds are synthesised by `tools/make_sounds.py`.

## Commands

```bash
dotnet build AVAPinballConstructionSet.slnx       # the SDK version is in global.json (10.0.400 or later)
dotnet test AVAPinballConstructionSet.slnx        # every test runs headless: no window opens
dotnet run --project src/AVAPinballConstructionSet   # play it; --no-audio for silence
PINBALL_SHOTS=$PWD/Images dotnet test tests/AVAPinballConstructionSet.Tests --filter ScreenshotTests   # README pictures
PINBALL_PROBE=1 PINBALL_GAMES=3 dotnet test tests/AVAPinballConstructionSet.Tests --filter PilotProbe \
  --logger "console;verbosity=detailed"           # how the computer scores on each sample (=lazy: never flips)
python3 tools/make_sounds.py                      # rebuild src/AVAPinballConstructionSet/Sounds (standard-library Python only)
```

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVAPinballConstructionSet/Engine/`: the program, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, demonstration, editing, play, pause, game over, initials, high scores), the
    `Sfx` list, saving and loading tables, and the fixed-step loop.
  - `Table.cs` has the measures (`Field`), the parts, rules and physics knobs, the `Table` itself, the part
    catalogue (`Parts`) and `TableFile` (JSON, `.pinball.json`).
  - `Geometry.cs` is `Vec`, the shapes (segment, circle, arc) and their surfaces, and the sweep tests the ball
    uses.
  - `World.cs` holds the state of a game (balls, part states, scores, players), `Controls` and the `Dice`
    generator.
  - `Sim.cs` steps the world: each 1/60 s step is cut into sub-steps, and the ball moves to the first thing in its
    way and bounces, so it can never pass through a wall or flipper.
  - `Editor.cs` is the construction set: the tools (hand, wall, shape, curve, paint, magnifier, wire), the tabs,
    and undo.
  - `Samples.cs` builds the five sample tables and the blank one.
  - `Pilot.cs` is the computer at the flippers, used by the demonstration and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepads) produce `Controls`; the mouse goes from
    `GameCanvas` straight to the `Editor`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVAPinballConstructionSet/Views/`: `GameCanvas` (split over partial files) draws a fixed 1024 x 768 picture
  scaled to the window, with the table on the left and the parts box, tools and panels (`GameCanvas.Panel.cs`) or
  the backglass on the right, and runs the 60 Hz timer. Ctrl+S saves a screenshot to the Pictures folder.
- `src/AVAPinballConstructionSet/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `tests/AVAPinballConstructionSet.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`. `Lab.cs` builds small test
  tables and places balls exactly; the other files cover physics, parts, rules, the editor, table files, the game
  and the real window driven by mouse and keys (`UiTests`).

## Rules that keep it working

- **Fixed steps.** `GameEngine` advances in fixed 1/60 s steps (`Field.Step`) from an accumulator, independent of
  the frame rate, so tests run deterministically. Draw random numbers from the world's seeded `Dice` (`w.Dice`;
  the `Pilot` has its own), never from `Random.Shared`.
- **No tunnelling.** Keep the swept, sub-stepped collision in `Sim`; `PhysicsTests` check the ball never passes
  through a wall or flipper however fast it goes.
- **Engine first, drawing second.** Gameplay and editing belong in `Engine/`, where tests can reach them without a
  window. `Views/` reads the engine, draws it and forwards clicks.
- **Input goes through `InputHandler`.** Table coordinates run down the screen, so a nudge up the table is
  `NudgeY = -1`. Test new controls through `InputHandler`, not by setting `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock, and Shift works the flippers. A test in `GameTests` covers this.
- **Table files.** Changes to `Table`, `Part`, `Rule` or `Physics` change the saved JSON. Files carry a
  `TableFile.Version` (now 1), and a file newer than that is refused. Extend `TableFileTests` with the change.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores (one file per table) and saved tables (`tables/`) live in
  the app-data folder (`AVAPinballConstructionSet`); tests give the engine a fresh folder under the system temp
  path (`Lab.TempFolder()`) and run with `Silent = true`.

## Making a change

- Add or update tests with every gameplay or editor change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, parts, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release; either then asks the portfolio site to rebuild.
