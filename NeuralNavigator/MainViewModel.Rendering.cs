using System.Numerics;
using HelixToolkit;
using HelixToolkit.Geometry;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using MeshGeometry3D = HelixToolkit.SharpDX.MeshGeometry3D;
using MeshGeometryModel3D = HelixToolkit.Wpf.SharpDX.MeshGeometryModel3D;
///////////////////////////////////////////////
namespace NeuralNavigator;

sealed partial class MainViewModel
{
    void RebuildPointCloud()
    {
        if (m_viewport is null || m_projected is null || m_tokenLabels is null) return;

        if (m_pointCloudModel is not null)
        {
            m_viewport.Items.Remove(m_pointCloudModel);
            m_pointCloudModel.Dispose();
        }
        if (m_highlightModel is not null)
        {
            m_viewport.Items.Remove(m_highlightModel);
            m_highlightModel.Dispose();
        }

        int count = Math.Min((int)m_visibleTokenCount, m_projected.Length);
        float sphereRadius = (float)m_pointSize * 0.1f;

        MeshBuilder builder = new();
        List<Color4> colors = [];

        for (int i = 0; i < count; i++)
        {
            builder.AddSphere(m_projected[i], sphereRadius, 4, 4);
            colors.Add(GetTokenColor(i));
        }

        MeshGeometry3D mesh = builder.ToMeshGeometry3D();

        while (colors.Count < mesh.Positions?.Count)
        {
            int tokenIdx = (int)Math.Min((long)colors.Count * count / Math.Max(mesh.Positions?.Count ?? 1, 1), count - 1);
            colors.Add(GetTokenColor(tokenIdx));
        }

        mesh.Colors = new Color4Collection(colors);

        m_pointCloudModel = new MeshGeometryModel3D
        {
            Geometry = mesh,
            Material = new PhongMaterial { DiffuseColor = new Color4(1, 1, 1, 1), EnableFlatShading = true },
            IsHitTestVisible = true,
        };

        m_viewport.Items.Add(m_pointCloudModel);
    }

    Color4 GetTokenColor(int tokenIdx) => m_selectedColorMode switch
    {
        "Magnitude" => GetMagnitudeColor(tokenIdx),
        "Cluster" => GetClusterColor(tokenIdx),
        _ => GetTokenIdColor(tokenIdx),
    };

    Color4 GetTokenIdColor(int tokenIdx)
    {
        float hue = (tokenIdx * 137.508f) % 360f;
        return HsvToColor4(hue, 0.7f, 0.9f);
    }

    Color4 GetMagnitudeColor(int tokenIdx)
    {
        if (m_embeddings is null) return new Color4(0.5f, 0.5f, 0.5f, 1f);
        int bi = tokenIdx * m_dims;
        float mag = 0f;
        for (int d = 0; d < m_dims; d++)
            mag += m_embeddings[bi + d] * m_embeddings[bi + d];
        mag = MathF.Sqrt(mag);
        float t = MathF.Min(mag / 10f, 1f);
        return new Color4(t, 0.3f, 1f - t, 1f);
    }

    Color4 GetClusterColor(int tokenIdx)
    {
        if (m_tokenLabels is null) return new Color4(0.5f, 0.5f, 0.5f, 1f);
        string token = m_tokenLabels[tokenIdx];
        if (token.Length == 0) return new Color4(0.3f, 0.3f, 0.3f, 1f);

        char c = char.ToLower(token[0]);
        float hue = c switch
        {
            >= 'a' and <= 'z' => (c - 'a') * (360f / 26f),
            >= '0' and <= '9' => 60f,
            _ => 0f,
        };
        return HsvToColor4(hue, 0.8f, 0.9f);
    }

