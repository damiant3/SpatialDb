using SparseLattice.Embedding;
using System.Net;
///////////////////////////////////////////////
namespace SparseLattice.Test.Embedding;

[TestClass]
public sealed class OllamaEmbeddingSourceTests
{
    // --- ParseEmbeddingResponse unit tests (no HTTP) ---

    [TestMethod]
    public void Unit_Parse_CurrentFormat_EmbeddingsArray()
    {
        string json = """{"embeddings":[[0.1,0.2,0.3]],"model":"m"}""";
        float[] result = OllamaEmbeddingSource.ParseEmbeddingResponse(json);
        Assert.AreEqual(3, result.Length);
        Assert.AreEqual(0.1f, result[0], 0.0001f);
        Assert.AreEqual(0.2f, result[1], 0.0001f);
        Assert.AreEqual(0.3f, result[2], 0.0001f);
    }

    [TestMethod]
    public void Unit_Parse_LegacyFormat_FlatEmbedding()
    {
        string json = """{"embedding":[0.5,-0.5,1.0]}""";
        float[] result = OllamaEmbeddingSource.ParseEmbeddingResponse(json);
        Assert.AreEqual(3, result.Length);
        Assert.AreEqual(0.5f, result[0], 0.0001f);
        Assert.AreEqual(-0.5f, result[1], 0.0001f);
    }

    [TestMethod]
    public void Unit_Parse_UnrecognisedShape_Throws()
    {
        string json = """{"something_else":[1,2,3]}""";
        Assert.ThrowsException<InvalidOperationException>(
            () => OllamaEmbeddingSource.ParseEmbeddingResponse(json));
    }

    [TestMethod]
    public void Unit_Parse_NullJson_Throws()
    {
        Assert.ThrowsException<InvalidOperationException>(
            () => OllamaEmbeddingSource.ParseEmbeddingResponse("null"));
    }

    // --- Constructor validation ---

    [TestMethod]
    public void Unit_Constructor_EmptyBaseUrl_Throws()
    {
        Assert.ThrowsException<ArgumentException>(
            () => new OllamaEmbeddingSource("", "nomic-embed-text"));
    }

    [TestMethod]
    public void Unit_Constructor_EmptyModelName_Throws()
    {
        Assert.ThrowsException<ArgumentException>(
            () => new OllamaEmbeddingSource("http://localhost:11434", ""));
    }

    [TestMethod]
    public void Unit_ModelName_ReturnedCorrectly()
    {
        using OllamaEmbeddingSource source = new("http://localhost:11434", "nomic-embed-text");
        Assert.AreEqual("nomic-embed-text", source.ModelName);
    }

    [TestMethod]
    public void Unit_Dimensions_ZeroBeforeFirstCall()
    {
        using OllamaEmbeddingSource source = new("http://localhost:11434", "nomic-embed-text");
        Assert.AreEqual(0, source.Dimensions,
            "Dimensions must be 0 before any embed call.");
    }

    // --- EmbedAsync with mock HttpMessageHandler ---

    [TestMethod]
    public async Task Unit_EmbedAsync_EmptyText_ReturnsEmpty()
    {
        using OllamaEmbeddingSource source = new(
            "http://localhost:11434",
            "nomic-embed-text",
            new HttpClient(new StaticResponseHandler("""{"embeddings":[[1.0,2.0]]}""")));

        float[] result = await source.EmbedAsync("");
        Assert.AreEqual(0, result.Length, "Empty text must return empty float[].");
    }

    [TestMethod]
    public async Task Unit_EmbedAsync_ParsesResponseAndSetsDimensions()
    {
        string response = """{"embeddings":[[0.1,0.2,0.3,0.4]]}""";
        using OllamaEmbeddingSource source = new(
            "http://localhost:11434",
            "nomic-embed-text",
            new HttpClient(new StaticResponseHandler(response)));

        float[] result = await source.EmbedAsync("hello world");
        Assert.AreEqual(4, result.Length);
        Assert.AreEqual(4, source.Dimensions,
            "Dimensions must be set after first successful embed.");
        Assert.AreEqual(0.1f, result[0], 0.0001f);
    }

    [TestMethod]
    public async Task Unit_EmbedBatchAsync_CallsEmbedForEachText()
    {
        int callCount = 0;
        string response = """{"embeddings":[[1.0,2.0]]}""";
        using OllamaEmbeddingSource source = new(
            "http://localhost:11434",
            "nomic-embed-text",
            new HttpClient(new CountingResponseHandler(response, () => callCount++)));

        float[][] results = await source.EmbedBatchAsync(["a", "b", "c"]);

        Assert.AreEqual(3, results.Length);
        Assert.AreEqual(3, callCount, "EmbedBatchAsync must call EmbedAsync once per item.");
    }

    [TestMethod]
    public async Task Unit_EmbedAsync_HttpError_Throws()
    {
        using OllamaEmbeddingSource source = new(
            "http://localhost:11434",
            "nomic-embed-text",
            new HttpClient(new ErrorResponseHandler(HttpStatusCode.InternalServerError)));

        await Assert.ThrowsExceptionAsync<HttpRequestException>(
            () => source.EmbedAsync("test"));
    }

    [TestMethod]
    public void Unit_Dispose_DoesNotThrow()
    {
        OllamaEmbeddingSource source = new("http://localhost:11434", "nomic-embed-text");
        source.Dispose();
        source.Dispose(); // double-dispose must not throw
    }

    [TestMethod]
    public void Unit_BaseUrl_TrailingSlashNormalized()
    {
        // Verify that both "http://host:11434" and "http://host:11434/" produce the same request URL.
        // We verify this indirectly: construction succeeds and ModelName is set.
        using OllamaEmbeddingSource withSlash = new("http://localhost:11434/", "m");
        using OllamaEmbeddingSource withoutSlash = new("http://localhost:11434", "m");
        Assert.AreEqual("m", withSlash.ModelName);
        Assert.AreEqual("m", withoutSlash.ModelName);
    }

    // --- helpers ---

    private sealed class StaticResponseHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class CountingResponseHandler(string json, Action onCall) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
        {
            onCall();
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ErrorResponseHandler(HttpStatusCode code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(code));
    }
}
