using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

string webhookSecret = Environment.GetEnvironmentVariable("WEBEX_WEBHOOK_SECRET")
    ?? Environment.GetEnvironmentVariable("WEBEX_SECRET")
    ?? throw new InvalidOperationException("Missing WEBEX_WEBHOOK_SECRET environment variable.");
string botToken = Environment.GetEnvironmentVariable("WEBEX_BOT_TOKEN")
    ?? throw new InvalidOperationException("Missing WEBEX_BOT_TOKEN environment variable.");
string googleApiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
    ?? throw new InvalidOperationException("Missing GOOGLE_API_KEY environment variable.");

// 註冊 GeminiService
builder.Services.AddSingleton<GeminiService>(sp =>
{
    return new GeminiService(googleApiKey);
});

var app = builder.Build();

// 綁定 Render 提供的 PORT（預設 8080）
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");

// 健康檢查路由
app.MapGet("/health", () => Results.Ok("Service is healthy!"));

// 取得 Bot 自己的 personId
string botPersonId = await GetBotPersonId(botToken);

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

        var signature = req.Headers["X-Spark-Signature"].ToString();
        if (!IsValidWebhookSignature(bodyBytes, signature, webhookSecret))
        {
            return Results.Unauthorized();
        }

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
                var message = await GetWebexMessage(botToken, messageId);
                if (!string.IsNullOrEmpty(message.Text) || message.Audio is not null)
                {
                    var reply = message.Audio is not null
                        ? await gemini.GenerateFromAudioAsync(message.Audio, message.MimeType!, message.Text)
                        : await gemini.GenerateAsync(message.Text!);
                    await SendWebexMessage(botToken, roomId, reply);
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
static async Task<(string? Text, byte[]? Audio, string? MimeType)> GetWebexMessage(string botToken, string messageId)
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.Authorization = new("Bearer", botToken);
    var resp = await client.GetAsync($"https://webexapis.com/v1/messages/{messageId}");
    if (!resp.IsSuccessStatusCode) return (null, null, null);
    var json = await resp.Content.ReadAsStringAsync();
    var node = JsonNode.Parse(json);
    var text = node?["text"]?.ToString();

    if (node?["files"] is JsonArray files)
    {
        foreach (var file in files)
        {
            var fileUrl = file?.ToString();
            if (string.IsNullOrEmpty(fileUrl)) continue;

            var audioResponse = await client.GetAsync(fileUrl);
            if (!audioResponse.IsSuccessStatusCode) continue;

            var contentType = audioResponse.Content.Headers.ContentType?.MediaType;
            var extension = Path.GetExtension(new Uri(fileUrl).AbsolutePath).ToLowerInvariant();
            var mimeType = contentType is null or "application/octet-stream"
                ? extension switch
                {
                    ".m4a" => "audio/mp4",
                    ".mp3" => "audio/mpeg",
                    ".ogg" => "audio/ogg",
                    ".wav" => "audio/wav",
                    ".webm" => "audio/webm",
                    ".aac" => "audio/aac",
                    _ => contentType
                }
                : contentType;

            if (mimeType?.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) == true)
            {
                return (text, await audioResponse.Content.ReadAsByteArrayAsync(), mimeType);
            }
        }
    }

    return (text, null, null);
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

static bool IsValidWebhookSignature(byte[] body, string signature, string secret)
{
    if (string.IsNullOrWhiteSpace(signature)) return false;

    byte[] suppliedSignature;
    try
    {
        suppliedSignature = Convert.FromHexString(signature);
    }
    catch (FormatException)
    {
        return false;
    }

    using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
    var expectedSignature = hmac.ComputeHash(body);
    return suppliedSignature.Length == expectedSignature.Length &&
        CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature);
}
