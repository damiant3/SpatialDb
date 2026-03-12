using System.Numerics;
using System.Windows;
using PerspectiveCamera = HelixToolkit.Wpf.SharpDX.PerspectiveCamera;
using Point3D = System.Windows.Media.Media3D.Point3D;
using Vector3D = System.Windows.Media.Media3D.Vector3D;
///////////////////////////////////////////////
namespace NeuralNavigator;

sealed partial class MainViewModel
{
    void SearchToken()
    {
        if (m_tokenLabels is null || m_projected is null || string.IsNullOrWhiteSpace(SearchText)) return;

        string query = SearchText.Trim();
        int foundIdx = -1;

        for (int i = 0; i < m_tokenLabels.Length; i++)
            if (m_tokenLabels[i].Equals(query, StringComparison.OrdinalIgnoreCase))
            { foundIdx = i; break; }

        if (foundIdx < 0)
            for (int i = 0; i < m_tokenLabels.Length; i++)
                if (m_tokenLabels[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                { foundIdx = i; break; }

        if (foundIdx < 0)
        {
            StatusText += $"\nNot found: \"{query}\"";
            return;
        }

        SelectToken(foundIdx);

        if (Camera is PerspectiveCamera cam)
        {
            Vector3 pos = m_projected[foundIdx];
            cam.Position = new Point3D(pos.X, pos.Y, pos.Z + 30);
            cam.LookDirection = new Vector3D(0, 0, -30);
            m_flyCamera?.SyncFromCamera();
        }
    }

    void SelectToken(int tokenIdx)
    {
        if (m_tokenLabels is null || m_embeddings is null || m_projected is null) return;

        SelectedTokenText = $"\"{m_tokenLabels[tokenIdx]}\"";
        SelectedTokenId = $"ID: {tokenIdx}";
        HasSelection = true;
        m_selectedTokenIdx = tokenIdx;

        FindNeighbors(tokenIdx, 16);
        HighlightTokens(tokenIdx);
    }

    void FindNeighbors(int tokenIdx, int k)
    {
        if (m_embeddings is null || m_tokenLabels is null) return;

        int baseA = tokenIdx * m_dims;
        (int idx, float dist)[] distances = new (int, float)[m_vocabSize];

        for (int i = 0; i < m_vocabSize; i++)
        {
            if (i == tokenIdx) { distances[i] = (i, float.MaxValue); continue; }
            int baseB = i * m_dims;
            float sum = 0f;
            for (int d = 0; d < m_dims; d++)
            {
                float diff = m_embeddings[baseA + d] - m_embeddings[baseB + d];
                sum += diff * diff;
            }
            distances[i] = (i, MathF.Sqrt(sum));
        }

        Array.Sort(distances, (a, b) => a.dist.CompareTo(b.dist));

        Neighbors.Clear();
        for (int i = 0; i < Math.Min(k, distances.Length); i++)
        {
            (int idx, float dist) = distances[i];
            if (dist >= float.MaxValue) break;
            Neighbors.Add(new NeighborInfo(m_tokenLabels[idx], idx, dist));
        }
    }

    void OnHoverMove(Point screenPos)
    {
        int idx = HitTestTokenAtScreen(screenPos);
        if (idx < 0) { HoverText = ""; return; }
        HoverText = $"\"{m_tokenLabels![idx]}\"  (ID {idx})";
    }

    void OnDoubleClick(Point screenPos)
    {
        int idx = HitTestTokenAtScreen(screenPos);
        if (idx >= 0)
            SelectToken(idx);
    }

    void OnRightClick(Point screenPos)
    {
        int idx = HitTestTokenAtScreen(screenPos);
        if (idx < 0) return;
        SelectToken(idx);
        ShowContextMenu(screenPos);
    }

    void ShowContextMenu(Point screenPos)
    {
        if (m_viewport is null) return;

        System.Windows.Controls.ContextMenu menu = new()
        {
            Style = null,
            Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#252526")!),
            Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#d4d4d4")!),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#3e3e42")!),
        };

        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "See Neighbors", Command = ContextSeeNeighborsCommand });
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Show in Other Dimensions", Command = ContextShowDimsCommand });
        menu.Items.Add(new System.Windows.Controls.MenuItem { Header = "Find Cluster (radius)", Command = ContextFindClusterCommand });
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(new System.Windows.Controls.MenuItem
        {
            Header = m_compareTokenIdx >= 0 && m_compareTokenIdx != m_selectedTokenIdx
                ? $"Compare to \"{m_tokenLabels?[m_compareTokenIdx] ?? "?"}\"..."
                : "Mark for Compare",
            Command = ContextCompareToCommand
        });

        menu.PlacementTarget = m_viewport;
        menu.IsOpen = true;
    }

