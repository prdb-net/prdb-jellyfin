"""A stand-in for the prdb public API, for the end-to-end tests.

It answers the four read endpoints the plugin uses from fixed data, rejects every request without the
test key, serves the images it refers to, and records every request so the test can check what the
plugin sent. Identification goes by file name; the hash case additionally requires the OSHash the test
computed independently, so a plugin that hashes differently fails it.

Stdlib only: it runs in a plain python image.
"""
import json
import os
import pathlib
import re
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

API_KEY = "e2e-key"
DATA = pathlib.Path(os.environ.get("MOCK_DATA", "/data"))
BASE = os.environ.get("MOCK_BASE_URL", "http://prdb-mock:8080")

SITE = {
    "id": "5e000000-0000-0000-0000-000000000001",
    "publicId": "S0001",
    "title": "Site A",
    "url": "https://site-a.example",
    "platformId": None,
    "platformKey": None,
    "platformTitle": None,
    "accountHandle": None,
    "network": {"id": "6e000000-0000-0000-0000-000000000001", "title": "Network A", "url": "https://network-a.example"},
}

# A site of no network, for the video entered by hand, so the studio collections differ in members.
SITE_B = {
    **SITE,
    "id": "5e000000-0000-0000-0000-000000000002",
    "publicId": "S0002",
    "title": "Site B",
    "url": "https://site-b.example",
    "network": None,
}

ACTOR_ID = "a0000000-0000-0000-0000-000000000001"


def video(number, title, site=SITE):
    video_id = f"00000000-0000-0000-0000-{number:012d}"
    return {
        "id": video_id,
        "title": title,
        "description": f"Description of {title}.",
        "stashdbSceneId": None,
        "previewEnrichmentEligible": False,
        "releaseDate": "2024-05-06",
        "createdAtUtc": "2024-05-07T00:00:00Z",
        "updatedAtUtc": "2024-05-07T00:00:00Z",
        "site": site,
        "qualityOverview": {},
        "images": [
            {"id": f"1{number:07d}-0000-0000-0000-000000000001", "url": f"{BASE}/images/cover.jpg"},
            {"id": f"1{number:07d}-0000-0000-0000-000000000002", "url": f"{BASE}/images/backdrop.jpg"},
        ],
        "preNames": [],
        "actors": [
            {
                "id": ACTOR_ID,
                "publicId": "A0001",
                "name": "Jane Doe",
                "gender": 1,
                "nationality": 0,
                "images": [{"id": "b0000000-0000-0000-0000-000000000001", "url": f"{BASE}/images/actor.jpg", "imageType": 2}],
            }
        ],
    }


VIDEOS = {
    1: video(1, "Exact Video"),
    2: video(2, "Strong Video"),
    3: video(3, "Probable Video"),
    4: video(4, "Late Probable Video"),
    5: video(5, "Manual Video", SITE_B),
}

ACTOR = {
    "id": ACTOR_ID,
    "publicId": "A0001",
    "name": "Jane Doe",
    "gender": 1,
    "genderLabel": "Female",
    "birthday": "1990-01-02",
    "birthplace": "Somewhere",
    "nationality": 0,
    "nationalityLabel": "Unknown",
    "images": [{"id": "b0000000-0000-0000-0000-000000000001", "imageType": 2, "imageTypeLabel": "Poster", "url": f"{BASE}/images/actor.jpg"}],
    "aliases": [],
    "links": [],
    "bios": [{"id": "c0000000-0000-0000-0000-000000000001", "text": "Jane Doe is a test performer."}],
}

REQUESTS = []
LOCK = threading.Lock()


def expected_hashes():
    path = DATA / "expected.json"
    return json.loads(path.read_text()) if path.exists() else {}


def identify(file):
    name = file.get("filename", "")
    hashes = expected_hashes()
    ref = file.get("ref")

    def match(confidence, kind, number):
        return {"ref": ref, "confidence": confidence, "matchedBy": kind, "videoId": VIDEOS[number]["id"], "video": VIDEOS[number], "candidates": []}

    if name == "exact.mp4" and file.get("osHash") and file.get("osHash") == hashes.get("exact.mp4"):
        return match(4, 0, 1)
    if name == "Strong Name.mp4":
        return match(3, 2, 2)
    if name == "Site.A.24.05.06.Probable.Title.mp4":
        return match(2, 3, 3)
    if name == "late.probable.mp4":
        return match(2, 6, 4)
    if name == "ambiguous.mp4":
        return {"ref": ref, "confidence": 5, "candidates": [VIDEOS[1]["id"], VIDEOS[2]["id"]]}
    return {"ref": ref, "confidence": 0, "candidates": []}


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"

    def log_message(self, format, *args):
        pass

    def _send(self, status, body=None, content_type="application/json"):
        data = b"" if body is None else (body if isinstance(body, bytes) else json.dumps(body).encode())
        self.send_response(status)
        self.send_header("Content-Type", content_type)
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def _record(self, body):
        if self.path.startswith("/_") or self.path.startswith("/images/"):
            return
        with LOCK:
            REQUESTS.append({
                "method": self.command,
                "path": self.path,
                "apiKey": self.headers.get("X-Api-Key"),
                "userAgent": self.headers.get("User-Agent"),
                "body": body,
            })

    def do_GET(self):
        self._record(None)
        if self.path == "/_requests":
            with LOCK:
                return self._send(200, REQUESTS)
        if self.path == "/health":
            return self._send(200, {"status": "ok"})
        if self.path.startswith("/images/"):
            image = DATA / "images" / pathlib.Path(self.path).name
            return self._send(200, image.read_bytes(), "image/jpeg") if image.exists() else self._send(404)
        if self.headers.get("X-Api-Key") != API_KEY:
            return self._send(401, {"title": "Unauthorized", "status": 401})
        if self.path == "/rate-limit":
            window = {"limit": 1000, "used": len(REQUESTS), "remaining": 1000 - len(REQUESTS), "resetsInSeconds": 3600}
            return self._send(200, {"isEnforced": True, "hourly": window, "monthly": window})
        found = re.fullmatch(r"/videos/([0-9a-f-]{36})", self.path)
        if found:
            item = next((v for v in VIDEOS.values() if v["id"] == found.group(1)), None)
            return self._send(200, item) if item else self._send(404, {"title": "Not Found", "status": 404})
        found = re.fullmatch(r"/actors/([0-9a-f-]{36})", self.path)
        if found:
            return self._send(200, ACTOR) if found.group(1) == ACTOR_ID else self._send(404, {"title": "Not Found", "status": 404})
        return self._send(404, {"title": "Not Found", "status": 404})

    def do_POST(self):
        length = int(self.headers.get("Content-Length") or 0)
        raw = self.rfile.read(length) if length else b""
        try:
            body = json.loads(raw) if raw else None
        except json.JSONDecodeError:
            body = raw.decode(errors="replace")
        self._record(body)
        if self.headers.get("X-Api-Key") != API_KEY:
            return self._send(401, {"title": "Unauthorized", "status": 401})
        if self.path == "/videos/identify" and isinstance(body, dict):
            return self._send(200, {"results": [identify(f) for f in body.get("files", [])]})
        return self._send(404, {"title": "Not Found", "status": 404})


if __name__ == "__main__":
    ThreadingHTTPServer(("0.0.0.0", 8080), Handler).serve_forever()
