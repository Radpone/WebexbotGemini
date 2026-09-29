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

    def test_head_root_probe_is_public(self) -> None:
        response = self.client.head("/")
        self.assertEqual(response.status_code, 200)

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
            b'"data":{"id":"message-id","roomId":"room-id"}}'
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
            "room-id",
            "抱歉，目前無法處理這則訊息，請稍後再試。",
        )

if __name__ == "__main__":
    unittest.main()