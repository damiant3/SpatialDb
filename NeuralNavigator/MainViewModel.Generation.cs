using System.Diagnostics;
using System.Numerics;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;
using MeshGeometryModel3D = HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D;
using PerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
using Point3D = System.Windows.Media.Media3D.Point3D;
using Vector3D = System.Windows.Media.Media3D.Vector3D;
///////////////////////////////////////////////
namespace NeuralNavigator;

sealed partial class MainViewModel
{
    CancellationTokenSource? m_generationCts;
    MeshGeometryModel3D? m_generationTrailModel;

    void StartGeneration()
    {
        if (m_reader is null || string.IsNullOrWhiteSpace(m_generationPrompt)) return;

        string arch = m_reader.Architecture;
        if (!arch.StartsWith("gemma", StringComparison.OrdinalIgnoreCase))
        {
            GenerationStatus = $"Architecture \"{arch}\" is not a causal model — generation requires Gemma.";
            return;
        }

        m_generationCts?.Cancel();
        m_generationCts = new CancellationTokenSource();
        CancellationToken ct = m_generationCts.Token;

        IsGenerating = true;
        GenerationOutput = "";
        GeneratedTokens.Clear();
        ClearGenerationTrail();
        GenerationStatus = "Loading causal model...";

        string ggufPath = m_ggufPath!;
        string prompt = m_generationPrompt;
        int maxTokens = m_generationMaxTokens;
        bool followCamera = m_generationFollowCamera;

        Task.Run(() =>
        {
            try
            {
                m_causalSource ??= SparseLattice.Gguf.IntegerCausalSource.Load(ggufPath, onProgress: (step, total, name) =>
                {
                    if (step % 10 == 0)
                        Dispatch(() => GenerationStatus = $"Loading layers... {step}/{total}");
                });

                Dispatch(() => GenerationStatus = "Encoding prompt...");

                int[] promptTokenIds = m_causalSource.Tokenizer.Encode(prompt, addSpecialTokens: true);
                List<int> allTokenIds = [.. promptTokenIds];

                Vector3? prevPosition = null;
                List<GenerationTokenInfo> promptInfos = [];

                for (int i = 0; i < promptTokenIds.Length; i++)
                {
                    string tokenText = m_causalSource.Tokenizer.Decode([promptTokenIds[i]]);
                    float[] embdState = GetTokenEmbedding(promptTokenIds[i]);
                    Vector3 pos = ProjectSingleState(embdState);
                    float movement = prevPosition.HasValue ? Vector3.Distance(pos, prevPosition.Value) : 0f;
                    prevPosition = pos;
                    promptInfos.Add(new GenerationTokenInfo(tokenText, promptTokenIds[i], i, pos, movement, isPromptToken: true));
                }

                Dispatch(() =>
                {
                    foreach (GenerationTokenInfo info in promptInfos)
                        GeneratedTokens.Add(info);
                    GenerationOutput = string.Join("", promptInfos.Select(t => t.Text));
                    GenerationStatus = $"Generating... (prompt: {promptTokenIds.Length} tokens)";
                    RenderGenerationTrail();
                });

                Stopwatch stepTimer = new();

                for (int step = 0; step < maxTokens; step++)
                {
                    ct.ThrowIfCancellationRequested();
                    stepTimer.Restart();

                    // Use ForwardCausalFloat — returns only the last hidden state,
                    // no per-layer trace overhead. This is the fast path.
                    float[] hiddenState = m_causalSource.ForwardCausalFloat([.. allTokenIds]);

                    int nextTokenId = PredictNextTokenFromHidden(hiddenState);
                    stepTimer.Stop();
                    double stepMs = stepTimer.Elapsed.TotalMilliseconds;

                    if (nextTokenId == m_causalSource.Tokenizer.EosTokenId)
                    {
                        Dispatch(() => GenerationStatus += " [EOS]");
                        break;
                    }

                    allTokenIds.Add(nextTokenId);
                    string decoded = m_causalSource.Tokenizer.Decode([nextTokenId]);

                    // Use the predicted token's embedding position for the 3D trail.
                    float[] tokenEmbd = GetTokenEmbedding(nextTokenId);
                    Vector3 pos = ProjectSingleState(tokenEmbd);
                    float movement = prevPosition.HasValue ? Vector3.Distance(pos, prevPosition.Value) : 0f;
                    prevPosition = pos;

                    int stepIdx = promptTokenIds.Length + step;
                    GenerationTokenInfo tokenInfo = new(decoded, nextTokenId, stepIdx, pos, movement, isPromptToken: false);

                    double tokPerSec = 1000.0 / Math.Max(stepMs, 1);
                    int seqLen = allTokenIds.Count;
                    Dispatch(() =>
                    {
                        GeneratedTokens.Add(tokenInfo);
                        GenerationOutput += decoded;
                        GenerationStatus = $"Step {step + 1}/{maxTokens} — \"{decoded}\"  " +
                                           $"{stepMs:F0}ms ({tokPerSec:F2} tok/s)  seq={seqLen}";
                        RenderGenerationTrail();

                        if (followCamera)
                            FlyToPosition(pos, 15f);
                    });
                }

                Dispatch(() =>
                {
                    IsGenerating = false;
                    int genCount = GeneratedTokens.Count(t => !t.IsPromptToken);
                    GenerationStatus = $"Done. Generated {genCount} tokens.";
                });
            }
            catch (OperationCanceledException)
            {
                Dispatch(() =>
                {
                    IsGenerating = false;
                    GenerationStatus = "Generation cancelled.";
                });
            }
            catch (Exception ex)
            {
                Dispatch(() =>
                {
                    IsGenerating = false;
                    GenerationStatus = $"Error: {ex.Message}";
                });
            }
        });
    }

