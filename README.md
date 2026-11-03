# prdb for Jellyfin

A metadata provider for [Jellyfin](https://jellyfin.org) that identifies your local videos against
[prdb.net](https://prdb.net) and fills in their title, description, release date, site, performers and
images.

- **Identifies by file hash and file name.** The plugin computes each file's OSHash (a cheap hash that
  reads only the first and last 64 KiB) and sends it together with the file name and size to the prdb API,
  which answers with the matching video.
- **Automatic.** New files are identified after every library scan; there is nothing to search or pick by hand.
- **You decide how cautious it is.** Three switches choose which kinds of match are applied.
- **Read-only.** The plugin never submits anything to prdb: no hashes, no images, no corrections.
- **Requires a prdb API key** of your own prdb account.

## Contents

- [Requirements](#requirements)
- [Installation](#installation)
- [Setup](#setup)
- [How it works](#how-it-works)
- [Everyday use](#everyday-use)
- [Troubleshooting](#troubleshooting)
- [Updating and uninstalling](#updating-and-uninstalling)
- [Privacy](#privacy)
- [License](#license)

## Requirements

- **Jellyfin 12.x.** Older versions are not supported.
- **A prdb account and an API key.** Create the key on prdb.net under [API Keys](https://prdb.net/api-keys).
- **A library of the content type _Movies_**, ideally one of its own for these videos – see
  [Set up a library](#3-set-up-a-library-for-your-prdb-videos). Videos in libraries of other types
  (*Home Videos & Photos*, *Mixed Movies & Shows*, …) are not looked at.

## Installation

### From the plugin repository (recommended)

1. In Jellyfin, open **Dashboard → Plugins → Manage Repositories → New Repository** and add:
   - Name: `prdb`
   - URL: `https://raw.githubusercontent.com/prdb-net/prdb-jellyfin/main/manifest.json`
2. Back on **Dashboard → Plugins**, choose **Available**, find **prdb** and install it.
3. Restart Jellyfin.

**prdb** now appears under **Dashboard → Plugins → Installed**. Updates are offered there like those of any
other plugin.

### Manually

Download `prdb-jellyfin_<version>.zip` from the [latest release](https://github.com/prdb-net/prdb-jellyfin/releases/latest),
extract `Jellyfin.Plugin.Prdb.dll` into `<jellyfin config>/plugins/prdb_<version>/` and restart Jellyfin.
A manually installed plugin is not updated automatically.

## Setup

### 1. Enter your API key

Open **Dashboard → Plugins → Installed** and click **prdb** to open its settings:

- **API key** – the key from [prdb.net/api-keys](https://prdb.net/api-keys).
- **Base URL** – leave it at `https://api.prdb.net`.

Click **Test connection**. It answers *Connected to api.prdb.net. N of M requests left this hour.* when the key
works, and says so when the key is rejected or prdb cannot be reached. Then click **Save**.

### 2. Choose which matches are applied

prdb tells the plugin how far each match can be trusted. Three switches on the same page decide which kinds
are applied automatically; all three are on by default.

| Switch | A match is applied when … | Reliability |
|---|---|---|
| **Hash matches** | prdb knows the file's hash. | Very high: it is this exact file. |
| **Exact file name matches** | prdb has stored this file name for this exact file. | High. |
| **File name heuristics** | the file name equals a scene release name, or carries a site, a date and a title. | Good, but names can collide, so these are occasionally wrong. |

Matches where prdb finds several equally good videos, and files prdb can only attribute to a site, are
never applied. If you would rather match name-based cases yourself, switch **File name heuristics** off.
Switching a kind off affects future identification only; matches already applied stay until you correct them.

### 3. Set up a library for your prdb videos

**Recommended: a library of its own.** Keep the videos prdb catalogues in their own folder and give that
folder its own library, separate from your regular films and shows. That way prdb is only asked about these
files, general-purpose providers such as TMDb or OMDb never try to match them to a mainstream film with a
similar name, and you can decide per user who sees the library.

1. Open **Dashboard → Libraries → Add Media Library**.
2. **Content type: Movies.** This is the only type the plugin works with. Do not use *Home Videos & Photos*,
   *Mixed Movies & Shows* or *Shows*: their items are not movies, and the plugin does not look at them.
3. Give it a display name (for example *Adult*) and add the folder.
4. Under **Metadata downloaders (Movies)**, tick **prdb** and untick everything else. Other movie databases
   do not know these videos and only produce wrong matches.
5. Under **Image fetchers (Movies)**, tick **prdb** and move it to the top. Jellyfin's own frame extraction
   can stay ticked below it; it then supplies a still for videos prdb does not know.
6. Save, and let Jellyfin scan the library.

**Using an existing library instead:** click the library's **⋮** menu on **Dashboard → Libraries**, choose
**Manage library** and make the same changes under **Metadata downloaders (Movies)** and **Image fetchers
(Movies)**. If the library also holds regular films, keep prdb ticked there as well; it then asks prdb about
every film in it, and a regular film prdb does not know simply gets no match.

The plugin only ever looks at movie libraries where prdb is ticked.

**Organising the files.** One video per file. A flat folder works, and so does a folder per site or per
video. Jellyfin treats files in the same folder whose names differ only by a suffix such as `- 1080p` or
`- part1` as versions or parts of one movie, so give separate videos clearly different names. Keep the
original release or download name if you have it: besides the hash, the name is what prdb identifies a file
by (see the heuristics switch above).

**Keeping the library private.** Under **Dashboard → Users**, open a user, switch off **Enable access to all
libraries** on the *Access* tab and tick only the libraries that user may see. Each user can additionally hide
a library from their home screen under **Settings → Home**.

### 4. Identify what is already there

Open **Dashboard → Scheduled Tasks**, find **Identify library** in the **prdb** section and click ▶.
Progress shows next to the task. When it is done, the identified videos carry their prdb title, description
and images; the others keep the name of their file.

From then on there is nothing more to do: new files are identified after each library scan.

## How it works

### When identification runs

- **After every library scan**, for the movies the scan added.
- **Daily**, as the scheduled task **Identify library**. It also asks again about files whose last answer is
  more than a week old, because prdb keeps learning new hashes and names. You can change the schedule on the
  task's page.
- **When Jellyfin refreshes an item** that has no prdb id yet, the item is identified on the spot.

A movie that already has a prdb id is never identified again: its metadata is simply refreshed from prdb.

### What is sent

For each file: its name without the folder, its size and its OSHash. One request carries up to 200 files.
Files are hashed only when they are about to be sent, and a file's hash is reused as long as its size and
modification time do not change.

### What is remembered

When prdb finds nothing to apply for a file, the plugin remembers the answer, so the next scan does not ask
again. A file is asked about again when its name, size or modification time changes, when you change one of
the three switches, or a week later. What the plugin remembers lives in `<jellyfin data>/prdb/`; deleting the
folder only means every file is asked about once more.

### What is filled in

| Jellyfin | From prdb |
|---|---|
| Title | the video's title |
| Overview | the description |
| Release date and year | the release date |
| Studios | the site, and its network when it has a different name |
| People (actors) | the performers, each with their prdb id |
| Primary image | the video's first image (normally the site's cover) |
| Backdrops | the video's other images |
| External IDs | the prdb id, and the StashDB scene id when prdb has one |
| Performer pages | name, biography, birth and death date, birthplace (or nationality) and portrait |

Identified videos and performers link to their pages on prdb.net. Fields you locked in Jellyfin's
metadata editor are never changed.

## Everyday use

### Checking the results

Every identification run ends with one line in the Jellyfin log (**Dashboard → Logs**), for example:

```
prdb identification finished: checked 120, applied 97 (OsHash 61, Filename 20, ReleaseName 16), switched off 0, ambiguous 3, no match 20, skipped 0, failed 0
```

| Count | Meaning |
|---|---|
| checked | files prdb was asked about in this run |
| applied | matches applied, by what matched: `OsHash` (hash), `Filename` (exact file name), `ReleaseName` / `SiteDateTitle` (heuristics) |
| switched off | matches found but not applied because their switch is off |
| ambiguous | prdb found several videos equally good; nothing applied |
| no match | prdb knows nothing about the file, or only its site |
| skipped | files not asked about because their remembered answer still holds |
| failed | files that could not be asked about because prdb was unreachable or rejected the request |

### Correcting a wrong match, or matching a file by hand

1. Open the video, click **⋮ → Edit metadata**.
2. Under **External IDs**, enter the right id in **prdb Movie Id**. You can paste the whole address of the video
   on prdb.net (`https://prdb.net/videos/<id>`) or just the id. Save.
3. Click **⋮ → Refresh metadata**, choose **Replace all metadata**, tick **Replace existing images** and click
   **Refresh**.

A prdb id that is set is never replaced by the automatic identification. Clearing the field is not enough to
undo a wrong match for good: the next identification would find the same match again. Enter the right id
instead; if prdb does not know the video, switch off the kind of match that produced the wrong one.

### Files that were not identified

- prdb may simply not know the file yet. The plugin asks again after a week.
- If the file name follows a scene release name or `Site - YYYY-MM-DD - Title`, check that **File name
  heuristics** is switched on.
- Otherwise set the prdb id by hand as described above.

### Videos that another provider already matched

The plugin identifies every movie without a prdb id, whatever other providers found for it. When it applies a
match, the item's metadata and images are replaced with prdb's. Keep prdb at the top of the library's metadata
downloaders so that a later refresh does not let another provider overwrite it.

### Browsing by studio on TVs and phones

The desktop web interface has a **Studios** tab in movie libraries. Other apps do not: the Android TV app and
the web interface's TV layout (used on Samsung and LG TVs) only offer genres, collections and favourites, and
mobile apps differ. To browse by studio there as well, switch on **Create a collection per studio** in the plugin
settings. It is off by default.

- The plugin then keeps one collection for every site and network of the identified videos, named after the
  studio and holding exactly its videos. They appear in the library's **Collections** tab and in Jellyfin's
  *Collections* library, in every app.
- A collection follows its videos: new matches are added shortly after their metadata is filled in, videos that
  are gone or now belong to another studio are taken out, and a collection whose last video is gone is deleted.
  Editing a video's studios in the metadata editor moves it as well.
- Each collection gets the cover of its newest video. prdb's site logos are not used.
- A collection is only visible to users who may see a library its videos are in.
- Switching the setting off deletes the collections the plugin made. Collections you made yourself are never
  touched. The plugin recognises its own by the **prdb studio Id** field under *External IDs*; a collection
  where you clear that field is no longer managed by the plugin.
- If a user has **Group movies into collections** switched on (**Settings → Display**), their *Movies* tab
  shows these collections instead of the single videos.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Nothing is identified at all | Check the log for *prdb is not configured*: enter and save the API key. Check that prdb is ticked under **Metadata downloaders (Movies)** of the library and that the library's content type is *Movies*. |
| Videos got the metadata of a mainstream film | Another provider matched them. Untick everything but **prdb** under **Metadata downloaders (Movies)** of that library, then refresh the items with **Replace all metadata**. |
| *Test connection* says the key was rejected | The key is wrong or was revoked. Create a new one on [prdb.net/api-keys](https://prdb.net/api-keys). |
| The log says *Stopped identifying against prdb* | prdb was unreachable, rejected the key, or the request budget of your account is used up for now. The next run continues where this one stopped; *Test connection* shows the remaining budget. |
| Titles are right but images are missing | Tick **prdb** under **Image fetchers (Movies)** of the library and refresh the item with **Replace existing images**. |
| A video got the wrong metadata | Correct it by hand (see above). If it was a name-based match and this happens often in your library, switch **File name heuristics** off. |
| Studio collections do not appear | They follow about half a minute after the videos' metadata changes. Check that **Create a collection per studio** is switched on and saved, and that the videos carry a prdb id and studios. |
| A changed setting has no effect on existing items | Run **Identify library** from **Dashboard → Scheduled Tasks**. |

When asking for help, include the plugin version, the Jellyfin version and the log lines that mention prdb –
but never your API key.

## Updating and uninstalling

- **Updating:** installed from the repository, new versions appear under **Dashboard → Plugins**. Install the
  update and restart Jellyfin. Your settings are kept.
- **Uninstalling:** uninstall **prdb** under **Dashboard → Plugins → Installed**, then restart Jellyfin. Metadata
  already filled in stays on your items; untick prdb in the libraries' settings so Jellyfin stops asking for
  it. The plugin's remembered answers in `<jellyfin data>/prdb/` can be deleted. If you use studio collections,
  switch the setting off and save before uninstalling, so the plugin deletes them; otherwise they stay.

## Privacy

Only files in libraries where prdb is switched on are looked at. For each such file the plugin sends prdb the
file name without its folder, the file size and the OSHash, together with your API key. According to the prdb
API documentation, identification stores neither full file names nor hashes. The plugin never uploads files,
images or anything else to prdb, and it contacts nothing but the prdb API and the image addresses prdb returns.

## Development

See [AGENTS.md](AGENTS.md) for the layout, the build, the unit and end-to-end tests and how releases are made.

## License

[MIT](LICENSE)
