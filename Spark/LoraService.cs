using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
///////////////////////////////////////////////
namespace Spark;

/// <summary>
/// Manages LoRA discovery, download, tag building, and trigger word storage.
/// Trigger words are persisted in lora_triggers.json so they survive across sessions
/// and are available even when LoRAs were manually downloaded outside the browser.
/// </summary>
sealed class LoraService
{
    readonly ImageGenerator m_generator;
    readonly string m_triggersPath;
    Dictionary<string, string> m_triggers = [];
    string m_currentTriggerWords = "";

    public ObservableCollection<string> AvailableLoras { get; } = ["(none)"];
    public string SelectedLora { get; set; } = "(none)";
    public double LoraWeight { get; set; } = 0.8;

    /// <summary>
    /// Trigger words for the currently selected LoRA.
    /// Updates automatically when SelectedLora changes.
    /// </summary>
    public string CurrentTriggerWords
    {
        get => m_currentTriggerWords;
        set
        {
            m_currentTriggerWords = value;
            // Persist the edit
            if (SelectedLora != "(none)" && !string.IsNullOrWhiteSpace(SelectedLora))
            {
                if (value.Length > 0)
                    m_triggers[SelectedLora] = value;
                else
                    m_triggers.Remove(SelectedLora);
                SaveTriggers();
            }
        }
    }

    public LoraService(ImageGenerator generator)
    {
        m_generator = generator;
        m_triggersPath = Path.Combine(AppContext.BaseDirectory, "lora_triggers.json");
        LoadTriggers();
    }

    public ImageGenerator Generator => m_generator;

    /// <summary>
    /// Looks up trigger words for the currently selected LoRA.
    /// Call this after changing SelectedLora.
    /// </summary>
    public void RefreshTriggerWords()
    {
        if (SelectedLora == "(none)" || string.IsNullOrWhiteSpace(SelectedLora))
            m_currentTriggerWords = "";
        else
            if(!m_triggers.TryGetValue(SelectedLora, out m_currentTriggerWords!))
                m_currentTriggerWords ??= "";
    }

    /// <summary>
    /// Records trigger words for a LoRA (e.g. from CivitAI browser results).
    /// </summary>
    public void SetTriggerWords(string loraName, string triggerWords)
    {
        if (string.IsNullOrWhiteSpace(loraName)) return;
        if (triggerWords.Length > 0)
            m_triggers[loraName] = triggerWords;
        else
            m_triggers.Remove(loraName);
        SaveTriggers();
    }

    /// <summary>
    /// Gets stored trigger words for a LoRA by name.
    /// </summary>
    public string GetTriggerWords(string loraName)
    {
        return m_triggers.TryGetValue(loraName, out string? tw) ? tw : "";
    }

    public void LoadLoras(Action<string> log)
    {
        Task.Run(async () =>
        {
            List<LoraInfo> loras = await m_generator.GetAvailableLoras(forceRefresh: true);
            Dispatch(() =>
            {
                AvailableLoras.Clear();
                AvailableLoras.Add("(none)");
                foreach (LoraInfo lora in loras)
                    AvailableLoras.Add(lora.Name);
                if (!AvailableLoras.Contains(SelectedLora))
                    SelectedLora = "(none)";
                RefreshTriggerWords();
                log($"Found {loras.Count} LoRAs");
            });
        });
    }

    public void DownloadLora(string url, Action<string> log)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        string fileName;
        try { fileName = Path.GetFileName(new Uri(url).LocalPath); }
        catch { fileName = "downloaded_lora"; }
        if (!fileName.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase))
            fileName += ".safetensors";

        Task.Run(async () =>
        {
            bool ok = await m_generator.DownloadLoraAsync(url, fileName,
                msg => Dispatch(() => log(msg)));
            if (ok) LoadLoras(log);
        });
    }

    public string? BuildLoraTag()
    {
        if (SelectedLora == "(none)" || string.IsNullOrWhiteSpace(SelectedLora))
            return null;
        return $"<lora:{SelectedLora}:{LoraWeight:F1}>";
    }

    /// <summary>
    /// Opens the LoRA browser dialog. Returns trigger words if a LoRA was installed, null otherwise.
    /// </summary>
    public (string? loraName, string? triggerWords) BrowseAndInstall(Action<string> log)
    {
        LoraBrowserDialog dialog = new(m_generator, this, log);
        dialog.ShowDialog();

        // Persist trigger words from browser results
        if (dialog.InstalledLoraName is not null && dialog.InstalledTriggerWords is { Length: > 0 })
            SetTriggerWords(dialog.InstalledLoraName, dialog.InstalledTriggerWords);

        return (dialog.InstalledLoraName, dialog.InstalledTriggerWords);
    }

    // ── Trigger words persistence ───────────────────────────────

    void LoadTriggers()
    {
        if (!File.Exists(m_triggersPath)) return;
        try
        {
            string json = File.ReadAllText(m_triggersPath);
            m_triggers = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch { m_triggers = []; }
    }

    void SaveTriggers()
    {
        try
        {
            string json = JsonSerializer.Serialize(m_triggers, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(m_triggersPath, json);
        }
        catch { /* non-fatal */ }
    }

    static void Dispatch(Action action)
        => System.Windows.Application.Current.Dispatcher.Invoke(action);
}
