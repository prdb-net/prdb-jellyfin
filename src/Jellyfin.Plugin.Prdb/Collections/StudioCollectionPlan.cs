using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Prdb.Collections;

/// <summary>
/// What has to change so that there is exactly one collection per studio of the identified movies, holding
/// exactly those movies, and none when the switch is off. A collection without members is always deleted:
/// Jellyfin shows an empty collection to every user, whatever libraries they may see.
/// </summary>
internal sealed class StudioCollectionPlan
{
    private StudioCollectionPlan(
        IReadOnlyList<StudioCollectionCreation> create,
        IReadOnlyList<StudioCollectionChange> change,
        IReadOnlyList<Guid> delete)
    {
        Create = create;
        Change = change;
        Delete = delete;
    }

    /// <summary>Gets the collections to make, by name.</summary>
    public IReadOnlyList<StudioCollectionCreation> Create { get; }

    /// <summary>Gets the collections whose members change.</summary>
    public IReadOnlyList<StudioCollectionChange> Change { get; }

    /// <summary>Gets the collections to delete.</summary>
    public IReadOnlyList<Guid> Delete { get; }

    /// <summary>Gets a value indicating whether nothing has to change.</summary>
    public bool IsEmpty => Create.Count == 0 && Change.Count == 0 && Delete.Count == 0;

    /// <summary>Plans the changes.</summary>
    /// <param name="enabled">Whether the studio collections are switched on.</param>
    /// <param name="movies">The identified movies with their studios.</param>
    /// <param name="existing">The collections the plugin made earlier.</param>
    /// <returns>The plan.</returns>
    public static StudioCollectionPlan For(bool enabled, IEnumerable<StudioMember> movies, IEnumerable<StudioCollection> existing)
    {
        // Studios are matched without regard to case or surrounding blanks; the first spelling seen names
        // a new collection.
        var wanted = new Dictionary<string, (string Name, List<Guid> Members)>(StringComparer.OrdinalIgnoreCase);
        if (enabled)
        {
            foreach (var movie in movies)
            {
                foreach (var studio in movie.Studios)
                {
                    var name = studio?.Trim();
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    if (!wanted.TryGetValue(name, out var entry))
                    {
                        entry = (name, []);
                        wanted[name] = entry;
                    }

                    if (!entry.Members.Contains(movie.MovieId))
                    {
                        entry.Members.Add(movie.MovieId);
                    }
                }
            }
        }

        var change = new List<StudioCollectionChange>();
        var delete = new List<Guid>();
        var kept = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var collection in existing)
        {
            var studio = collection.Studio.Trim();

            // A second collection for the same studio (made by a run that overlapped, or copied by hand) goes.
            if (!wanted.TryGetValue(studio, out var entry) || !kept.Add(studio))
            {
                delete.Add(collection.Id);
                continue;
            }

            var add = entry.Members.Where(id => !collection.Members.Contains(id)).ToList();
            var remove = collection.Members.Where(id => !entry.Members.Contains(id)).ToList();
            if (add.Count > 0 || remove.Count > 0)
            {
                change.Add(new StudioCollectionChange(collection.Id, add, remove));
            }
        }

        var create = wanted.Values
            .Where(entry => !kept.Contains(entry.Name))
            .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .Select(entry => new StudioCollectionCreation(entry.Name, entry.Members))
            .ToList();

        return new StudioCollectionPlan(create, change, delete);
    }
}
