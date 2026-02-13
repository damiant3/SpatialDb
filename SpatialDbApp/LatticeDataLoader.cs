using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using SpatialDbLib.Math;
using SpatialDbLib.Simulation;
using SpatialDbLib.Lattice;

namespace SpatialDbApp.Loader;

public sealed class PointData
{
    public LongVector3 Position { get; init; }
    public (int R, int G, int B)? ColorRgb { get; init; }
    // Optional size token (integer). Interpreted by UI relative to scene extents.
    public int? SizeToken { get; init; }
    public PointData(LongVector3 pos, (int, int, int)? rgb = null, int? size = null)
    {
        Position = pos;
        ColorRgb = rgb;
        SizeToken = size;
    }
}

public static class LatticeDataLoader
{
    // Simple rules:
    // - Lines beginning with a digit or '-' are treated as data lines.
    // - Any other leading character (including '#') makes the line a comment; it is skipped.
    // - If any data line contains decimals/exponent or cannot be parsed consistently, the file is considered corrupt
    //   and parsing aborts with an InvalidDataException.
    // - Each data line represents a single point and must have a consistent token count across the file.
    // - After successful numeric parsing we heuristically decide frame-based vs static and validate the token-count pattern.
    private static bool LineIsData(string trimmed) => trimmed.Length > 0 && (char.IsDigit(trimmed[0]) || trimmed[0] == '-');

    private static bool TokenHasDecimalOrExp(string t)
        => t.Contains('.') || t.IndexOf('e', StringComparison.OrdinalIgnoreCase) >= 0;

    private static bool TryParseLongStrict(string token, out long value)
    {
        value = 0;
        if (TokenHasDecimalOrExp(token)) return false;
        return long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseIntStrict(string token, out int value)
    {
        value = 0;
        if (TokenHasDecimalOrExp(token)) return false;
        return int.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);
    }

