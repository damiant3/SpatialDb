using System.Diagnostics;
using System.IO;
using System.Linq;
///////////////////////////////////////
namespace Spark.Presenters;

sealed class PythonProbeResult
{
    public bool HasTorch { get; init; }
    public bool HasAv { get; init; }
    public bool HasAudiocraft { get; init; }
    public bool HasCpuOnlyTorch { get; init; }
    public bool HasWrongTorchVersion { get; init; }
    public bool HasXformersMismatch { get; init; }
    public bool HasTransformersMismatch { get; init; }
    public string TorchDiagnostics { get; init; } = "";
    public string AudiocraftWarnings { get; init; } = "";

    public bool HasAnyError => HasCpuOnlyTorch || HasWrongTorchVersion
        || HasXformersMismatch || HasTransformersMismatch;
    public bool IsFullyInstalled => HasTorch && HasAv && HasAudiocraft && !HasAnyError;
}

static class PythonEnvironmentScanner
{
    public static async Task<PythonProbeResult> ScanVenvAsync(
        string venvPythonPath, Action<string>? onStatus = null)
    {
        onStatus?.Invoke("Probing torch…");
        (bool hasTorch, string torchDiag) = await Task.Run(
            () => ProbeCode(venvPythonPath, "import sys; import torch; print(torch.__version__, file=sys.stderr)"));

        onStatus?.Invoke("Probing av…");
        (bool hasAv, _) = await Task.Run(
            () => ProbeCode(venvPythonPath, "import av"));

        onStatus?.Invoke("Probing audiocraft…");
        (bool hasAudiocraft, string audioWarnings) = await Task.Run(
            () => ProbeCode(venvPythonPath, "import audiocraft"));

        bool hasCpuOnlyTorch = hasTorch && (torchDiag.Contains("+cpu") || audioWarnings.Contains("+cpu"));

        bool hasWrongTorchVersion = false;
        if (hasTorch && !hasCpuOnlyTorch)
        {
            string versionLine = ExtractTorchVersionLine(torchDiag);
            hasWrongTorchVersion = versionLine.Length > 0 && !versionLine.StartsWith("2.1.0");
        }

        bool hasXformersMismatch = audioWarnings.Contains("xFormers can't load")
            || audioWarnings.Contains("XFORMERS")
            || torchDiag.Contains("xFormers can't load");

        // "Disabling PyTorch because PyTorch >= X.Y is required" means transformers is too new
        bool hasTransformersMismatch = audioWarnings.Contains("Disabling PyTorch because PyTorch");

        return new PythonProbeResult
        {
            HasTorch = hasTorch,
            HasAv = hasAv,
            HasAudiocraft = hasAudiocraft,
            HasCpuOnlyTorch = hasCpuOnlyTorch,
            HasWrongTorchVersion = hasWrongTorchVersion,
            HasXformersMismatch = hasXformersMismatch,
            HasTransformersMismatch = hasTransformersMismatch,
            TorchDiagnostics = torchDiag,
            AudiocraftWarnings = audioWarnings,
        };
    }

    public static string? FindOllamaExe()
    {
        string local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Ollama", "ollama.exe");
        if (File.Exists(local)) return local;
        string progFiles = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Ollama", "ollama.exe");
        if (File.Exists(progFiles)) return progFiles;
        return FindInPath("ollama.exe");
    }

    public static string? FindInPath(string exeName)
    {
        string? pathVar = Environment.GetEnvironmentVariable("PATH");
        if (pathVar is null) return null;
        foreach (string dir in pathVar.Split(Path.PathSeparator))
        {
            try
            {
                string full = Path.Combine(dir.Trim(), exeName);
                if (File.Exists(full)) return full;
            }
            catch { }
        }
        return null;
    }

    static (bool success, string diagnostics) ProbeCode(string pythonExe, string code)
    {
        try
        {
            ProcessStartInfo psi = new(pythonExe, $"-c \"{code}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            using Process? proc = Process.Start(psi);
            if (proc is null) return (false, "");

            string stdout = "";
            string stderr = "";
            Thread outReader = new(() => stdout = proc.StandardOutput.ReadToEnd());
            Thread errReader = new(() => stderr = proc.StandardError.ReadToEnd());
            outReader.Start();
            errReader.Start();

            if (!proc.WaitForExit(15000))
            {
                try { proc.Kill(); } catch { }
                return (false, "(timed out)\n" + stderr);
            }

            outReader.Join(2000);
            errReader.Join(2000);
            return (proc.ExitCode == 0, stderr.Trim());
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    static string ExtractTorchVersionLine(string diagnostics)
    {
        if (diagnostics.Length == 0) return "";

        string[] lines = diagnostics.Split('\n');
        string? versionLine = lines
            .Select(line => line.Trim())
            .LastOrDefault(line => line.Length > 0 && char.IsDigit(line[0]));

        return versionLine ?? "";
    }
}
