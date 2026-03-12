using System.Numerics;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using MeshGeometryModel3D = HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D;
///////////////////////////////////////////////
namespace NeuralNavigator;

sealed partial class MainViewModel
{
    void PopulateWeightTensors()
    {
        WeightTensorNames = [];
        if (m_reader is null) return;

        foreach (SparseLattice.Gguf.GgufTensorInfo ti in m_reader.TensorInfos)
            if (ti.Shape.Length >= 2 &&
                (ti.Name.Contains("attn") || ti.Name.Contains("ffn") || ti.Name.Contains("weight") || ti.Name.Contains("embd")))
                WeightTensorNames.Add(ti.Name);

        if (WeightTensorNames.Count > 0)
            SelectedWeightTensor = WeightTensorNames[0];
    }

    void ShowWeights()
    {
        if (m_reader is null || string.IsNullOrEmpty(m_selectedWeightTensor) || m_projected is null) return;

        WeightStatus = $"Loading {m_selectedWeightTensor}...";
        string tensorName = m_selectedWeightTensor;

        Task.Run(() =>
        {
            try
            {
                float[] weights = m_reader.ReadTensorF32(tensorName);
                SparseLattice.Gguf.GgufTensorInfo info = m_reader.GetTensorInfo(tensorName);
                int[] shape = info.Shape;

                if (shape.Length < 2)
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        WeightStatus = $"{tensorName}: 1D tensor ({shape[0]} elements), skipped.");
                    return;
                }

                int cols = shape[0];
                int rows = shape[1];

                int d0, d1, d2;
                if (cols >= 3)
                    (d0, d1, d2) = FindTopVarianceDims(weights, rows, cols);
                else
                    (d0, d1, d2) = (0, Math.Min(1, cols - 1), Math.Min(2, cols - 1));

                float min0 = float.MaxValue, max0 = float.MinValue;
                float min1 = float.MaxValue, max1 = float.MinValue;
                float min2 = float.MaxValue, max2 = float.MinValue;
                for (int i = 0; i < rows; i++)
                {
                    int bi = i * cols;
                    { float v = weights[bi + d0]; if (v < min0) min0 = v; if (v > max0) max0 = v; }
                    { float v = weights[bi + d1]; if (v < min1) min1 = v; if (v > max1) max1 = v; }
                    { float v = weights[bi + d2]; if (v < min2) min2 = v; if (v > max2) max2 = v; }
                }
                float range0 = max0 - min0; if (range0 < 1e-8f) range0 = 1f;
                float range1 = max1 - min1; if (range1 < 1e-8f) range1 = 1f;
                float range2 = max2 - min2; if (range2 < 1e-8f) range2 = 1f;
                const float Scale = 100f;

                int maxRows = Math.Min(rows, 5000);
                Vector3[] positions = new Vector3[maxRows];
                for (int i = 0; i < maxRows; i++)
                {
                    int bi = i * cols;
                    positions[i] = new Vector3(
                        (weights[bi + d0] - min0) / range0 * Scale - Scale / 2f,
                        (weights[bi + d1] - min1) / range1 * Scale - Scale / 2f,
                        (weights[bi + d2] - min2) / range2 * Scale - Scale / 2f);
                }

                // approximate effective rank via per-column variance
                double totalVar = 0;
                double[] colVar = new double[cols];
                for (int c = 0; c < cols; c++)
                {
                    double mean = 0;
                    for (int r = 0; r < rows; r++) mean += weights[r * cols + c];
                    mean /= rows;
                    double v = 0;
                    for (int r = 0; r < rows; r++) { double diff = weights[r * cols + c] - mean; v += diff * diff; }
                    colVar[c] = v;
                    totalVar += v;
                }
                Array.Sort(colVar);
                Array.Reverse(colVar);
                double cumVar = 0;
                int effectiveRank = 0;
                for (int c = 0; c < cols; c++)
                {
                    cumVar += colVar[c];
                    effectiveRank++;
                    if (cumVar >= totalVar * 0.9) break;
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    RenderWeightPoints(positions);
                    WeightStatus = $"{tensorName}: {rows}×{cols}\n" +
                                   $"Showing {maxRows} rows, effective rank ≈ {effectiveRank}/{cols} (90% variance)";
                });
            }
            catch (Exception ex)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    WeightStatus = $"Error: {ex.Message}");
            }
        });
    }

    void RenderWeightPoints(Vector3[] positions)
    {
        if (m_viewport is null) return;
        ClearWeightModel();

        MeshBuilder builder = new();
        float sphereRadius = (float)m_pointSize * 0.06f;

        for (int i = 0; i < positions.Length; i++)
            builder.AddSphere(positions[i], sphereRadius, 3, 3);

        m_weightModel = new MeshGeometryModel3D
        {
            Geometry = builder.ToMeshGeometry3D(),
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(0.3f, 0.7f, 1f, 0.6f),
                EmissiveColor = new Color4(0.05f, 0.1f, 0.2f, 0f),
            },
        };
        m_viewport.Items.Add(m_weightModel);
    }

    void ClearWeightModel()
    {
        if (m_weightModel is null || m_viewport is null) return;
        m_viewport.Items.Remove(m_weightModel);
        m_weightModel.Dispose();
        m_weightModel = null;
    }
}
