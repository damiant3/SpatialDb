////////////////////////////
namespace Common.Core.Net;

// Marker types for ServiceUri<T> — each service gets a distinct type
// so the DI container can resolve the correct endpoint per client.

public sealed class OllamaApi;
public sealed class StableDiffusionApi;
public sealed class MusicGenApi;
