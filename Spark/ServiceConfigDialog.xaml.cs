using System.IO;
using System.Windows;
using Microsoft.Win32;
using Spark.Services;
///////////////////////////////////////////////
namespace Spark;

partial class ServiceConfigDialog : Window
{
    readonly ServiceEndpointConfig m_config;
    readonly LocalServiceManager? m_serviceManager;
    readonly string m_serviceName;
    readonly ServiceEndpoint m_endpoint;

    public ServiceConfigDialog(ServiceEndpointConfig config, string serviceName,
        LocalServiceManager? serviceManager = null)
    {
        InitializeComponent();
        m_config = config;
        m_serviceName = serviceName;
        m_serviceManager = serviceManager;
        m_endpoint = ResolveEndpoint();

        string displayName = serviceName switch
        {
            "Ollama" => "Ollama (LLM)",
            "StableDiffusion" => "Stable Diffusion (Forge)",
            "MusicGen" => "MusicGen (AudioCraft)",
            _ => serviceName,
        };
        HeaderText.Text = $"Configure {displayName}";
        Title = $"Configure {displayName}";

        BaseUrlBox.Text = m_endpoint.BaseUrl;
        ExePathBox.Text = m_endpoint.ExecutablePath;
        ArgsBox.Text = m_endpoint.StartArguments;
        DownloadUrlBox.Text = m_endpoint.DownloadUrl;
        AutoStartCheck.IsChecked = m_endpoint.AutoStart;
        AiBaseDirBox.Text = m_config.AiBaseDir;

        // Extract port from URL
        try
        {
            Uri uri = new(m_endpoint.BaseUrl);
            PortBox.Text = uri.Port.ToString();
        }
        catch { PortBox.Text = ""; }

        UpdateDetectionInfo();
        UpdateStartStopButtons();
    }

    ServiceEndpoint ResolveEndpoint() => m_serviceName switch
    {
        "Ollama" => m_config.Ollama,
        "StableDiffusion" => m_config.StableDiffusion,
        "MusicGen" => m_config.MusicGen,
        _ => m_config.Ollama,
    };

    void OnBrowseExe(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dlg = new()
        {
            Title = "Select executable or batch file",
            Filter = "Executables|*.exe;*.bat;*.cmd;*.ps1|All files|*.*",
        };
        string currentPath = ExePathBox.Text;
        if (currentPath.Length > 0 && Directory.Exists(Path.GetDirectoryName(currentPath)))
            dlg.InitialDirectory = Path.GetDirectoryName(currentPath);
        else if (Directory.Exists(AiBaseDirBox.Text))
            dlg.InitialDirectory = AiBaseDirBox.Text;

        if (dlg.ShowDialog() == true)
        {
            ExePathBox.Text = dlg.FileName;
            UpdateDetectionInfo();
        }
    }

    void OnBrowseAiDir(object sender, RoutedEventArgs e)
    {
        OpenFolderDialog dlg = new()
        {
            Title = "Select your AI tools base directory",
        };
        if (Directory.Exists(AiBaseDirBox.Text))
            dlg.InitialDirectory = AiBaseDirBox.Text;

        if (dlg.ShowDialog() == true)
        {
            AiBaseDirBox.Text = dlg.FolderName;
            UpdateDetectionInfo();
        }
    }

    void OnAutoDetect(object sender, RoutedEventArgs e)
    {
        m_config.AiBaseDir = AiBaseDirBox.Text;
        m_config.AutoDetectPaths();

        // Refresh from config
        ServiceEndpoint ep = ResolveEndpoint();
        ExePathBox.Text = ep.ExecutablePath;
        UpdateDetectionInfo();
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        // Apply port change to URL
        string baseUrl = BaseUrlBox.Text.Trim();
        if (int.TryParse(PortBox.Text.Trim(), out int port) && port > 0)
        {
            try
            {
                Uri existing = new(baseUrl);
                UriBuilder ub = new(existing) { Port = port };
                baseUrl = ub.ToString().TrimEnd('/');
            }
            catch { /* keep baseUrl as-is */ }
        }

        m_endpoint.BaseUrl = baseUrl;
        m_endpoint.ExecutablePath = ExePathBox.Text.Trim();
        m_endpoint.StartArguments = ArgsBox.Text.Trim();
        m_endpoint.DownloadUrl = DownloadUrlBox.Text.Trim();
        m_endpoint.AutoStart = AutoStartCheck.IsChecked == true;
        m_config.AiBaseDir = AiBaseDirBox.Text.Trim();

        m_config.Save();
        DialogResult = true;
    }

    async void OnStart(object sender, RoutedEventArgs e)
    {
        if (m_serviceManager is null) return;

        // Save current settings first
        OnSaveInternal();

        StartBtn.IsEnabled = false;
        StartBtn.Content = "Starting…";
        string result = await m_serviceManager.TryStartServiceAsync(m_serviceName);
        StartBtn.Content = "▶ Start";
        StartBtn.IsEnabled = true;

        UpdateStartStopButtons();
        DetectionInfo.Text = result;
    }

    void OnStop(object sender, RoutedEventArgs e)
    {
        if (m_serviceManager is null) return;

        string result = m_serviceManager.TryStopService(m_serviceName);
        UpdateStartStopButtons();
        DetectionInfo.Text = result;
    }

    void OnSaveInternal()
    {
        string baseUrl = BaseUrlBox.Text.Trim();
        if (int.TryParse(PortBox.Text.Trim(), out int port) && port > 0)
        {
            try
            {
                Uri existing = new(baseUrl);
                UriBuilder ub = new(existing) { Port = port };
                baseUrl = ub.ToString().TrimEnd('/');
            }
            catch { }
        }

        m_endpoint.BaseUrl = baseUrl;
        m_endpoint.ExecutablePath = ExePathBox.Text.Trim();
        m_endpoint.StartArguments = ArgsBox.Text.Trim();
        m_endpoint.DownloadUrl = DownloadUrlBox.Text.Trim();
        m_endpoint.AutoStart = AutoStartCheck.IsChecked == true;
        m_config.AiBaseDir = AiBaseDirBox.Text.Trim();
        m_config.Save();
    }

    void UpdateStartStopButtons()
    {
        bool launched = m_serviceManager?.IsServiceLaunchedByUs(m_serviceName) == true;
        StartBtn.IsEnabled = !launched;
        StopBtn.IsEnabled = launched;
    }

    void UpdateDetectionInfo()
    {
        string exe = ExePathBox.Text.Trim();
        if (exe.Length > 0 && File.Exists(exe))
        {
            DetectionInfo.Text = $"✓ Executable found: {exe}";
        }
        else if (exe.Length > 0)
        {
            DetectionInfo.Text = $"⚠ File not found: {exe}\nClick Auto-Detect or Browse to locate it.";
        }
        else
        {
            DetectionInfo.Text = "No executable configured. Click Auto-Detect to scan your AI directory, or Browse to select manually.";
        }

        string baseDir = AiBaseDirBox.Text.Trim();
        if (baseDir.Length > 0 && Directory.Exists(baseDir))
        {
            string[] subdirs = Directory.GetDirectories(baseDir);
            if (subdirs.Length > 0)
            {
                string names = string.Join(", ", subdirs.Select(Path.GetFileName).Take(8));
                DetectionInfo.Text += $"\n\nAI directory contents: {names}" +
                    (subdirs.Length > 8 ? $" (+{subdirs.Length - 8} more)" : "");
            }
        }
    }
}
