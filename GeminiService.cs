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
                //model = "gemini-1.5-flash",
                model= "gemini-2.5-flash",
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                }
            };

            Console.WriteLine("Request Body: " + JsonSerializer.Serialize(body));
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";

            //var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_apiKey}";

            var resp = await _client.PostAsJsonAsync(url, body);

            if (!resp.IsSuccessStatusCode)
            {
                var errorResponse = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"Error {resp.StatusCode}: {errorResponse}");
                return "(error response)";
            }

            var json = await resp.Content.ReadAsStringAsync();
            Console.WriteLine("API Response: " + json);

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("candidates", out var candidates) &&
                candidates.GetArrayLength() > 0 &&
                candidates[0].TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) &&
                parts.GetArrayLength() > 0 &&
                parts[0].TryGetProperty("text", out var text))
            {
                return text.GetString() ?? "(empty)";
            }

            return "(no response)";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Exception: {ex.Message}");
            return "(exception occurred)";
        }
    }
}
