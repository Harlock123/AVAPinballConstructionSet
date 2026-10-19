using AVAPinballConstructionSet.Engine;

namespace AVAPinballConstructionSet.Tests;

/// <summary>
/// How the computer player gets on, table by table (set PINBALL_PROBE to run it; PINBALL_PROBE=lazy never flips,
/// to compare). PINBALL_GAMES sets how many games a table.
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        if (Environment.GetEnvironmentVariable("PINBALL_PROBE") is not { } mode) return;
        int games = int.TryParse(Environment.GetEnvironmentVariable("PINBALL_GAMES"), out int n) ? n : 3;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var name in Samples.Names)
        {
            var scores = new List<int>();
            var seconds = new List<double>();
            int flips = 0, hits = 0, stuck = 0;
            for (int game = 0; game < games; game++)
            {
                var w = new World();
                Sim.Start(w, Samples.Make(name), 1, 100 + game);
                var p = new Pilot(100 + game);
                int t = 0;
                var parts = new Dictionary<PartKind, int>();
                for (; t < 60 * 60 * 20 && !w.Over; t++)
                {
                    var c = p.Decide(w);
                    if (mode == "lazy") c = c with { Left = false, Right = false };
                    Sim.Step(w, c);
                    foreach (var b in w.Balls)
                        if (b.Still > Sim.SearchSeconds - Field.Step) stuck++;
                }
                scores.Add(w.Scores[0]);
                seconds.Add(t / 60.0);
                flips += p.Flips;
                hits += w.Hits;
            }
            output.WriteLine($"{name,-14} scores {string.Join(", ", scores.Select(s => s.ToString("N0")))}; {seconds.Average() / 3:0}s a ball; {flips / games} flips, {hits / games} hits a game; ball searches {stuck}");
        }
        output.WriteLine($"{sw.ElapsedMilliseconds} ms");
    }
}
