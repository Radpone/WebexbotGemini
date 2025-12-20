using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

public class GeminiService
{
    private readonly string _apiKey;
    private readonly HttpClient _client;

    public GeminiService(string apiKey)
    {
        _apiKey = apiKey;
        _client = new HttpClient();
    }
public async Task<string> GenerateAsync(string prompt)
{
    try
    {
        var body = new
        {
            model = "gemini-2.5-flash",
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            }
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";
        var json = JsonSerializer.Serialize(body);
        var resp = await _client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

        if (!resp.IsSuccessStatusCode)
        {
            var errorResponse = await resp.Content.ReadAsStringAsync();
            Console.WriteLine($"Error {resp.StatusCode}: {errorResponse}");
            return "(error response)";
        }

        var responseJson = await resp.Content.ReadAsStringAsync();
        Console.WriteLine("API Response: " + responseJson);

        using var doc = JsonDocument.Parse(responseJson);
        if (doc.RootElement.TryGetProperty("candidates", out var candidates))
        {
            foreach (var candidate in candidates.EnumerateArray())
            {
                if (candidate.TryGetProperty("content", out var content) &&
                    content.TryGetProperty("parts", out var parts))
                {
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var text))
                        {
                            return text.GetString() ?? "(empty)";
                        }
                    }
                }
            }
        }

        return "(no response)";
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Exception: {ex.Message}\n{ex.StackTrace}");
        return "(exception occurred)";
    }
}
}
