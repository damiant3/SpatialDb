using System.IO;
using System.Numerics;
using System.Windows;
using Microsoft.Win32;
///////////////////////////////////////////////
namespace NeuralNavigator;

sealed partial class MainViewModel
{
    void LoadModel()
    {
        OpenFileDialog dlg = new()
        {
            Title = "Select a GGUF model file",
            Filter = "GGUF files|*.gguf;sha256-*|All files|*.*",
            InitialDirectory = @"D:\AI\OllamaModels\blobs"
        };

        string? testData = FindTestDataDir();
        if (testData is not null && !Directory.Exists(dlg.InitialDirectory))
            dlg.InitialDirectory = testData;

        if (dlg.ShowDialog() != true) return;

        StatusText = "Loading model...";

        Task.Run(() =>
        {
            try
            {
                m_reader?.Dispose();
                m_reader = SparseLattice.Gguf.GgufReader.Open(dlg.FileName);
                m_ggufPath = dlg.FileName;

                m_dims = m_reader.EmbeddingLength;
                m_vocabSize = m_reader.Tokens.Count;
                m_embeddings = m_reader.ReadTensorF32("token_embd.weight");

                int tensorRows = m_embeddings.Length / Math.Max(m_dims, 1);
                if (m_vocabSize > tensorRows)
                    m_vocabSize = tensorRows;

                m_tokenLabels = new string[m_vocabSize];
                for (int i = 0; i < m_vocabSize; i++)
                    m_tokenLabels[i] = m_reader.Tokens[i];

                m_projected = ProjectEmbeddings(m_embeddings, m_vocabSize, m_dims);

                Application.Current.Dispatcher.Invoke(() =>
                {
                    StatusText = $"Loaded: {m_reader.ModelName}\n" +
                                 $"Arch: {m_reader.Architecture}\n" +
                                 $"Vocab: {m_vocabSize:N0} tokens\n" +
                                 $"Dims: {m_dims}\n" +
                                 $"Layers: {m_reader.LayerCount}";
                    RebuildPointCloud();
                    PopulateWeightTensors();
                });
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                    StatusText = $"Error: {ex.Message}");
            }
        });
    }

    Vector3[] ProjectEmbeddings(float[] embeddings, int vocabSize, int dims)
    {
        int d0, d1, d2;
        if (m_selectedProjection.StartsWith("PCA"))
            (d0, d1, d2) = FindTopVarianceDims(embeddings, vocabSize, dims);
        else
            (d0, d1, d2) = m_selectedProjection switch
            {
                "Raw (dims 1,2,3)" => (1, 2, 3),
                _ => (0, 1, 2),
            };

        Vector3[] result = new Vector3[vocabSize];
        const float Scale = 100f;

        float min0 = float.MaxValue, max0 = float.MinValue;
        float min1 = float.MaxValue, max1 = float.MinValue;
        float min2 = float.MaxValue, max2 = float.MinValue;

        for (int i = 0; i < vocabSize; i++)
        {
            int bi = i * dims;
            if (d0 < dims) { float v = embeddings[bi + d0]; if (v < min0) min0 = v; if (v > max0) max0 = v; }
            if (d1 < dims) { float v = embeddings[bi + d1]; if (v < min1) min1 = v; if (v > max1) max1 = v; }
            if (d2 < dims) { float v = embeddings[bi + d2]; if (v < min2) min2 = v; if (v > max2) max2 = v; }
        }

        float range0 = max0 - min0; if (range0 < 1e-8f) range0 = 1f;
        float range1 = max1 - min1; if (range1 < 1e-8f) range1 = 1f;
        float range2 = max2 - min2; if (range2 < 1e-8f) range2 = 1f;

        for (int i = 0; i < vocabSize; i++)
        {
            int bi = i * dims;
            float x = d0 < dims ? (embeddings[bi + d0] - min0) / range0 * Scale - Scale / 2f : 0f;
            float y = d1 < dims ? (embeddings[bi + d1] - min1) / range1 * Scale - Scale / 2f : 0f;
            float z = d2 < dims ? (embeddings[bi + d2] - min2) / range2 * Scale - Scale / 2f : 0f;
            result[i] = new Vector3(x, y, z);
        }

        return result;
    }

    static (int, int, int) FindTopVarianceDims(float[] embeddings, int vocabSize, int dims)
    {
        double[] mean = new double[dims];
        double[] variance = new double[dims];

        for (int i = 0; i < vocabSize; i++)
        {
            int bi = i * dims;
            for (int d = 0; d < dims; d++)
                mean[d] += embeddings[bi + d];
        }
        for (int d = 0; d < dims; d++) mean[d] /= vocabSize;

        for (int i = 0; i < vocabSize; i++)
        {
            int bi = i * dims;
            for (int d = 0; d < dims; d++)
            {
                double diff = embeddings[bi + d] - mean[d];
                variance[d] += diff * diff;
            }
        }

        int[] indices = Enumerable.Range(0, dims).ToArray();
        Array.Sort(variance, indices);
        return (indices[dims - 1], indices[dims - 2], indices[dims - 3]);
    }

    void ReprojectAndRebuild()
    {
        if (m_embeddings is null || m_vocabSize == 0) return;
        m_projected = ProjectEmbeddings(m_embeddings, m_vocabSize, m_dims);
        RebuildPointCloud();
    }
}
