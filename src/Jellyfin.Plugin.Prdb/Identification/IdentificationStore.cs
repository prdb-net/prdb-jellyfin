using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Prdb.Identification;

/// <summary>
/// Remembers what prdb said about files the plugin did not apply a match to, so that a library scan does
/// not ask about the same files again and again. A file is asked about again when its name, size or
/// modification time changes, when the administrator changes the switches, or once the answer is older than
/// <see cref="RecheckAfter"/>, because prdb keeps learning hashes and names.
/// </summary>
public sealed class IdentificationStore
{
    /// <summary>How long an answer without an applied match is trusted.</summary>
    public static readonly TimeSpan RecheckAfter = TimeSpan.FromDays(7);

    private readonly string _storePath;
    private readonly ILogger<IdentificationStore> _logger;
    private readonly object _lock = new();
    private Dictionary<string, Entry>? _entries;

    /// <summary>Initializes a new instance of the <see cref="IdentificationStore"/> class.</summary>
    /// <param name="dataDirectory">The plugin's data directory.</param>
    /// <param name="logger">The logger.</param>
    public IdentificationStore(PrdbDataDirectory dataDirectory, ILogger<IdentificationStore> logger)
    {
        ArgumentNullException.ThrowIfNull(dataDirectory);
        _storePath = Path.Combine(dataDirectory.Path, "identifications.json");
        _logger = logger;
    }

    /// <summary>Gets the remembered answers, read from disk on first use. Call under the lock.</summary>
    private Dictionary<string, Entry> Entries => _entries ??= Load();

    /// <summary>
    /// Gets a value indicating whether a file has to be asked about.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <param name="file">The file as it is now; its hash is not looked at.</param>
    /// <param name="switchesKey">The current switches.</param>
    /// <param name="now">The current time.</param>
    /// <param name="recheckStale">Whether answers older than <see cref="RecheckAfter"/> are asked again.</param>
    /// <returns><c>true</c> when the file has to be asked about.</returns>
    public bool NeedsCheck(string path, FileFingerprint file, string switchesKey, DateTimeOffset now, bool recheckStale)
    {
        lock (_lock)
        {
            if (!Entries.TryGetValue(path, out var entry) || !file.SameFileAs(entry.File) || entry.SwitchesKey != switchesKey)
            {
                return true;
            }

            return recheckStale && now - entry.CheckedAtUtc >= RecheckAfter;
        }
    }

    /// <summary>Gets the hash computed earlier for the same file, so it is not read again.</summary>
    /// <param name="path">The full path of the file.</param>
    /// <param name="file">The file as it is now.</param>
    /// <returns>The stored hash, or <c>null</c>.</returns>
    public string? KnownHash(string path, FileFingerprint file)
    {
        lock (_lock)
        {
            return Entries.TryGetValue(path, out var entry) && file.SameFileAs(entry.File) ? entry.File.OsHash : null;
        }
    }

    /// <summary>Remembers the answer for a file no match was applied to.</summary>
    /// <param name="path">The full path of the file.</param>
    /// <param name="file">The file, with its hash.</param>
    /// <param name="switchesKey">The switches the answer was judged by.</param>
    /// <param name="decision">What was decided.</param>
    /// <param name="checkedAtUtc">When prdb answered.</param>
    public void Remember(string path, FileFingerprint file, string switchesKey, MatchDecision decision, DateTimeOffset checkedAtUtc)
    {
        lock (_lock)
        {
            Entries[path] = new Entry(file, switchesKey, decision, checkedAtUtc);
        }
    }

    /// <summary>Forgets a file, e.g. once a match was applied to it.</summary>
    /// <param name="path">The full path of the file.</param>
    public void Forget(string path)
    {
        lock (_lock)
        {
            Entries.Remove(path);
        }
    }

    /// <summary>Writes the remembered answers to disk.</summary>
    public void Save()
    {
        lock (_lock)
        {
            if (_entries is null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
                var temporary = _storePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(_entries));
                File.Move(temporary, _storePath, overwrite: true);
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "Could not save the remembered prdb answers to {Path}", _storePath);
            }
        }
    }

    private Dictionary<string, Entry> Load()
    {
        try
        {
            if (File.Exists(_storePath))
            {
                return JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_storePath)) ?? [];
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // Losing the memory costs one extra round of questions, nothing more.
            _logger.LogWarning(ex, "Could not read the remembered prdb answers from {Path}; starting afresh", _storePath);
        }

        return [];
    }

    /// <summary>One remembered answer.</summary>
    /// <param name="File">The file it was about.</param>
    /// <param name="SwitchesKey">The switches it was judged by.</param>
    /// <param name="Decision">What was decided.</param>
    /// <param name="CheckedAtUtc">When prdb answered.</param>
    internal sealed record Entry(FileFingerprint File, string SwitchesKey, MatchDecision Decision, DateTimeOffset CheckedAtUtc);
}
