using LiteDB;

namespace YAMOH.Models
{
    public class OverlayStateManager : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<OverlayStateItem> _collection;

        public OverlayStateManager(string dbPath = "state/overlay_state.db")
        {
            _db = new LiteDatabase(dbPath);
            _collection = _db.GetCollection<OverlayStateItem>("overlay_state");
            _collection.EnsureIndex(x => x.PlexId);
        }

        public void Upsert(OverlayStateItem item)
        {
            _collection.Upsert(item);
        }

        public OverlayStateItem? GetByPlexId(string plexId) => _collection.FindById(plexId);

        public IEnumerable<OverlayStateItem> GetAll() => _collection.FindAll();

        public void Remove(string plexId)
        {
            _collection.Delete(plexId);
        }

        public IEnumerable<OverlayStateItem> GetAppliedOverlays()
        {
            return _collection.Find(x => x.OverlayApplied);
        }

        public IEnumerable<OverlayStateItem> GetPendingRestores(IEnumerable<string> currentPlexIds)
        {
            currentPlexIds = currentPlexIds.ToList();
            // Items that have overlays applied but are no longer in Maintainerr
            return _collection.Find(x => x.OverlayApplied && !(currentPlexIds.Contains(x.PlexId) || currentPlexIds.Contains(x.ParentPlexId)));
        }

        public void Dispose()
        {
            _db.Dispose();
        }
    }
}
