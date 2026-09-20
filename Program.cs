using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

string? webhookSecret = Environment.GetEnvironmentVariable("WEBEX_SECRET");
string? botToken = Environment.GetEnvironmentVariable("WEBEX_BOT_TOKEN");
string? googleApiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");

// 註冊 GeminiService
builder.Services.AddSingleton<GeminiService>(sp =>
{
    if (string.IsNullOrEmpty(googleApiKey))
    {
        throw new InvalidOperationException("Missing GOOGLE_API_KEY environment variable.");
    }
    return new GeminiService(googleApiKey);
});

var app = builder.Build();

// 綁定 Render 提供的 PORT（預設 8080）
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");

// 健康檢查路由
app.MapGet("/health", () => Results.Ok("Service is healthy!"));

// 取得 Bot 自己的 personId
string botPersonId = await GetBotPersonId(botToken!);

// Webhook 路由
app.MapPost("/webhook", async (HttpRequest req, GeminiService gemini) =>
{
    try
    {
        req.EnableBuffering();
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms);
        var bodyBytes = ms.ToArray();
        req.Body.Position = 0;

        var json = Encoding.UTF8.GetString(bodyBytes);
        var payload = JsonNode.Parse(json);

        var resource = payload?["resource"]?.ToString();
        if (resource == "messages")
        {
            var data = payload?["data"];
            var roomId = data?["roomId"]?.ToString();
            var messageId = data?["id"]?.ToString();
            var senderId = data?["personId"]?.ToString();

            if (senderId == botPersonId) return Results.Ok();

            if (!string.IsNullOrEmpty(roomId) && !string.IsNullOrEmpty(messageId))
            {
                var userMsg = await GetWebexMessage(botToken!, messageId);
                if (!string.IsNullOrEmpty(userMsg))
                {
                    var reply = await gemini.GenerateAsync(userMsg);
                    await SendWebexMessage(botToken!, roomId, reply);
                }
            }
        }

        return Results.Ok();
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error: " + ex.Message);
        return Results.Problem("Internal Server Error", statusCode: 500);
    }
});

app.Run();

// Webex API
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

static async Task<string> GetBotPersonId(string botToken)
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", botToken);
    var resp = await client.GetAsync("https://webexapis.com/v1/people/me");
    var json = await resp.Content.ReadAsStringAsync();
    var node = JsonNode.Parse(json);
    return node?["id"]?.ToString() ?? string.Empty;
}
