using System.Net.Http.Headers;
using System.Text.Json;

public class GeminiService
{
    private readonly string _apiKey;
    private readonly HttpClient _client;

    public GeminiService(string apiKey)
    {
        _apiKey = apiKey;
        _client = new HttpClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public async Task<string> GenerateAsync(string prompt)
    {
        var body = new
        {
            model = "gemini-1.5-flash",
            contents = new[]
            {
                new { parts = new[] { new { text = prompt } } }
            }
        };

        var resp = await _client.PostAsJsonAsync("https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key=" + _apiKey, body);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("candidates")[0]
                  .GetProperty("content")
                  .GetProperty("parts")[0]
                  .GetProperty("text").GetString() ?? "(no response)";
    }
}
