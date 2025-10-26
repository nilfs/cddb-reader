using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace CddbReder.Cddb;

public static class CdPlayerIniExporter
{
    private const string DefaultPath = @"C:\\Windows\\cdplayer.ini";
    private const string UnknownArtist = "Unknown Artist";
    private const string UnknownTitle = "Unknown Title";
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public static async Task<string> ExportAsync(
        string volumeSerialNumber,
        XmcdRecord record,
        int trackCount,
        IReadOnlyList<string>? trackTitleOverrides = null,
        Encoding? encoding = null)
    {
        if (string.IsNullOrWhiteSpace(volumeSerialNumber))
            throw new ArgumentException("Volume serial number is required.", nameof(volumeSerialNumber));
        if (record is null)
            throw new ArgumentNullException(nameof(record));

        if (trackCount <= 0)
            trackCount = trackTitleOverrides?.Count ?? record.TrackTitles.Count;
        if (trackCount <= 0)
            throw new ArgumentException("Track count must be greater than zero.", nameof(trackCount));

        var (artist, title) = ExtractArtistAndTitle(record.DTitle);
        var tracks = BuildTrackTitles(trackTitleOverrides ?? record.TrackTitles, trackCount);

        var content = BuildContent(volumeSerialNumber, artist, title, tracks, trackCount);
        var targetEncoding = encoding ?? Utf8NoBom;

        var primaryPath = DefaultPath;
        try
        {
            await WriteAsync(primaryPath, content, targetEncoding);
            return primaryPath;
        }
        catch (UnauthorizedAccessException)
        {
            var fallback = GetVirtualStorePath();
            await WriteAsync(fallback, content, targetEncoding);
            return fallback;
        }
    }

    private static string BuildContent(string volumeSerialNumber, string artist, string title, IReadOnlyList<string> tracks, int trackCount)
    {
        var sb = new StringBuilder();
        sb.Append('[').Append(volumeSerialNumber).AppendLine("]");
        sb.Append("artist=").AppendLine(artist);
        sb.Append("title=").AppendLine(title);
        sb.Append("numtracks=").AppendLine(trackCount.ToString(CultureInfo.InvariantCulture));
        for (int i = 0; i < tracks.Count; i++)
        {
            sb.Append(i.ToString(CultureInfo.InvariantCulture))
              .Append('=')
              .AppendLine(tracks[i]);
        }

        return sb.ToString();
    }

    private static (string Artist, string Title) ExtractArtistAndTitle(string dTitle)
    {
        var sanitized = Sanitize(dTitle);
        if (string.IsNullOrEmpty(sanitized))
            return (UnknownArtist, UnknownTitle);

        var parts = sanitized.Split('/', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            var artist = string.IsNullOrEmpty(parts[0]) ? UnknownArtist : parts[0];
            var title = string.IsNullOrEmpty(parts[1]) ? UnknownTitle : parts[1];
            return (artist, title);
        }

        return (sanitized, UnknownTitle);
    }

    private static List<string> BuildTrackTitles(IReadOnlyList<string> source, int trackCount)
    {
        var result = new List<string>(trackCount);
        for (int i = 0; i < trackCount; i++)
        {
            string candidate = string.Empty;
            if (source != null && i < source.Count)
            {
                candidate = Sanitize(source[i]);
            }

            if (string.IsNullOrEmpty(candidate))
            {
                candidate = $"Track{(i + 1).ToString("D2", CultureInfo.InvariantCulture)}";
            }

            result.Add(candidate);
        }

        return result;
    }

    private static async Task WriteAsync(string destination, string content, Encoding encoding)
    {
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(destination, content, encoding);
    }

    private static string GetVirtualStorePath()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(basePath, "VirtualStore", "Windows", "cdplayer.ini");
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
