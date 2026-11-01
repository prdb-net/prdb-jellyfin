#!/usr/bin/env python3
"""End-to-end test: the built plugin in a real Jellyfin 12.x, against a stand-in for the prdb API.

    python3 tests/e2e/run.py            # build, run, tear down
    python3 tests/e2e/run.py --keep     # leave the containers running afterwards (http://localhost:18100)

Needs the .NET SDK and Docker with the compose plugin. Stdlib only.

What it checks:
  1. A library scan identifies and fills the files whose matches the switches allow (hash, exact file
     name, file name heuristic) and leaves ambiguous and unknown files alone.
  2. The hash case only matches with the OSHash this script computes itself, so the plugin's hashing is
     checked against an independent implementation.
  3. A prdb id entered by hand wins: the item gets that video's metadata.
  4. With the heuristics switch off, a new file prdb matches only by name stays unidentified, also when
     the scheduled task runs.
  5. Every request carried the API key, sent no folder names, and nothing was submitted to prdb.
"""
import argparse
import json
import os
import pathlib
import shutil
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parents[2]
E2E = pathlib.Path(__file__).resolve().parent
COMPOSE = ["docker", "compose", "-f", str(E2E / "docker-compose.yml")]
JELLYFIN_IMAGE = os.environ.get("JELLYFIN_IMAGE", "jellyfin/jellyfin:12.4.20261026-211555")
PLUGIN_ID = "74e0efe4-afea-43e4-8b19-1cf114c7755b"
FFMPEG = "/usr/lib/jellyfin-ffmpeg/ffmpeg"
AUTH = 'MediaBrowser Client="prdb-e2e", Device="e2e", DeviceId="prdb-e2e", Version="1.0"'

VIDEO_ID = {n: f"00000000-0000-0000-0000-{n:012d}" for n in range(1, 6)}
ACTOR_ID = "a0000000-0000-0000-0000-000000000001"

# file name -> (ffmpeg source, seconds); every file differs in size and content, and all are well above
# the 128 KiB an OSHash needs.
MEDIA = {
    "exact.mp4": ("testsrc", 3),
    "Strong Name.mp4": ("testsrc2", 4),
    "Site.A.24.05.06.Probable.Title.mp4": ("smptebars", 3),
    "ambiguous.mp4": ("rgbtestsrc", 3),
    "none.mp4": ("mandelbrot", 3),
}
LATE_FILE = "late.probable.mp4"


class Failure(Exception):
    pass


def log(message):
    print(f"[e2e] {message}", flush=True)


def check(condition, message):
    if not condition:
        raise Failure(message)
    log(f"ok   {message}")


def oshash(path):
    """The OpenSubtitles hash, written independently of the plugin's C# implementation."""
    size = path.stat().st_size
    if size < 2 * 65536:
        return None
    value = size
    with path.open("rb") as stream:
        for offset in (0, size - 65536):
            stream.seek(offset)
            chunk = stream.read(65536)
            for i in range(0, 65536, 8):
                value = (value + int.from_bytes(chunk[i:i + 8], "little")) & 0xFFFFFFFFFFFFFFFF
    return f"{value:016x}"


