using LiteDB;

namespace YAMOH.Domain.State
{
    public class OverlayStateManager : IDisposable
    {
        private readonly LiteDatabase _db;
        private readonly ILiteCollection<OverlayStateItem> _collection;

        public OverlayStateManager(string? dbPath = null)
        {
            dbPath ??= Path.Combine(Program.AppEnvironment.StateFolder, "yamoh_state.db");

            if (!File.Exists(dbPath))
            {
                File.Create(dbPath).Close();
            }

            this._db = new LiteDatabase(dbPath);
            this._collection = this._db.GetCollection<OverlayStateItem>("yamoh_state");
            this._collection.EnsureIndex(x => x.PlexId);
        }

        public void Upsert(OverlayStateItem item)
        {
            this._collection.Upsert(item);
        }

        public OverlayStateItem? GetByPlexId(int plexId) => this._collection.FindById(plexId);

        public IEnumerable<OverlayStateItem> GetAll() => this._collection.FindAll();

        public void Remove(int plexId)
        {
            this._collection.Delete(plexId);
        }

        public IEnumerable<OverlayStateItem> GetAppliedOverlays()
        {
            return this._collection.Find(x => x.OverlayApplied);
        }

        public IEnumerable<OverlayStateItem> GetPendingRestores(IEnumerable<int> currentPlexIds)
        {
            currentPlexIds = currentPlexIds.ToList();
            // Items that have overlays applied but are no longer in Maintainerr
            return this._collection.Find(x => x.OverlayApplied && !(currentPlexIds.Contains(x.PlexId) || (x.ParentPlexId != null && currentPlexIds.Contains(x.ParentPlexId.Value))));
        }

        public void Dispose()
        {
            this._db.Dispose();
        }
    }
}
