using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
////////////////////////////
namespace Common.Core.Net;

public sealed class ProbeResult
{
    public bool IsAvailable { get; }
    public string StatusText { get; }
    public string DetailText { get; }
    public string[] Models { get; }

    ProbeResult(bool available, string status, string detail, string[] models)
    {
        IsAvailable = available;
        StatusText = status;
        DetailText = detail;
        Models = models;
    }

    public static ProbeResult Online(string status, string detail, string[]? models = null)
        => new(true, status, detail, models ?? []);

    public static ProbeResult Offline(string reason)
        => new(false, reason, "", []);
}

/// <summary>
/// Typed URI wrapper. Registered once at DI composition and injected into
/// service clients so each service's endpoint is a distinct type.
/// The base URI always ends with <c>/</c> for correct relative resolution.
/// </summary>
public class ServiceUri<TMarker>
{
    public Uri Value { get; }

    public ServiceUri(string url)
    {
        string trimmed = url.TrimEnd('/') + "/";
        Value = new Uri(trimmed);
    }

    public ServiceUri(Uri uri) : this(uri.ToString()) { }

    public Uri Relative(string path) => new(Value, path);

    public override string ToString() => Value.ToString();
}

/// <summary>
/// Base class for HTTP service clients. Provides typed-URI resolution,
/// safe GET/POST helpers, JSON streaming, and exception-safe probing.
/// Subclasses add named API methods.
/// </summary>
public abstract class HttpServiceClient : IDisposable
{
    private readonly HttpClient m_http;
    private readonly Uri m_baseUri;

    protected HttpServiceClient(Uri baseUri, TimeSpan timeout)
    {
        m_baseUri = baseUri;
        m_http = new HttpClient { Timeout = timeout };
    }

    protected Uri BaseUri => m_baseUri;

    protected Uri Endpoint(string relativePath) => new(m_baseUri, relativePath);

    // ── Reachability (zero exceptions) ──────────────────────────

    public Task<bool> IsReachableAsync(int timeoutMs = 2000)
        => IsPortOpenAsync(m_baseUri.Host, m_baseUri.Port, timeoutMs);

    // ── GET ─────────────────────────────────────────────────────

    protected async Task<string> GetStringAsync(string path, CancellationToken ct = default)
    {
        return await m_http.GetStringAsync(Endpoint(path)
#if NET8_0_OR_GREATER
            , ct
#endif
        ).ConfigureAwait(false);
    }

    protected async Task<HttpResponseMessage> GetAsync(string path, CancellationToken ct = default)
    {
        return await m_http.GetAsync(Endpoint(path), ct).ConfigureAwait(false);
    }

    protected async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken ct = default)
    {
        return await m_http.GetAsync(uri, ct).ConfigureAwait(false);
    }

    // ── POST ────────────────────────────────────────────────────

    protected async Task<HttpResponseMessage> PostJsonAsync(
        string path, string json, CancellationToken ct = default)
    {
        StringContent content = new(json, Encoding.UTF8, "application/json");
        return await m_http.PostAsync(Endpoint(path), content, ct).ConfigureAwait(false);
    }

    // ── Streaming POST (for LLM token streaming) ────────────────

    protected async Task<Stream> PostStreamAsync(
        string path, string json, CancellationToken ct = default)
    {
        HttpRequestMessage req = new(HttpMethod.Post, Endpoint(path))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        HttpResponseMessage resp = await m_http.SendAsync(
            req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStreamAsync(
#if NET8_0_OR_GREATER
            ct
#endif
        ).ConfigureAwait(false);
    }

    // ── Download (for file retrieval) ───────────────────────────

    protected async Task DownloadToFileAsync(Uri uri, string filePath, CancellationToken ct = default)
    {
        using HttpResponseMessage resp = await m_http.GetAsync(uri,
            HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
#if NET8_0_OR_GREATER
        await using Stream src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using FileStream dst = File.Create(filePath);
#else
        using Stream src = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using FileStream dst = File.Create(filePath);
#endif
        await src.CopyToAsync(dst
#if NET8_0_OR_GREATER
            , ct
#endif
        ).ConfigureAwait(false);
    }

    // ── Socket-level reachability (truly zero exceptions) ──────

    static async Task<bool> IsPortOpenAsync(string host, int port, int timeoutMs = 2000)
    {
        return await Task.Run(() => TryConnect(host, port, timeoutMs)).ConfigureAwait(false);
    }

    static bool TryConnect(string host, int port, int timeoutMs)
    {
        Socket? socket = null;
        try
        {
            socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IAsyncResult ar = socket.BeginConnect(host, port, null, null);
            ar.AsyncWaitHandle.WaitOne(timeoutMs);
            return socket.Connected;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { socket?.Close(); } catch { /* disposal is best-effort */ }
        }
    }

    // ── Safe probe (never throws, socket pre-check avoids HTTP exceptions) ─

    public async Task<ProbeResult> ProbeAsync(string path, Func<string, ProbeResult> onSuccess)
    {
        if (!await IsReachableAsync().ConfigureAwait(false))
            return ProbeResult.Offline("Not running");

        try
        {
            using HttpResponseMessage resp = await m_http.GetAsync(Endpoint(path)).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
                return ProbeResult.Offline($"HTTP {(int)resp.StatusCode}");
            string body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return onSuccess(body);
        }
        catch (TaskCanceledException) { return ProbeResult.Offline("Timeout"); }
        catch (HttpRequestException) { return ProbeResult.Offline("Not running"); }
#pragma warning disable CA1031
        catch { return ProbeResult.Offline("Probe failed"); }
#pragma warning restore CA1031
    }

    public async Task<ProbeResult> ProbeAsync(string path, string onlineDetail = "")
    {
        if (!await IsReachableAsync().ConfigureAwait(false))
            return ProbeResult.Offline("Not running");

        try
        {
            using HttpResponseMessage resp = await m_http.GetAsync(Endpoint(path)).ConfigureAwait(false);
            return resp.IsSuccessStatusCode
                ? ProbeResult.Online("Online", onlineDetail)
                : ProbeResult.Offline($"HTTP {(int)resp.StatusCode}");
        }
        catch (TaskCanceledException) { return ProbeResult.Offline("Timeout"); }
        catch (HttpRequestException) { return ProbeResult.Offline("Not running"); }
#pragma warning disable CA1031
        catch { return ProbeResult.Offline("Probe failed"); }
#pragma warning restore CA1031
    }

    public void Dispose()
    {
        m_http.Dispose();
        GC.SuppressFinalize(this);
    }
}
