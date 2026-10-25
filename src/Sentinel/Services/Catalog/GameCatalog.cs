using Sentinel.Core;
using Sentinel.Models;

namespace Sentinel.Services.Catalog;

/// <summary>The embedded game database: every Data/Catalog/*.json file merged into one lookup.</summary>
public sealed class GameCatalog
{
    private readonly Dictionary<int, CatalogGame> _bySteam = [];
    private readonly Dictionary<string, CatalogGame> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatalogGame> _byLoose = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CatalogGame> _byExe = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<CatalogGame> Games { get; }

    public static GameCatalog Instance { get; } = new();

    private GameCatalog()
    {
        var all = new List<CatalogGame>();
        foreach (var (file, games) in EmbeddedData.LoadAll<List<CatalogGame>>("Sentinel.Data.Catalog."))
        {
            foreach (var g in games)
            {
                if (_byName.ContainsKey(NameNormalizer.Normalize(g.Name)))
                {
                    Log.Write($"catalog: duplicate '{g.Name}' in {file}");
                    continue;
                }
                all.Add(g);
                Index(g);
            }
        }
        // Most-owned first; games without a Steam rank follow, alphabetically.
        Games = all.OrderBy(g => g.Rank > 0 ? g.Rank : int.MaxValue)
                   .ThenBy(g => g.SortName, StringComparer.OrdinalIgnoreCase)
                   .ToList();
    }

    private void Index(CatalogGame g)
    {
        foreach (var id in g.SteamIds) _bySteam.TryAdd(id, g);
        foreach (var n in g.Aliases.Prepend(g.Name))
        {
            _byName.TryAdd(NameNormalizer.Normalize(n), g);
            _byLoose.TryAdd(NameNormalizer.Loose(n), g);
        }
        foreach (var exe in g.Exes) _byExe.TryAdd(exe, g);
    }

    public CatalogGame? Match(string name, int? steamId = null, string? exe = null)
    {
        if (steamId is int id && _bySteam.TryGetValue(id, out var bySteam)) return bySteam;
        if (_byName.TryGetValue(NameNormalizer.Normalize(name), out var exact)) return exact;
        if (_byLoose.TryGetValue(NameNormalizer.Loose(name), out var loose)) return loose;
        if (exe is not null && _byExe.TryGetValue(Path.GetFileName(exe), out var byExe)) return byExe;
        return null;
    }

    public CatalogGame? MatchExe(string exeFileName) => _byExe.TryGetValue(exeFileName, out var g) ? g : null;
}