    void CancelGeneration()
    {
        m_generationCts?.Cancel();
    }

    int PredictNextTokenFromHidden(float[] hiddenState)
    {
        // Use the embeddings already loaded by the GgufReader (m_embeddings) to avoid
        // the 4 GB allocation from IntegerCausalSource.TokenEmbeddingsFloat.
        if (m_embeddings is null || m_vocabSize == 0) return 0;
        return SparseLattice.Lattice.VocabLattice.ArgmaxBruteForce(
            hiddenState, m_embeddings, m_vocabSize, m_dims);
    }

    float[] GetTokenEmbedding(int tokenId)
    {
        if (m_embeddings is null) return new float[m_dims];
        float[] result = new float[m_dims];
        int baseIdx = tokenId * m_dims;
        if (baseIdx + m_dims <= m_embeddings.Length)
            Array.Copy(m_embeddings, baseIdx, result, 0, m_dims);
        return result;
    }

    Vector3 ProjectSingleState(float[] state)
    {
        if (m_embeddings is null || state.Length == 0)
            return Vector3.Zero;

        int dims = state.Length;
        int d0, d1, d2;
        if (m_selectedProjection.StartsWith("PCA"))
            (d0, d1, d2) = FindTopVarianceDims(m_embeddings, m_vocabSize, m_dims);
        else
            (d0, d1, d2) = m_selectedProjection switch
            {
                "Raw (dims 1,2,3)" => (1, 2, 3),
                _ => (0, 1, 2),
            };

        float min0 = float.MaxValue, max0 = float.MinValue;
        float min1 = float.MaxValue, max1 = float.MinValue;
        float min2 = float.MaxValue, max2 = float.MinValue;
        for (int i = 0; i < m_vocabSize; i++)
        {
            int bi = i * m_dims;
            if (d0 < m_dims) { float v = m_embeddings[bi + d0]; if (v < min0) min0 = v; if (v > max0) max0 = v; }
            if (d1 < m_dims) { float v = m_embeddings[bi + d1]; if (v < min1) min1 = v; if (v > max1) max1 = v; }
            if (d2 < m_dims) { float v = m_embeddings[bi + d2]; if (v < min2) min2 = v; if (v > max2) max2 = v; }
        }
        float range0 = max0 - min0; if (range0 < 1e-8f) range0 = 1f;
        float range1 = max1 - min1; if (range1 < 1e-8f) range1 = 1f;
        float range2 = max2 - min2; if (range2 < 1e-8f) range2 = 1f;
        const float Scale = 100f;

        float x = d0 < dims ? (state[d0] - min0) / range0 * Scale - Scale / 2f : 0f;
        float y = d1 < dims ? (state[d1] - min1) / range1 * Scale - Scale / 2f : 0f;
        float z = d2 < dims ? (state[d2] - min2) / range2 * Scale - Scale / 2f : 0f;
        return new Vector3(x, y, z);
    }

