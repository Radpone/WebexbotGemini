using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
string? webhookSecret = Environment.GetEnvironmentVariable("WEBEX_SECRET");
string? botToken = Environment.GetEnvironmentVariable("WEBEX_BOT_TOKEN");
string? googleApiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");

var gemini = new GeminiService(googleApiKey!);

app.MapPost("/webhook", async (HttpRequest req) =>
{
    req.EnableBuffering();
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    var bodyBytes = ms.ToArray();
    req.Body.Position = 0;

    var signature = req.Headers["X-Spark-Signature"].FirstOrDefault()
                 ?? req.Headers["X-Webex-Signature"].FirstOrDefault();

    if (string.IsNullOrEmpty(signature) || !VerifySignature(bodyBytes, webhookSecret!, signature))
        return Results.BadRequest("Invalid signature");

    var json = Encoding.UTF8.GetString(bodyBytes);
    var payload = JsonNode.Parse(json);

    var resource = payload?["resource"]?.ToString();
    if (resource == "messages")
    {
        var data = payload?["data"];
        var roomId = data?["roomId"]?.ToString();
        var messageId = data?["id"]?.ToString();

        if (!string.IsNullOrEmpty(roomId) && !string.IsNullOrEmpty(messageId))
        {
            // 取得使用者原始訊息
            var userMsg = await GetWebexMessage(botToken!, messageId);

            if (!string.IsNullOrEmpty(userMsg))
            {
                // 呼叫 Gemini 生成回覆
                var reply = await gemini.GenerateAsync(userMsg);
                await SendWebexMessage(botToken!, roomId, reply);
            }
        }
    }

    return Results.Ok();
});

app.Run();

static bool VerifySignature(byte[] body, string secret, string signature)
{
    using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
    var computed = hmac.ComputeHash(body);
    var computedHex = BitConverter.ToString(computed).Replace("-", "").ToLowerInvariant();
    return computedHex == signature.ToLowerInvariant();
}

static async Task<string?> GetWebexMessage(string botToken, string messageId)
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.Authorization = new("Bearer", botToken);
    var resp = await client.GetAsync($"https://webexapis.com/v1/messages/{messageId}");
    if (!resp.IsSuccessStatusCode) return null;
    var json = await resp.Content.ReadAsStringAsync();
    var node = JsonNode.Parse(json);
    return node?["text"]?.ToString();
}

static async Task SendWebexMessage(string botToken, string roomId, string text)
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.Authorization = new("Bearer", botToken);
    var payload = new { roomId = roomId, text = text };
    await client.PostAsJsonAsync("https://webexapis.com/v1/messages", payload);
}
