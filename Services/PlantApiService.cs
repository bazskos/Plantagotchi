using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Plantagotchi.Services;

public class PlantWeatherAlert
{
    public string PlantName { get; set; } = string.Empty;
    public string AlertMessage { get; set; } = string.Empty;
}

public class PlantApiService
{
    private readonly HttpClient _httpClient;

    public PlantApiService()
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "PlantagotchiApp/1.0");

        try
        {
            DotNetEnv.Env.TraversePath().Load();
        }
        catch
        {
            Console.WriteLine("[WARN] Failed to load the .env file.");
        }
    }

    private async Task<HttpResponseMessage> SendOpenAiRequestAsync(object requestBody)
    {
        string openAiUrl = "https://" + "api.openai.com" + "/v1/chat/completions";
        var request = new HttpRequestMessage(HttpMethod.Post, openAiUrl);

        string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new Exception("MISSING API KEY! Check your .env file!");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
        return await _httpClient.SendAsync(request);
    }

    public async Task<(string ScientificName, string CareGuide, int WateringIntervalDays)?> AnalyzePlantImageAsync(string imagePath)
    {
        if (!File.Exists(imagePath)) return null;
        try
        {
            byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
            string base64Image = Convert.ToBase64String(imageBytes);
            string mimeType = Path.GetExtension(imagePath).TrimStart('.').ToLower() == "png" ? "image/png" : "image/jpeg";

            var requestBody = new
            {
                model = "gpt-4o",
                messages = new[] {
                    new { role = "user", content = new object[] {
                        new { type = "text", text = "You are a master botanist. Identify this plant. Respond STRICTLY in valid JSON. Keys required: 'scientificName' (string), 'careGuide' (string, 3-4 sentences covering light, temp, soil, and MUST explicitly mention watering frequency), and 'wateringIntervalDays' (integer, the average number of days between waterings)." },
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64Image}" } }
                    }}
                },
                temperature = 0.2
            };

            var response = await SendOpenAiRequestAsync(requestBody);

            if (response.IsSuccessStatusCode)
            {
                using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                string replyMessage = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? "";
                if (replyMessage.StartsWith("```json")) replyMessage = replyMessage.Replace("```json", "").Replace("```", "").Trim();
                using JsonDocument aiDoc = JsonDocument.Parse(replyMessage);
                return (
                    aiDoc.RootElement.GetProperty("scientificName").GetString() ?? "Unknown",
                    aiDoc.RootElement.GetProperty("careGuide").GetString() ?? "No guide.",
                    aiDoc.RootElement.TryGetProperty("wateringIntervalDays", out var i) ? i.GetInt32() : 7
                );
            }
        }
        catch { return null; }
        return null;
    }

    public async Task<string?> DiagnosePlantAsync(string imagePath)
    {
        if (!File.Exists(imagePath)) return "Error: Cannot find the picture.";
        try
        {
            byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
            string base64Image = Convert.ToBase64String(imageBytes);
            string mimeType = Path.GetExtension(imagePath).TrimStart('.').ToLower() == "png" ? "image/png" : "image/jpeg";

            var requestBody = new
            {
                model = "gpt-4o",
                messages = new[] {
                    new { role = "user", content = new object[] {
                        new { type = "text", text = "Inspect this plant image. If it looks healthy, explicitly praise it. If you see signs of disease, pests, or issues, provide a brief remedy. Max 3-4 sentences." },
                        new { type = "image_url", image_url = new { url = $"data:{mimeType};base64,{base64Image}" } }
                    }}
                },
                temperature = 0.3
            };

            var response = await SendOpenAiRequestAsync(requestBody);

            if (response.IsSuccessStatusCode)
            {
                using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim();
            }
        }
        catch (Exception ex) { return $"Code Error: {ex.Message}"; }
        return "Unexpected error.";
    }

    private string GetWeatherConditionFromCode(int code) => code switch
    {
        0 => "Clear sky",
        1 or 2 or 3 => "Partly cloudy",
        45 or 48 => "Fog",
        51 or 53 or 55 => "Drizzle",
        61 or 63 or 65 => "Rain",
        71 or 73 or 75 or 77 => "Snow",
        95 or 96 or 99 => "Thunderstorm",
        _ => "Variable"
    };

    private async Task<(double latitude, double longitude, string city)> GetLocationAsync()
    {
        try
        {
            string ipApiUrl = "https://" + "ipapi.co" + "/json/";
            string response = await _httpClient.GetStringAsync(ipApiUrl);
            using JsonDocument doc = JsonDocument.Parse(response);

            double lat = doc.RootElement.GetProperty("latitude").GetDouble();
            double lon = doc.RootElement.GetProperty("longitude").GetDouble();
            string city = doc.RootElement.GetProperty("city").GetString() ?? "Unknown City";

            return (lat, lon, city);
        }
        catch
        {
            return (46.4925, 18.4111, "Hőgyész (Fallback)");
        }
    }

    public async Task<(string currentDisplay, string forecastForAI)> GetWeatherAndForecastAsync()
    {
        try
        {
            var (lat, lon, city) = await GetLocationAsync();

            string latStr = lat.ToString(CultureInfo.InvariantCulture);
            string lonStr = lon.ToString(CultureInfo.InvariantCulture);
            string meteoBase = "https://" + "api.open-meteo.com" + "/v1/forecast";

            string url = $"{meteoBase}?latitude={latStr}&longitude={lonStr}&current_weather=true";
            var response = await _httpClient.GetStringAsync(url);
            using JsonDocument doc = JsonDocument.Parse(response);

            var current = doc.RootElement.GetProperty("current_weather");
            double temp = current.GetProperty("temperature").GetDouble();
            int code = current.GetProperty("weathercode").GetInt32();

            string currentDisplay = $"{city}: {temp}°C, {GetWeatherConditionFromCode(code)}";

            string forecastUrl = $"{meteoBase}?latitude={latStr}&longitude={lonStr}&daily=weathercode,temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=7";
            var forecastResponse = await _httpClient.GetStringAsync(forecastUrl);
            using JsonDocument forecastDoc = JsonDocument.Parse(forecastResponse);
            var daily = forecastDoc.RootElement.GetProperty("daily");

            var times = daily.GetProperty("time").EnumerateArray().Select(x => x.GetString()).ToList();
            var codes = daily.GetProperty("weathercode").EnumerateArray().Select(x => x.GetInt32()).ToList();
            var maxTemps = daily.GetProperty("temperature_2m_max").EnumerateArray().Select(x => x.GetDouble()).ToList();
            var minTemps = daily.GetProperty("temperature_2m_min").EnumerateArray().Select(x => x.GetDouble()).ToList();

            var forecastBuilder = new StringBuilder();
            for (int i = 0; i < times.Count; i++)
            {
                forecastBuilder.Append($"Day {i + 1} ({DateTime.Parse(times[i]!).ToString("ddd", CultureInfo.InvariantCulture)}): Min {minTemps[i]}°C, Max {maxTemps[i]}°C, {GetWeatherConditionFromCode(codes[i])}. ");
            }

            return (currentDisplay, forecastBuilder.ToString());
        }
        catch (Exception ex)
        {
            return ($"Err: {ex.Message}", string.Empty);
        }
    }

    public async Task<List<PlantWeatherAlert>> GetPlantSpecificWeatherAlertsAsync(string forecast, string plantsList)
    {
        if (string.IsNullOrWhiteSpace(plantsList) || string.IsNullOrWhiteSpace(forecast)) return new List<PlantWeatherAlert>();

        // Test data for extreme weather:
        // forecast = "WARNING: EXTREME BLIZZARD, -20°C HEAVY SNOW EVERY DAY THIS WEEK. BRING ALL PLANTS INSIDE IMMEDIATELY.";

        try
        {
            var requestBody = new
            {
                model = "gpt-4o",
                messages = new[]
                {
                    new { role = "system", content = "You are a smart gardening assistant." },
                    new { role = "user", content = $@"The 7-day weather forecast is: {forecast}. The user has these plants: {plantsList}. Write a specific, friendly advisory in English for each plant that needs special attention. Mention exact days. Respond STRICTLY with a JSON array of objects: [{{""plantName"": ""..."", ""alertMessage"": ""...""}}]. If all plants are fine, return an empty array []." }
                },
                temperature = 0.2
            };

            var response = await SendOpenAiRequestAsync(requestBody);

            if (response.IsSuccessStatusCode)
            {
                using JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                string reply = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? "[]";

                // 1. Clean up Markdown formatting
                if (reply.StartsWith("```json")) reply = reply.Replace("```json", "").Replace("```", "").Trim();
                else if (reply.StartsWith("```")) reply = reply.Replace("```", "").Trim();

                // 2. Bulletproof extraction
                using JsonDocument replyDoc = JsonDocument.Parse(reply);
                JsonElement root = replyDoc.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    bool foundArray = false;
                    foreach (var property in root.EnumerateObject())
                    {
                        if (property.Value.ValueKind == JsonValueKind.Array)
                        {
                            reply = property.Value.GetRawText();
                            foundArray = true;
                            break;
                        }
                    }
                    if (!foundArray) reply = "[" + root.GetRawText() + "]";
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    reply = root.GetRawText();
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<List<PlantWeatherAlert>>(reply, options) ?? new List<PlantWeatherAlert>();
            }
            else
            {
                string errorTxt = await response.Content.ReadAsStringAsync();
                return new List<PlantWeatherAlert> { new PlantWeatherAlert { PlantName = "System", AlertMessage = $"OpenAI Error ({response.StatusCode}): {errorTxt}" } };
            }
        }
        catch (Exception ex)
        {
            return new List<PlantWeatherAlert> { new PlantWeatherAlert { PlantName = "System", AlertMessage = $"Critical processing error: {ex.Message}" } };
        }
    }
}