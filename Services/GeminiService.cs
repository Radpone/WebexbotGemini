using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace YourProjectNamespace.Services
{
    public class GeminiService
    {
        private readonly string _googleApiKey;

        public GeminiService(string googleApiKey)
        {
            _googleApiKey = googleApiKey;
        }

        public async Task<string> GenerateAsync(string userMessage)
        {
            // 呼叫 Gemini API 生成回覆
            var response = await CallGeminiAPI(userMessage);
            return response;
        }

        private async Task<string> CallGeminiAPI(string userMessage)
        {
            using var client = new HttpClient();

            // 假設這是 Gemini API 端點
            var endpoint = "https://gemini-api.example.com/generate";
            var requestPayload = new
            {
                prompt = userMessage,
                max_tokens = 150 // 可根據需要調整生成文本的長度
            };

            // 設置請求頭
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_googleApiKey}");

            // 發送 POST 請求
            var response = await client.PostAsJsonAsync(endpoint, requestPayload);
            if (!response.IsSuccessStatusCode)
            {
                // 發生錯誤，返回默認的錯誤訊息
                return "Sorry, I couldn't process your request right now.";
            }

            // 解析回應
            var responseContent = await response.Content.ReadAsStringAsync();
            // 假設回應包含生成的文本在 `text` 字段
            var jsonResponse = JsonNode.Parse(responseContent);
            return jsonResponse?["text"]?.ToString() ?? "No response from Gemini.";
        }
    }
}
