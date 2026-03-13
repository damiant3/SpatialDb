using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Spark.Services;
using Spark.ViewModels;
///////////////////////////////////////////
namespace Spark.Presenters;

sealed class SetupGuidePresenter
{
    readonly string m_serviceName;
    readonly ServiceEndpointConfig m_config;
    readonly SetupGuideViewModel m_vm;
    readonly string m_downloadUrl;
    string m_repairPythonExe = "";
    string m_repairSourceDir = "";
    string m_repairAction = "";
    string m_resetSourceDir = "";

    public SetupGuidePresenter(string serviceName, ServiceEndpointConfig config, SetupGuideViewModel vm)
    {
        m_serviceName = serviceName;
        m_config = config;
        m_vm = vm;

        ServiceEndpoint? ep = serviceName switch
        {
            "Ollama" => config.Ollama,
            "StableDiffusion" => config.StableDiffusion,
            "MusicGen" => config.MusicGen,
            _ => null,
        };
        m_downloadUrl = ep?.DownloadUrl ?? "";

        string displayName = serviceName switch
        {
            "Ollama" => "Ollama (LLM)",
            "StableDiffusion" => "Stable Diffusion Forge",
            "MusicGen" => "MusicGen / AudioCraft",
            _ => serviceName,
        };
        vm.HeaderText = $"Setup Guide — {displayName}";
    }

    public async Task ScanAsync()
    {
        if (m_vm.IsScanning) return;
        m_vm.IsScanning = true;
        m_vm.Items.Clear();
        m_repairAction = "";
        m_vm.ShowRepairButton = false;
        m_vm.ShowResetVenvButton = false;
        m_vm.ScanStatus = "Scanning…";

        try
        {
            switch (m_serviceName)
            {
                case "Ollama": BuildOllamaGuide(); break;
                case "StableDiffusion": BuildForgeGuide(); break;
                case "MusicGen": await BuildMusicGenGuideAsync(); break;
            }
        }
        catch (Exception ex)
        {
            m_vm.Items.Add(GuideItem.Warning($"Error scanning system: {ex.Message}"));
        }

        UpdateDetectionStatus();
        m_vm.ScanStatus = "";
        m_vm.IsScanning = false;
    }

    public async Task RescanAsync()
    {
        m_config.AutoDetectPaths();
        m_config.Save();
        await ScanAsync();
    }

    public void OpenDownloadPage()
    {
        if (m_downloadUrl.Length == 0) return;
        try { Process.Start(new ProcessStartInfo(m_downloadUrl) { UseShellExecute = true }); }
        catch { }
    }

