using System.Diagnostics;
using System.IO;
///////////////////////////////////////
namespace Spark.Presenters;

static class RepairScriptBuilder
{
    public static string TorchInstallLine => AudioCraftInstallSteps.TorchInstallDisplay;
    public static string XformersInstallLine => AudioCraftInstallSteps.XFormersInstallDisplay;

    public static string ReinstallTorchCuda(string pipExe)
    {
        return $"\"{pipExe}\" uninstall torch torchvision torchaudio xformers -y && "
             + TorchCudaChain(pipExe);
    }

    public static string FixAv(string pipExe, string sourceDir)
    {
        PatchSourceForWindows(sourceDir);
        return $"\"{pipExe}\" install av && "
             + $"\"{pipExe}\" install setuptools wheel && "
             + $"\"{pipExe}\" install --no-deps --no-build-isolation -e \"{sourceDir}\"";
    }

    public static string FullInstall(string pipExe, string sourceDir)
    {
        PatchSourceForWindows(sourceDir);
        return UpgradePipChain(pipExe)
             + TorchCudaChain(pipExe)
             + $"\"{pipExe}\" install av && "
             + $"\"{pipExe}\" install setuptools wheel && "
             + $"\"{pipExe}\" install --no-deps --no-build-isolation -e \"{sourceDir}\" && "
             + DepsChain(pipExe);
    }

    public static string InstallAudiocraft(string pipExe, string sourceDir)
    {
        PatchSourceForWindows(sourceDir);
        return $"\"{pipExe}\" install setuptools wheel && "
             + $"\"{pipExe}\" install --no-deps --no-build-isolation -e \"{sourceDir}\" && "
             + DepsChain(pipExe);
    }

    public static string CreateVenv(string sourceDir)
    {
        string? sysPython = PythonEnvironmentScanner.FindInPath("python.exe");
        if (sysPython is null) return "echo Python not found in PATH — install Python 3.10/3.11 first";

        string venvDir = Path.Combine(sourceDir, "venv");
        string pip = Path.Combine(venvDir, "Scripts", "pip.exe");
        PatchSourceForWindows(sourceDir);

        return $"\"{sysPython}\" -m venv \"{venvDir}\" && "
             + UpgradePipChain(pip)
             + TorchCudaChain(pip)
             + $"\"{pip}\" install av && "
             + $"\"{pip}\" install setuptools wheel && "
             + $"\"{pip}\" install --no-deps --no-build-isolation -e \"{sourceDir}\" && "
             + DepsChain(pip);
    }

    public static string ResetVenv(string sourceDir)
    {
        string? sysPython = PythonEnvironmentScanner.FindInPath("python.exe");
        if (sysPython is null) return "echo Python not found in PATH — install Python 3.10/3.11 first";

        string venvDir = Path.Combine(sourceDir, "venv");
        string pip = Path.Combine(venvDir, "Scripts", "pip.exe");
        PatchSourceForWindows(sourceDir);

        return $"if exist \"{venvDir}\" rmdir /s /q \"{venvDir}\" && "
             + $"\"{sysPython}\" -m venv \"{venvDir}\" && "
             + UpgradePipChain(pip)
             + TorchCudaChain(pip)
             + $"\"{pip}\" install av && "
             + $"\"{pip}\" install setuptools wheel && "
             + $"\"{pip}\" install --no-deps --no-build-isolation -e \"{sourceDir}\" && "
             + DepsChain(pip);
    }

    public static async Task<bool> RunAsync(string cmdLine, string workingDir, Action<string>? onStatus = null)
    {
        string batFile = Path.Combine(Path.GetTempPath(), $"spark_repair_{Environment.TickCount}.bat");
        try
        {
            File.WriteAllText(batFile,
                "@echo off\r\n"
                + $"cd /d \"{workingDir}\"\r\n"
                + cmdLine + "\r\n"
                + "echo.\r\n"
                + "if errorlevel 1 (\r\n"
                + "    echo *** REPAIR FAILED — see errors above ***\r\n"
                + ") else (\r\n"
                + "    echo *** REPAIR COMPLETE ***\r\n"
                + ")\r\n"
                + "echo.\r\n"
                + "pause\r\n");

            Process? proc = Process.Start(new ProcessStartInfo
            {
                FileName = batFile,
                WorkingDirectory = workingDir,
                UseShellExecute = true,
            });
            if (proc is null) return false;

            onStatus?.Invoke("Running repair (see console window)…");
            await proc.WaitForExitAsync();
            return proc.ExitCode == 0;
        }
        finally
        {
            try { File.Delete(batFile); } catch { }
        }
    }

    // ── Helpers ─────────────────────────────────────────────────

    static string UpgradePipChain(string pipExe) =>
        $"\"{pipExe}\" install --upgrade pip && ";

    static string TorchCudaChain(string pipExe)
    {
        string result = "";
        foreach (string line in AudioCraftInstallSteps.TorchCudaLines)
            result += $"\"{pipExe}\" install {line} && ";
        return result;
    }

    static string DepsChain(string pipExe) =>
        $"\"{pipExe}\" install {AudioCraftInstallSteps.DepsLine}";

