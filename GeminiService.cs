using System.Net.Http.Headers;
using System.Text.Json;
using System.Net.Http;

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
        try
        {
            // 构造请求体
            var body = new
            {
                model = "gemini-1.5-flash",
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                }
            };

            // 输出请求的内容
            Console.WriteLine("Request Body: " + JsonSerializer.Serialize(body));

            // 发送 POST 请求到 API
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key={_apiKey}";
            var resp = await _client.PostAsJsonAsync(url, body);

            // 检查是否请求成功
            if (!resp.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error: {resp.StatusCode} - {resp.ReasonPhrase}");
                var errorResponse = await resp.Content.ReadAsStringAsync();
                Console.WriteLine("Error Response: " + errorResponse);
                return "(error response)";
            }

            // 解析 API 响应
            var json = await resp.Content.ReadAsStringAsync();
            Console.WriteLine("API Response: " + json);

            // 提取并返回生成的内容
            using var doc = JsonDocument.Parse(json);
            var responseText = doc.RootElement.GetProperty("candidates")[0]
                               .GetProperty("content")
                               .GetProperty("parts")[0]
                               .GetProperty("text").GetString();

            return responseText ?? "(no response)";
        }
        catch (Exception ex)
        {
            // 捕获并输出异常
            Console.WriteLine($"Exception: {ex.Message}");
            return "(exception occurred)";
        }
    }
}
