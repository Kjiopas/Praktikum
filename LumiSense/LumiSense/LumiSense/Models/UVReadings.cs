using System;
using System.ComponentModel.DataAnnotations;

namespace LumiSense.Models
{
    public class UVReading
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public double Value { get; set; }

        [Required]
        public DateTime Timestamp { get; set; }

        [MaxLength(100)]
        public string? Location { get; set; }

        [MaxLength(50)]
        public string? SafetyStatus { get; set; }

        [MaxLength(50)]
        public string? DeviceId { get; set; }
    }
}