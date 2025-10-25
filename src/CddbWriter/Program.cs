using System.Text;
using System.Text.Json;
using CddbWriter.Cddb;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/CddbWriter -- --cgi <url> [--encoding <name>] [--toc <path>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --cgi       FreeDB-compatible CGI endpoint URL (e.g., http://gnudb.gnudb.org/~cddb/cddb.cgi)");
    Console.WriteLine("  --encoding  Response encoding: euc-jp (default), shift_jis, utf-8, etc.");
    Console.WriteLine("  --toc       Path to TOC JSON file with { trackOffsetsFrames: number[], leadoutOffsetFrames: number }");
}

string? cgiUrl = null;
string encodingName = "euc-jp";
string? tocPath = null;

// simple args parse
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--cgi":
            cgiUrl = (i + 1 < args.Length) ? args[++i] : null;
            break;
        case "--encoding":
            encodingName = (i + 1 < args.Length) ? args[++i] : encodingName;
            break;
        case "--toc":
            tocPath = (i + 1 < args.Length) ? args[++i] : null;
            break;
    }
}

if (string.IsNullOrWhiteSpace(cgiUrl))
{
    PrintUsage();
    Console.WriteLine();
    Console.WriteLine("Error: --cgi is required.");
    return;
}

DiscToc toc;
if (!string.IsNullOrWhiteSpace(tocPath))
{
    try
    {
        var json = await File.ReadAllTextAsync(tocPath);
        var dto = JsonSerializer.Deserialize<TocDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (dto is null || dto.TrackOffsetsFrames is null || dto.TrackOffsetsFrames.Count == 0)
            throw new Exception("Invalid TOC JSON.");
        toc = new DiscToc
        {
            LeadoutOffsetFrames = dto.LeadoutOffsetFrames
        };
        toc.TrackOffsetsFrames.AddRange(dto.TrackOffsetsFrames);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to read TOC JSON: {ex.Message}");
        return;
    }
}
else
{
    Console.WriteLine("Warning: --toc not specified. Using sample values.");
    toc = new DiscToc
    {
        LeadoutOffsetFrames = 180000
    };
    toc.TrackOffsetsFrames.AddRange(new[] { 150, 15000, 30000, 45000, 60000, 75000, 90000, 105000, 120000, 135000 });
}

var client = new FreedbClient(cgiUrl!, encodingName: encodingName);

// Query
var matches = await client.QueryAsync(toc);
if (matches.Count == 0)
{
    Console.WriteLine("No match.");
    return;
}

Console.WriteLine("Matches:");
foreach (var m in matches)
{
    Console.WriteLine($"{m.Category} {m.DiscId} {m.Title}");
}

// Read first
var first = matches[0];
var xmcd = await client.ReadAsync(first.Category, first.DiscId);
if (xmcd == null)
{
    Console.WriteLine("Failed to read XMCD.");
    return;
}

Console.WriteLine();
Console.WriteLine("=== XMCD ===");
Console.WriteLine($"DTITLE: {xmcd.DTitle}");
if (!string.IsNullOrEmpty(xmcd.DYear)) Console.WriteLine($"DYEAR: {xmcd.DYear}");
if (!string.IsNullOrEmpty(xmcd.DGenre)) Console.WriteLine($"DGENRE: {xmcd.DGenre}");
for (int i = 0; i < xmcd.TrackTitles.Count; i++)
{
    Console.WriteLine($"TTITLE{i}: {xmcd.TrackTitles[i]}");
}