    // Auto-parse file (strict). On corrupt data throws InvalidDataException.
    private static (bool IsFrameFile, List<PointData> Points, Dictionary<int, List<PointData>> Frames) AutoParseFile(string path)
    {
        var numericTokenLines = new List<string[]>();
        int lineNo = 0;
        foreach (var raw in File.ReadLines(path))
        {
            lineNo++;
            if (raw == null) continue;
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;

            // Comment lines (anything not starting with digit or '-') are silently skipped.
            if (!LineIsData(trimmed)) continue;

            var tokens = trimmed.Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            // If any token contains decimal/exponent -> file is corrupt
            if (tokens.Any(TokenHasDecimalOrExp))
                throw new InvalidDataException($"Corrupt file: decimal/exponent token on line {lineNo}");

            numericTokenLines.Add(tokens);
        }

        if (numericTokenLines.Count == 0)
            return (false, new List<PointData>(), new Dictionary<int, List<PointData>>());

        // All data lines must have the same token count; otherwise file is corrupt.
        var tokenCount = numericTokenLines[0].Length;
        if (numericTokenLines.Any(t => t.Length != tokenCount))
            throw new InvalidDataException("Corrupt file: inconsistent token counts across data lines");

        // Token count must be between 3 and 8 (we support these formats)
        if (tokenCount < 3 || tokenCount > 8)
            throw new InvalidDataException($"Corrupt file: unsupported tokens-per-line: {tokenCount}");

        // Convert all tokens to longs (strict). If any fails -> corrupt.
        var numericLines = new List<long[]>();
        for (int i = 0; i < numericTokenLines.Count; i++)
        {
            var tokens = numericTokenLines[i];
            var arr = new long[tokens.Length];
            for (int j = 0; j < tokens.Length; j++)
            {
                if (!TryParseLongStrict(tokens[j], out var v))
                    throw new InvalidDataException($"Corrupt file: invalid integer token at data line {i + 1}, token {j + 1}");
                arr[j] = v;
            }
            numericLines.Add(arr);
        }

        // Heuristic: decide whether file is frame-based. Use first column as candidate frame id:
        bool IsLikelyFrame()
        {
            if (numericLines.Count == 0) return false;
            // first column must exist
            var firstCol = numericLines.Select(a => a[0]).ToArray();
            // If first column values are all non-negative, non-decreasing, not all unique, and max not absurdly large => frame
            if (firstCol.Any(v => v < 0)) return false;
            for (int i = 1; i < firstCol.Length; i++)
                if (firstCol[i] < firstCol[i - 1]) return false;
            var distinct = firstCol.Distinct().Count();
            if (distinct == firstCol.Length) return false; // all unique => unlikely frames
            var max = firstCol.Max();
            if (max > numericLines.Count * 8L) return false; // absurdly large frame ids
            return true;
        }

        if (IsLikelyFrame())
        {
            // Validate allowed token counts for frame formats: 4,5,7,8 (frame + xyz [+size] or +rgb[+size])
            if (!(tokenCount == 4 || tokenCount == 5 || tokenCount == 7 || tokenCount == 8))
                throw new InvalidDataException($"Corrupt file: frame-based file with unexpected token count {tokenCount}");

            var frames = new Dictionary<int, List<PointData>>();
            foreach (var nums in numericLines)
            {
                // nums[0]=frame, nums[1]=x, nums[2]=y, nums[3]=z
                int frame = (int)nums[0];
                long x = nums[1], y = nums[2], z = nums[3];
                (int, int, int)? rgb = null;
                int? sizeToken = null;
                if (tokenCount >= 7)
                {
                    int r = (int)nums[4], g = (int)nums[5], b = (int)nums[6];
                    if (r < 0 || r > 255 || g < 0 || g > 255 || b < 0 || b > 255)
                        throw new InvalidDataException("Corrupt file: RGB components out of 0..255 range");
                    rgb = (r, g, b);
                }
                if (tokenCount == 5) sizeToken = (int)nums[4];
                if (tokenCount == 8) sizeToken = (int)nums[7];

                if (!frames.TryGetValue(frame, out var list)) { list = new List<PointData>(); frames[frame] = list; }
                list.Add(new PointData(new LongVector3(x, y, z), rgb, sizeToken));
            }
            return (true, new List<PointData>(), frames);
        }
        else
        {
            // Static file: allowed token counts: 3,4,6,7 (xyz [size] or xyz+r,g,b [size])
            if (!(tokenCount == 3 || tokenCount == 4 || tokenCount == 6 || tokenCount == 7))
                throw new InvalidDataException($"Corrupt file: static file with unexpected token count {tokenCount}");

            var points = new List<PointData>();
            foreach (var nums in numericLines)
            {
                long x = nums[0], y = nums[1], z = nums[2];
                (int, int, int)? rgb = null;
                int? sizeToken = null;
                if (tokenCount >= 6)
                {
                    int r = (int)nums[3], g = (int)nums[4], b = (int)nums[5];
                    if (r < 0 || r > 255 || g < 0 || g > 255 || b < 0 || b > 255)
                        throw new InvalidDataException("Corrupt file: RGB components out of 0..255 range");
                    rgb = (r, g, b);
                }
                if (tokenCount == 4) sizeToken = (int)nums[3];
                if (tokenCount == 7) sizeToken = (int)nums[6];
                points.Add(new PointData(new LongVector3(x, y, z), rgb, sizeToken));
            }
            return (false, points, new Dictionary<int, List<PointData>>());
        }
    }

    // Public helpers: backward-compatible signatures but use auto-detect internally.
    public static List<PointData> ParsePoints(string path)
    {
        var (isFrame, points, frames) = AutoParseFile(path);
        return points;
    }

    public static Dictionary<int, List<PointData>> ParseFrames(string path)
    {
        var (isFrame, points, frames) = AutoParseFile(path);
        return frames;
    }

    public static List<TickableSpatialObject> LoadStaticSceneToLattice(TickableSpatialLattice lattice, string csvPath, bool registerForTicks = true)
    {
        var pts = ParsePoints(csvPath);
        var objs = new List<TickableSpatialObject>();
        foreach (var p in pts)
        {
            var o = new TickableSpatialObject(p.Position);
            lattice.Insert(o);
            if (registerForTicks) o.RegisterForTicks();
            objs.Add(o);
        }
        return objs;
    }
}