    /// <summary>
    /// Applies all source-level patches needed for Windows compatibility.
    /// Called before running any install scripts.
    /// </summary>
    public static void PatchSourceForWindows(string sourceDir)
    {
        PatchRequirementsTxt(sourceDir);
        PatchAudioPy(sourceDir);
        PatchMakeWaveform(sourceDir);
    }

    /// <summary>
    /// Copies the ffmpeg binary from imageio_ffmpeg (pip-installed) into venv/Scripts
    /// so audiocraft's subprocess calls to 'ffmpeg' can find it.
    /// </summary>
    public static void LinkFfmpeg(string sourceDir)
    {
        string venvScripts = Path.Combine(sourceDir, "venv", "Scripts");
        string target = Path.Combine(venvScripts, "ffmpeg.exe");
        if (File.Exists(target)) return;

        string binDir = Path.Combine(sourceDir, "venv", "Lib", "site-packages",
            "imageio_ffmpeg", "binaries");
        if (!Directory.Exists(binDir)) return;

        string? ffmpegBin = Directory.GetFiles(binDir, "ffmpeg*.exe").FirstOrDefault();
        if (ffmpegBin is null) return;

        try { File.Copy(ffmpegBin, target, overwrite: false); } catch { }
    }

    static void PatchRequirementsTxt(string sourceDir)
    {
        string reqFile = Path.Combine(sourceDir, "requirements.txt");
        if (!File.Exists(reqFile)) return;
        string content = File.ReadAllText(reqFile);
        bool changed = false;
        foreach ((string original, string replacement) in AudioCraftInstallSteps.RequirementsPatches)
        {
            if (content.Contains(original))
            {
                content = content.Replace(original, replacement);
                changed = true;
            }
        }
        if (changed) File.WriteAllText(reqFile, content);
    }

    /// <summary>
    /// Patches audiocraft/data/audio.py for two Windows issues:
    /// 1) bare 'ffmpeg' command not found — resolve via sys.executable dir
    /// 2) path.unlink() fails due to file locking from ffmpeg subprocess
    /// </summary>
    static void PatchAudioPy(string sourceDir)
    {
        string audioFile = Path.Combine(sourceDir, "audiocraft", "data", "audio.py");
        if (!File.Exists(audioFile)) return;

        string content = File.ReadAllText(audioFile);
        bool changed = false;

        // Patch 1: resolve ffmpeg path instead of bare 'ffmpeg' command
        if (!content.Contains("shutil.which('ffmpeg')"))
        {
            const string oldCmd = "    command = [\n        'ffmpeg',";
            const string newCmd =
                "    import shutil, os, sys\n"
                + "    _ffmpeg = shutil.which('ffmpeg') or os.path.join(os.path.dirname(sys.executable), 'ffmpeg')\n"
                + "    command = [\n        _ffmpeg, ";
            if (content.Contains(oldCmd))
            {
                content = content.Replace(oldCmd, newCmd);
                changed = true;
            }
        }

        // Patch 2: wrap path.unlink() in try/except for Windows file locking
        if (!content.Contains("except OSError:"))
        {
            const string oldUnlink =
                "        if path.exists():\n"
                + "            # we do not want to leave half written files around.\n"
                + "            path.unlink()";
            const string newUnlink =
                "        if path.exists():\n"
                + "            # we do not want to leave half written files around.\n"
                + "            try:\n"
                + "                path.unlink()\n"
                + "            except OSError:\n"
                + "                pass  # Windows file locking";
            if (content.Contains(oldUnlink))
            {
                content = content.Replace(oldUnlink, newUnlink);
                changed = true;
            }
        }

        if (changed) File.WriteAllText(audioFile, content);
    }

    /// <summary>
    /// Patches demos/musicgen_app.py: gr.make_waveform was removed in Gradio 5+.
    /// Add a hasattr guard so it falls back to returning the audio path.
    /// </summary>
    static void PatchMakeWaveform(string sourceDir)
    {
        string appFile = Path.Combine(sourceDir, "demos", "musicgen_app.py");
        if (!File.Exists(appFile)) return;

        string content = File.ReadAllText(appFile);
        if (content.Contains("hasattr(gr, 'make_waveform')")) return; // already patched

        const string oldFn =
            "def make_waveform(*args, **kwargs):\n"
            + "    # Further remove some warnings.\n"
            + "    be = time.time()\n"
            + "    with warnings.catch_warnings():\n"
            + "        warnings.simplefilter('ignore')\n"
            + "        out = gr.make_waveform(*args, **kwargs)\n"
            + "        print(\"Make a video took\", time.time() - be)\n"
            + "        return out";
        const string newFn =
            "def make_waveform(*args, **kwargs):\n"
            + "    # gr.make_waveform was removed in Gradio 5+. Return the audio path as-is.\n"
            + "    if hasattr(gr, 'make_waveform'):\n"
            + "        be = time.time()\n"
            + "        with warnings.catch_warnings():\n"
            + "            warnings.simplefilter('ignore')\n"
            + "            out = gr.make_waveform(*args, **kwargs)\n"
            + "            print(\"Make a video took\", time.time() - be)\n"
            + "            return out\n"
            + "    return args[0] if args else None";

        if (!content.Contains(oldFn)) return;
        content = content.Replace(oldFn, newFn);
        File.WriteAllText(appFile, content);
    }
}
