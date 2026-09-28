import base64
import hashlib
import hmac
import unittest
from unittest.mock import patch

from fastapi.testclient import TestClient

import app


class ServiceTests(unittest.TestCase):
    def setUp(self) -> None:
        self.client = TestClient(app.app)

    def test_health_is_public(self) -> None:
        response = self.client.get("/health")
        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json(), {"status": "healthy"})

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

    def test_generated_audio_decodes_from_interaction_response(self) -> None:
        audio = b"wav test data"
        payload = {
            "steps": [{"content": [{"type": "audio", "data": base64.b64encode(audio).decode()}]}]
        }
        self.assertEqual(app.extract_generated_audio(payload), audio)


if __name__ == "__main__":
    unittest.main()