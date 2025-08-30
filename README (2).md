# Webex Bot + Gemini (ASP.NET Core)

## 簡介
這是一個 ASP.NET Core minimal API 範例，
- 接收 Webex webhook → 驗證簽章
- 抓取使用者訊息
- 呼叫 Google Gemini 產生回覆
- 把結果回傳到 Webex 房間

## 環境變數
- `WEBEX_WEBHOOK_SECRET` = 你建立 Webex webhook 時設定的 secret
- `WEBEX_BOT_TOKEN` = 你在 Webex Developer portal 建立的 Bot Token
- `GOOGLE_API_KEY` = Google Generative AI API Key

## 本地測試
```bash
dotnet run
```
配合 `ngrok http 5000`，將 Webex webhook 指到 `https://xxxx.ngrok.io/webhook`

## Render 部署
1. Push 到 GitHub。
2. 在 Render 建立 Web Service，選 Docker 部署。
3. 設定環境變數。
4. 在 Webex Developer Portal 建 webhook，target 指向 `https://yourapp.onrender.com/webhook`。

## 延伸
- 可以在 `GeminiService.cs` 修改模型或加上溫度、maxTokens 參數。
- 可以在 `Program.cs` 加上更多事件處理（如 members、attachments）。
