using System.IO;
using Common.Core.Storage;
///////////////////////////////////////////////
namespace Spark;

sealed class ImageCatalog
{
    readonly JsonStore<ImageRecord> m_store;
    readonly string m_conceptDir;

    public ImageCatalog(string conceptDir)
    {
        m_conceptDir = conceptDir;
        m_store = new JsonStore<ImageRecord>(
            Path.Combine(conceptDir, "catalog.json"),
            r => r.Id);
        MigrateLegacyDeleted();
        CleanupExpiredDeletes();
    }

    public IReadOnlyList<ImageRecord> All => m_store.GetAll();

    public void Add(ImageRecord record) => m_store.Upsert(record);
    public void Update() => m_store.Save();
    public void Remove(ImageRecord record) => m_store.Remove(record.Id);

    public List<ImageRecord> GetStack(int promptNumber) =>
        m_store.GetAll()
            .Where(r => r.PromptNumber == promptNumber && !r.Deleted)
            .OrderByDescending(r => r.GeneratedUtc)
            .ToList();

    public List<ImageRecord> GetVisible() =>
        m_store.GetAll().Where(r => !r.Deleted).OrderBy(r => r.PromptNumber).ToList();

    public int UnseenCount => m_store.GetAll().Count(r => !r.Seen && !r.Deleted);
    public int SavedCount => m_store.GetAll().Count(r => r.Saved && !r.Deleted);

    /// <summary>
    /// Migrates old records that have LegacyDeleted=true but no DeletedUtc.
    /// Sets DeletedUtc to now so they enter the 24-hour window.
    /// </summary>
    void MigrateLegacyDeleted()
    {
        bool changed = false;
        foreach (ImageRecord r in m_store.GetAll())
        {
            if (r.LegacyDeleted && !r.DeletedUtc.HasValue)
            {
                r.DeletedUtc = DateTime.UtcNow;
                changed = true;
            }
        }
        if (changed) m_store.Save();
    }

    /// <summary>
    /// Permanently removes records soft-deleted more than 24 hours ago.
    /// Deletes the image file from disk too. Called on startup.
    /// </summary>
    public int CleanupExpiredDeletes()
    {
        DateTime cutoff = DateTime.UtcNow.AddHours(-24);
        List<ImageRecord> expired = m_store.GetAll()
            .Where(r => r.DeletedUtc.HasValue && r.DeletedUtc.Value < cutoff)
            .ToList();

        foreach (ImageRecord r in expired)
        {
            try
            {
                if (File.Exists(r.FilePath))
                    File.Delete(r.FilePath);
            }
            catch { /* non-fatal */ }
            m_store.Remove(r.Id);
        }
        return expired.Count;
    }

    public int IngestExisting(List<ArtPrompt> prompts)
    {
        int added = 0;
        HashSet<string> known = new(m_store.GetAll().Select(r => r.FilePath));

        if (!Directory.Exists(m_conceptDir)) return 0;
        foreach (string settingsDir in Directory.GetDirectories(m_conceptDir))
        {
            string settingsTag = Path.GetFileName(settingsDir);
            foreach (string file in Directory.GetFiles(settingsDir, "*.png"))
            {
                if (known.Contains(file)) continue;
                string fileName = Path.GetFileNameWithoutExtension(file);
                ArtPrompt? matched = prompts.FirstOrDefault(p => p.Filename == fileName);
                if (matched is null) continue;

                m_store.Upsert(new ImageRecord
                {
                    PromptNumber = matched.Number,
                    Title = matched.Title,
                    Series = matched.Series,
                    FilePath = file,
                    SettingsTag = settingsTag,
                    PromptText = matched.FullText,
                    Style = matched.Style,
                    Seed = -1,
                    GeneratedUtc = File.GetCreationTimeUtc(file),
                });
                added++;
            }
        }
        return added;
    }
}
