using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AVAPinballConstructionSet.Engine;

public record HighScoreEntry(string Initials, int Score);

/// <summary>
/// A pinball table's top ten, kept in the user's application-data folder (one file for each table, by its name).
/// A fresh one starts with modest made-up scores, the way the machines shipped, so there is something to beat.
/// </summary>
public class HighScoreTable
{
    public const int Size = 10;
    public const int InitialsLength = 3;

    public static readonly string DefaultFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AVAPinballConstructionSet");

    private readonly string _folder, _file;
    private string TablePath => Path.Combine(_folder, _file);

    /// <summary>Where the scores are kept.</summary>
    public string Folder => _folder;

    /// <summary>A pinball table's scores, kept in <paramref name="folder"/> (the user's application-data folder by default).</summary>
    public HighScoreTable(string? folder = null, string? tableName = null)
    {
        _folder = folder ?? DefaultFolder;
        _file = $"scores-{TableFile.Slug(tableName ?? "table")}.txt";
    }

    private static readonly string[] DefaultInitials = { "BUD", "AVA", "EAS", "PIN", "LOL", "TIL", "KIK", "POP", "ROL", "FLP" };

    private readonly List<HighScoreEntry> _entries = new();

    public IReadOnlyList<HighScoreEntry> Entries => _entries;
    public int TopScore => _entries.Count > 0 ? _entries[0].Score : 0;

    /// <summary>The initials entered last time, offered as the starting point next time.</summary>
    public string LastInitials { get; private set; } = "AAA";

    public static HighScoreTable Load(string? folder = null, string? tableName = null)
    {
        var table = new HighScoreTable(folder, tableName);
        try
        {
            if (File.Exists(table.TablePath))
            {
                foreach (var line in File.ReadAllLines(table.TablePath))
                {
                    if (line.StartsWith("#last "))
                    {
                        table.LastInitials = Clean(line[6..]);
                        continue;
                    }

                    var parts = line.Split('|');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int score))
                        table._entries.Add(new HighScoreEntry(Clean(parts[0]), score));
                }
            }
            else
            {
                table.FillDefaults();
            }
        }
        catch
        {
            // An unreadable table just starts fresh.
            table._entries.Clear();
            table.FillDefaults();
        }

        table.Sort();
        if (table._entries.Count > Size) table._entries.RemoveRange(Size, table._entries.Count - Size);
        return table;
    }

    private void FillDefaults()
    {
        for (int i = 0; i < Size; i++)
            _entries.Add(new HighScoreEntry(DefaultInitials[i], 60000 - i * 5000));
    }

    public bool Qualifies(int score) =>
        score > 0 && (_entries.Count < Size || score > _entries[^1].Score);

    /// <summary>Adds an entry and returns its place (0 = top), or -1 if it didn't make the table.</summary>
    public int Insert(string initials, int score)
    {
        if (!Qualifies(score)) return -1;

        // A new score goes above an equal older one: it was set more recently.
        int index = _entries.FindIndex(e => score >= e.Score);
        if (index < 0) index = _entries.Count;

        _entries.Insert(index, new HighScoreEntry(Clean(initials), score));
        if (_entries.Count > Size)
            _entries.RemoveRange(Size, _entries.Count - Size);

        LastInitials = Clean(initials);
        return index;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(_folder);
            var lines = new List<string> { $"#last {LastInitials}" };
            lines.AddRange(_entries.Select(e => $"{e.Initials}|{e.Score}"));

            // Write then swap, so a crash mid-write can't leave a half-written table.
            string temp = TablePath + ".tmp";
            File.WriteAllLines(temp, lines);
            File.Move(temp, TablePath, overwrite: true);
        }
        catch
        {
            // A read-only home folder just means the table isn't remembered.
        }
    }

    private void Sort() => _entries.Sort((a, b) => b.Score.CompareTo(a.Score));

    private static string Clean(string initials)
    {
        var chars = initials.ToUpperInvariant()
            .Where(c => InitialsEntry.Alphabet.Contains(c))
            .Take(InitialsLength)
            .ToArray();
        return new string(chars).PadRight(InitialsLength, ' ');
    }
}
