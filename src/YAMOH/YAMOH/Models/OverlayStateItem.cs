using System;
using LiteDB;

namespace YAMOH.Models
{
    public class OverlayStateItem
    {
        [BsonId]
        public string PlexId { get; set; } = string.Empty;
        public int MaintainerrCollectionId { get; set; }
        public string PosterPath { get; set; } = string.Empty;
        public string OriginalPosterPath { get; set; } = string.Empty;
        public bool OverlayApplied { get; set; }
        public DateTime LastChecked { get; set; }
        public string? PosterHash { get; set; }
    }
}

