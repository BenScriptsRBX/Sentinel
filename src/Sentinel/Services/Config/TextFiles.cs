using System.Text;

namespace Sentinel.Services.Config;

/// <summary>
/// Byte-faithful text I/O for game config files: keeps the original byte-order mark (or lack of one) and every
/// line's own terminator, so the only bytes that change are the values Sentinel edits.
/// </summary>
internal static class TextFiles
{
    public sealed record Line(string Content, string Ending);

    public static (string Text, Encoding Encoding) Read(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var encoding = DetectEncoding(bytes);
        int bom = encoding.Preamble.Length;
        return (encoding.GetString(bytes, bom, bytes.Length - bom), encoding);
    }

    public static void Write(string path, string text, Encoding encoding) =>
        File.WriteAllBytes(path, [.. encoding.Preamble, .. encoding.GetBytes(text)]);

    /// <summary>
    /// Only reports a BOM-emitting encoding when the file actually starts with one. StreamReader reports
    /// Encoding.UTF8 for BOM-less files, and that instance writes a BOM on save.
    /// </summary>
    public static Encoding DetectEncoding(byte[] b)
    {
        if (b is [0xEF, 0xBB, 0xBF, ..]) return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        if (b is [0xFF, 0xFE, ..]) return new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        if (b is [0xFE, 0xFF, ..]) return new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    }

    /// <summary>Splits into lines, each keeping its own "\r\n", "\n" or "" (last line without a newline).</summary>
    public static List<Line> SplitLines(string text)
    {
        var lines = new List<Line>();
        int start = 0;
        while (start < text.Length)
        {
            int nl = text.IndexOf('\n', start);
            if (nl < 0)
            {
                lines.Add(new Line(text[start..], ""));
                break;
            }
            bool crlf = nl > start && text[nl - 1] == '\r';
            lines.Add(new Line(text[start..(crlf ? nl - 1 : nl)], crlf ? "\r\n" : "\n"));
            start = nl + 1;
        }
        return lines;
    }

    public static string JoinLines(IEnumerable<Line> lines)
    {
        var sb = new StringBuilder();
        foreach (var l in lines) sb.Append(l.Content).Append(l.Ending);
        return sb.ToString();
    }

    /// <summary>Rewrites the content of matching lines in place; returns how many lines changed.</summary>
    public static int EditLines(string path, Func<string, string?> edit)
    {
        var (text, encoding) = Read(path);
        var lines = SplitLines(text);
        int changed = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            var updated = edit(lines[i].Content);
            if (updated is null || updated == lines[i].Content) continue;
            lines[i] = lines[i] with { Content = updated };
            changed++;
        }
        if (changed > 0) Write(path, JoinLines(lines), encoding);
        return changed;
    }
}
