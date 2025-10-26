namespace CddbReder.Cddb;

public class DiscToc
{
    // トラック開始オフセット（フレーム数: 75fps）
    public List<int> TrackOffsetsFrames { get; } = new();

    // リードアウトオフセット（フレーム数）
    public int LeadoutOffsetFrames { get; set; }

    public int TrackCount => TrackOffsetsFrames.Count;

    public int TotalSeconds
    {
        get
        {
            if (TrackOffsetsFrames.Count == 0) return 0;
            int start = TrackOffsetsFrames[0];
            return (LeadoutOffsetFrames - start) / 75;
        }
    }
}