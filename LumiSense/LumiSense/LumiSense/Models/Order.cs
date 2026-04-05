using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace LumiSense.Models
{
    public class Order
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string CustomerName { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        [EmailAddress]
        public string CustomerEmail { get; set; } = string.Empty;

        [MaxLength(30)]
        public string? PhoneNumber { get; set; }

        [Required]
        public DateTime OrderDate { get; set; } = DateTime.Now;

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }

        [MaxLength(500)]
        public string? CourierInfo { get; set; }

        [MaxLength(30)]
        public string Status { get; set; } = "Pending";

        // Delivery
        [MaxLength(20)]
        public string DeliveryMethod { get; set; } = "office"; // office | address

        [MaxLength(50)]
        public string? DeliveryCompany { get; set; }

        [MaxLength(120)]
        public string? DeliveryPointName { get; set; }

        [MaxLength(20)]
        public string? DeliveryPointType { get; set; } // office | locker

        [MaxLength(80)]
        public string? DeliveryPointCity { get; set; }

        [MaxLength(200)]
        public string? DeliveryPointAddress { get; set; }

        public double? DeliveryPointLat { get; set; }
        public double? DeliveryPointLng { get; set; }

        [MaxLength(80)]
        public string? AddressCity { get; set; }

        [MaxLength(200)]
        public string? AddressLine1 { get; set; }

        [MaxLength(200)]
        public string? AddressLine2 { get; set; }

        [MaxLength(20)]
        public string? AddressPostCode { get; set; }

        public string? UserId { get; set; }
        public IdentityUser? User { get; set; }

        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    }
}