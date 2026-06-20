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
        using var locResp = await _http.GetAsync("http://ip-api.com/json/?fields=status,city,lat,lon", ct);
        using var locDoc  = JsonDocument.Parse(await locResp.Content.ReadAsStringAsync(ct));
        var loc = locDoc.RootElement;
        if (loc.GetProperty("status").GetString() != "success")
            return "I couldn't peek outside right now.";

        double lat = loc.GetProperty("lat").GetDouble();
        double lon = loc.GetProperty("lon").GetDouble();
        string city = loc.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";

        string url = $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}" +
                     "&current=temperature_2m,weather_code&temperature_unit=fahrenheit";
        using var wResp = await _http.GetAsync(url, ct);
        using var wDoc  = JsonDocument.Parse(await wResp.Content.ReadAsStringAsync(ct));
        var cur  = wDoc.RootElement.GetProperty("current");
        double temp = cur.GetProperty("temperature_2m").GetDouble();
        int code    = cur.GetProperty("weather_code").GetInt32();
        string where = string.IsNullOrEmpty(city) ? "outside" : $"in {city}";
        return $"It's {Math.Round(temp)}°F and {WeatherText(code)} {where}.";
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
