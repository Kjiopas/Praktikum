using System.Collections.Generic;

namespace LumiSense.Models
{
    public class HomeViewModel
    {
        public UVReading? CurrentReading { get; set; }
        public UVSafetyRecommendation? SafetyRecommendation { get; set; }
        public UVStatistics? Statistics { get; set; }
        public List<UVReading>? History { get; set; }
    }
}