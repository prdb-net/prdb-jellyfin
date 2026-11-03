using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Prdb.Collections;

/// <summary>A collection the plugin made for a studio, as it is now.</summary>
/// <param name="Id">The collection's item id.</param>
/// <param name="Studio">The studio it was made for.</param>
/// <param name="Members">The item ids it holds.</param>
internal sealed record StudioCollection(Guid Id, string Studio, IReadOnlyCollection<Guid> Members);
