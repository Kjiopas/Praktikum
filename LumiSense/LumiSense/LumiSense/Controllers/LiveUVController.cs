using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using LumiSense.Data;
using LumiSense.Models;

namespace LumiSense.Controllers
{
    public class LiveUVController : Controller
    {
        private readonly ApplicationDbContext _context;
        private static readonly Random _random = new Random();
        private static readonly HttpClient _httpClient = new HttpClient();

        public LiveUVController(ApplicationDbContext context)
        {
            _context = context;
        }

        private UVSafetyRecommendation GetRecommendation(double uvValue)
        {
            if (uvValue <= 2.9)
            {
                return new UVSafetyRecommendation
                {
                    Status = "Low",
                    Message = "✅ SAFE TO GO OUT! Perfect weather! No protection needed.",
                    ColorCode = "#2ecc71",
                    Icon = "fa-smile-wink",
                    Protection = "No protection needed"
                };
            }
            if (uvValue <= 5.9)
            {
                return new UVSafetyRecommendation
                {
                    Status = "Moderate",
                    Message = "⚠️ MODERATE UV - Wear sunscreen SPF 30+, hat, and sunglasses.",
                    ColorCode = "#f39c12",
                    Icon = "fa-sunglasses",
                    Protection = "SPF 30+, hat, sunglasses"
                };
            }
            if (uvValue <= 7.9)
            {
                return new UVSafetyRecommendation
                {
                    Status = "High",
                    Message = "🔥 HIGH UV - SPF 50+ required. Seek shade 10AM-4PM. Reapply sunscreen every 2 hours.",
                    ColorCode = "#e67e22",
                    Icon = "fa-umbrella-beach",
                    Protection = "SPF 50+, shade, full coverage"
                };
            }
            if (uvValue <= 10.9)
            {
                return new UVSafetyRecommendation
                {
                    Status = "Very High",
                    Message = "🚨 VERY HIGH UV! Limit outdoor time. Stay in shade! Wear protective clothing.",
                    ColorCode = "#e74c3c",
                    Icon = "fa-exclamation-triangle",
                    Protection = "Avoid exposure, full protection"
                };
            }
            return new UVSafetyRecommendation
            {
                Status = "Extreme",
                Message = "‼️ EXTREME UV! DANGER! Stay indoors! Extreme risk of sunburn and skin damage.",
                ColorCode = "#c0392b",
                Icon = "fa-skull",
                Protection = "STAY INDOORS"
            };
        }

