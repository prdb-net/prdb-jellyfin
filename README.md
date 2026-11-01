# prdb for Jellyfin

A metadata provider for [Jellyfin](https://jellyfin.org) that identifies your local videos against
[prdb.net](https://prdb.net) and fills in their title, description, release date, site, performers and
images.

- **Identifies by file hash and file name.** The plugin computes each file's OSHash (a cheap hash that
  reads only the first and last 64 KiB) and sends it together with the file name and size to the prdb API,
  which answers with the matching video.
- **Read-only.** The plugin never submits anything to prdb: no hashes, no images, no corrections.
- **Requires a prdb API key.** Every Jellyfin administrator uses the key of their own prdb account.
- **Jellyfin 12.x only.**

## Installation

### From the plugin repository (recommended)

1. In Jellyfin, open **Dashboard → Plugins → Repositories** (or **Catalog → ⚙**) and add a repository:
   - Name: `prdb`
   - URL: `https://raw.githubusercontent.com/prdb-net/prdb-jellyfin/main/manifest.json`
2. Open the **Catalog**, find **prdb** under *Metadata* and install it.
3. Restart Jellyfin.

Updates then appear in the catalog like those of any other plugin.

### Manually

Download `prdb-jellyfin_<version>.zip` from the [latest release](https://github.com/prdb-net/prdb-jellyfin/releases/latest),
extract `Jellyfin.Plugin.Prdb.dll` into `<jellyfin config>/plugins/prdb_<version>/` and restart Jellyfin.

## Configuration

Open **Dashboard → Plugins → prdb**:

- **API key** – the API key of your own prdb account. Required; without it the plugin identifies nothing.
- **Base URL** – leave it at `https://api.prdb.net`.
- **Test connection** checks the key before you save it.

Then switch prdb on for the libraries it should handle: **Dashboard → Libraries → (library) → Manage library**,
and under *Metadata downloaders (Movies)* and *Image fetchers (Movies)* tick **prdb** and move it to the top. The
plugin only ever looks at movie libraries where prdb is ticked; files in other libraries are never sent to prdb.
Then run **Dashboard → Scheduled Tasks → prdb → Identify library** once, or wait for the next library scan.

prdb says how far each match can be trusted. Three switches decide which kinds the plugin applies on its own;
all three are on by default:

| Switch | Applies a match when … |
|---|---|
| Hash matches | prdb knows the file's hash. The most reliable kind. |
| Exact file name matches | prdb has stored this file name for this exact file. |
| File name heuristics | the file name equals a scene release name, or carries a site, a date and a title. Names can collide, so these are occasionally wrong. |

Ambiguous matches, and files prdb can only attribute to a site, are never applied.

**Correcting a match:** open the video's metadata editor, enter the right prdb video id under *External IDs* (the id
is the last part of the video's address on prdb.net, `https://prdb.net/videos/<id>`) and refresh its metadata. An id
that is set is never replaced by the automatic identification.

## Privacy

Only files in libraries where prdb is switched on are looked at. For each such file the plugin sends prdb the file name without its folder, the file size and the OSHash. According
to the prdb API documentation, identification stores neither full file names nor hashes. The plugin submits nothing
to prdb and works only with the API key you enter.

## License

[MIT](LICENSE)