    public void OpenTerminal()
    {
        string workDir = m_config.AiBaseDir;
        if (m_serviceName == "MusicGen" && m_repairSourceDir.Length > 0 && Directory.Exists(m_repairSourceDir))
            workDir = m_repairSourceDir;
        else if (m_serviceName == "MusicGen" && m_resetSourceDir.Length > 0 && Directory.Exists(m_resetSourceDir))
            workDir = m_resetSourceDir;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k cd /d \"{workDir}\"",
                UseShellExecute = true,
            });
        }
        catch { }
    }

    public async Task AutoRepairAsync()
    {
        if (m_repairAction.Length == 0) return;

        m_vm.ScanStatus = "Repairing…";
        m_vm.Items.Add(GuideItem.Hint("🔧 Running automatic repair — a console window will open. Please wait…"));

        bool ok = false;
        try
        {
            string pip = m_repairPythonExe.Length > 0
                ? Path.Combine(Path.GetDirectoryName(m_repairPythonExe)!, "pip.exe")
                : "";

            string script = m_repairAction switch
            {
                "reinstall-torch-cuda" => RepairScriptBuilder.ReinstallTorchCuda(pip),
                "fix-av" => RepairScriptBuilder.FixAv(pip, m_repairSourceDir),
                "full-install" => RepairScriptBuilder.FullInstall(pip, m_repairSourceDir),
                "install-audiocraft" => RepairScriptBuilder.InstallAudiocraft(pip, m_repairSourceDir),
                "create-venv" => RepairScriptBuilder.CreateVenv(m_repairSourceDir),
                _ => "",
            };

            if (script.Length > 0)
                ok = await RepairScriptBuilder.RunAsync(script, m_repairSourceDir,
                    s => m_vm.ScanStatus = s);

            // Post-install fixups: copy ffmpeg binary & apply source patches
            if (ok && m_repairSourceDir.Length > 0)
            {
                RepairScriptBuilder.LinkFfmpeg(m_repairSourceDir);
                RepairScriptBuilder.PatchSourceForWindows(m_repairSourceDir);
            }
        }
        catch (Exception ex)
        {
            m_vm.Items.Add(GuideItem.Warning($"Repair failed: {ex.Message}"));
        }

        if (ok)
            m_vm.Items.Add(GuideItem.Hint("✅ Repair commands completed. Running re-scan…"));
        else
            m_vm.Items.Add(GuideItem.Warning("Repair process finished with errors. Check the console output above for details."));

        m_config.AutoDetectPaths();
        m_config.Save();
        await ScanAsync();
    }

    public async Task ResetVenvAsync()
    {
        if (m_resetSourceDir.Length == 0) return;

        m_vm.ScanStatus = "Resetting venv…";
        m_vm.Items.Add(GuideItem.Hint("🧹 Deleting the venv and reinstalling AudioCraft from scratch. This downloads ~2.5 GB…"));

        bool ok = false;
        try
        {
            string script = RepairScriptBuilder.ResetVenv(m_resetSourceDir);
            if (script.Length > 0)
                ok = await RepairScriptBuilder.RunAsync(script, m_resetSourceDir,
                    s => m_vm.ScanStatus = s);

            // Post-install fixups
            if (ok && m_resetSourceDir.Length > 0)
            {
                RepairScriptBuilder.LinkFfmpeg(m_resetSourceDir);
                RepairScriptBuilder.PatchSourceForWindows(m_resetSourceDir);
            }
        }
        catch (Exception ex)
        {
            m_vm.Items.Add(GuideItem.Warning($"Reset failed: {ex.Message}"));
        }

        if (ok)
            m_vm.Items.Add(GuideItem.Hint("✅ Reset completed. Running re-scan…"));
        else
            m_vm.Items.Add(GuideItem.Warning("Reset finished with errors. Check the console output above for details."));

        m_config.AutoDetectPaths();
        m_config.Save();
        await ScanAsync();
    }

    // ── Ollama guide ────────────────────────────────────────────

    void BuildOllamaGuide()
    {
        string? ollamaExe = PythonEnvironmentScanner.FindOllamaExe();
        bool installed = ollamaExe is not null;

        m_vm.Items.Add(GuideItem.Step("1", "Download & Install Ollama",
            "Ollama is a standalone installer. Download from ollama.com, run the installer, and it will register in your PATH.",
            installed ? $"✓ Ollama is installed ({ollamaExe})" : ""));

        if (!installed)
        {
            m_vm.Items.Add(GuideItem.Warning("Ollama is a standard Windows installer (.exe). It installs to your user profile and doesn't require admin."));
            return;
        }

        m_vm.Items.Add(GuideItem.Step("2", "Pull a Model",
            "Open a terminal and run:\n\n"
            + "    ollama pull llama3\n\n"
            + "This downloads the model (~4 GB). Other good options: mistral, codellama, phi3"));

        m_vm.Items.Add(GuideItem.Step("3", "Start Ollama",
            "Ollama usually runs as a background service after install. If not, run:\n\n"
            + "    ollama serve\n\n"
            + "Spark will auto-detect it on localhost:11434."));

        m_vm.Items.Add(GuideItem.Hint("After pulling a model, click Re-scan — Spark will detect Ollama and start using it."));
    }

    // ── Forge guide ─────────────────────────────────────────────

    void BuildForgeGuide()
    {
        string baseDir = m_config.AiBaseDir;
        string[] candidates =
        [
            Path.Combine(baseDir, "DiffusionForge", "run.bat"),
            Path.Combine(baseDir, "DiffusionForge", "webui", "webui.bat"),
            Path.Combine(baseDir, "stable-diffusion-webui-forge", "run.bat"),
            Path.Combine(baseDir, "stable-diffusion-webui-forge", "webui", "webui.bat"),
            Path.Combine(baseDir, "stable-diffusion-webui", "webui.bat"),
        ];
        string? found = null;
        foreach (string c in candidates)
            if (File.Exists(c)) { found = c; break; }

        m_vm.Items.Add(GuideItem.Step("1", "Download Forge",
            "Go to the Forge releases page and download the latest one-click package (.7z or .zip).\n"
            + "Look for the file named like \"webui_forge_cu121_torch231.7z\".",
            found is not null ? $"✓ Found: {found}" : ""));

        if (found is null)
        {
            m_vm.Items.Add(GuideItem.Step("2", $"Extract to {baseDir}",
                $"Extract the archive into your AI directory. The result should look like:\n\n"
                + $"    {baseDir}\\DiffusionForge\\run.bat\n"
                + $"    {baseDir}\\DiffusionForge\\webui\\webui.bat\n\n"
                + "Or rename the extracted folder to \"DiffusionForge\"."));

            m_vm.Items.Add(GuideItem.Step("3", "First Run",
                "Double-click run.bat (or double-click the indicator in Spark).\n"
                + "The first run downloads ~6 GB of model files. This takes 10-20 minutes.\n"
                + "A console window will appear showing progress."));

            m_vm.Items.Add(GuideItem.Warning("Forge requires an NVIDIA GPU with CUDA support and ~15 GB free disk space.\nThe first launch downloads everything automatically."));
        }
        else
        {
            m_vm.Items.Add(GuideItem.Step("2", "Launch Forge",
                "Double-click the Stable Diffusion indicator in Spark's toolbar, or run:\n\n"
                + $"    {found}\n\n"
                + "First run downloads ~6 GB of model data. Watch the console for progress.\n"
                + "Once you see \"Running on local URL: http://127.0.0.1:7860\" it's ready."));
        }

        m_vm.Items.Add(GuideItem.Hint("After extracting/running, click Re-scan — Spark will find run.bat and configure itself."));
    }

    // ── MusicGen / AudioCraft guide ─────────────────────────────

    async Task BuildMusicGenGuideAsync()
    {
        string baseDir = m_config.AiBaseDir;
        string? sourceDir = FindMusicGenSourceDir(baseDir);

        m_vm.Items.Add(GuideItem.Step("1", "Download AudioCraft Source",
            $"Download the source code zip from GitHub and extract to:\n\n"
            + $"    {baseDir}\n\n"
            + "The extracted folder (audiocraft-main) should contain setup.py, requirements.txt, etc.",
            sourceDir is not null ? $"✓ Found: {sourceDir}" : ""));

        if (sourceDir is null)
        {
            m_vm.Items.Add(GuideItem.Warning(
                "After downloading the .zip from GitHub, extract it so you get:\n\n"
                + $"    {baseDir}\\audiocraft-main\\setup.py\n"
                + $"    {baseDir}\\audiocraft-main\\requirements.txt\n\n"
                + "Then click Re-scan below."));
            return;
        }

        m_vm.ScanStatus = "Checking Python…";
        bool hasPython = PythonEnvironmentScanner.FindInPath("python.exe") is not null;

        m_vm.Items.Add(GuideItem.Step("2", "Install Python 3.10 or 3.11",
            "MusicGen requires Python 3.10 or 3.11 (3.12+ has issues with some deps).\n"
            + "Download from python.org. During install, check \"Add Python to PATH\".",
            hasPython ? "✓ Python found in PATH" : "⚠ Python not found in PATH"));

        if (!hasPython)
        {
            m_vm.Items.Add(GuideItem.Warning("Python was not found in your PATH. Install it from python.org, restart Spark, then click Re-scan."));
            return;
        }

        m_resetSourceDir = sourceDir;
        m_vm.ShowResetVenvButton = true;

        // Apply Windows compatibility patches whenever we scan
        RepairScriptBuilder.PatchSourceForWindows(sourceDir);

        string venvPython = Path.Combine(sourceDir, "venv", "Scripts", "python.exe");
        bool hasVenv = File.Exists(venvPython);

        // Ensure ffmpeg is available in venv if imageio_ffmpeg was installed
        if (hasVenv)
            RepairScriptBuilder.LinkFfmpeg(sourceDir);

        PythonProbeResult probe = hasVenv
            ? await PythonEnvironmentScanner.ScanVenvAsync(venvPython, s => m_vm.ScanStatus = s)
            : new PythonProbeResult();

        string installStatus = BuildInstallStatus(hasVenv, probe);

        m_vm.Items.Add(GuideItem.Step("3", "Create Virtual Environment & Install",
            "Open a terminal (click 📂 Open Terminal below) and run:\n\n"
            + AudioCraftInstallSteps.ManualSteps(sourceDir),
            installStatus));

        AddRepairOffers(hasVenv, probe, venvPython, sourceDir);

        if (!hasVenv)
        {
            m_vm.Items.Add(GuideItem.Hint("Click 🔧 Auto-Fix to create the virtual environment and install everything automatically."));
            m_repairPythonExe = "";
            m_repairSourceDir = sourceDir;
            m_repairAction = "create-venv";
            m_vm.ShowRepairButton = true;
            return;
        }

        if (!probe.IsFullyInstalled) return;

        string cdCmd = $"cd /d \"{sourceDir}\"";
        m_vm.Items.Add(GuideItem.Step("4", "Ready to Run",
            $"Spark will start the MusicGen server automatically using:\n\n"
            + $"    {venvPython}\n"
            + $"    {m_config.MusicGen.StartArguments}\n\n"
            + "Or start manually:\n\n"
            + $"    {cdCmd}\n"
            + $"    venv\\Scripts\\activate\n"
            + $"    python demos/musicgen_app.py --server_port 7861",
            "✓ Ready to launch"));

        m_vm.Items.Add(GuideItem.Hint("Click Done — Spark will auto-start MusicGen when you switch to the Music tab."));
    }

    // ── Repair offers ───────────────────────────────────────────

    void AddRepairOffers(bool hasVenv, PythonProbeResult probe, string venvPython, string sourceDir)
    {
        if (!hasVenv) return;

        if (probe.HasCpuOnlyTorch)
        {
            m_vm.Items.Add(GuideItem.Warning(
                "PyTorch is installed but it's the CPU-only build (no CUDA/GPU support).\n"
                + "MusicGen will be extremely slow or non-functional without GPU.\n\n"
                + "Click 🔧 Auto-Fix to reinstall PyTorch with CUDA.\n"
                + "This downloads ~2.5 GB and takes a few minutes."));
            SetRepair(venvPython, sourceDir, "reinstall-torch-cuda");
        }
        else if (probe.HasWrongTorchVersion)
        {
            m_vm.Items.Add(GuideItem.Warning(
                $"PyTorch is the wrong version. audiocraft 1.4 requires {AudioCraftInstallSteps.Torch}.\n"
                + "This usually happens when an unpinned pip install upgraded torch.\n\n"
                + "Click 🔧 Auto-Fix to uninstall and reinstall the correct version."));
            SetRepair(venvPython, sourceDir, "reinstall-torch-cuda");
        }
        else if (probe.HasTransformersMismatch)
        {
            m_vm.Items.Add(GuideItem.Warning(
                "The installed transformers package is too new for torch 2.1.0.\n"
                + "This happens when pip pulls the latest transformers (v5+) which requires torch ≥ 2.4.\n\n"
                + "Click 🧹 Reset venv to rebuild with pinned versions that are known to work."));
            m_vm.ShowResetVenvButton = true;
        }
        else if (probe.HasXformersMismatch)
        {
            m_vm.Items.Add(GuideItem.Warning(
                "xFormers has a version mismatch with the installed PyTorch.\n"
                + "This causes warnings and disables GPU-accelerated attention.\n\n"
                + "Click 🔧 Auto-Fix to reinstall PyTorch + xFormers with matching versions."));
            SetRepair(venvPython, sourceDir, "reinstall-torch-cuda");
        }
        else if (!probe.HasAv)
        {
            m_vm.Items.Add(GuideItem.Warning(
                "The av==11.0.0 package pinned by AudioCraft has no pre-built Windows binary.\n\n"
                + "Click 🔧 Auto-Fix to patch requirements.txt and install av automatically."));
            SetRepair(venvPython, sourceDir, "fix-av");
        }
        else if (!probe.HasTorch)
        {
            m_vm.Items.Add(GuideItem.Warning(
                "PyTorch is not installed in the venv. This usually means the initial install failed partway.\n\n"
                + "Click 🔧 Auto-Fix to install PyTorch with CUDA, av, and audiocraft."));
            SetRepair(venvPython, sourceDir, "full-install");
        }
        else if (probe.HasTorch && probe.HasAv && !probe.HasAudiocraft)
        {
            m_vm.Items.Add(GuideItem.Warning(
                "torch and av are installed but audiocraft itself is missing.\n\n"
                + "Click 🔧 Auto-Fix to install audiocraft and its dependencies."));
            SetRepair(venvPython, sourceDir, "install-audiocraft");
        }

        if (probe.AudiocraftWarnings.Length > 0 && !probe.HasAnyError)
        {
            string filtered = FilterHarmlessWarnings(probe.AudiocraftWarnings);
            if (filtered.Length > 0)
                m_vm.Items.Add(GuideItem.Warning("Python environment warnings during import:\n\n" + filtered));
        }
    }

    void SetRepair(string pythonExe, string sourceDir, string action)
    {
        m_repairPythonExe = pythonExe;
        m_repairSourceDir = sourceDir;
        m_repairAction = action;
        m_vm.ShowRepairButton = true;
    }

    // ── Status text ─────────────────────────────────────────────

    static string BuildInstallStatus(bool hasVenv, PythonProbeResult p)
    {
        if (!hasVenv) return "";
        if (p.IsFullyInstalled) return "✓ Fully installed";
        if (p.HasCpuOnlyTorch) return "❌ PyTorch is CPU-only — must reinstall with CUDA";
        if (p.HasWrongTorchVersion) return $"❌ Wrong PyTorch version — requires {AudioCraftInstallSteps.Torch}";
        if (p.HasTransformersMismatch) return "❌ transformers is too new for torch 2.1 — click 🧹 Reset venv";
        if (p.HasXformersMismatch) return "❌ xFormers mismatch — click Auto-Fix";
        if (!p.HasTorch) return "⚠ venv exists but torch is NOT installed";
        if (!p.HasAv) return "⚠ venv exists but av (PyAV) failed to install";
        if (!p.HasAudiocraft) return "⚠ venv exists but audiocraft not installed";
        return "";
    }

    /// <summary>Strips triton/Windows warnings that are expected and harmless.</summary>
    static string FilterHarmlessWarnings(string warnings)
    {
        string[] lines = warnings.Split('\n');
        List<string> filtered = [];
        foreach (string line in lines)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            if (trimmed.Contains("Triton is not available")) continue;
            if (trimmed.Contains("No module named 'triton'")) continue;
            filtered.Add(trimmed);
        }
        return string.Join("\n", filtered);
    }

    // ── Helpers ─────────────────────────────────────────────────

    static string? FindMusicGenSourceDir(string baseDir)
    {
        string[] names = ["audiocraft-main", "audiocraft", "musicgen"];
        foreach (string name in names)
        {
            string candidate = Path.Combine(baseDir, name);
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "setup.py")))
                return candidate;
        }
        return null;
    }

    void UpdateDetectionStatus()
    {
        m_config.AutoDetectPaths();
        ServiceEndpoint? ep = m_serviceName switch
        {
            "Ollama" => m_config.Ollama,
            "StableDiffusion" => m_config.StableDiffusion,
            "MusicGen" => m_config.MusicGen,
            _ => null,
        };

        bool configured = ep is not null
            && !string.IsNullOrWhiteSpace(ep.ExecutablePath)
            && File.Exists(ep.ExecutablePath);

        Brush FindBrush(string key)
        {
            if (Application.Current.TryFindResource(key) is Brush b) return b;
            return Brushes.Gray;
        }

        m_vm.StatusBackground = FindBrush("ControlBg");
        if (configured)
        {
            m_vm.StatusForeground = FindBrush("CodeFg");
            m_vm.StatusText = $"✓ Ready — executable found at:\n{ep!.ExecutablePath}\n\nClose this dialog. Spark will use it automatically.";
        }
        else
        {
            m_vm.StatusForeground = FindBrush("GoldBrush");
            m_vm.StatusText = $"Not yet configured. Complete the steps above, then click Re-scan.\nSpark scans: {m_config.AiBaseDir}";
        }
    }
}
