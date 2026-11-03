# Repository Guidelines

This repository holds the prdb metadata provider for Jellyfin: a plugin that identifies local videos
against the prdb public API and maps prdb's metadata onto Jellyfin items. The repository is public and
MIT-licensed.

## Rules that do not bend

- **Jellyfin 12.x only.** The plugin builds against `Jellyfin.Controller`/`Jellyfin.Model` 12.x and
  targets `net10.0`. Older Jellyfin versions are not supported; do not add compatibility shims for them.
- **Read-only against prdb.** The plugin calls `POST /videos/identify`, `GET /videos/{id}`,
  `GET /actors/{id}` and `GET /rate-limit`, nothing else. It never calls a submission endpoint
  (`/videos/filehash-submissions`, `/video-user-images`, …).
- **No Emby code.** Emby gets its own, entirely separate repository. Do not add `#if` branches,
  abstractions or shared projects for it here.
- **No secrets in the repository.** The prdb API key lives only in the plugin configuration of the
  Jellyfin instance that uses it. Tests run against fakes and mocks; a real key, where one is needed
  locally, comes through `vaultaffe run --` and is never written to a file.
- **Keep the dependency footprint small.** The plugin runs inside the Jellyfin process. Ship nothing
  Jellyfin already provides (reference its packages with `ExcludeAssets=runtime`), and do not add
  libraries whose versions could collide with Jellyfin's own.
- Code, comments, UI texts, README and commit messages are in English.

## Layout

| Path | What |
|------|------|
| `src/Jellyfin.Plugin.Prdb` | The plugin |
| `tests/Jellyfin.Plugin.Prdb.Tests` | Unit tests (xUnit v3, Microsoft Testing Platform) |
| `Jellyfin.Plugin.Prdb.slnx` | The solution; use the `.slnx` format only |

## Build and test

```sh
dotnet build Jellyfin.Plugin.Prdb.slnx
dotnet test --solution Jellyfin.Plugin.Prdb.slnx
```

`global.json` selects the .NET 10 SDK and the Microsoft Testing Platform runner. CI runs the same
commands on every push to `main` and on pull requests.

### End-to-end tests

```sh
python3 tests/e2e/run.py          # add --keep to leave Jellyfin running at http://localhost:18100
```

`tests/e2e/run.py` builds the plugin, generates small test videos with the ffmpeg inside the Jellyfin image,
and starts `tests/e2e/docker-compose.yml`: a pinned `jellyfin/jellyfin` 12.x with the plugin, and
`tests/e2e/mock/prdb_mock.py`, a stand-in for the prdb API that answers from fixed data. It then drives Jellyfin
through its HTTP API — wizard, plugin settings, a movie library with prdb switched on, scans, the scheduled task —
and checks the outcome: hash, exact-file-name and heuristic matches applied, ambiguous and unknown files left alone,
a hand-entered id winning, the heuristics switch honoured, the opt-in studio collections (members, cover,
visibility, clean-up), and what went over the wire. The hash case only matches
with an OSHash the script computes independently. It needs Docker with the compose plugin and runs in CI after the
unit tests; the work directory is `artifacts/e2e` (ignored).

When the Jellyfin image tag is raised, raise it in `docker-compose.yml` and `run.py` together.

To try the plugin in a real server, copy the built `Jellyfin.Plugin.Prdb.dll` into
`<jellyfin config>/plugins/prdb_<version>/` and restart Jellyfin; the log line
`Loaded plugin: prdb <version>` confirms it loaded.

## Releases

A release is a tag `vX.Y.Z` on `main`. The `Release` workflow tests and builds the plugin as version `X.Y.Z.0`,
publishes `prdb-jellyfin_X.Y.Z.zip` as a GitHub release and commits the new version to `manifest.json` on `main`
(`build/update_manifest.py`), which is the plugin repository Jellyfin reads. The tag's subject line becomes the
changelog in Jellyfin's catalog, so tag with a message:

```sh
git tag -a v0.2.0 -m "What changed, in one line"
git push origin v0.2.0
```

Versions follow SemVer; `targetAbi` in the manifest is the oldest Jellyfin the plugin is built against
(`12.0.0.0`). Running the workflow by hand is a dry run: it uploads the archive and the updated manifest as an
artifact and publishes nothing. Publishing a release is a decision for a maintainer, not a step an agent takes on
its own.

## Tickets

Work for this repository is tracked in planaffe, project `JELLYFIN`; the checked-in `.planaffe` names it.
`pa` takes its instance and token from `PLANAFFE_URL` and `PLANAFFE_TOKEN`, which come from vaultaffe:
the checked-in `.vaultaffe` binds this checkout to `prdb/development` (run `vaultaffe setup` once after
cloning). So every call is `vaultaffe run -- pa …`, and neither value is ever written into a file, a
commit or a ticket. Check with `vaultaffe run -- pa me` that the token is an agent's before claiming
anything; if it names a user, stop and say so.

```sh
vaultaffe run -- pa next --claim              # the highest-ranked workable ticket, claimed
vaultaffe run -- pa issue view JELLYFIN-42    # the ticket with its epic and blockers
vaultaffe run -- pa issue comment JELLYFIN-42 "…"
vaultaffe run -- pa issue ask JELLYFIN-42 "…"  # a question: the ticket waits for a human
vaultaffe run -- pa issue close JELLYFIN-42 --done --result-file -
vaultaffe run -- pa issue release JELLYFIN-42 # give it back unfinished
```

Ticket keys and ticket content are internal: keep them out of branch names, commit messages and pull
request descriptions, since this repository is public.
