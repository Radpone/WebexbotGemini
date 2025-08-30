using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using YourProjectNamespace.Services;  // 引用 GeminiService 所在的命名空間

var builder = WebApplication.CreateBuilder(args);

// 註冊 GeminiService
builder.Services.AddSingleton<GeminiService>(sp =>
{
    var googleApiKey = builder.Configuration["GOOGLE_API_KEY"];
    return new GeminiService(googleApiKey);
});

var app = builder.Build();

// 綁定 Render 提供的 PORT（預設 8080）
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
app.Urls.Add($"http://0.0.0.0:{port}");

// 健康檢查路由
app.MapGet("/health", () => Results.Ok("Service is healthy!"));

string? webhookSecret = Environment.GetEnvironmentVariable("WEBEX_SECRET");
string? botToken = Environment.GetEnvironmentVariable("WEBEX_BOT_TOKEN");
string? googleApiKey = Environment.GetEnvironmentVariable("GOOGLE_API_KEY");

var gemini = new GeminiService(googleApiKey!);

// Webhook 路由
app.MapPost("/webhook", async (HttpRequest req, GeminiService gemini) =>
{
    try
    {
        Console.WriteLine("Webhook received!");

        // 讀取請求內容
        req.EnableBuffering();
        using var ms = new MemoryStream();
        await req.Body.CopyToAsync(ms);
        var bodyBytes = ms.ToArray();
        req.Body.Position = 0;

        // 取得簽名並進行驗證
        var signature = req.Headers["X-Spark-Signature"].FirstOrDefault()
                     ?? req.Headers["X-Webex-Signature"].FirstOrDefault();

        Console.WriteLine("Received Signature: " + signature);

        if (string.IsNullOrEmpty(signature) || !VerifySignature(bodyBytes, webhookSecret!, signature))
        {
            Console.WriteLine("Invalid signature");
            return Results.BadRequest("Invalid signature");
        }

        // 解析 JSON
        var json = Encoding.UTF8.GetString(bodyBytes);
        Console.WriteLine("Received JSON: " + json);
        var payload = JsonNode.Parse(json);

        // 檢查 resource 和消息內容
        var resource = payload?["resource"]?.ToString();
        Console.WriteLine("Resource: " + resource);

        if (resource == "messages")
        {
            var data = payload?["data"];
            var roomId = data?["roomId"]?.ToString();
            var messageId = data?["id"]?.ToString();

            Console.WriteLine($"RoomId: {roomId}, MessageId: {messageId}");

            if (!string.IsNullOrEmpty(roomId) && !string.IsNullOrEmpty(messageId))
            {
                // 取得使用者原始訊息
                var userMsg = await GetWebexMessage(botToken!, messageId);

                if (!string.IsNullOrEmpty(userMsg))
                {
                    Console.WriteLine($"User message: {userMsg}");
                    // 呼叫 Gemini 生成回覆
                    var reply = await gemini.GenerateAsync(userMsg);
                    Console.WriteLine($"Gemini reply: {reply}");
                    await SendWebexMessage(botToken!, roomId, reply);
                }
                else
                {
                    Console.WriteLine("No message found for messageId: " + messageId);
                }
            }
        }
        else
        {
            Console.WriteLine("Not a message resource.");
        }

        return Results.Ok();
    }
    catch (Exception ex)
    {
        Console.WriteLine("Error: " + ex.Message);
        // 使用 Results.Problem() 返回錯誤結果
        return Results.Problem("Internal Server Error", statusCode: 500);
    }
});

app.Run();

// 簽名驗證
static bool VerifySignature(byte[] body, string secret, string signature)
{
    using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(secret));
    var computed = hmac.ComputeHash(body);
    var computedHex = BitConverter.ToString(computed).Replace("-", "").ToLowerInvariant();

    // 輸出計算出來的簽名與收到的簽名
    Console.WriteLine($"Computed Signature: {computedHex}");
    Console.WriteLine($"Received Signature: {signature}");

    return computedHex == signature.ToLowerInvariant();
}
// 取得 Webex 訊息
static async Task<string?> GetWebexMessage(string botToken, string messageId)
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.Authorization = new("Bearer", botToken);
    var resp = await client.GetAsync($"https://webexapis.com/v1/messages/{messageId}");
    if (!resp.IsSuccessStatusCode)
    {
        Console.WriteLine($"Error getting message: {resp.StatusCode}");
        return null;
    }
    var json = await resp.Content.ReadAsStringAsync();
    Console.WriteLine("Message response: " + json);
    var node = JsonNode.Parse(json);
    return node?["text"]?.ToString();
}

// 發送訊息到 Webex
static async Task SendWebexMessage(string botToken, string roomId, string text)
{
    using var client = new HttpClient();
    client.DefaultRequestHeaders.Authorization = new("Bearer", botToken);
    var payload = new { roomId = roomId, text = text };
    var resp = await client.PostAsJsonAsync("https://webexapis.com/v1/messages", payload);
    if (!resp.IsSuccessStatusCode)
    {
        Console.WriteLine($"Failed to send message: {resp.StatusCode}");
    }
    else
    {
        Console.WriteLine($"Message sent successfully to room: {roomId}");
    }
}
