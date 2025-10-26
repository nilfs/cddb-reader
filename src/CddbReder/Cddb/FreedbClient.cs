using MetaBrainz.MusicBrainz.DiscId;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;

namespace CddbReder.Cddb;

public class FreedbClient
{
    private readonly HttpClient _http;
    private readonly Uri _cgiBase;
    private readonly string _helloParam;
    private readonly int _proto;
    private readonly Encoding _encoding;

    // cgiBase: 例) http://gnudb.gnudb.org/~cddb/cddb.cgi
    public FreedbClient(string cgiBase, string appName = "cddb-reader", string appVersion = "0.1.0", string? user = null, string? host = null, int proto = 6, string encodingName = "euc-jp")
    {
        _http = new HttpClient();
        _cgiBase = new Uri(cgiBase);
        _proto = proto;

        if (string.IsNullOrWhiteSpace(user)) throw new ArgumentException("user must be provided", nameof(user));
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("host must be provided", nameof(host));

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

    public async Task<List<CddbMatch>> QueryAsync(TableOfContents toc)
    {
        //string discId = CddbUtil.ComputeDiscIdHex(toc);
        string discId = toc.FreeDbId;
        var parts = new List<string> { "cddb", "query", discId, toc.Tracks.Count.ToString(CultureInfo.InvariantCulture) };
        // 各トラックのオフセット（フレーム数）
        //parts.AddRange(toc.TrackOffsetsFrames.Select(o => o.ToString(CultureInfo.InvariantCulture)));
        parts.AddRange(toc.Tracks.Select(o => o.Offset.ToString(CultureInfo.InvariantCulture)));
        // 総再生秒
        parts.Add(((int)Math.Floor(toc.Length / 75.0)).ToString(CultureInfo.InvariantCulture));
        //parts.Add(toc.TotalSeconds.ToString(CultureInfo.InvariantCulture));

        string cmd = string.Join('+', parts);
        var response = await SendCommandAsync(cmd);

        return response.StatusCode switch
        {
            200 => response.ParseSingleMatch(),
            210 or 211 => response.ParseMultipleMatches(),
            202 => [],
            _ => []
        };
    }

    public async Task<List<string>> ReadAsync(string category, string discId)
    {
        string cmd = $"cddb+read+{category}+{discId}";
        var response = await SendCommandAsync(cmd);

        return response.StatusCode == 210 ? response.BodyLines : null;
    }

    private async Task<CddbResponse> SendCommandAsync(string cmd)
    {
        var url = $"{_cgiBase}?cmd={cmd}&hello={_helloParam}&proto={_proto}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        var resp = await _http.SendAsync(req);
        resp.EnsureSuccessStatusCode();

        var bytes = await resp.Content.ReadAsByteArrayAsync();
        return CddbResponse.Parse(_encoding.GetString(bytes));
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

    private sealed record CddbResponse(int StatusCode, string StatusMessage, List<string> BodyLines)
    {
        public static CddbResponse Parse(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new CddbResponse(0, string.Empty, []);
            }

            var lines = text.Split('\n', StringSplitOptions.TrimEntries);
            if (lines.Length == 0)
            {
                return new CddbResponse(0, string.Empty, []);
            }

            var header = lines[0];
            int status = 0;
            string message = string.Empty;

            var space = header.IndexOf(' ');
            if (space > 0)
            {
                if (!int.TryParse(header[..space], NumberStyles.Integer, CultureInfo.InvariantCulture, out status))
                {
                    status = 0;
                }
                message = header[(space + 1)..];
            }
            else
            {
                if (!int.TryParse(header, NumberStyles.Integer, CultureInfo.InvariantCulture, out status))
                {
                    status = 0;
                }
                message = string.Empty;
            }

            var body = new List<string>();
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line == ".") break;
                body.Add(line);
            }

            return new CddbResponse(status, message, body);
        }

        public List<CddbMatch> ParseSingleMatch()
        {
            if (BodyLines.Count > 0)
            {
                var candidate = ParseMatchLine(BodyLines[0]);
                if (candidate != null)
                    return [candidate];
            }

            if (!string.IsNullOrEmpty(StatusMessage))
            {
                var candidate = ParseMatchLine(StatusMessage);
                if (candidate != null)
                    return [candidate];
            }

            return [];
        }

        public List<CddbMatch> ParseMultipleMatches()
        {
            var results = new List<CddbMatch>();
            foreach (var line in BodyLines)
            {
                var candidate = ParseMatchLine(line);
                if (candidate != null)
                    results.Add(candidate);
            }
            return results;
        }
    }

    public static XmcdRecord ParseXmcd(string category, string discId, List<string> lines)
    {
        var rec = new XmcdRecord { Category = category, DiscId = discId };
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith('#')) continue; // コメント行

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
                if (int.TryParse(key.AsSpan("TTITLE".Length), out int idx))
                {
                    while (rec.TrackTitles.Count <= idx) rec.TrackTitles.Add(string.Empty);
                    rec.TrackTitles[idx] = val;
                }
            }
        }
        return rec;
    }
}