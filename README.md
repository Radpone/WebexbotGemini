# Relay | Webex + Gemini

Python FastAPI service for the Webex bot, with a protected operations dashboard, webhook diagnostics, Gemini text/audio understanding, and a Gemini speech preview.

## Run locally

Requires Python 3.11 or newer.

```powershell
python -m venv .venv
.venv\Scripts\Activate.ps1
python -m pip install -r requirements.txt
$env:WEBEX_BOT_TOKEN="your-webex-bot-token"
$env:WEBEX_WEBHOOK_SECRET="your-webex-webhook-secret"
$env:GOOGLE_API_KEY="your-google-ai-api-key"
$env:DASHBOARD_PASSWORD="choose-a-long-dashboard-password"
python -m uvicorn app:app --reload --host 0.0.0.0 --port 8000
```

Open `http://localhost:8000`. The dashboard uses HTTP Basic Auth with username `admin` and the `DASHBOARD_PASSWORD` value. The public health check is `http://localhost:8000/health`.

## Deploy to Render from GitHub

1. Push this repository to GitHub.
2. In Render, create a **Web Service** connected to the repository and choose the **Python** runtime. Set the build command to `pip install -r requirements.txt` and the start command to `uvicorn app:app --host 0.0.0.0 --port $PORT`.
3. Add these environment variables in the Render service settings:
	- `WEBEX_BOT_TOKEN`: Webex bot access token.
	- `WEBEX_WEBHOOK_SECRET`: the exact secret configured on the Webex webhook.
	- `GOOGLE_API_KEY`: Google AI Studio API key.
	- `DASHBOARD_PASSWORD`: a long, unique password for the dashboard.
	- `GEMINI_MODEL` (optional): defaults to `gemini-3.8-flash`.
	- `GEMINI_TTS_MODEL` (optional): defaults to `gemini-3.8-flash-tts`.
4. Set the Webex webhook target URL to `https://<your-render-service>.onrender.com/webhook`, with resource `messages`, event `created`, and the matching webhook secret.
5. Open `https://<your-render-service>.onrender.com` and sign in as `admin` with the dashboard password.

Render redeploys automatically when new commits are pushed to the connected branch.

## Dashboard

- Service readiness and configured/not-configured indicators; secret values are never exposed.
- Recent webhook events and signature rejection reasons.
- A Gemini API connection test.
- A text-to-speech preview with an audio player.

The dashboard and its management APIs require HTTP Basic Auth. Keep `DASHBOARD_PASSWORD` private. `/health` and `/webhook` remain public for Render and Webex.

## Verification

```powershell
python -m unittest discover -s tests
```

The legacy C# files remain in the repository for reference; the Python service runs independently and does not build the .NET project.