    static Color4 HsvToColor4(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1f - MathF.Abs((h / 60f) % 2f - 1f));
        float m = v - c;
        float r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return new Color4(r + m, g + m, b + m, 1f);
    }

    void HighlightTokens(int centerIdx)
    {
        if (m_viewport is null || m_projected is null) return;

        if (m_highlightModel is not null)
        {
            m_viewport.Items.Remove(m_highlightModel);
            m_highlightModel.Dispose();
        }

        MeshBuilder builder = new();
        float radius = (float)m_pointSize * 0.3f;
        builder.AddSphere(m_projected[centerIdx], radius, 8, 8);

        foreach (NeighborInfo n in Neighbors)
            if (n.TokenId < m_projected.Length)
                builder.AddSphere(m_projected[n.TokenId], radius * 0.7f, 6, 6);

        m_highlightModel = new MeshGeometryModel3D
        {
            Geometry = builder.ToMeshGeometry3D(),
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(1f, 1f, 0f, 1f),
                EmissiveColor = new Color4(0.3f, 0.3f, 0f, 0f),
            },
        };
        m_viewport.Items.Add(m_highlightModel);
    }

    void HighlightCluster(int centerIdx, List<int> memberIndices)
    {
        if (m_viewport is null || m_projected is null) return;

        if (m_highlightModel is not null)
        {
            m_viewport.Items.Remove(m_highlightModel);
            m_highlightModel.Dispose();
        }

        MeshBuilder builder = new();
        float radius = (float)m_pointSize * 0.3f;
        builder.AddSphere(m_projected[centerIdx], radius, 8, 8);

        foreach (int idx in memberIndices)
            if (idx < m_projected.Length)
                builder.AddSphere(m_projected[idx], radius * 0.6f, 6, 6);

        m_highlightModel = new MeshGeometryModel3D
        {
            Geometry = builder.ToMeshGeometry3D(),
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(0.2f, 1f, 0.3f, 1f),
                EmissiveColor = new Color4(0f, 0.3f, 0f, 0f),
            },
        };
        m_viewport.Items.Add(m_highlightModel);
    }

    void HighlightComparison(int idxA, int idxB, List<int> analogous)
    {
        if (m_viewport is null || m_projected is null) return;

        if (m_highlightModel is not null)
        {
            m_viewport.Items.Remove(m_highlightModel);
            m_highlightModel.Dispose();
        }

        MeshBuilder builder = new();
        float radius = (float)m_pointSize * 0.3f;
        builder.AddSphere(m_projected[idxA], radius, 8, 8);
        builder.AddSphere(m_projected[idxB], radius, 8, 8);

        foreach (int idx in analogous)
            if (idx < m_projected.Length)
                builder.AddSphere(m_projected[idx], radius * 0.6f, 6, 6);

        m_highlightModel = new MeshGeometryModel3D
        {
            Geometry = builder.ToMeshGeometry3D(),
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(0.8f, 0.3f, 1f, 1f),
                EmissiveColor = new Color4(0.2f, 0.05f, 0.3f, 0f),
            },
        };
        m_viewport.Items.Add(m_highlightModel);
    }

    void DrawVectorLine(int idxA, int idxB)
    {
        if (m_viewport is null || m_projected is null) return;
        ClearVectorLine();

        Vector3 posA = m_projected[idxA];
        Vector3 posB = m_projected[idxB];
        float tubeRadius = (float)m_pointSize * 0.05f;

        MeshBuilder builder = new();
        builder.AddCylinder(posA, posB, tubeRadius, 6);
        builder.AddSphere(posB, tubeRadius * 4f, 6, 6);

        m_vectorLineModel = new MeshGeometryModel3D
        {
            Geometry = builder.ToMeshGeometry3D(),
            Material = new PhongMaterial
            {
                DiffuseColor = new Color4(1f, 0.5f, 0f, 1f),
                EmissiveColor = new Color4(0.3f, 0.1f, 0f, 0f),
            },
        };
        m_viewport.Items.Add(m_vectorLineModel);
    }

    void ClearVectorLine()
    {
        if (m_vectorLineModel is null || m_viewport is null) return;
        m_viewport.Items.Remove(m_vectorLineModel);
        m_vectorLineModel.Dispose();
        m_vectorLineModel = null;
    }
}
