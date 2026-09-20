using System.Net.Http;
using System.Text.Json;

namespace DesktopPet.Services.Ai;

/// <summary>
/// The small set of "cute" things the cat can look up for you. Everything is best-effort and
/// offline-safe — a failure just returns a friendly note rather than throwing. System stats
/// reuse the existing <see cref="SystemMonitor"/> rather than re-reading the machine.
/// </summary>
public sealed class CuteTools
{
    private readonly SystemMonitor? _system;
    private readonly HttpClient _http;

    public CuteTools(SystemMonitor? system, HttpClient http)
    {
        _system = system;
        _http   = http;
    }

    public async Task<string> RunAsync(string name, CancellationToken ct)
    {
        try
        {
            return name switch
            {
                "get_time"     => GetTime(),
                "system_stats" => GetSystemStats(),
                "weather"      => await GetWeatherAsync(ct),
                _              => $"(no tool called '{name}')"
            };
        }
        catch
        {
            return $"(couldn't check '{name}' right now)";
        }
    }

    private static string GetTime() => $"It is {DateTime.Now:dddd, MMMM d, h:mm tt}.";

    private string GetSystemStats()
    {
        if (_system == null) return "I can't sense the computer right now.";
        _system.Poll();
        string cpu  = $"{Math.Round(_system.CpuLoad * 100)}% CPU";
        string batt = _system.BatteryPercent >= 0
            ? $", battery {_system.BatteryPercent}%{(_system.OnBattery ? " (unplugged)" : " (plugged in)")}"
            : "";
        string focus = _system.Foreground switch
        {
            AppContextKind.Focus  => ", you're in a work app",
            AppContextKind.Browse => ", you're browsing the web",
            AppContextKind.Play   => ", you're in something fun",
            _ => ""
        };
        return $"Right now: {cpu}{batt}{focus}.";
    }

    private async Task<string> GetWeatherAsync(CancellationToken ct)
    {
        // Keyless: locate by IP, then pull current conditions from open-meteo (also keyless).
        // HTTPS throughout — the old ip-api.com endpoint is cleartext-only on its free tier, so
        // the user's IP and their inferred city travelled the network in the clear.
        using var locResp = await _http.GetAsync("https://ipapi.co/json/", ct);
        if (!locResp.IsSuccessStatusCode) return "I couldn't peek outside right now.";

        using var locDoc = JsonDocument.Parse(await locResp.Content.ReadAsStringAsync(ct));
        var loc = locDoc.RootElement;
        if (loc.TryGetProperty("error", out _)) return "I couldn't peek outside right now.";

        if (!TryReadCoordinate(loc, "latitude", out double lat) ||
            !TryReadCoordinate(loc, "longitude", out double lon))
            return "I couldn't peek outside right now.";

        string city = loc.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";

        // Invariant formatting: a comma-decimal locale would otherwise emit "latitude=39,75".
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string url = $"https://api.open-meteo.com/v1/forecast?latitude={lat.ToString(inv)}&longitude={lon.ToString(inv)}" +
                     "&current=temperature_2m,weather_code&temperature_unit=fahrenheit";
        using var wResp = await _http.GetAsync(url, ct);
        using var wDoc  = JsonDocument.Parse(await wResp.Content.ReadAsStringAsync(ct));
        var cur  = wDoc.RootElement.GetProperty("current");
        double temp = cur.GetProperty("temperature_2m").GetDouble();
        int code    = cur.GetProperty("weather_code").GetInt32();
        string where = string.IsNullOrEmpty(city) ? "outside" : $"in {city}";
        return $"It's {Math.Round(temp)}°F and {WeatherText(code)} {where}.";
    }

    /// <summary>Read a coordinate that geolocation services return as either a JSON number or a
    /// quoted string, depending on the provider and the day.</summary>
    private static bool TryReadCoordinate(JsonElement obj, string name, out double value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out var el)) return false;
        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetDouble(out value),
            JsonValueKind.String => double.TryParse(el.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value),
            _ => false
        };
    }

    private static string WeatherText(int code) => code switch
    {
        0               => "clear",
        1 or 2          => "mostly clear",
        3               => "cloudy",
        45 or 48        => "foggy",
        >= 51 and <= 67 => "drizzly",
        >= 71 and <= 77 => "snowy",
        >= 80 and <= 82 => "rainy",
        >= 95           => "stormy",
        _               => "mild"
    };
}
