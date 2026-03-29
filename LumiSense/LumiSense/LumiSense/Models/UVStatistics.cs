namespace LumiSense.Models
{
    public class UVStatistics
    {
        public double CurrentUV { get; set; }
        public double AverageUV { get; set; }
        public double MaxUVToday { get; set; }
        public string PeakTime { get; set; } = string.Empty;
        public int ReadingsCount { get; set; }
    }
}