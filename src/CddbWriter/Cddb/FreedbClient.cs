using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

namespace CddbWriter.Cddb;

public static class CddbUtil
{
    // FreeDB/CDDB の discid 算出
    public static string ComputeDiscIdHex(DiscToc toc)
    {
        if (toc.TrackCount == 0) throw new ArgumentException("TOC is empty.");

        int cddbSum = 0;
        foreach (var offset in toc.TrackOffsetsFrames)
        {
            int seconds = offset / 75;
            cddbSum += SumOfDigits(seconds);
        }
        int n = toc.TrackCount;
        int t = toc.TotalSeconds;
        int discId = ((cddbSum % 255) << 24) | (t << 8) | n;
        return discId.ToString("x8"); // 8桁の16進
    }

    private static int SumOfDigits(int n)
    {
        int s = 0;
        while (n > 0)
        {
            s += n % 10;
            n /= 10;
        }
        return s;
    }
}

public class FreedbClient
{
    private readonly HttpClient _http;
    private readonly Uri _cgiBase;
    private readonly string _helloParam;
    private readonly int _proto;
    private readonly Encoding _encoding;

    // cgiBase: 例) http://gnudb.gnudb.org/~cddb/cddb.cgi
    // encodingName: Freedb日本語は EUC-JP のことが多い。状況に応じて "euc-jp" / "shift_jis" / "utf-8" を切替
    public FreedbClient(string cgiBase, string appName = "cddb-writer", string appVersion = "0.1.0", string? user = null, string? host = null, int proto = 6, string encodingName = "euc-jp")
    {
        _http = new HttpClient();
        _cgiBase = new Uri(cgiBase);
        _proto = proto;

        user ??= Environment.UserName;
        host ??= Environment.MachineName;
        _helloParam = $"{Uri.EscapeDataString(user)}+{Uri.EscapeDataString(host)}+{Uri.EscapeDataString(appName)}+{Uri.EscapeDataString(appVersion)}";

        try
        {
            _encoding = Encoding.GetEncoding(encodingName);
        }
        catch
        {
            _encoding = Encoding.UTF8; // フォールバック
        }

        _http.DefaultRequestHeaders.UserAgent.Clear();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(appName, appVersion));
    }

    public async Task<List<CddbMatch>> QueryAsync(DiscToc toc)
    {
        string discId = CddbUtil.ComputeDiscIdHex(toc);
        var parts = new List<string> { "cddb", "query", discId, toc.TrackCount.ToString(CultureInfo.InvariantCulture) };
        // 各トラックのオフセット（フレーム数）
        parts.AddRange(toc.TrackOffsetsFrames.Select(o => o.ToString(CultureInfo.InvariantCulture)));
        // 総再生秒
        parts.Add(toc.TotalSeconds.ToString(CultureInfo.InvariantCulture));

        string cmd = string.Join('+', parts);
        var url = $"{_cgiBase}?cmd={cmd}&hello={_helloParam}&proto={_proto}";
        var text = await GetTextAsync(url);

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (lines.Length == 0) return new List<CddbMatch>();

        var first = lines[0];
        if (first.StartsWith("200 ")) // single exact match
        {
            // 1行: "200 category discid title"
            var m = ParseMatchLine(first.Substring(4).Trim());
            return m is null ? new List<CddbMatch>() : new List<CddbMatch> { m };
        }
        else if (first.StartsWith("210 ") || first.StartsWith("211 ")) // multi
        {
            var results = new List<CddbMatch>();
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line == ".") break;
                var m = ParseMatchLine(line);
                if (m != null) results.Add(m);
            }
            return results;
        }
        else if (first.StartsWith("202 ")) // no match
        {
            return new List<CddbMatch>();
        }
        else
        {
            // その他コードは空扱い
            return new List<CddbMatch>();
        }
    }

    public async Task<XmcdRecord?> ReadAsync(string category, string discId)
    {
        string cmd = $"cddb+read+{category}+{discId}";
        var url = $"{_cgiBase}?cmd={cmd}&hello={_helloParam}&proto={_proto}";
        var text = await GetTextAsync(url);

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        if (lines.Length == 0) return null;
        if (!lines[0].StartsWith("210 ")) // 210 OK, CDDB database entry follows (until terminating `.`)
            return null;

        var xmcdLines = new List<string>();
        for (int i = 1; i < lines.Length; i++)
        {
            if (lines[i] == ".") break;
            xmcdLines.Add(lines[i]);
        }
        return ParseXmcd(category, discId, xmcdLines);
    }

    private async Task<string> GetTextAsync(string url)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        return _encoding.GetString(bytes);
    }

    private static CddbMatch? ParseMatchLine(string line)
    {
        // 形式: "category discid title..." タイトルは空白を含む可能性
        var parts = line.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return null;
        return new CddbMatch
        {
            Category = parts[0],
            DiscId = parts[1],
            Title = parts[2]
        };
    }

    private static XmcdRecord ParseXmcd(string category, string discId, List<string> lines)
    {
        var rec = new XmcdRecord { Category = category, DiscId = discId };
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("#")) continue; // コメント行

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line.Substring(0, eq);
            var val = line.Substring(eq + 1);

            rec.Raw[key] = val;

            if (key == "DTITLE") rec.DTitle = val;
            else if (key == "DYEAR") rec.DYear = val;
            else if (key == "DGENRE") rec.DGenre = val;
            else if (key.StartsWith("TTITLE"))
            {
                if (int.TryParse(key.Substring("TTITLE".Length), out int idx))
                {
                    while (rec.TrackTitles.Count <= idx) rec.TrackTitles.Add(string.Empty);
                    rec.TrackTitles[idx] = val;
                }
            }
        }
        return rec;
    }
}