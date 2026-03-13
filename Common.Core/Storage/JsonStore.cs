using System.IO;
using System.Text.Json;
///////////////////////////////
namespace Common.Core.Storage;

public interface IStore<T>
{
    T? Get(string id);
    IReadOnlyList<T> GetAll();
    void Upsert(T item);
    void Remove(string id);
    void Save();
    event EventHandler? Changed;
}

public sealed class JsonStore<T> : IStore<T>
{
    readonly string m_path;
    readonly Func<T, string> m_getId;
    readonly Dictionary<string, T> m_index = new();
    readonly List<T> m_items = new();
    public event EventHandler? Changed;

    static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public JsonStore(string filePath, Func<T, string> getId)
    {
        m_path = filePath;
        m_getId = getId;

        string? dir = Path.GetDirectoryName(filePath);
        if (dir is { Length: > 0 })
            Directory.CreateDirectory(dir);

        Load();
    }

    void Load()
    {
        m_items.Clear();
        m_index.Clear();
        if (!File.Exists(m_path)) return;
        string json = File.ReadAllText(m_path);
        List<T>? loaded = JsonSerializer.Deserialize<List<T>>(json, SerializerOptions);
        if (loaded is null) return;
        foreach (T item in loaded)
        {
            string id = m_getId(item);
            m_index[id] = item;
            m_items.Add(item);
        }
    }

    public T? Get(string id) => m_index.TryGetValue(id, out T? v) ? v : default;
    public IReadOnlyList<T> GetAll() => m_items;

    public void Upsert(T item)
    {
        string id = m_getId(item);
        if (m_index.TryGetValue(id, out T? existing))
        {
            int idx = m_items.IndexOf(existing);
            if (idx >= 0) m_items[idx] = item;
            m_index[id] = item;
        }
        else
        {
            m_index[id] = item;
            m_items.Add(item);
        }
        Save();
    }

    public void Remove(string id)
    {
        if (!m_index.TryGetValue(id, out T? item)) return;
        m_index.Remove(id);
        m_items.Remove(item);
        Save();
    }

    public void Save()
    {
        string json = JsonSerializer.Serialize(m_items, SerializerOptions);
        File.WriteAllText(m_path, json);
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
