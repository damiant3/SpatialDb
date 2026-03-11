#!/usr/bin/env dotnet-script
// Measures raw I/O time vs full LatticeEmbeddingSource load time for the GGUF file.
// Run with: dotnet script diag_load.csx
// Or invoke via: dotnet run --project SparseLattice.Perf -- diagload

using System.Diagnostics;

string gguf = @"D:\Projects\SpatialDb\SparseLattice.Perf\TestData\Embeddings\sha256-970aa74c0a90ef7482477cf803618e776e173c007bf957f635f1015bfcfef0e6";
long fileSize = new FileInfo(gguf).Length;
Console.WriteLine($"File: {Path.GetFileName(gguf)}");
Console.WriteLine($"Size: {fileSize / 1024.0 / 1024.0:F1} MB");
Console.WriteLine();

// Raw sequential read — measures pure I/O bandwidth, no parsing
{
    var sw = Stopwatch.StartNew();
    using var fs = new FileStream(gguf, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
    byte[] buf = new byte[1 << 20]; // 1 MB buffer
    long total = 0;
    int read;
    while ((read = fs.Read(buf, 0, buf.Length)) > 0) total += read;
    sw.Stop();
    double mb = total / 1024.0 / 1024.0;
    double sec = sw.Elapsed.TotalSeconds;
    Console.WriteLine($"Raw sequential read:  {sec:F3}s   {mb / sec:F0} MB/s   ({mb:F0} MB read)");
}

// Read only the token_embd.weight tensor bytes (first 30522*768*2 = ~45 MB of F16 data)
{
    long embdBytes = 30522L * 768 * 2; // F16
    var sw = Stopwatch.StartNew();
    using var fs = new FileStream(gguf, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
    // Skip to approximate tensor data offset (rough: seek past header)
    byte[] buf = new byte[embdBytes];
    fs.Seek(fileSize - embdBytes - 4096, SeekOrigin.Begin); // token_embd.weight is near the start of tensor data
    fs.Read(buf, 0, buf.Length);
    sw.Stop();
    Console.WriteLine($"Raw 45 MB chunk read: {sw.Elapsed.TotalMilliseconds:F1}ms");
}