    void ContextSeeNeighbors()
    {
        if (m_selectedTokenIdx >= 0)
            SelectToken(m_selectedTokenIdx);
    }

    void ContextShowInOtherDims()
    {
        if (m_embeddings is null || m_selectedTokenIdx < 0) return;
        int current = Array.IndexOf(ProjectionModes, m_selectedProjection);
        SelectedProjection = ProjectionModes[(current + 1) % ProjectionModes.Length];
        StatusText += $"\nProjection → {SelectedProjection}";
    }

    void ContextFindCluster()
    {
        if (m_embeddings is null || m_tokenLabels is null || m_selectedTokenIdx < 0 || m_projected is null) return;

        float radius = ComputeAdaptiveRadius(m_selectedTokenIdx, 8);
        int baseA = m_selectedTokenIdx * m_dims;
        List<(int idx, float dist)> cluster = [];

        for (int i = 0; i < m_vocabSize; i++)
        {
            if (i == m_selectedTokenIdx) continue;
            int baseB = i * m_dims;
            float sum = 0f;
            for (int d = 0; d < m_dims; d++)
            {
                float diff = m_embeddings[baseA + d] - m_embeddings[baseB + d];
                sum += diff * diff;
            }
            float dist = MathF.Sqrt(sum);
            if (dist <= radius)
                cluster.Add((i, dist));
        }

        cluster.Sort((a, b) => a.dist.CompareTo(b.dist));

        Neighbors.Clear();
        int shown = Math.Min(cluster.Count, 50);
        for (int i = 0; i < shown; i++)
            Neighbors.Add(new NeighborInfo(m_tokenLabels[cluster[i].idx], cluster[i].idx, cluster[i].dist));

        HighlightCluster(m_selectedTokenIdx, cluster.Select(c => c.idx).Take(shown).ToList());
        StatusText += $"\nCluster: {cluster.Count} tokens within radius {radius:F2}";
    }

    float ComputeAdaptiveRadius(int tokenIdx, int k)
    {
        if (m_embeddings is null) return 1f;

        int baseA = tokenIdx * m_dims;
        float[] dists = new float[m_vocabSize];
        for (int i = 0; i < m_vocabSize; i++)
        {
            if (i == tokenIdx) { dists[i] = float.MaxValue; continue; }
            int baseB = i * m_dims;
            float sum = 0f;
            for (int d = 0; d < m_dims; d++)
            {
                float diff = m_embeddings[baseA + d] - m_embeddings[baseB + d];
                sum += diff * diff;
            }
            dists[i] = MathF.Sqrt(sum);
        }

        Array.Sort(dists);
        float meanDist = 0f;
        for (int i = 0; i < k && i < dists.Length; i++)
            meanDist += dists[i];
        return (meanDist / k) * 2f;
    }

