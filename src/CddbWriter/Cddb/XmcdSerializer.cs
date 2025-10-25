using System.Text;

namespace CddbWriter.Cddb;

public static class XmcdSerializer
{
    // Generate XMCD text from a record and optional TOC. Does not include the terminating '.'
    public static string ToXmcd(XmcdRecord rec, DiscToc? toc = null, string? appName = null, string? appVersion = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# xmcd");
        if (toc != null)
        {
            sb.AppendLine("# Track frame offsets:");
            foreach (var off in toc.TrackOffsetsFrames)
            {
                sb.AppendLine("#\t" + off.ToString());
            }
            sb.AppendLine($"# Disc length: {toc.TotalSeconds} seconds");
        }
        if (!string.IsNullOrEmpty(appName))
        {
            sb.AppendLine($"# Submitted via: {appName}{(string.IsNullOrEmpty(appVersion) ? string.Empty : " " + appVersion)}");
        }

        if (!string.IsNullOrEmpty(rec.DiscId)) sb.AppendLine($"DISCID={OneLine(rec.DiscId)}");
        if (!string.IsNullOrEmpty(rec.DTitle)) sb.AppendLine($"DTITLE={OneLine(rec.DTitle)}");
        if (!string.IsNullOrEmpty(rec.DYear)) sb.AppendLine($"DYEAR={OneLine(rec.DYear)}");
        if (!string.IsNullOrEmpty(rec.DGenre)) sb.AppendLine($"DGENRE={OneLine(rec.DGenre)}");

        for (int i = 0; i < rec.TrackTitles.Count; i++)
        {
            sb.AppendLine($"TTITLE{i}={OneLine(rec.TrackTitles[i] ?? string.Empty)}");
        }

        if (rec.Raw.TryGetValue("EXTD", out var extd))
            sb.AppendLine($"EXTD={OneLine(extd)}");

        foreach (var kv in rec.Raw)
        {
            if (kv.Key.StartsWith("EXTT"))
            {
                sb.AppendLine($"{kv.Key}={OneLine(kv.Value)}");
            }
        }
        if (rec.Raw.TryGetValue("PLAYORDER", out var playorder))
            sb.AppendLine($"PLAYORDER={OneLine(playorder)}");

        return sb.ToString();
    }

    private static string OneLine(string s)
        => (s ?? string.Empty).Replace("\r", string.Empty).Replace("\n", " ");
}