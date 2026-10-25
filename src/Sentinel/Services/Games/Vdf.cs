using System.Text;

namespace Sentinel.Services.Games;

/// <summary>Minimal reader for Valve's text KeyValues format (libraryfolders.vdf, appmanifest_*.acf).</summary>
public sealed class VdfNode
{
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VdfNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? this[string key] => Values.TryGetValue(key, out var v) ? v : null;
    public VdfNode? Child(string key) => Children.TryGetValue(key, out var c) ? c : null;

    public static VdfNode Parse(string text)
    {
        int i = 0;
        var root = new VdfNode();
        ParseInto(root, text, ref i);
        return root;
    }

    public static VdfNode Load(string path) => Parse(File.ReadAllText(path));

    private static void ParseInto(VdfNode node, string s, ref int i)
    {
        while (true)
        {
            var key = NextToken(s, ref i);
            if (key is null || key == "}") return;
            var value = NextToken(s, ref i);
            if (value is null) return;
            if (value == "{")
            {
                var child = new VdfNode();
                ParseInto(child, s, ref i);
                node.Children[key] = child;
            }
            else node.Values[key] = value;
        }
    }

    private static string? NextToken(string s, ref int i)
    {
        while (i < s.Length)
        {
            char c = s[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
            {
                while (i < s.Length && s[i] != '\n') i++;
                continue;
            }
            if (c is '{' or '}') { i++; return c.ToString(); }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', _ => s[i] });
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                i++;
                return sb.ToString();
            }
            int start = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] is not ('{' or '}' or '"')) i++;
            return s[start..i];
        }
        return null;
    }
}
