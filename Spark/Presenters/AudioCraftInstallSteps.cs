namespace Spark.Presenters;

/// <summary>
/// Defines the exact, tested install sequence for AudioCraft on Windows.
/// Every version pin was verified on a real machine with Python 3.11 + CUDA 12.1.
/// </summary>
static class AudioCraftInstallSteps
{
    // ── Pinned packages ─────────────────────────────────────────

    public const string Torch       = "torch==2.1.0";
    public const string TorchVision = "torchvision==0.16.0";
    public const string TorchAudio  = "torchaudio==2.1.0";
    public const string TorchText   = "torchtext==0.16.0";
    public const string XFormers    = "xformers==0.0.22.post7";
    public const string Spacy       = "spacy==3.7.6";

    /// <summary>transformers 5.x requires torch ≥ 2.4 and crashes on 2.1.0.</summary>
    public const string Transformers = "transformers>=4.31.0,<4.40.0";

    public const string CudaIndex = "https://download.pytorch.org/whl/cu121";

    // ── Step definitions (order matters) ────────────────────────

    /// <summary>pip install lines for the torch+CUDA stack.</summary>
    public static readonly string[] TorchCudaLines =
    [
        $"{Torch} {TorchVision} {TorchAudio} --index-url {CudaIndex}",
        $"{XFormers} --index-url {CudaIndex}",
    ];

    /// <summary>pip install line for the remaining audiocraft deps (run AFTER audiocraft editable install).</summary>
    public static readonly string DepsLine =
        $"einops \"flashy>=0.0.1\" \"hydra-core>=1.1\" hydra_colorlog julius num2words \"numpy<2.0.0\" "
        + $"sentencepiece \"{Spacy}\" huggingface_hub tqdm \"{Transformers}\" demucs librosa soundfile "
        + $"gradio torchmetrics encodec protobuf \"{TorchText}\" pesq pystoi torchdiffeq "
        + "imageio-ffmpeg";

    // ── Display text for the setup guide ────────────────────────

    public static string TorchInstallDisplay =>
        $"pip install {Torch} {TorchVision} {TorchAudio} --index-url {CudaIndex}";

    public static string XFormersInstallDisplay =>
        $"pip install {XFormers} --index-url {CudaIndex}";

    public static string ManualSteps(string sourceDir) =>
        $"    cd /d \"{sourceDir}\"\n"
        + $"    python -m venv venv\n"
        + $"    venv\\Scripts\\activate\n"
        + $"    python -m pip install --upgrade pip\n"
        + $"    pip install setuptools wheel\n\n"
        + "Then install PyTorch + xFormers with CUDA support:\n\n"
        + $"    {TorchInstallDisplay}\n"
        + $"    {XFormersInstallDisplay}\n\n"
        + "Then install av and audiocraft:\n\n"
        + $"    pip install av\n"
        + $"    pip install --no-deps --no-build-isolation -e .\n\n"
        + "Then install the remaining deps (transformers pinned for torch 2.1 compat):\n\n"
        + $"    pip install {DepsLine}";

    // ── requirements.txt patches ────────────────────────────────

    /// <summary>
    /// Patches that must be applied to requirements.txt before install.
    /// Key = original text, Value = replacement text.
    /// </summary>
    public static readonly (string Original, string Replacement)[] RequirementsPatches =
    [
        ("av==11.0.0", "av>=11.0.0"),
    ];
}
