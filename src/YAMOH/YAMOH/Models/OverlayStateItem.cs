using System;
using LiteDB;
using YAMOH.Models.Maintainerr;

namespace YAMOH.Models
{
    public class OverlayStateItem
    {
        [BsonId]
        public int PlexId { get; set; }
        public int MaintainerrCollectionId { get; set; }
        public string PosterPath { get; set; } = string.Empty;
        public string OriginalPosterPath { get; set; } = string.Empty;
        public bool OverlayApplied { get; set; }
        public bool IsChild { get; set; }
        public int? ParentPlexId { get; set; }
        public DateTimeOffset LastChecked { get; set; } = DateTimeOffset.Now;
        public DateTimeOffset LastKnownExpirationDate { get; set; } = DateTimeOffset.Now;
        public string? PosterHash { get; set; }
        public bool KometaLabelExists { get; set; }
        public long LibrarySectionId { get; set; }
        public MaintainerrPlexDataType MaintainerrPlexType { get; set; }
    }
}

