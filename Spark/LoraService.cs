using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
///////////////////////////////////////////////
namespace Spark;

/// <summary>
/// Manages LoRA discovery, download, and tag building.
/// </summary>
sealed class LoraService
{
    readonly ImageGenerator m_generator;

    public ObservableCollection<string> AvailableLoras { get; } = ["(none)"];
    public string SelectedLora { get; set; } = "(none)";
    public double LoraWeight { get; set; } = 0.8;

    public LoraService(ImageGenerator generator) => m_generator = generator;

    public ImageGenerator Generator => m_generator;

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
        return (dialog.InstalledLoraName, dialog.InstalledTriggerWords);
    }

    static void Dispatch(Action action)
        => System.Windows.Application.Current.Dispatcher.Invoke(action);
}
