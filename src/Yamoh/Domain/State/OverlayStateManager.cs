using LiteDB;

namespace Yamoh.Domain.State
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

        public OverlayStateItem? GetByPlexId(string mediaServerId) =>
            int.TryParse(mediaServerId, out var id)
                ? this._collection.FindById(id)
                : throw new ArgumentException($"MediaServerId {mediaServerId} is not a valid Id",  nameof(mediaServerId));

        public IEnumerable<OverlayStateItem> GetAll() => this._collection.FindAll();

        public void Remove(string mediaServerId)
        {
            if (int.TryParse(mediaServerId, out var id))
            {
                this._collection.Delete(id);
            }
            else
            {
                throw new ArgumentException($"MediaServerId {mediaServerId} is not a valid Id",  nameof(mediaServerId));
            }
        }

        public void Remove(int plexId)
        {
            this._collection.Delete(plexId);
        }

        public IEnumerable<OverlayStateItem> GetAppliedOverlays()
        {
            return this._collection.Find(x => x.OverlayApplied);
        }

        public IEnumerable<OverlayStateItem> GetNeedsRestoresMissingFromList(IEnumerable<string> currentMediaServerIds)
        {
            var currentIds = currentMediaServerIds
                .Select(id => int.TryParse(id, out var i) ? (int?)i : null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToList();

            return this._collection.Find(x =>
                x.OverlayApplied &&
                !(currentIds.Contains(x.PlexId) ||
                  (x.ParentPlexId != null && currentIds.Contains(x.ParentPlexId.Value))));
        }

        public void Dispose()
        {
            this._db.Dispose();
        }
    }
}