        private async Task<double?> GetRealUVData(double lat, double lng)
        {
            try
            {
                var apiKey = "openuv-dpvbrmnahaqfm-io";
                var url = $"https://api.openuv.io/api/v1/uv?lat={lat}&lng={lng}";

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("x-access-token", apiKey);

                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var uv = doc.RootElement.GetProperty("result").GetProperty("uv").GetDouble();
                    return Math.Round(uv, 1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting UV data: {ex.Message}");
            }
            return null;
        }

        private async Task<string?> GetLocationName(double lat, double lng)
        {
            try
            {
                var url = $"https://nominatim.openstreetmap.org/reverse?format=json&lat={lat}&lon={lng}&zoom=10";
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("address", out var address))
                    {
                        if (address.TryGetProperty("city", out var city))
                            return city.GetString() ?? $"{lat:F2}, {lng:F2}";
                        if (address.TryGetProperty("town", out var town))
                            return town.GetString() ?? $"{lat:F2}, {lng:F2}";
                        if (address.TryGetProperty("village", out var village))
                            return village.GetString() ?? $"{lat:F2}, {lng:F2}";
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting location name: {ex.Message}");
            }
            return $"{lat:F2}, {lng:F2}";
        }

        public async Task<IActionResult> Index()
        {
            var latestReading = await _context.UVReadings
                .OrderByDescending(r => r.Timestamp)
                .FirstOrDefaultAsync();

            if (latestReading == null)
            {
                latestReading = new UVReading
                {
                    Value = 4.5,
                    Timestamp = DateTime.Now,
                    Location = "Demo Location",
                    DeviceId = "Arduino_UVMeter_001",
                    SafetyStatus = "Moderate"
                };
                _context.UVReadings.Add(latestReading);
                await _context.SaveChangesAsync();
            }

            var today = DateTime.Today;
            var todayReadings = await _context.UVReadings
                .Where(r => r.Timestamp.Date == today)
                .ToListAsync();

            var statistics = new UVStatistics
            {
                CurrentUV = latestReading.Value,
                AverageUV = todayReadings.Any() ? Math.Round(todayReadings.Average(r => r.Value), 1) : 0,
                MaxUVToday = todayReadings.Any() ? todayReadings.Max(r => r.Value) : 0,
                PeakTime = todayReadings.Any() ? todayReadings.OrderByDescending(r => r.Value).First().Timestamp.ToString("hh:mm tt") : "N/A",
                ReadingsCount = todayReadings.Count
            };

            var cutoff = DateTime.Now.AddHours(-24);
            var history = await _context.UVReadings
                .Where(r => r.Timestamp >= cutoff)
                .OrderBy(r => r.Timestamp)
                .ToListAsync();

            var viewModel = new HomeViewModel
            {
                CurrentReading = latestReading,
                SafetyRecommendation = GetRecommendation(latestReading.Value),
                Statistics = statistics,
                History = history
            };

            return View(viewModel);
        }

        [HttpPost]
        public async Task<IActionResult> GetUVByLocation([FromBody] LocationRequest request)
        {
            if (request == null || request.Lat == 0 || request.Lng == 0)
            {
                return Json(new { success = false, message = "Invalid location coordinates" });
            }

            var realUV = await GetRealUVData(request.Lat, request.Lng);
            var locationName = await GetLocationName(request.Lat, request.Lng);

            if (realUV.HasValue)
            {
                var reading = new UVReading
                {
                    Value = realUV.Value,
                    Timestamp = DateTime.Now,
                    Location = locationName ?? "Unknown Location",
                    DeviceId = "OpenUV_API",
                    SafetyStatus = GetRecommendation(realUV.Value).Status
                };

                _context.UVReadings.Add(reading);
                await _context.SaveChangesAsync();

                var recommendation = GetRecommendation(realUV.Value);

                var today = DateTime.Today;
                var todayReadings = await _context.UVReadings
                    .Where(r => r.Timestamp.Date == today)
                    .ToListAsync();

                var statistics = new
                {
                    CurrentUV = realUV.Value,
                    AverageUV = todayReadings.Any() ? Math.Round(todayReadings.Average(r => r.Value), 1) : realUV.Value,
                    MaxUVToday = todayReadings.Any() ? todayReadings.Max(r => r.Value) : realUV.Value,
                    PeakTime = todayReadings.Any() ? todayReadings.OrderByDescending(r => r.Value).First().Timestamp.ToString("hh:mm tt") : DateTime.Now.ToString("hh:mm tt"),
                    ReadingsCount = todayReadings.Count
                };

                return Json(new
                {
                    success = true,
                    isRealData = true,
                    reading = new
                    {
                        realUV.Value,
                        Timestamp = reading.Timestamp.ToString("HH:mm:ss"),
                        reading.SafetyStatus,
                        Location = locationName ?? "Unknown Location"
                    },
                    recommendation = new
                    {
                        recommendation.Status,
                        recommendation.Message,
                        recommendation.ColorCode,
                        recommendation.Icon,
                        recommendation.Protection
                    },
                    statistics
                });
            }

            return Json(new { success = false, message = "Could not get UV data for this location." });
        }

        [HttpPost]
        public async Task<IActionResult> GenerateNewReading()
        {
            var hour = DateTime.Now.Hour;
            double finalUV;

            if (hour >= 10 && hour <= 16)
                finalUV = 6 + (_random.NextDouble() * 5);
            else if ((hour >= 8 && hour < 10) || (hour > 16 && hour <= 18))
                finalUV = 3 + (_random.NextDouble() * 4);
            else if (hour >= 6 && hour < 8)
                finalUV = 1 + (_random.NextDouble() * 2);
            else
                finalUV = _random.NextDouble() * 1;

            finalUV = Math.Round(finalUV, 1);
            finalUV = Math.Max(0, Math.Min(12, finalUV));

            var newReading = new UVReading
            {
                Value = finalUV,
                Timestamp = DateTime.Now,
                Location = "Simulated Data",
                DeviceId = "Arduino_Simulated",
                SafetyStatus = GetRecommendation(finalUV).Status
            };

            _context.UVReadings.Add(newReading);
            await _context.SaveChangesAsync();

            var recommendation = GetRecommendation(finalUV);

            var today = DateTime.Today;
            var todayReadings = await _context.UVReadings
                .Where(r => r.Timestamp.Date == today)
                .ToListAsync();

            var statistics = new
            {
                CurrentUV = finalUV,
                AverageUV = todayReadings.Any() ? Math.Round(todayReadings.Average(r => r.Value), 1) : finalUV,
                MaxUVToday = todayReadings.Any() ? todayReadings.Max(r => r.Value) : finalUV,
                PeakTime = todayReadings.Any() ? todayReadings.OrderByDescending(r => r.Value).First().Timestamp.ToString("hh:mm tt") : DateTime.Now.ToString("hh:mm tt"),
                ReadingsCount = todayReadings.Count
            };

            return Json(new
            {
                success = true,
                isRealData = false,
                reading = new
                {
                    newReading.Value,
                    Timestamp = newReading.Timestamp.ToString("HH:mm:ss"),
                    newReading.SafetyStatus,
                    Location = "Simulated Data"
                },
                recommendation = new
                {
                    recommendation.Status,
                    recommendation.Message,
                    recommendation.ColorCode,
                    recommendation.Icon,
                    recommendation.Protection
                },
                statistics
            });
        }

        [HttpGet]
        public async Task<IActionResult> GetHistory()
        {
            var cutoff = DateTime.Now.AddHours(-24);
            var history = await _context.UVReadings
                .Where(r => r.Timestamp >= cutoff)
                .OrderBy(r => r.Timestamp)
                .Select(r => new { r.Value, Timestamp = r.Timestamp.ToString("HH:mm") })
                .ToListAsync();

            return Json(history);
        }
    }

    // LocationRequest class - only defined once here
    public class LocationRequest
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
    }
}