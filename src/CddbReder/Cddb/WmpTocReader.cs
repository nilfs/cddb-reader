using System.Globalization;
using WMPLib;

namespace CddbReder.Cddb;

public class WmpTocReader
{
    public DiscToc? TryRead(string? driveSpecifier = null)
    {
        // Requires Windows Media Player to be installed/enabled on the system
        var wmp = new WindowsMediaPlayer();
        var cds = wmp.cdromCollection;

        IWMPCdrom? cd = null;
        if (!string.IsNullOrWhiteSpace(driveSpecifier))
        {
            try { cd = cds.getByDriveSpecifier(NormalizeDrive(driveSpecifier)); }
            catch { /* ignore and fallback */ }
        }
        if (cd == null && cds.count > 0)
        {
            // pick the first cdrom that has a non-empty playlist
            for (int i = 0; i < cds.count; i++)
            {
                var c = cds.Item(i);
                try
                {
                    var pl = c?.Playlist;
                    if (pl != null && pl.count > 0)
                    {
                        cd = c;
                        break;
                    }
                }
                catch { /* continue */ }
            }
        }
        if (cd == null) return null;

        var playlist = cd.Playlist; // IWMPPlaylist
        if (playlist == null || playlist.count == 0) return null;

        var toc = new DiscToc();
        // Start offset of Track 1 is 150 frames
        int currentStart = 150;

        for (int i = 0; i < playlist.count; i++)
        {
            var media = playlist.get_Item(i); // IWMPMedia
            // Prefer the duration property as double seconds
            double seconds = 0;
            try
            {
                seconds = media?.duration ?? 0;
                if (seconds <= 0)
                {
                    // fallback to attribute text
                    var s = media?.getItemInfo("Duration");
                    if (!string.IsNullOrEmpty(s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                        seconds = d;
                }
            }
            catch
            {
                // ignore
            }

            toc.TrackOffsetsFrames.Add(currentStart);

            // Convert seconds to frames; round to nearest to minimize drift
            int frames = (int)Math.Round(seconds * 75.0, MidpointRounding.AwayFromZero);
            if (frames < 0) frames = 0;

            currentStart += frames;
        }

        toc.LeadoutOffsetFrames = currentStart;
        return toc.TrackCount > 0 ? toc : null;
    }

    private static string NormalizeDrive(string d)
    {
        d = d.Trim();
        if (d.Length == 1) return d + ":";
        if (d.Length >= 2 && d[1] == ':') return d.Substring(0, 2).ToUpperInvariant();
        return d.ToUpperInvariant();
    }
}