class Jellyfin:
    def __init__(self, base):
        self.base = base
        self.token = None

    def request(self, method, path, body=None, expect=(200, 204)):
        headers = {"Authorization": f'{AUTH}, Token="{self.token}"' if self.token else AUTH}
        data = None
        if body is not None:
            data = json.dumps(body).encode()
            headers["Content-Type"] = "application/json"
        request = urllib.request.Request(self.base + path, data=data, method=method, headers=headers)
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                raw = response.read()
                status = response.status
        except urllib.error.HTTPError as error:
            raw = error.read()
            status = error.code
        if status not in expect:
            raise Failure(f"{method} {path} answered {status}: {raw[:300]!r}")
        return json.loads(raw) if raw and raw[:1] in (b"{", b"[") else None

    def wait_until_up(self, timeout=180):
        deadline = time.time() + timeout
        while time.time() < deadline:
            try:
                with urllib.request.urlopen(self.base + "/health", timeout=5) as response:
                    if response.status == 200:
                        return
            except OSError:
                pass
            time.sleep(2)
        raise Failure("Jellyfin did not come up")

    def complete_wizard(self, user, password):
        self.request("POST", "/Startup/Configuration", {"UICulture": "en-US", "MetadataCountryCode": "US", "PreferredMetadataLanguage": "en"})
        self.request("GET", "/Startup/User")
        self.request("POST", "/Startup/User", {"Name": user, "Password": password})
        self.request("POST", "/Startup/Complete")
        session = self.request("POST", "/Users/AuthenticateByName", {"Username": user, "Pw": password})
        self.token = session["AccessToken"]
        self.user_id = session["User"]["Id"]

    def movies(self):
        query = urllib.parse.urlencode({
            "userId": self.user_id,
            "Recursive": "true",
            "IncludeItemTypes": "Movie",
            "Fields": "ProviderIds,Studios,People,Overview,PremiereDate,Path",
        })
        return {pathlib.Path(item["Path"]).name: item for item in self.request("GET", "/Items?" + query)["Items"]}

    def task(self, key):
        return next(task for task in self.request("GET", "/ScheduledTasks") if task.get("Key") == key)

    def wait_for_task(self, key, timeout=300):
        deadline = time.time() + timeout
        time.sleep(2)
        while time.time() < deadline:
            if self.task(key)["State"] == "Idle":
                return
            time.sleep(2)
        raise Failure(f"task {key} did not finish")


def wait_for(description, probe, timeout=180):
    deadline = time.time() + timeout
    last = None
    while time.time() < deadline:
        last = probe()
        if last:
            return last
        time.sleep(3)
    raise Failure(f"timed out waiting for {description}")


def build_plugin(work):
    log("building the plugin")
    subprocess.run(
        ["dotnet", "build", str(ROOT / "src/Jellyfin.Plugin.Prdb/Jellyfin.Plugin.Prdb.csproj"), "--configuration", "Release", "-nologo", "-v", "quiet"],
        check=True,
    )
    target = work / "config/plugins/prdb_0.0.0.0"
    target.mkdir(parents=True)
    shutil.copy(ROOT / "src/Jellyfin.Plugin.Prdb/bin/Release/net10.0/Jellyfin.Plugin.Prdb.dll", target)


def ffmpeg(work, *args):
    subprocess.run(
        ["docker", "run", "--rm", "--entrypoint", FFMPEG, "-v", f"{work}:/work", JELLYFIN_IMAGE, "-loglevel", "error", "-y", *args],
        check=True,
    )


def generate_media(work, files):
    for name, (source, seconds) in files.items():
        # Temporal noise keeps x264 from compressing a synthetic source below the 128 KiB an OSHash needs.
        ffmpeg(work, "-f", "lavfi", "-i", f"{source}=size=640x360:rate=25", "-t", str(seconds),
               "-vf", "noise=alls=30:allf=t", "-c:v", "libx264", "-b:v", "2M", "-pix_fmt", "yuv420p", f"/work/staging/{name}")


def prepare(work):
    for directory in ("config", "cache", "media", "staging", "mock/images"):
        (work / directory).mkdir(parents=True, exist_ok=True)
    build_plugin(work)

    log("generating test media")
    generate_media(work, {**MEDIA, LATE_FILE: ("testsrc", 5)})
    for name, color, size in (("cover.jpg", "red", "600x900"), ("backdrop.jpg", "blue", "1280x720"), ("actor.jpg", "green", "400x600")):
        ffmpeg(work, "-f", "lavfi", "-i", f"color=c={color}:s={size}", "-frames:v", "1", f"/work/mock/images/{name}")
    for name in MEDIA:
        shutil.move(work / "staging" / name, work / "media" / name)

    sizes = {name: (work / "media" / name).stat().st_size for name in MEDIA}
    if min(sizes.values()) < 2 * 65536:
        raise Failure(f"a test video is too small to carry an OSHash: {sizes}")
    expected = {"exact.mp4": oshash(work / "media/exact.mp4")}
    (work / "mock/expected.json").write_text(json.dumps(expected))
    log(f"exact.mp4 has OSHash {expected['exact.mp4']}")


def mock_requests(port):
    with urllib.request.urlopen(f"http://localhost:{port}/_requests", timeout=10) as response:
        return json.loads(response.read())


def configure(jellyfin, **switches):
    configuration = {
        "ApiKey": "e2e-key",
        "BaseUrl": "http://prdb-mock:8080",
        "AcceptHashMatches": True,
        "AcceptExactFilenameMatches": True,
        "AcceptNameHeuristicMatches": True,
        **switches,
    }
    jellyfin.request("POST", f"/Plugins/{PLUGIN_ID}/Configuration", configuration)


