using System.Numerics;
using HelixToolkit;
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
    void RunTrace()
    {
        if (m_reader is null || string.IsNullOrWhiteSpace(m_tracePrompt)) return;

        string arch = m_reader.Architecture;
        if (!arch.StartsWith("gemma", StringComparison.OrdinalIgnoreCase))
        {
            TraceStatus = $"Architecture \"{arch}\" is not a causal model.\nTracing uses token embedding lookup instead.";
            RunEmbeddingTrace();
            return;
        }

        TraceStatus = "Loading causal model for trace...";
        string ggufPath = m_ggufPath!;

        Task.Run(() =>
        {
            try
            {
                m_causalSource ??= SparseLattice.Gguf.IntegerCausalSource.Load(ggufPath, onProgress: (step, total, name) =>
                {
                    if (step % 10 == 0)
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            TraceStatus = $"Loading layers... {step}/{total}");
                });

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    TraceStatus = "Running forward pass...");

                int[] tokens = m_causalSource.Tokenizer.Encode(m_tracePrompt, addSpecialTokens: true);
                float[][] trace = m_causalSource.ForwardCausalWithTrace(tokens);

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    m_traceStates = trace;
                    ProjectAndRenderTrace();
                    BuildLayerMovements();
                    MaxLayerIndex = trace.Length - 1;
                    SelectedLayerIndex = 0;
                    HasTrace = true;
                    TraceStatus = $"Traced {tokens.Length} tokens through {trace.Length - 1} layers.";
                });
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    TraceStatus = $"Trace error: {ex.Message}");
            }
        });
    }

    void RunEmbeddingTrace()
    {
        if (m_reader is null || m_embeddings is null || m_tokenLabels is null) return;

        string prompt = m_tracePrompt.Trim();
        List<int> tokenIds = [];

        int pos = 0;
        while (pos < prompt.Length)
        {
            int bestLen = 0;
            int bestIdx = -1;
            for (int i = 0; i < m_vocabSize; i++)
            {
                string tok = m_tokenLabels[i];
                if (tok.Length == 0 || tok.Length <= bestLen || pos + tok.Length > prompt.Length) continue;
                string candidate = prompt.Substring(pos, tok.Length);
                if (candidate.Equals(tok, StringComparison.OrdinalIgnoreCase) ||
                    candidate.Equals(tok.TrimStart('▁', '_', ' '), StringComparison.OrdinalIgnoreCase))
                {
                    bestLen = tok.Length;
                    bestIdx = i;
                }
            }

            if (bestIdx >= 0) { tokenIds.Add(bestIdx); pos += bestLen; }
            else pos++;

            if (tokenIds.Count > 50) break;
        }

        if (tokenIds.Count == 0) { TraceStatus = "No tokens matched from prompt."; return; }

        float[][] trace = new float[tokenIds.Count][];
        for (int i = 0; i < tokenIds.Count; i++)
        {
            trace[i] = new float[m_dims];
            int bi = tokenIds[i] * m_dims;
            for (int d = 0; d < m_dims; d++)
                trace[i][d] = m_embeddings[bi + d];
        }

        m_traceStates = trace;
        ProjectAndRenderTrace();
        BuildLayerMovements();
        MaxLayerIndex = trace.Length - 1;
        SelectedLayerIndex = 0;
        HasTrace = true;

        string tokenList = string.Join(" → ", tokenIds.Select(id => $"\"{m_tokenLabels[id]}\""));
        TraceStatus = $"Embedding trace: {tokenIds.Count} tokens\n{tokenList}";
    }

    void ProjectAndRenderTrace()
    {
        if (m_traceStates is null || m_projected is null || m_viewport is null) return;

        int dims = m_traceStates[0].Length;
        int n = m_traceStates.Length;
        m_traceProjected = new Vector3[n];

        int d0, d1, d2;
        if (m_selectedProjection.StartsWith("PCA") && m_embeddings is not null)
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
        if (m_embeddings is not null)
        {
            for (int i = 0; i < m_vocabSize; i++)
            {
                int bi = i * m_dims;
                if (d0 < m_dims) { float v = m_embeddings[bi + d0]; if (v < min0) min0 = v; if (v > max0) max0 = v; }
                if (d1 < m_dims) { float v = m_embeddings[bi + d1]; if (v < min1) min1 = v; if (v > max1) max1 = v; }
                if (d2 < m_dims) { float v = m_embeddings[bi + d2]; if (v < min2) min2 = v; if (v > max2) max2 = v; }
            }
        }
        float range0 = max0 - min0; if (range0 < 1e-8f) range0 = 1f;
        float range1 = max1 - min1; if (range1 < 1e-8f) range1 = 1f;
        float range2 = max2 - min2; if (range2 < 1e-8f) range2 = 1f;
        const float Scale = 100f;

        for (int i = 0; i < n; i++)
        {
            float[] state = m_traceStates[i];
            float x = d0 < dims ? (state[d0] - min0) / range0 * Scale - Scale / 2f : 0f;
            float y = d1 < dims ? (state[d1] - min1) / range1 * Scale - Scale / 2f : 0f;
            float z = d2 < dims ? (state[d2] - min2) / range2 * Scale - Scale / 2f : 0f;
            m_traceProjected[i] = new Vector3(x, y, z);
        }

        RenderTrace();
    }

    void RenderTrace()
    {
        if (m_traceProjected is null || m_viewport is null) return;
        ClearTraceModel();

        MeshBuilder builder = new();
        float tubeRadius = (float)m_pointSize * 0.08f;
        float nodeRadius = (float)m_pointSize * 0.2f;

        for (int i = 0; i < m_traceProjected.Length - 1; i++)
            builder.AddCylinder(m_traceProjected[i], m_traceProjected[i + 1], tubeRadius, 6);

        for (int i = 0; i < m_traceProjected.Length; i++)
        {
            float t = m_traceProjected.Length > 1 ? (float)i / (m_traceProjected.Length - 1) : 0f;
            builder.AddSphere(m_traceProjected[i], nodeRadius * (1f + t * 0.5f), 8, 8);
        }

        MeshGeometry3D mesh = builder.ToMeshGeometry3D();
        List<Color4> colors = new(mesh.Positions?.Count ?? 0);
        for (int i = 0; i < mesh.Positions?.Count; i++)
        {
            float minDist = float.MaxValue;
            float bestT = 0f;
            for (int j = 0; j < m_traceProjected.Length; j++)
            {
                float d = Vector3.Distance(new Vector3(mesh.Positions[i].X, mesh.Positions[i].Y, mesh.Positions[i].Z), m_traceProjected[j]);
                if (d < minDist) { minDist = d; bestT = m_traceProjected.Length > 1 ? (float)j / (m_traceProjected.Length - 1) : 0f; }
            }
            colors.Add(TraceGradientColor(bestT));
        }
        mesh.Colors = [.. colors];

        m_traceModel = new MeshGeometryModel3D
        {
            Geometry = mesh,
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(1, 1, 1, 1),
                EmissiveColor = new Color4(0.15f, 0.15f, 0.15f, 0f),
            },
        };
        m_viewport.Items.Add(m_traceModel);
    }

    static Color4 TraceGradientColor(float t)
    {
        // blue → cyan → green → yellow → red
        if (t < 0.25f) { float s = t / 0.25f; return new Color4(0, s, 1, 1); }
        if (t < 0.5f) { float s = (t - 0.25f) / 0.25f; return new Color4(0, 1, 1 - s, 1); }
        if (t < 0.75f) { float s = (t - 0.5f) / 0.25f; return new Color4(s, 1, 0, 1); }
        { float s = (t - 0.75f) / 0.25f; return new Color4(1, 1 - s, 0, 1); }
    }

    void BuildLayerMovements()
    {
        LayerMovements.Clear();
        if (m_traceStates is null || m_traceStates.Length < 2) return;

        int dims = m_traceStates[0].Length;
        float[] distances = new float[m_traceStates.Length - 1];
        float maxDist = 0f;

        for (int i = 0; i < distances.Length; i++)
        {
            float sum = 0f;
            for (int d = 0; d < dims; d++)
            {
                float diff = m_traceStates[i + 1][d] - m_traceStates[i][d];
                sum += diff * diff;
            }
            distances[i] = MathF.Sqrt(sum);
            if (distances[i] > maxDist) maxDist = distances[i];
        }

        for (int i = 0; i < distances.Length; i++)
            LayerMovements.Add(new LayerMovementInfo($"L{i}", distances[i], maxDist));
    }

    void OnLayerSelected()
    {
        if (m_traceProjected is null || m_selectedLayerIndex < 0 || m_selectedLayerIndex >= m_traceProjected.Length) return;

        Vector3 pos = m_traceProjected[m_selectedLayerIndex];
        if (Camera is PerspectiveCamera cam)
        {
            cam.Position = new Point3D(pos.X, pos.Y, pos.Z + 20);
            cam.LookDirection = new Vector3D(0, 0, -20);
            m_flyCamera?.SyncFromCamera();
        }
    }

    void ClearTrace()
    {
        ClearTraceModel();
        m_traceStates = null;
        m_traceProjected = null;
        LayerMovements.Clear();
        HasTrace = false;
        TraceStatus = "";
    }

    void ClearTraceModel()
    {
        if (m_traceModel is null || m_viewport is null) return;
        m_viewport.Items.Remove(m_traceModel);
        m_traceModel.Dispose();
        m_traceModel = null;
    }
}
