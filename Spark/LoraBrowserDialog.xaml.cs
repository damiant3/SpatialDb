using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
///////////////////////////////////////////////
namespace Spark;

/// <summary>
/// Browsable LoRA catalog that queries CivitAI for SDXL-compatible LoRAs,
/// shows preview images, and allows one-click install into Forge.
/// </summary>
partial class LoraBrowserDialog : Window
{
    static readonly HttpClient s_http = new()
    {
        Timeout = TimeSpan.FromSeconds(30),
        DefaultRequestHeaders = { { "User-Agent", "Spark/1.0" } }
    };

    readonly ImageGenerator m_generator;
    readonly LoraService m_loraService;
    readonly Action<string> m_log;

    /// <summary>Set after a successful install — the VM reads this to auto-select.</summary>
    public string? InstalledLoraName { get; private set; }
    public string? InstalledTriggerWords { get; private set; }

    public LoraBrowserDialog(ImageGenerator generator, LoraService loraService, Action<string> log)
    {
        InitializeComponent();
        m_generator = generator;
        m_loraService = loraService;
        m_log = log;
        Owner = Application.Current.MainWindow;
    }

    // ── Search / UI events ──────────────────────────────────────

    void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SearchCivitAI(SearchBox.Text.Trim());
    }

    void OnSearchClick(object sender, RoutedEventArgs e)
        => SearchCivitAI(SearchBox.Text.Trim());

    void OnPopularClick(object sender, RoutedEventArgs e)
        => SearchCivitAI("", sortByDownloads: true);

    void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is LoraSearchResult item)
        {
            string trigger = item.TriggerWords.Length > 0 ? $"  •  Trigger: {item.TriggerWords}" : "";
            StatusLabel.Text = $"Selected: {item.Name}  •  {item.BaseModel}  •  {item.FileName}{trigger}";
        }
    }

    async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LoraSearchResult item } btn) return;

        // Always persist trigger words — even if direct download fails and
        // the user has to download manually via browser, the trigger words
        // will be available when they refresh the LoRA list.
        string loraBaseName = System.IO.Path.GetFileNameWithoutExtension(item.FileName);
        if (item.TriggerWords.Length > 0 && loraBaseName.Length > 0)
            m_loraService.SetTriggerWords(loraBaseName, item.TriggerWords);

        if (string.IsNullOrEmpty(item.DownloadUrl))
        {
            StatusLabel.Text = "No download URL — opening CivitAI page…";
            if (item.TriggerWords.Length > 0)
                StatusLabel.Text += $"  •  Trigger words saved: {item.TriggerWords}";
            OpenModelPage(item);
            return;
        }

        btn.IsEnabled = false;
        btn.Content = "⏳…";
        StatusLabel.Text = $"Downloading {item.Name}…";

        bool ok = await Task.Run(async () =>
        {
            try
            {
                return await m_generator.DownloadLoraAsync(item.DownloadUrl, item.FileName,
                    msg => Dispatcher.Invoke(() =>
                    {
                        StatusLabel.Text = msg;
                        m_log(msg);
                    }));
            }
            catch { return false; }
        });

        if (ok)
        {
            InstalledLoraName = loraBaseName;
            InstalledTriggerWords = item.TriggerWords;
            StatusLabel.Text = $"✓ Installed {item.Name}" +
                (item.TriggerWords.Length > 0 ? $"  •  Trigger words: {item.TriggerWords}" : "");
            m_loraService.LoadLoras(m_log);
            btn.Content = "✓ Done";
        }
        else
        {
            // 401/403 = auth required — fall back to browser
            StatusLabel.Text = "Direct download failed (auth required) — opening CivitAI page…";
            if (item.TriggerWords.Length > 0)
                StatusLabel.Text += $"  •  Trigger words saved: {item.TriggerWords}";
            OpenModelPage(item);
            btn.Content = "🌐 Opened";
            btn.IsEnabled = true;
        }
    }

    static void OpenModelPage(LoraSearchResult item)
    {
        if (item.ModelPageUrl.Length > 0)
        {
            try
            {
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(item.ModelPageUrl) { UseShellExecute = true });
            }
            catch { /* non-fatal */ }
        }
    }

    void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    // ── CivitAI API ─────────────────────────────────────────────

    async void SearchCivitAI(string query, bool sortByDownloads = false)
    {
        StatusLabel.Text = "Searching CivitAI…";
        ResultsList.ItemsSource = null;

        try
        {
            string sort = sortByDownloads ? "Most Downloaded" : "Highest Rated";
            string url = $"https://civitai.com/api/v1/models?types=LORA&sort={Uri.EscapeDataString(sort)}&limit=30&nsfw=false";
            if (!string.IsNullOrWhiteSpace(query))
                url += $"&query={Uri.EscapeDataString(query)}";

            string json = await s_http.GetStringAsync(url);
            JsonNode? root = JsonNode.Parse(json);
            JsonArray? items = root?["items"]?.AsArray();

            if (items is null || items.Count == 0)
            {
                StatusLabel.Text = "No results found.";
                return;
            }

            List<LoraSearchResult> results = [];
            foreach (JsonNode? item in items)
            {
                if (item is null) continue;
                int modelId = item["id"]?.GetValue<int>() ?? 0;
                string name = item["name"]?.GetValue<string>() ?? "";
                int downloads = item["stats"]?["downloadCount"]?.GetValue<int>() ?? 0;
                double rating = item["stats"]?["rating"]?.GetValue<double>() ?? 0;

                JsonArray? tagsArr = item["tags"]?.AsArray();
                string tags = tagsArr is not null
                    ? string.Join(", ", tagsArr.Select(t => t?.GetValue<string>() ?? "").Where(t => t.Length > 0).Take(5))
                    : "";

                JsonArray? versions = item["modelVersions"]?.AsArray();
                if (versions is null || versions.Count == 0) continue;

                // Prefer SDXL versions
                JsonNode? bestVersion = null;
                foreach (JsonNode? v in versions)
                {
                    string? baseModel = v?["baseModel"]?.GetValue<string>();
                    if (baseModel?.Contains("SDXL", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        bestVersion = v;
                        break;
                    }
                }
                bestVersion ??= versions[0];

                string baseModelStr = bestVersion?["baseModel"]?.GetValue<string>() ?? "Unknown";
                int versionId = bestVersion?["id"]?.GetValue<int>() ?? 0;

                // Extract trigger words
                JsonArray? trainedWords = bestVersion?["trainedWords"]?.AsArray();
                string triggerWords = trainedWords is not null
                    ? string.Join(", ", trainedWords.Select(w => w?.GetValue<string>() ?? "").Where(w => w.Length > 0))
                    : "";

                // Thumbnail
                string thumbnailUrl = "";
                JsonArray? images = bestVersion?["images"]?.AsArray();
                if (images is { Count: > 0 })
                    thumbnailUrl = images[0]?["url"]?.GetValue<string>() ?? "";

                // Download URL + filename
                string downloadUrl = "";
                string fileName = "";
                JsonArray? files = bestVersion?["files"]?.AsArray();
                if (files is not null)
                {
                    foreach (JsonNode? f in files)
                    {
                        string? fName = f?["name"]?.GetValue<string>();
                        if (fName?.EndsWith(".safetensors", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            downloadUrl = f?["downloadUrl"]?.GetValue<string>() ?? "";
                            fileName = fName;
                            break;
                        }
                    }
                    if (downloadUrl.Length == 0 && files.Count > 0)
                    {
                        downloadUrl = files[0]?["downloadUrl"]?.GetValue<string>() ?? "";
                        fileName = files[0]?["name"]?.GetValue<string>() ?? "";
                    }
                }

                bool isSdxl = baseModelStr.Contains("SDXL", StringComparison.OrdinalIgnoreCase);
                string modelPageUrl = modelId > 0
                    ? $"https://civitai.com/models/{modelId}?modelVersionId={versionId}"
                    : "";

                results.Add(new LoraSearchResult
                {
                    Name = name,
                    BaseModel = baseModelStr,
                    Downloads = downloads,
                    Rating = Math.Round(rating, 1),
                    Tags = tags,
                    TriggerWords = triggerWords,
                    ThumbnailUrl = thumbnailUrl,
                    DownloadUrl = downloadUrl,
                    FileName = fileName,
                    IsSdxlCompatible = isSdxl,
                    ModelPageUrl = modelPageUrl,
                });
            }

            // SDXL-compatible first, then by downloads
            results.Sort((a, b) =>
            {
                int cmp = b.IsSdxlCompatible.CompareTo(a.IsSdxlCompatible);
                return cmp != 0 ? cmp : b.Downloads.CompareTo(a.Downloads);
            });

            ResultsList.ItemsSource = results;
            StatusLabel.Text = $"Found {results.Count} LoRAs ({results.Count(r => r.IsSdxlCompatible)} SDXL-compatible)";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Search failed: {ex.Message}";
        }
    }
}

sealed class LoraSearchResult
{
    public string Name { get; init; } = "";
    public string BaseModel { get; init; } = "";
    public int Downloads { get; init; }
    public double Rating { get; init; }
    public string Tags { get; init; } = "";
    public string TriggerWords { get; init; } = "";
    public string ThumbnailUrl { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string FileName { get; init; } = "";
    public bool IsSdxlCompatible { get; init; }
    public string ModelPageUrl { get; init; } = "";
    public bool HasTriggerWords => TriggerWords.Length > 0;
}
