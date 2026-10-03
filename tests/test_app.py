import asyncio
import hashlib
import hmac
import unittest
from unittest.mock import AsyncMock, patch

from fastapi.testclient import TestClient

import app


class ServiceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.client = TestClient(app.app)

    def test_health_is_public(self) -> None:
        response = self.client.get("/health")
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json(), {"status": "healthy"})

    def test_send_webex_message_uses_recipient_person_id(self) -> None:
        client = AsyncMock()
        client.__aenter__.return_value = client
        client.__aexit__.return_value = False
        client.post.return_value = app.httpx.Response(200)
        with patch.object(app.httpx, "AsyncClient", return_value=client):
            asyncio.run(app.send_webex_message("person-id", "Hello"))

        self.assertEqual(
            client.post.await_args.kwargs["json"],
            {"toPersonId": "person-id", "text": "Hello"},
        )

    def test_head_root_probe_is_public(self) -> None:
        response = self.client.head("/")
        self.assertEqual(response.status_code, 200)

    def test_gemini_error_detail_extracts_provider_message(self) -> None:
        response = app.httpx.Response(
            503,
            json={"error": {"status": "UNAVAILABLE", "message": "Model overloaded"}},
        )
        self.assertEqual(
            app.gemini_error_detail(response),
            "UNAVAILABLE: Model overloaded",
        )

    def test_gemini_retries_503_then_returns_text(self) -> None:
        client = AsyncMock()
        client.__aenter__.return_value = client
        client.__aexit__.return_value = False
        client.post.side_effect = [
            app.httpx.Response(503, json={"error": {"message": "overloaded"}}),
            app.httpx.Response(
                200,
                json={"candidates": [{"content": {"parts": [{"text": "OK"}]}}]},
            ),
        ]
        with (
            patch.object(app, "GOOGLE_API_KEY", "test-google-key"),
            patch.object(app.httpx, "AsyncClient", return_value=client),
            patch.object(app.asyncio, "sleep", new_callable=AsyncMock) as sleep,
        ):
            result = asyncio.run(app.gemini_generate([{"text": "test"}]))

        self.assertEqual(result, "OK")
        self.assertEqual(client.post.await_count, 2)
        sleep.assert_awaited_once_with(1)

    def test_dashboard_requires_authentication(self) -> None:
        with patch.object(app, "DASHBOARD_PASSWORD", "test-dashboard-password"):
            response = self.client.get("/")
            self.assertEqual(response.status_code, 401)

            response = self.client.get(
                "/api/status",
                auth=("admin", "test-dashboard-password"),
            )
            self.assertEqual(response.status_code, 200)

    def test_webhook_rejects_missing_signature(self) -> None:
        with patch.object(app, "WEBEX_SECRET", "test-webhook-secret"):
            response = self.client.post("/webhook", content=b'{"resource":"messages"}')
        self.assertEqual(response.status_code, 401)
        self.assertEqual(app.events[0]["detail"], "Signature missing")

    def test_webhook_accepts_valid_signature(self) -> None:
        secret = "test-webhook-secret"
        body = b'{"resource":"memberships","event":"created"}'
        signature = hmac.new(secret.encode(), body, hashlib.sha1).hexdigest()
        with patch.object(app, "WEBEX_SECRET", secret):
            response = self.client.post(
                "/webhook",
                content=body,
                headers={"X-Spark-Signature": signature},
            )
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json(), {"ok": True})

    def test_webhook_sends_notice_when_gemini_fails(self) -> None:
        secret = "test-webhook-secret"
        body = (
            b'{"resource":"messages","event":"created",'
            b'"data":{"id":"message-id","personId":"person-id"}}'
        )
        signature = hmac.new(secret.encode(), body, hashlib.sha1).hexdigest()
        with (
            patch.object(app, "WEBEX_SECRET", secret),
            patch.object(app, "get_bot_person_id", new_callable=AsyncMock, return_value=None),
            patch.object(app, "get_webex_message", new_callable=AsyncMock, return_value="hello"),
            patch.object(
                app,
                "gemini_text",
                new_callable=AsyncMock,
                side_effect=app.HTTPException(status_code=502, detail="Gemini failed"),
            ),
            patch.object(app, "send_webex_message", new_callable=AsyncMock) as send_message,
        ):
            response = self.client.post(
                "/webhook",
                content=body,
                headers={"X-Spark-Signature": signature},
            )

        self.assertEqual(response.status_code, 200)
        send_message.assert_awaited_once_with(
            "person-id",
            "抱歉，目前無法處理這則訊息，請稍後再試。",
        )

    def test_webhook_replies_to_message_sender(self) -> None:
        secret = "test-webhook-secret"
        body = (
            b'{"resource":"messages","event":"created",'
            b'"data":{"id":"message-id","personId":"person-id"}}'
        )
        signature = hmac.new(secret.encode(), body, hashlib.sha1).hexdigest()
        with (
            patch.object(app, "WEBEX_SECRET", secret),
            patch.object(app, "get_bot_person_id", new_callable=AsyncMock, return_value=None),
            patch.object(app, "get_webex_message", new_callable=AsyncMock, return_value="hello"),
            patch.object(app, "gemini_text", new_callable=AsyncMock, return_value="Hi!"),
            patch.object(app, "send_webex_message", new_callable=AsyncMock) as send_message,
        ):
            response = self.client.post(
                "/webhook",
                content=body,
                headers={"X-Spark-Signature": signature},
            )

        self.assertEqual(response.status_code, 200)
        send_message.assert_awaited_once_with("person-id", "Hi!")

if __name__ == "__main__":
    unittest.main()