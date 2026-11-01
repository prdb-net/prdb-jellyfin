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

Installation and configuration instructions follow with the first release.

## License

[MIT](LICENSE)
