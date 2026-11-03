using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Prdb.Collections;

/// <summary>Movies to add to and remove from an existing collection.</summary>
/// <param name="CollectionId">The collection.</param>
/// <param name="Add">The movies to add.</param>
/// <param name="Remove">The items to remove.</param>
internal sealed record StudioCollectionChange(Guid CollectionId, IReadOnlyList<Guid> Add, IReadOnlyList<Guid> Remove);
