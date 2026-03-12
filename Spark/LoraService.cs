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

    public static readonly (string name, string url)[] PopularLoras =
    [
        ("Detail Tweaker XL", "https://civitai.com/api/download/models/135867"),
        ("SDXL Offset Noise", "https://civitai.com/api/download/models/134820"),
        ("Pixel Art XL", "https://civitai.com/api/download/models/120096"),
        ("Anime Lineart Style", "https://civitai.com/api/download/models/128713"),
        ("Cinematic Film Grain", "https://civitai.com/api/download/models/162627"),
        ("Sci-Fi Environments", "https://civitai.com/api/download/models/170268"),
    ];

    public LoraService(ImageGenerator generator) => m_generator = generator;

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

    public static void BrowseCivitAI()
    {
        try { Process.Start(new ProcessStartInfo("https://civitai.com/models?types=LORA&sort=Most+Downloaded") { UseShellExecute = true }); }
        catch { /* non-fatal */ }
    }

    static void Dispatch(Action action)
        => System.Windows.Application.Current.Dispatcher.Invoke(action);
}