    void FlyToPosition(Vector3 pos, float distance)
    {
        if (Camera is not PerspectiveCamera cam) return;
        Vector3D lookDir = cam.LookDirection;
        lookDir.Normalize();
        cam.Position = new Point3D(
            pos.X - lookDir.X * distance,
            pos.Y - lookDir.Y * distance,
            pos.Z - lookDir.Z * distance);
        m_flyCamera?.SyncFromCamera();
    }

    void RenderGenerationTrail()
    {
        if (m_viewport is null || GeneratedTokens.Count < 1) return;
        ClearGenerationTrail();

        MeshBuilder builder = new();
        float tubeRadius = (float)m_pointSize * 0.06f;
        float nodeRadius = (float)m_pointSize * 0.15f;

        List<GenerationTokenInfo> tokens = [.. GeneratedTokens];
        int promptCount = tokens.Count(t => t.IsPromptToken);
        int genCount = tokens.Count - promptCount;

        // Draw trail segments between consecutive tokens.
        for (int i = 1; i < tokens.Count; i++)
            builder.AddCylinder(tokens[i - 1].Position, tokens[i].Position, tubeRadius, 6);

        // Draw node spheres.
        for (int i = 0; i < tokens.Count; i++)
        {
            float r = tokens[i].IsPromptToken ? nodeRadius * 0.7f : nodeRadius;
            builder.AddSphere(tokens[i].Position, r, 6, 6);
        }

        MeshGeometry3D mesh = builder.ToMeshGeometry3D();

        // Color vertices: prompt = blue, generated = green→yellow→red.
        List<Color4> colors = new(mesh.Positions?.Count ?? 0);
        for (int i = 0; i < (mesh.Positions?.Count ?? 0); i++)
        {
            Vector3 vertPos = new(mesh.Positions![i].X, mesh.Positions[i].Y, mesh.Positions[i].Z);
            float bestDist = float.MaxValue;
            int bestIdx = 0;
            for (int j = 0; j < tokens.Count; j++)
            {
                float d = Vector3.Distance(vertPos, tokens[j].Position);
                if (d < bestDist) { bestDist = d; bestIdx = j; }
            }

            if (tokens[bestIdx].IsPromptToken)
                colors.Add(new Color4(0.3f, 0.5f, 1f, 1f)); // blue for prompt
            else
            {
                float t = genCount > 1 ? (float)(bestIdx - promptCount) / (genCount - 1) : 0f;
                colors.Add(GenerationGradientColor(t));
            }
        }
        mesh.Colors = [.. colors];

        m_generationTrailModel = new MeshGeometryModel3D
        {
            Geometry = mesh,
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(1, 1, 1, 1),
                EmissiveColor = new Color4(0.1f, 0.1f, 0.1f, 0f),
            },
        };
        m_viewport.Items.Add(m_generationTrailModel);
    }

    static Color4 GenerationGradientColor(float t)
    {
        // green → yellow → red
        if (t < 0.5f) { float s = t / 0.5f; return new Color4(s, 1f, 0f, 1f); }
        { float s = (t - 0.5f) / 0.5f; return new Color4(1f, 1f - s, 0f, 1f); }
    }

    void ClearGenerationTrail()
    {
        if (m_generationTrailModel is null || m_viewport is null) return;
        m_viewport.Items.Remove(m_generationTrailModel);
        m_generationTrailModel.Dispose();
        m_generationTrailModel = null;
    }

    void ClearGeneration()
    {
        m_generationCts?.Cancel();
        ClearGenerationTrail();
        GeneratedTokens.Clear();
        GenerationOutput = "";
        GenerationStatus = "";
        IsGenerating = false;
    }

    void OnGeneratedTokenClicked(GenerationTokenInfo? token)
    {
        if (token is null || m_projected is null || m_tokenLabels is null) return;

        // Select the token in the main explorer.
        if (token.TokenId < m_vocabSize)
            SelectToken(token.TokenId);

        // Fly to its generation position.
        FlyToPosition(token.Position, 20f);
    }
}
