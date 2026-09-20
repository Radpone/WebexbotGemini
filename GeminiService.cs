using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class GeminiService
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";
    private const string Model = "gemini-3.8-flash"; // 🔥 最新版本
    private readonly string _apiKey;
    private static readonly HttpClient _client = new HttpClient();

    public GeminiService(string apiKey)
    {
        _apiKey = apiKey;
    }

    public async Task<string> GenerateAsync(string prompt)
    {
        try
        {
            var body = new
            {
                model = Model,
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                }
            };

            var url = $"{BaseUrl}{Model}:generateContent?key={_apiKey}";
            var json = JsonSerializer.Serialize(body);
            var resp = await _client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

            if (!resp.IsSuccessStatusCode)
            {
                var errorResponse = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"Error {resp.StatusCode}: {errorResponse}");
                return "(error response)";
            }

            var responseJson = await resp.Content.ReadAsStringAsync();
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
