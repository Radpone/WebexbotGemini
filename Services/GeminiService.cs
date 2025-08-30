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
            // �I�s Gemini API �ͦ��^��
            var response = await CallGeminiAPI(userMessage);
            return response;
        }

        private async Task<string> CallGeminiAPI(string userMessage)
        {
            using var client = new HttpClient();

            // ���]�o�O Gemini API ���I
            var endpoint = "https://gemini-api.example.com/generate";
            var requestPayload = new
            {
                prompt = userMessage,
                max_tokens = 150 // �i�ھڻݭn�վ�ͦ��奻������
            };

            // �]�m�ШD�Y
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_googleApiKey}");

            // �o�e POST �ШD
            var response = await client.PostAsJsonAsync(endpoint, requestPayload);
            if (!response.IsSuccessStatusCode)
            {
                // �o�Ϳ��~�A��^�q�{�����~�T��
                return "Sorry, I couldn't process your request right now.";
            }

            // �ѪR�^��
            var responseContent = await response.Content.ReadAsStringAsync();
            // ���]�^���]�t�ͦ����奻�b `text` �r�q
            var jsonResponse = JsonNode.Parse(responseContent);
            return jsonResponse?["text"]?.ToString() ?? "No response from Gemini.";
        }
    }
}
