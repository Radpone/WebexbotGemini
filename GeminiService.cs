using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class GeminiStreamService
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";
    private const string Model = "gemini-2.5-flash"; 
    private readonly string _apiKey;

    private static readonly HttpClient _client = new HttpClient();

    public GeminiStreamService(string apiKey)
    {
        _apiKey = apiKey;
    }

    /// <summary>
    /// Streaming 回覆，帶 System Instruction + History
    /// </summary>
    public async IAsyncEnumerable<string> GenerateStreamAsync(
        string systemInstruction,
        string userPrompt,
        List<ChatTurn>? history = null)
    {
        var url = $"{BaseUrl}{Model}:streamGenerateContent?key={_apiKey}";

        // 🔥 組成多輪對話（Gemini 格式）
        var contents = new List<object>();

        if (!string.IsNullOrWhiteSpace(systemInstruction))
        {
            contents.Add(new
            {
                role = "system",
                parts = new[]
                {
                    new { text = systemInstruction }
                }
            });
        }

        if (history != null)
        {
            foreach (var turn in history)
            {
                contents.Add(new
                {
                    role = "user",
                    parts = new[] { new { text = turn.User } }
                });

                contents.Add(new
                {
                    role = "model",
                    parts = new[] { new { text = turn.Assistant } }
                });
            }
        }

        // 加入當前使用者訊息
        contents.Add(new
        {
            role = "user",
            parts = new[] { new { text = userPrompt } }
        });

        var body = new
        {
            model = Model,
            contents = contents
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };

        var response = await _client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead
        );

        var stream = await response.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            var line = await reader.ReadLineAsync();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Google 會送來 "data: {...}" 格式 → 要解析 JSON
            if (!line.StartsWith("data:")) 
                continue;

            var json = line.Substring(5).Trim();
            if (json == "[DONE]") yield break;

            try
            {
                var doc = JsonDocument.Parse(json);

                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                if (!string.IsNullOrEmpty(text))
                    yield return text;  // 🔥 Streaming 回傳片段
            }
            catch
            {
                // ignore parse error
            }
        }
    }
}

public class ChatTurn
{
    public string User { get; set; } = "";
    public string Assistant { get; set; } = "";
}
