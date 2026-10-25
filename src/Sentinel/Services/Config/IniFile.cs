using System.Text;

namespace Sentinel.Services.Config;

/// <summary>Line-preserving INI editor: untouched lines (comments, ordering, unknown keys) round-trip byte-for-byte.</summary>
public sealed class IniFile
{
    private readonly List<string> _lines;
    private readonly Encoding _encoding;
    private readonly string _newline;
    private readonly bool _trailingNewline;

    private IniFile(List<string> lines, Encoding encoding, string newline, bool trailingNewline)
    {
        _lines = lines;
        _encoding = encoding;
        _newline = newline;
        _trailingNewline = trailingNewline;
    }

    public static IniFile Load(string path)
    {
        var (text, encoding) = TextFiles.Read(path);
        string newline = text.Contains("\r\n") || !text.Contains('\n') ? "\r\n" : "\n";
        bool trailing = text.EndsWith('\n');
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        if (trailing) lines.RemoveAt(lines.Count - 1);
        return new IniFile(lines, encoding, newline, trailing);
    }

    public void Save(string path) =>
        TextFiles.Write(path, string.Join(_newline, _lines) + (_trailingNewline ? _newline : ""), _encoding);

    private static string? SectionOf(string line)
    {
        var t = line.Trim();
        return t.StartsWith('[') && t.EndsWith(']') ? t[1..^1] : null;
    }

    private int FindKey(string? section, string key, out int sectionEnd)
    {
        string? current = null;
        sectionEnd = -1;
        bool inTarget = section is null;
        for (int i = 0; i < _lines.Count; i++)
        {
            var s = SectionOf(_lines[i]);
            if (s is not null)
            {
                if (inTarget && section is not null) { sectionEnd = i; return -1; }
                current = s;
                inTarget = section is null || current.Equals(section, StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inTarget) continue;
            int eq = _lines[i].IndexOf('=');
            if (eq > 0 && _lines[i][..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return i;
        }
        if (inTarget) sectionEnd = _lines.Count;
        return -1;
    }

    public bool HasSection(string section) => _lines.Any(l => SectionOf(l)?.Equals(section, StringComparison.OrdinalIgnoreCase) == true);

    public string? Get(string? section, string key)
    {
        int i = FindKey(section, key, out _);
        return i < 0 ? null : _lines[i][(_lines[i].IndexOf('=') + 1)..].Trim();
    }

    /// <summary>Finds which section holds <paramref name="key"/>, if any.</summary>
    public string? SectionContaining(string key)
    {
        string? current = null;
        foreach (var line in _lines)
        {
            var s = SectionOf(line);
            if (s is not null) { current = s; continue; }
            int eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return current;
        }
        return null;
    }

    /// <summary>Sets a value. Returns true if the file changed.</summary>
    public bool Set(string section, string key, string value, bool addIfMissing)
    {
        int i = FindKey(section, key, out int sectionEnd);
        if (i >= 0)
        {
            var updated = $"{_lines[i][.._lines[i].IndexOf('=')].TrimEnd()}={value}";
            if (_lines[i] == updated) return false;
            _lines[i] = updated;
            return true;
        }
        if (!addIfMissing) return false;

        if (sectionEnd < 0)
        {
            if (_lines.Count > 0 && _lines[^1].Trim().Length > 0) _lines.Add("");
            _lines.Add($"[{section}]");
            _lines.Add($"{key}={value}");
            return true;
        }
        // Insert before the blank lines that separate this section from the next.
        int at = sectionEnd;
        while (at > 0 && _lines[at - 1].Trim().Length == 0) at--;
        _lines.Insert(at, $"{key}={value}");
        return true;
    }
}