    void ContextCompareTo()
    {
        if (m_embeddings is null || m_tokenLabels is null || m_projected is null || m_selectedTokenIdx < 0) return;

        if (m_compareTokenIdx < 0 || m_compareTokenIdx == m_selectedTokenIdx)
        {
            m_compareTokenIdx = m_selectedTokenIdx;
            StatusText += $"\nMarked \"{m_tokenLabels[m_compareTokenIdx]}\" for comparison. Right-click another token → Compare.";
            return;
        }

        int idxA = m_compareTokenIdx;
        int idxB = m_selectedTokenIdx;
        int baseA = idxA * m_dims;
        int baseB = idxB * m_dims;

        float[] direction = new float[m_dims];
        for (int d = 0; d < m_dims; d++)
            direction[d] = m_embeddings[baseB + d] - m_embeddings[baseA + d];

        float dirMag = 0f;
        for (int d = 0; d < m_dims; d++)
            dirMag += direction[d] * direction[d];
        dirMag = MathF.Sqrt(dirMag);
        if (dirMag < 1e-8f) return;

        for (int d = 0; d < m_dims; d++)
            direction[d] /= dirMag;

        List<(int idx, float alignment, float dist)> candidates = [];
        for (int i = 0; i < m_vocabSize; i++)
        {
            if (i == idxA || i == idxB) continue;
            int bi = i * m_dims;
            float dot = 0f;
            float distSq = 0f;
            for (int d = 0; d < m_dims; d++)
            {
                float diff = m_embeddings[bi + d] - m_embeddings[baseA + d];
                dot += diff * direction[d];
                distSq += diff * diff;
            }
            float dist = MathF.Sqrt(distSq);
            float cosine = dist > 1e-8f ? dot / dist : 0f;

            if (cosine > 0.5f && MathF.Abs(dot - dirMag) < dirMag * 0.5f)
                candidates.Add((i, cosine, MathF.Abs(dot - dirMag)));
        }

        candidates.Sort((a, b) => a.dist.CompareTo(b.dist));

        Neighbors.Clear();
        Neighbors.Add(new NeighborInfo($"[A] {m_tokenLabels[idxA]}", idxA, 0f));
        Neighbors.Add(new NeighborInfo($"[B] {m_tokenLabels[idxB]}", idxB, dirMag));

        int shown = Math.Min(candidates.Count, 20);
        for (int i = 0; i < shown; i++)
            Neighbors.Add(new NeighborInfo(m_tokenLabels[candidates[i].idx], candidates[i].idx, candidates[i].dist));

        DrawVectorLine(idxA, idxB);
        HighlightComparison(idxA, idxB, candidates.Take(shown).Select(c => c.idx).ToList());

        StatusText += $"\nCompare: \"{m_tokenLabels[idxA]}\" → \"{m_tokenLabels[idxB]}\" " +
                      $"(distance {dirMag:F2}, {candidates.Count} aligned tokens)";
        m_compareTokenIdx = -1;
    }

    int HitTestTokenAtScreen(Point screenPos)
    {
        if (m_projected is null || m_tokenLabels is null || m_viewport is null) return -1;
        if (Camera is not PerspectiveCamera cam) return -1;

        Point3D camPos = cam.Position;
        Vector3D lookDir = cam.LookDirection;
        Vector3D upDir = cam.UpDirection;

        double fovRad = cam.FieldOfView * (Math.PI / 180.0);
        double aspect = m_viewport.ActualWidth / Math.Max(m_viewport.ActualHeight, 1);

        Vector3D rightDir = Vector3D.CrossProduct(lookDir, upDir);
        rightDir.Normalize();
        Vector3D trueUp = Vector3D.CrossProduct(rightDir, lookDir);
        trueUp.Normalize();
        Vector3D fwd = lookDir;
        fwd.Normalize();

        double halfH = Math.Tan(fovRad / 2.0);
        double halfW = halfH * aspect;
        double nx = (screenPos.X / m_viewport.ActualWidth) * 2.0 - 1.0;
        double ny = 1.0 - (screenPos.Y / m_viewport.ActualHeight) * 2.0;

        Vector3D rayDir = fwd + rightDir * (nx * halfW) + trueUp * (ny * halfH);
        rayDir.Normalize();

        Vector3 origin = new((float)camPos.X, (float)camPos.Y, (float)camPos.Z);
        Vector3 direction = new((float)rayDir.X, (float)rayDir.Y, (float)rayDir.Z);

        int count = Math.Min((int)m_visibleTokenCount, m_projected.Length);
        return TokenSpatialIndex.FindNearestToRay(m_projected, count, origin, direction, (float)m_pointSize * 0.5f);
    }
}
