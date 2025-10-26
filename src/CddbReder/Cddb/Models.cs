namespace CddbReder.Cddb;

public class CddbMatch
{
    public string Category { get; set; } = "";
    public string DiscId { get; set; } = "";
    public string Title { get; set; } = ""; // "Artist / Album"
}

public class XmcdRecord
{
    public string Category { get; set; } = "";
    public string DiscId { get; set; } = "";
    public string DTitle { get; set; } = ""; // "Artist / Album"
    public string DYear { get; set; } = "";
    public string DGenre { get; set; } = "";
    public List<string> TrackTitles { get; set; } = new();
    public Dictionary<string, string> Raw { get; set; } = new(); // 他フィールド用
}