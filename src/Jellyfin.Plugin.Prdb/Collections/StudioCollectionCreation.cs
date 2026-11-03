using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Prdb.Collections;

/// <summary>A collection to make.</summary>
/// <param name="Studio">The studio, which is also its name.</param>
/// <param name="Members">The movies it holds.</param>
internal sealed record StudioCollectionCreation(string Studio, IReadOnlyList<Guid> Members);
