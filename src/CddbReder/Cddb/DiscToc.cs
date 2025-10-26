namespace CddbReder.Cddb;

public class DiscToc
{
    // トラック開始オフセット（フレーム数: 75fps）
    public List<int> TrackOffsetsFrames { get; } = [];

    // リードアウトオフセット（フレーム数）
    public int LeadoutOffsetFrames { get; set; }

    public int TrackCount => TrackOffsetsFrames.Count;

    public int TotalSeconds
    {
        get
        {
            if (TrackOffsetsFrames.Count == 0) return 0;
            int start = TrackOffsetsFrames[0];
            int totalFrames = LeadoutOffsetFrames - start;
            if (totalFrames <= 0) return 0;
            // FreeDB/CDDB の仕様では総再生時間は端数を切り上げた秒数を用いる
            return (totalFrames + 74) / 75;
        }
    }
}