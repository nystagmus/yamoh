using LiteDB;
using Yamoh.Domain.Maintainerr;

namespace Yamoh.Domain.State
{
    public class OverlayStateItem
    {
        [BsonId]
        public int PlexId { get; set; }
        public int MaintainerrCollectionId { get; set; }
        public string? FriendlyTitle { get; set; }
        public string PosterPath { get; set; } = string.Empty;
        public string OriginalPosterPath { get; set; } = string.Empty;
        public bool OverlayApplied { get; set; }
        public bool IsChild { get; set; }
        public int? ParentPlexId { get; set; }
        public DateTimeOffset LastChecked { get; set; } = DateTimeOffset.Now;
        public DateTimeOffset LastKnownExpirationDate { get; set; } = DateTimeOffset.Now;
        public string? PosterHash { get; set; }
        public bool KometaLabelExists { get; set; }
        public int LibrarySectionId { get; set; }
        public MaintainerrDataType MaintainerrPlexType { get; set; }
        public string OverlayText { get; set; } = string.Empty;

        public static OverlayStateItem Create(string mediaServerId, string? parentMediaServerId)
        {
            if (!int.TryParse(mediaServerId, out var id))
                throw new ArgumentException($"MediaServerId '{mediaServerId}' is not a valid Plex ID", nameof(mediaServerId));

            int? parentId = null;
            if (parentMediaServerId == null) return new OverlayStateItem { PlexId = id, ParentPlexId = parentId };

            if (!int.TryParse(parentMediaServerId, out var parsedParentId))
                throw new ArgumentException($"ParentMediaServerId '{parentMediaServerId}' is not a valid Plex ID", nameof(parentMediaServerId));
            parentId = parsedParentId;

            return new OverlayStateItem { PlexId = id, ParentPlexId = parentId };
        }
    }
}

