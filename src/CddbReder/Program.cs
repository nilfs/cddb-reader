using System.Text;
using System.Text.Json;
using CddbWriter.Cddb;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/CddbWriter -- --cgi <url> [--encoding <name>] [--toc <path>] [--wmp-drive <D:>] [--xmcd-out <path>] [--out-encoding <name>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --cgi          FreeDB-compatible CGI endpoint URL (e.g., http://gnudb.gnudb.org/~cddb/cddb.cgi)");
    Console.WriteLine("  --encoding     Response encoding: euc-jp (default), shift_jis, utf-8, etc.");
    Console.WriteLine("  --toc          Path to TOC JSON file with { trackOffsetsFrames: number[], leadoutOffsetFrames: number }");
    Console.WriteLine("  --wmp-drive    Read TOC from Windows Media Player for the given drive (e.g., D:). Requires Windows Media Player.");
    Console.WriteLine("  --xmcd-out     Save fetched data as XMCD text to the given file path");
    Console.WriteLine("  --out-encoding Encoding for the saved XMCD (default: same as --encoding)");
}

string? cgiUrl = null;
string encodingName = "euc-jp";
string? tocPath = null;
string? wmpDrive = null;
string? xmcdOut = null;
string? outEncodingName = null;

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
        case "--wmp-drive":
            wmpDrive = (i + 1 < args.Length) ? args[++i] : null;
            break;
        case "--xmcd-out":
            xmcdOut = (i + 1 < args.Length) ? args[++i] : null;
            break;
        case "--out-encoding":
            outEncodingName = (i + 1 < args.Length) ? args[++i] : outEncodingName;
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

DiscToc? toc = null;

// Prefer WMP if specified
if (!string.IsNullOrWhiteSpace(wmpDrive))
{
    try
    {
        var reader = new WmpTocReader();
        toc = reader.TryRead(wmpDrive);
        if (toc == null)
        {
            Console.WriteLine($"Failed to read TOC via Windows Media Player for drive {wmpDrive}. Falling back...");
        }
        else
        {
            Console.WriteLine($"TOC read from Windows Media Player for drive {wmpDrive}.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"WMP TOC read error: {ex.Message}. Falling back...");
    }
}

// Fallback to JSON TOC
if (toc == null)
{
    if (!string.IsNullOrWhiteSpace(tocPath))
    {
        try
        {
            var json = await File.ReadAllTextAsync(tocPath);
            var dto = System.Text.Json.JsonSerializer.Deserialize<TocDto>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto is null || dto.TrackOffsetsFrames is null || dto.TrackOffsetsFrames.Count == 0)
                throw new Exception("Invalid TOC JSON.");
            toc = new DiscToc { LeadoutOffsetFrames = dto.LeadoutOffsetFrames };
            toc.TrackOffsetsFrames.AddRange(dto.TrackOffsetsFrames);
            Console.WriteLine($"TOC loaded from JSON: {tocPath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to read TOC JSON: {ex.Message}");
            return;
        }
    }
    else
    {
        Console.WriteLine("Warning: no TOC source specified. Using sample values.");
        toc = new DiscToc { LeadoutOffsetFrames = 180000 };
        toc.TrackOffsetsFrames.AddRange(new[] { 150, 15000, 30000, 45000, 60000, 75000, 90000, 105000, 120000, 135000 });
    }
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

// Save XMCD if requested
if (!string.IsNullOrWhiteSpace(xmcdOut))
{
    try
    {
        var text = XmcdSerializer.ToXmcd(xmcd, toc, "cddb-writer", "0.2.0");
        var encName = outEncodingName ?? encodingName;
        Encoding outEnc;
        try { outEnc = Encoding.GetEncoding(encName); } catch { outEnc = Encoding.UTF8; }
        var full = Path.GetFullPath(xmcdOut);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(full, text, outEnc);
        Console.WriteLine($"Saved XMCD: {full} ({outEnc.WebName})");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Failed to save XMCD: {ex.Message}");
    }
}

public class TocDto
{
    public List<int> TrackOffsetsFrames { get; set; } = new();
    public int LeadoutOffsetFrames { get; set; }
}