def scenario(jellyfin, work, mock_port):
    jellyfin.wait_until_up()
    jellyfin.complete_wizard("admin", "e2e-admin-password")

    plugins = {p["Name"]: p for p in jellyfin.request("GET", "/Plugins")}
    check(plugins.get("prdb", {}).get("Status") == "Active", "the plugin is loaded and active")

    configure(jellyfin)
    test = jellyfin.request("POST", "/Prdb/TestConnection", {"ApiKey": "e2e-key", "BaseUrl": "http://prdb-mock:8080"})
    check(test["Outcome"] == "ok", "the connection test succeeds against the mock")

    library_options = {
        "LibraryOptions": {
            "PathInfos": [{"Path": "/media"}],
            "EnableRealtimeMonitor": False,
            "EnableChapterImageExtraction": False,
            "ExtractChapterImagesDuringLibraryScan": False,
            "TypeOptions": [{
                "Type": "Movie",
                "MetadataFetchers": ["prdb"],
                "MetadataFetcherOrder": ["prdb"],
                "ImageFetchers": ["prdb"],
                "ImageFetcherOrder": ["prdb"],
            }],
        }
    }
    jellyfin.request("POST", "/Library/VirtualFolders?" + urllib.parse.urlencode({"name": "Videos", "collectionType": "movies", "refreshLibrary": "false"}), library_options)
    jellyfin.request("POST", "/Library/Refresh")
    jellyfin.wait_for_task("RefreshLibrary")

    # 1. Scan: three matches applied, two left alone.
    def identified():
        movies = jellyfin.movies()
        wanted = {"exact.mp4": "Exact Video", "Strong Name.mp4": "Strong Video", "Site.A.24.05.06.Probable.Title.mp4": "Probable Video"}
        if len(movies) == len(MEDIA) and all(movies.get(f, {}).get("Name") == t for f, t in wanted.items()):
            return movies
        return None

    movies = wait_for("the scan to identify the matching files", identified)
    for name, number in (("exact.mp4", 1), ("Strong Name.mp4", 2), ("Site.A.24.05.06.Probable.Title.mp4", 3)):
        movie = movies[name]
        check(movie["ProviderIds"].get("Prdb") == VIDEO_ID[number], f"{name} carries prdb id {VIDEO_ID[number]}")
        check(movie.get("Overview", "").startswith("Description of"), f"{name} has the prdb description")
        check([s["Name"] for s in movie.get("Studios", [])] == ["Site A", "Network A"], f"{name} has site and network as studios")
        check((movie.get("PremiereDate") or "").startswith("2024-05-06"), f"{name} has the release date")
        people = movie.get("People", [])
        check(any(p["Name"] == "Jane Doe" and p.get("Type") == "Actor" for p in people), f"{name} lists the performer")

    def with_images():
        movie = jellyfin.movies()["exact.mp4"]
        return movie if movie.get("ImageTags", {}).get("Primary") and movie.get("BackdropImageTags") else None

    wait_for("the prdb images of exact.mp4", with_images)
    check(True, "exact.mp4 has a primary image and a backdrop from prdb")

    for name in ("ambiguous.mp4", "none.mp4"):
        check("Prdb" not in movies[name]["ProviderIds"], f"{name} is left unidentified")

    # The performer carries the prdb actor id and gets the prdb biography on refresh.
    person = next(p for p in movies["exact.mp4"]["People"] if p["Name"] == "Jane Doe")
    jellyfin.request("POST", f"/Items/{person['Id']}/Refresh?" + urllib.parse.urlencode({
        "metadataRefreshMode": "FullRefresh", "imageRefreshMode": "FullRefresh", "replaceAllMetadata": "true", "replaceAllImages": "true"}))

    def person_filled():
        item = jellyfin.request("GET", f"/Items/{person['Id']}?userId={jellyfin.user_id}")
        return item if item.get("Overview") == "Jane Doe is a test performer." and item.get("ImageTags", {}).get("Primary") else None

    item = wait_for("the performer's prdb metadata", person_filled)
    check(item["ProviderIds"].get("Prdb") == ACTOR_ID, "the performer carries the prdb actor id, biography and image")

    # 3. A prdb id entered by hand wins.
    none = movies["none.mp4"]
    full = jellyfin.request("GET", f"/Items/{none['Id']}?userId={jellyfin.user_id}")
    full["ProviderIds"] = {**full.get("ProviderIds", {}), "Prdb": f"https://prdb.net/videos/{VIDEO_ID[5]}"}
    jellyfin.request("POST", f"/Items/{none['Id']}", full)
    jellyfin.request("POST", f"/Items/{none['Id']}/Refresh?" + urllib.parse.urlencode({
        "metadataRefreshMode": "FullRefresh", "imageRefreshMode": "FullRefresh", "replaceAllMetadata": "true", "replaceAllImages": "true"}))
    wait_for("the hand-entered id to apply", lambda: jellyfin.movies()["none.mp4"].get("Name") == "Manual Video")
    check(True, "a prdb id entered by hand (as an address) gives the item that video's metadata")

    # 4. Heuristics off: a new file prdb matches only by name stays unidentified.
    configure(jellyfin, AcceptNameHeuristicMatches=False)
    shutil.move(work / "staging" / LATE_FILE, work / "media" / LATE_FILE)
    jellyfin.request("POST", "/Library/Refresh")
    jellyfin.wait_for_task("RefreshLibrary")
    wait_for("the late file to appear", lambda: LATE_FILE in jellyfin.movies())
    task = jellyfin.task("PrdbIdentifyLibrary")
    jellyfin.request("POST", f"/ScheduledTasks/Running/{task['Id']}")
    jellyfin.wait_for_task("PrdbIdentifyLibrary")
    late = jellyfin.movies()[LATE_FILE]
    asked = [r for r in mock_requests(mock_port) if r["path"] == "/videos/identify" and any(f["filename"] == LATE_FILE for f in r["body"]["files"])]
    check(asked, f"prdb was asked about {LATE_FILE}")
    check("Prdb" not in late["ProviderIds"] and late["Name"] != "Late Probable Video", f"{LATE_FILE} stays unidentified with the heuristics switch off")

    # 5. What went over the wire.
    requests = mock_requests(mock_port)
    check(all(r["apiKey"] == "e2e-key" for r in requests), "every request to prdb carried the API key")
    check(all(r["method"] == "GET" or r["path"] == "/videos/identify" for r in requests), "nothing but identify was posted to prdb")
    files = [f for r in requests if r["path"] == "/videos/identify" for f in r["body"]["files"]]
    check(files and all("/" not in f["filename"] and "\\" not in f["filename"] for f in files), "no folder names were sent")
    check(all((r["userAgent"] or "").startswith("Jellyfin.Plugin.Prdb/") for r in requests), "requests identify the plugin")
    hashed = [f for f in files if f["filename"] == "exact.mp4"]
    expected_hash = oshash(work / "media/exact.mp4")
    check(expected_hash and hashed and hashed[0].get("osHash") == expected_hash, "the plugin's OSHash equals the independent implementation's")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--keep", action="store_true", help="leave the containers running")
    parser.add_argument("--port", type=int, default=int(os.environ.get("E2E_PORT", "18100")))
    args = parser.parse_args()

    work = pathlib.Path(os.environ.get("E2E_WORK") or (ROOT / "artifacts/e2e")).resolve()
    if work.exists():
        shutil.rmtree(work)
    work.mkdir(parents=True)

    env = {**os.environ, "E2E_WORK": str(work), "E2E_PORT": str(args.port), "E2E_MOCK_PORT": str(args.port + 1), "JELLYFIN_IMAGE": JELLYFIN_IMAGE}
    subprocess.run(COMPOSE + ["down", "-v", "--remove-orphans"], env=env, check=False, capture_output=True)

    failed = False
    try:
        prepare(work)
        subprocess.run(COMPOSE + ["up", "-d"], env=env, check=True)
        scenario(Jellyfin(f"http://localhost:{args.port}"), work, args.port + 1)
        log("all checks passed")
    except (Failure, subprocess.CalledProcessError) as error:
        failed = True
        log(f"FAILED: {error}")
        subprocess.run(COMPOSE + ["logs", "--no-color", "--tail", "200", "jellyfin"], env=env, check=False)
    finally:
        if not args.keep:
            subprocess.run(COMPOSE + ["down", "-v", "--remove-orphans"], env=env, check=False, capture_output=True)
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
