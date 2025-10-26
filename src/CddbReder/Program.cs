using CddbReder.Cddb;
using MetaBrainz.MusicBrainz.DiscId;
using System.Management;
using System.Text;
using static System.Net.Mime.MediaTypeNames;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

// ボリュームシリアル番号を取得する
static string? TryGetVolumeSerialNumber(string? driveSpecifier)
{
    if (string.IsNullOrWhiteSpace(driveSpecifier)) return null;

    var normalized = driveSpecifier.Trim();
    normalized = normalized.TrimEnd('\\', '/');
    if (normalized.EndsWith(":", StringComparison.Ordinal))
        normalized = normalized[..^1];
    if (normalized.Length == 0) return null;

    string query = $"SELECT DeviceID,VolumeSerialNumber FROM Win32_LogicalDisk WHERE DeviceID = '{normalized.ToUpperInvariant()}:'";
    using (var searcher = new ManagementObjectSearcher(query))
    {
        foreach (ManagementObject disk in searcher.Get())
        {
            var serial = disk["VolumeSerialNumber"]?.ToString();
            if (string.IsNullOrWhiteSpace(serial))
                continue;

            var trimmed = serial.Trim();
            trimmed = trimmed.TrimStart('0');
            if (trimmed.Length == 0)
                trimmed = "0";

            return trimmed;
        }
    }
    return null;
}

static void PrintUsage()
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project src/CddbReader -- --cgi <url> [--encoding <name>] [--toc <path>] [--wmp-drive <D:>] [--xmcd-out <path>] [--out-encoding <name>]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --cgi          FreeDB-compatible CGI endpoint URL (e.g., http://gnudb.gnudb.org/~cddb/cddb.cgi)");
    Console.WriteLine("  --user         Username to send in the hello parameter (required)");
    Console.WriteLine("  --host         Hostname to send in the hello parameter (required)");
    Console.WriteLine("  --encoding     Response encoding: euc-jp (default), shift_jis, utf-8, etc.");
    Console.WriteLine("  --toc          Path to TOC JSON file with { trackOffsetsFrames: number[], leadoutOffsetFrames: number }");
    Console.WriteLine("  --wmp-drive    Read TOC from Windows Media Player for the given drive (e.g., D:). Requires Windows Media Player.");
    Console.WriteLine("  --xmcd-out     Save fetched data as XMCD text to the given file path");
    Console.WriteLine("  --out-encoding Encoding for the saved XMCD (default: same as --encoding)");
    Console.WriteLine("  --cdplayer-ini  Export cdplayer.ini entry (writes to Windows or VirtualStore path)");
}
string? cgiUrl = "http://freedbtest.dyndns.org/~cddb/cddb.cgi";
string encodingName = "utf-8";
string? tocPath = null;
string? wmpDrive = null;
string? xmcdOut = null;
string? outEncodingName = null;
string? helloUser = null;
string? helloHost = null;
bool exportCdPlayerIni = false;

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
        case "--user":
            helloUser = (i + 1 < args.Length) ? args[++i] : null;
            break;
        case "--host":
            helloHost = (i + 1 < args.Length) ? args[++i] : null;
            break;
        case "--cdplayer-ini":
            exportCdPlayerIni = true;
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

if (string.IsNullOrWhiteSpace(helloUser) || string.IsNullOrWhiteSpace(helloHost))
{
    PrintUsage();
    Console.WriteLine();
    Console.WriteLine("Error: --user and --host are required.");
    return;
}

TableOfContents? tableOfContents = TableOfContents.ReadDisc(null, DiscReadFeature.TableOfContents);

var client = new FreedbClient(cgiUrl!, encodingName: encodingName, user: helloUser, host: helloHost);

// Query
var matches = await client.QueryAsync(tableOfContents);
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
var xmcdLines = await client.ReadAsync(first.Category, first.DiscId);
var xmcd = FreedbClient.ParseXmcd(first.Category, first.DiscId, xmcdLines);
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

if (!string.IsNullOrWhiteSpace(xmcdOut))
{
    var encName = outEncodingName ?? encodingName;
    Encoding outEnc;
    try { outEnc = Encoding.GetEncoding(encName); } catch { outEnc = Encoding.UTF8; }

    var full = Path.GetFullPath(xmcdOut);
    var dir = Path.GetDirectoryName(full);
    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
    await File.WriteAllLinesAsync(full, xmcdLines, outEnc);
    Console.WriteLine($"Saved XMCD: {full} ({outEnc.WebName})");
}

if (exportCdPlayerIni)
{
    var volumeSerial = TryGetVolumeSerialNumber(TableOfContents.DefaultDevice);
    if (string.IsNullOrWhiteSpace(volumeSerial))
    {
        Console.WriteLine("Warning: Unable to determine volume serial number. Skipping cdplayer.ini export.");
    }
    else
    {
        int trackCount = tableOfContents?.Tracks.Count ?? xmcd.TrackTitles.Count;
        if (trackCount <= 0)
        {
            Console.WriteLine("Warning: No track information available. Skipping cdplayer.ini export.");
        }
        else
        {
            try
            {
                var exportedPath = await CdPlayerIniExporter.ExportAsync(volumeSerial, xmcd, trackCount, encoding: Encoding.GetEncoding(932));
                Console.WriteLine($"Exported cdplayer.ini entry: {exportedPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to export cdplayer.ini: {ex.Message}");
            }
        }
    }
}

public class TocDto
{
    public List<int> TrackOffsetsFrames { get; set; } = [];
    public int LeadoutOffsetFrames { get; set; }
}