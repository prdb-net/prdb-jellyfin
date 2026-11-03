using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.Prdb.Collections;

/// <summary>An identified movie and the studios Jellyfin has for it.</summary>
/// <param name="MovieId">The movie's item id.</param>
/// <param name="Studios">Its studios.</param>
internal sealed record StudioMember(Guid MovieId, IReadOnlyList<string> Studios);
