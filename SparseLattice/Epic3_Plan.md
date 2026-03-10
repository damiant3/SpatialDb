# SparseLattice — Epic 3 Plan

## Scope and editing rules

This document covers all work from Epic 3 onward.
- All changes are restricted to `SparseLattice` and `SparseLattice.Test`.
- All other projects are locked and may only be read as style/architecture references.
- Amendments prepend to the top; archival text below is never edited.
- When a phase completes, a Checkpoint entry is prepended above the relevant phase section.

---

## Checkpoint: Epic 2 Complete

**Date:** 2026-03-10  
**Tests:** 193 passing, 0 failing

### Everything delivered in Epic 2

| Layer | File | Status |
|---|---|---|
| `IEmbeddingSource` contract | `SparseLattice/Embedding/IEmbeddingSource.cs` | ✅ |
| `OllamaEmbeddingSource` | `SparseLattice/Embedding/OllamaEmbeddingSource.cs` | ✅ |
| `LatticeIndexBuilder<TPayload>` | `SparseLattice/Embedding/LatticeIndexBuilder.cs` | ✅ |
| `SparsityReport` + `CollectSparsityReport()` | `SparseLattice/Lattice/SparsityReport.cs` | ✅ |
| `SparsityBudget` in `QuantizationOptions` | `SparseLattice/Math/EmbeddingAdapter.cs` | ✅ |
| `IdentityValidationTests` + recall harness | `SparseLattice.Test/Embedding/IdentityValidationTests.cs` | ✅ |
| Ollama unit tests (mock handler) | `SparseLattice.Test/Embedding/OllamaEmbeddingSourceTests.cs` | ✅ |
| `LatticeIndexBuilder` tests | `SparseLattice.Test/Embedding/LatticeIndexBuilderTests.cs` | ✅ |
| `SparsityReport` tests | `SparseLattice.Test/Lattice/SparsityReportTests.cs` | ✅ |
| `EmbeddingAdapterSparsityBudget` tests | `SparseLattice.Test/Math/EmbeddingAdapterSparsityBudgetTests.cs` | ✅ |

### What the identity tests prove (and do not prove) after Epic 2

**Prove:** Given `float[]` embeddings from any `IEmbeddingSource`, the frozen
`EmbeddingLattice` preserves KNN nearest-neighbor relationships after quantization
(mean recall@K ≥ 0.90 at `zeroThreshold ≤ 0.02` for real embedding models via Ollama).

**Do not prove:** That the lattice can *generate* the same `float[]` embeddings from
raw text as a reference model without Ollama running. That requires reading the GGUF
model file and executing a transformer forward pass — the core goal of Epic 3.

### What `OllamaEmbeddingSource` is

It is **test scaffolding and a ground-truth oracle**. It calls the Ollama HTTP server
to produce reference `float[]` embeddings so that recall metrics are meaningful.
It is NOT the thing being replaced — it is the measuring stick.

---

## TestData layout (established entering Epic 3)

```
SparseLattice.Test/TestData/Embeddings/
  README.md                              -- drop-zone instructions for float CSV files
  nomic-embed-text                       -- Ollama manifest JSON (model → blob mapping)
  embeddinggemma                         -- Ollama manifest JSON (model → blob mapping)
  sha256-970aa74c...  (261.6 MB)         -- GGUF blob: nomic-embed-text
  sha256-0800cbac...  (593.1 MB)         -- GGUF blob: embeddinggemma:300m-bf16
```

**Manifest → blob mapping (verified):**

| Manifest file | Model | GGUF blob | Size |
|---|---|---|---|
| `nomic-embed-text` | `nomic-embed-text` | `sha256-970aa74c...` | 261.6 MB |
| `embeddinggemma` | `embeddinggemma:300m-bf16` | `sha256-0800cbac...` | 593.1 MB |

**Why `TestData/Embeddings` and not `TestData/Models`:**
The blobs and manifests are test-only artifacts consumed exclusively by integration
tests. `TestData` is the established convention in this repo. The `SparseLattice`
library itself never references a file path directly — callers always pass a path
as a string. A `Models/` folder would only make sense if the library bundled models,
which it does not and should not.

---

## Epic 3 — Local Model Forward Pass

### Goal

Given a GGUF model file already present in `TestData/Embeddings`, produce `float[]`
embeddings from text that match Ollama's output closely enough that a frozen
`EmbeddingLattice` built from one is usable with the other. "Close enough" is
measured by cosine similarity ≥ 0.95 between local and Ollama outputs on the
same input texts.

This makes `TransformerEmbeddingSource` a true CPU-only drop-in for the Ollama
embedding endpoint, with no external process and no NuGet dependencies beyond what
already exists in the project.

---

## Phase E1 — `OllamaModelLocator`: resolve manifest → GGUF path

**File:** `SparseLattice/Gguf/OllamaModelLocator.cs`

This is the simplest phase and unblocks everything else. It reads the Ollama-format
manifest JSON files (already in `TestData/Embeddings`) and resolves a model name to
the corresponding GGUF blob file path.

```
public static class OllamaModelLocator
{
    // Scans a directory for Ollama manifest files (JSON with "layers" array).
    // Returns the full path to the GGUF blob for the given model name,
    // or null if not found.
    // modelName: e.g. "nomic-embed-text", "embeddinggemma"
    // searchDir: defaults to TestData/Embeddings relative to the test assembly
    public static string? LocateGguf(string modelName, string searchDir);

    // Reads a single manifest file and returns the digest of the
    // layer with mediaType "application/vnd.ollama.image.model".
    // Returns null if the file is not a valid manifest or has no model layer.
    public static string? ReadModelDigest(string manifestPath);

    // Returns the blob file path for a given digest in a directory.
    // Blob files are named "sha256-{hex}" (no extension).
    // Returns null if no matching file exists.
    public static string? ResolveBlob(string digest, string blobDir);
}
```

**Manifest format (as seen in TestData/Embeddings):**
```json
{
  "schemaVersion": 2,
  "layers": [
    {
      "mediaType": "application/vnd.ollama.image.model",
      "digest": "sha256:970aa74c...",
      "size": 274290656
    }
  ]
}
```
Note: digest in manifest uses colon separator (`sha256:abc...`);
blob file on disk uses hyphen (`sha256-abc...`). The locator handles this.

**Tests:**
- Unit: parse a hand-crafted manifest JSON string, assert correct digest extraction.
- Unit: given a directory containing a manifest and a blob file, assert correct
  path resolution for a matching model name.
- Unit: assert null returned for missing model name and missing blob file.
- Integration: resolve `"nomic-embed-text"` against the real `TestData/Embeddings`
  directory. Assert returned path ends with `sha256-970aa74c...` and file exists.
  `[TestCategory("Integration")]` — skipped if `TestData/Embeddings` has no manifests.

---

## Phase E2 — `GgufReader`: parse GGUF file format

**File:** `SparseLattice/Gguf/GgufReader.cs`

GGUF is the native model format used by Ollama and llama.cpp. It is a documented
open binary format. The full spec is at:
https://github.com/ggerganov/ggml/blob/master/docs/gguf.md

### GGUF binary layout

```
[4 bytes]  Magic: 0x47 0x47 0x55 0x46  ("GGUF")
[4 bytes]  Version: uint32 (must be 2 or 3)
[8 bytes]  TensorCount: uint64
[8 bytes]  MetadataKVCount: uint64

-- Metadata key-value pairs (MetadataKVCount entries) --
  [string]   Key
  [uint32]   ValueType  (0=uint8, 1=int8, 2=uint16, 3=int16, 4=uint32, 5=int32,
                         6=float32, 7=bool, 8=string, 9=array, 10=uint64, 11=int64,
                         12=float64)
  [value]    Value (format depends on ValueType)

-- Tensor info table (TensorCount entries) --
  [string]   Name
  [uint32]   NDimensions
  [uint64 x NDimensions]  Shape
  [uint32]   DType  (0=F32, 1=F16, 2=Q4_0, 3=Q4_1, 6=Q5_0, 7=Q5_1, 8=Q8_0 ...)
  [uint64]   FileOffset  (offset from start of tensor data section, 32-byte aligned)

-- Padding to 32-byte alignment --
-- Tensor data --
```

Strings are encoded as: `[uint64 length][utf8 bytes]` (no null terminator).

### API

```csharp
// SparseLattice/Gguf/GgufReader.cs
public sealed class GgufReader : IDisposable
{
    public static GgufReader Open(string path);

    // Top-level metadata
    public uint Version { get; }
    public string Architecture { get; }          // general.architecture
    public string ModelName { get; }             // general.name (or filename if absent)
    public int EmbeddingLength { get; }          // {arch}.embedding_length
    public int ContextLength { get; }            // {arch}.context_length
    public int HeadCount { get; }                // {arch}.attention.head_count
    public int LayerCount { get; }               // {arch}.block_count
    public int FeedForwardLength { get; }        // {arch}.feed_forward_length

    // Full metadata access
    public IReadOnlyDictionary<string, GgufValue> Metadata { get; }

    // Tokenizer data (populated from tokenizer.ggml.* metadata)
    public IReadOnlyList<string> Tokens { get; }
    public IReadOnlyList<string> Merges { get; }   // BPE merge rules, "a b" format
    public IReadOnlyList<int> TokenTypes { get; }
    public int BosTokenId { get; }
    public int EosTokenId { get; }
    public int UnkTokenId { get; }

    // Tensor access
    public IReadOnlyList<GgufTensorInfo> TensorInfos { get; }
    public bool HasTensor(string name);
    public GgufTensorInfo GetTensorInfo(string name);

    // Read a tensor as float32 (dequantizes Q4/Q8 automatically)
    public float[] ReadTensorF32(string name);

    // Read a 2D tensor as a row-major float matrix [rows, cols]
    public float[,] ReadTensorF32Matrix(string name);

    public void Dispose();
}

public sealed class GgufTensorInfo
{
    public string Name { get; }
    public int[] Shape { get; }      // e.g. [768, 768] for a weight matrix
    public GgufDType DType { get; }
    public long FileOffset { get; }  // offset from start of tensor data section
    public long ElementCount { get; }
    public long ByteCount { get; }
}

public enum GgufDType
{
    F32 = 0,
    F16 = 1,
    Q4_0 = 2,
    Q4_1 = 3,
    Q5_0 = 6,
    Q5_1 = 7,
    Q8_0 = 8
}

public sealed class GgufValue
{
    public GgufValueType Type { get; }
    // Typed accessors — throw InvalidOperationException if type mismatch
    public uint AsUInt32();
    public int AsInt32();
    public long AsInt64();
    public ulong AsUInt64();
    public float AsFloat32();
    public bool AsBool();
    public string AsString();
    public IReadOnlyList<GgufValue> AsArray();
}
```

### Dequantization (required for Q4_0 and Q8_0 — the formats used by nomic-embed-text)

**Q8_0:** each block of 32 elements has 1 float16 scale + 32 int8 values.
  `float[i] = scale * (float)int8[i]`

**Q4_0:** each block of 32 elements has 1 float16 scale + 16 bytes (two 4-bit values per byte).
  `float[i] = scale * (float)(nibble[i] - 8)`  (nibbles are unsigned, center at 8)

**F16:** stored as IEEE 754 half-precision. Convert via `BitConverter` + manual
  sign/exponent/mantissa extraction or `System.Half` (available in .NET 5+).

**Tests:**
- Unit: parse a hand-crafted minimal GGUF byte sequence (magic + version + 1 metadata
  entry + 1 F32 tensor). Assert all fields round-trip correctly.
- Unit: Q8_0 dequantization: hand-crafted block, assert output matches expected floats.
- Unit: Q4_0 dequantization: hand-crafted block, assert output matches expected floats.
- Unit: F16 conversion: known half-precision bytes → expected float32 values.
- Integration: open `sha256-970aa74c...` (nomic-embed-text GGUF). Assert:
  - `Architecture == "nomic-bert"` (or `"bert"`)
  - `EmbeddingLength == 768`
  - `Tokens.Count > 30000`
  - `TensorInfos.Count > 100`
  - `[TestCategory("Integration")]` — skipped if blob not found.

---

## Phase E3 — `BpeTokenizer`: tokenize text from GGUF vocab

**File:** `SparseLattice/Gguf/BpeTokenizer.cs`

Most Ollama embedding models use Byte Pair Encoding (BPE) tokenization. The complete
vocabulary and merge rules are stored inside the GGUF file in the metadata.

### BPE algorithm (as used by nomic-embed-text / GPT-style models)

1. Pre-tokenize: split input into words on whitespace/punctuation boundaries.
2. Byte-encode: convert each character to its UTF-8 byte representation using the
   GPT-2 byte-to-unicode mapping (maps bytes 0-255 to printable unicode characters).
3. For each word: initialize the token sequence as individual characters.
4. Repeatedly find the highest-priority merge rule that applies to any adjacent pair.
   Apply it (merge the pair into one token). Repeat until no merge rules apply.
5. Look up each final token in the vocabulary to get its token ID.
6. Prepend BOS token ID and append EOS token ID if `addSpecialTokens = true`.

### API

```csharp
// SparseLattice/Gguf/BpeTokenizer.cs
public sealed class BpeTokenizer
{
    public static BpeTokenizer FromGguf(GgufReader reader);

    public int VocabSize { get; }
    public int BosTokenId { get; }
    public int EosTokenId { get; }
    public int UnkTokenId { get; }

    // Returns token IDs. Includes BOS and EOS when addSpecialTokens=true.
    public int[] Encode(string text, bool addSpecialTokens = true);

    // Decodes token IDs back to string (for verification only).
    public string Decode(IReadOnlyList<int> tokenIds);
}
```

**Tests:**
- Unit: tokenize `"hello world"` against a minimal hand-built vocab + merge table.
  Assert exact token ID sequence.
- Unit: round-trip `Encode` → `Decode` returns original string (modulo BPE
  whitespace prefix handling).
- Unit: BOS token is first element, EOS token is last element when
  `addSpecialTokens = true`.
- Integration: load tokenizer from `sha256-970aa74c...`, tokenize
  `"public static void Main"`. Assert `tokenIds.Length > 0` and
  `tokenIds[0] == BosTokenId`. `[TestCategory("Integration")]`.

---

## Phase E4 — `TransformerEmbeddingSource`: forward pass on CPU

**File:** `SparseLattice/Gguf/TransformerEmbeddingSource.cs`

This is the core of Epic 3. It implements `IEmbeddingSource` using only
`SparseLattice/Gguf/GgufReader.cs`, `SparseLattice/Gguf/BpeTokenizer.cs`,
and `System.Numerics` (SIMD via `Vector<float>`). No new NuGet packages.

### Supported architectures in Phase E4

| Architecture | Model | GGUF `general.architecture` | Status |
|---|---|---|---|
| `nomic-bert` | `nomic-embed-text` | `"nomic-bert"` | **Primary target** |
| `bert` | standard BERT encoders | `"bert"` | Secondary target |

Causal models (llama, mistral, gemma) use a different attention pattern and
different pooling. They are deferred — see Phase E5.

### BERT/nomic-bert tensor naming convention (in GGUF)

```
token_embd.weight                        -- [vocab_size, n_embd]          token embeddings
position_embd.weight                     -- [n_ctx, n_embd]               position embeddings
token_types.weight                       -- [2, n_embd]                   token type embeddings
blk.{i}.attn_norm.weight                 -- [n_embd]                      pre-attention LayerNorm scale
blk.{i}.attn_norm.bias                   -- [n_embd]                      pre-attention LayerNorm bias
blk.{i}.attn_q.weight                    -- [n_embd, n_embd]              query projection
blk.{i}.attn_q.bias                      -- [n_embd]
blk.{i}.attn_k.weight                    -- [n_embd, n_embd]              key projection
blk.{i}.attn_k.bias                      -- [n_embd]
blk.{i}.attn_v.weight                    -- [n_embd, n_embd]              value projection
blk.{i}.attn_v.bias                      -- [n_embd]
blk.{i}.attn_output.weight               -- [n_embd, n_embd]              attention output projection
blk.{i}.attn_output.bias                 -- [n_embd]
blk.{i}.ffn_norm.weight                  -- [n_embd]                      post-attention LayerNorm scale
blk.{i}.ffn_norm.bias                    -- [n_embd]
blk.{i}.ffn_up.weight                    -- [n_ff, n_embd]                FFN first layer
blk.{i}.ffn_up.bias                      -- [n_ff]
blk.{i}.ffn_down.weight                  -- [n_embd, n_ff]                FFN second layer
blk.{i}.ffn_down.bias                    -- [n_embd]
output_norm.weight                       -- [n_embd]                      final LayerNorm scale
output_norm.bias                         -- [n_embd]
```

### Forward pass (BERT encoder)

```
Input text
  → BpeTokenizer.Encode()                                 → int[] tokenIds  (length T)
  → token_embd[tokenId] + position_embd[pos]             → float[T, n_embd]
    + token_types_embd[0] (all tokens are type 0)
  → LayerNorm (output_norm equivalent at input for nomic-bert)

For i = 0 .. n_layers-1:
  residual = x
  x = LayerNorm(x, blk.i.attn_norm)
  [Q, K, V] = x @ [Wq, Wk, Wv] + [bq, bk, bv]   -- shape [T, n_embd] each
  reshape Q, K, V to [T, n_head, head_dim]
  scores = Q @ K^T / sqrt(head_dim)               -- shape [T, n_head, T]
  scores = softmax(scores, dim=-1)
  attn_out = scores @ V                            -- shape [T, n_head, head_dim]
  attn_out = reshape to [T, n_embd]
  attn_out = attn_out @ W_out + b_out
  x = residual + attn_out

  residual = x
  x = LayerNorm(x, blk.i.ffn_norm)
  x = x @ W_up + b_up
  x = GELU(x)
  x = x @ W_down + b_down
  x = residual + x

x = LayerNorm(x, output_norm)                     -- final layer norm

// Mean pooling: average x over all non-padding token positions
pooled = mean(x[1 : T-1], axis=0)                 -- exclude BOS/EOS

// L2 normalize
pooled = pooled / ||pooled||_2

return pooled                                      -- float[] of length n_embd
```

### API

```csharp
// SparseLattice/Gguf/TransformerEmbeddingSource.cs
public sealed class TransformerEmbeddingSource : IEmbeddingSource, IDisposable
{
    // Loads from a GGUF file path directly.
    public static TransformerEmbeddingSource Load(string ggufPath);

    // Convenience: resolve model name via OllamaModelLocator,
    // load from TestData/Embeddings or a custom directory.
    public static TransformerEmbeddingSource LoadFromModelDir(
        string modelName, string modelDir);

    public string ModelName { get; }    // from GGUF general.name metadata
    public int Dimensions { get; }      // from GGUF {arch}.embedding_length

    public Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
    public Task<float[][]> EmbedBatchAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);

    public void Dispose();
}
```

### Performance notes (correctness first, then optimize)

- All quantized tensors (Q4_0, Q8_0) are dequantized to `float[]` on load into memory.
  For `nomic-embed-text` at 768 dimensions, this is ~261 MB of weights in float32 —
  approximately 1 GB resident. Acceptable for a dev/test workload.
- Matrix multiply uses `System.Numerics.Vector<float>` (SIMD, 128–512 bit depending
  on CPU). No BLAS dependency.
- Batch processing runs texts sequentially (no parallelism in Phase E4).
- Future optimization: keep weights in F16 and dequantize per-layer. Deferred.

**Tests:**
- Unit: construct a `TransformerEmbeddingSource` from a minimal hand-crafted 1-layer
  1-head 4-dim BERT model (all weights set to identity/zero/known values). Assert
  that `EmbedAsync("a")` returns a vector of the expected shape and values.
  No GGUF file required — tests use an internal constructor that accepts pre-loaded
  weights directly.
- Integration: load `nomic-embed-text` from `TestData/Embeddings`. Embed
  `"public static void Main"`. Assert `result.Length == 768` and vector is L2-normalized.
  `[TestCategory("Integration")]`.

---

## Phase E5 — Cross-validation: local vs Ollama

**File:** `SparseLattice.Test/Embedding/ModelFidelityTests.cs`

This is the definitive test that answers: "does our local forward pass match Ollama?"

```csharp
[TestClass]
class ModelFidelityTests
{
    [TestMethod]
    [TestCategory("Integration")]
    public async Task Integration_NomicEmbedText_LocalMatchesOllama_CosineAbove095()
    // For each text in BuildCodeSnippetCorpus() (20 items):
    //   local[]  = TransformerEmbeddingSource.EmbedAsync(text)
    //   remote[] = OllamaEmbeddingSource.EmbedAsync(text)
    //   cosine   = dot(local, remote) / (||local|| * ||remote||)
    // Assert mean cosine >= 0.95.
    // Write per-text similarity to test output.
    // Skipped if GGUF not found or Ollama not reachable.

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Integration_NomicEmbedText_LocalLattice_RecallVsOllamaBrute()
    // Build lattice from local embeddings.
    // Run queries using Ollama embeddings as query vectors.
    // Assert mean recall@10 >= 0.90.
    // Skipped if GGUF not found or Ollama not reachable.

    [TestMethod]
    [TestCategory("Integration")]
    public async Task Integration_EmbeddingGemma_LocalMatchesOllama_CosineAbove095()
    // Same as above but for embeddinggemma:300m-bf16 (sha256-0800cbac..., 593 MB).
    // Architecture is likely "bert" or custom — detected from GGUF metadata.
    // Skipped if GGUF not found or Ollama not reachable.
}
```

---

## Phase E6 — Causal model support (deferred)

`embeddinggemma:300m-bf16` uses a Gemma architecture (causal, not encoder-only).
It likely requires:
- Rotary positional embeddings (RoPE) instead of learned absolute positions
- Causal (masked) self-attention
- Different pooling strategy (last non-padding token or mean of all)
- Different tensor naming conventions in the GGUF

This is deferred to Phase E6 once Phase E4 (BERT/nomic-bert) is validated. The
`TransformerEmbeddingSource` will dispatch to architecture-specific implementations
detected from `GgufReader.Architecture`.

---

## Checkpoint: Epic 3 — E3/E4 Progress

**Date:** 2026-03-10  
**Tests:** 237 passing, 0 failing

### Summary
This checkpoint records work completed through Phase E3 (tokenizers) and the initial
implementation of Phase E4 (CPU forward pass). It documents deviations discovered
while integrating real GGUF blobs (nomic-embed-text) so future work and tuning
can proceed with full context.

### Delivered in E3/E4 (high-level)
| Item | File(s) | Status |
|---|---:|---:|
| `OllamaModelLocator` (E1) | `SparseLattice/Gguf/OllamaModelLocator.cs` | ✅ (earlier)
| `GgufReader` (E2)         | `SparseLattice/Gguf/GgufReader.cs`       | ✅ (earlier)
| `BpeTokenizer` (E3)      | `SparseLattice/Gguf/BpeTokenizer.cs`     | ✅
| `WordPieceTokenizer` (E3 addendum)
| `TransformerEmbeddingSource` (E4) | `SparseLattice/Gguf/WordPieceTokenizer.cs`<br/>`SparseLattice/Gguf/TransformerEmbeddingSource.cs` | ✅
| Unit + Integration tests added | `SparseLattice.Test/Gguf/*` (BPE + WordPiece + E4 forward-pass tests) | ✅ (237 green)

### Key discoveries / deviations from Epic 3 plan
- Tokenizer type: `nomic-embed-text` uses a BERT-style WordPiece tokenizer
  (`tokenizer.ggml.model = "bert"`), not GPT-2 BPE. To support the model we
  implemented `WordPieceTokenizer` in addition to the original `BpeTokenizer`.
- Token embedding layout: `token_embd.weight` is stored with GGUF shape
  `[n_embd, vocab_size]`. The load path yields a flat array where each token's
  vector is contiguous, allowing direct lookup by `tokenId * n_embd`.
- `token_types.weight` layout: shape `[n_embd, 2]` (GGUF shape[0]=n_embd,
  shape[1]=2) produces an interleaved flat layout. We extract the type-0 vector
  by reading the even-indexed elements (offset `d*2`) when constructing the
  token-type embedding for type 0.
- Fused QKV: attention projection is a single tensor `blk.{i}.attn_qkv.weight`
  with shape `[n_embd, 3*n_embd]`. We split the output into Q, K, V after the
  matrix multiply instead of expecting separate Q/K/V tensors.
- RoPE (rotary) positional embeddings: nomic-bert uses RoPE rather than learned
  absolute position embeddings. We implemented a RoPE application for Q/K using
  `rope.freq_base` metadata (observed value `1000` in the blob).
- Gated FFN: blocks contain `ffn_up`, `ffn_gate`, and `ffn_down` (SwiGLU/GeGLU).
  The correct computation is `ffn_out = (GELU(gate) * up) @ W_down`.
- LayerNorm placement and names: nomic-bert uses `token_embd_norm` at input and
  per-block norms named `attn_output_norm` and `layer_output_norm`. We matched
  their semantics when wiring the forward pass.
- Quantized / F16 storage: many weight tensors are F16 or quantized in the
  GGUF. `GgufReader` dequantizes to `float[]` on load; for correctness we
  currently hold weights in float32 (tradeoff: correctness and simplicity vs.
  memory footprint (~1 GB for nomic-embed-text)).
- Performance vs correctness: initial implementation focuses on correctness.
  Matrix multiplies use `System.Numerics.Vector<float>` for SIMD. Further
  optimization (blocked GEMM, keeping weights in F16 or dequantizing lazily)
  is planned for E4 profiling and E5 optimization.

### Practical implications / next steps
- E4 follow-up: profile and optimize matrix kernels (SIMD blocking, cache
  friendly layout) and reduce working set (store F16 and dequantize per-layer
  or implement streaming dequantization to avoid 1 GB resident footprint).
- E5: cross-validate local forward pass vs. Ollama outputs (cosine ≥ 0.95 target)
  using the existing integration tests. Tweak numerical details (RoPE scaling,
  LayerNorm epsilon) to increase fidelity where necessary.
- E6 (deferred): causal models (Gemma / Llama) require separate attention
  patterns (masked attention), RoPE handling differences, and pooling changes.

---

## Implementation order for Epic 3

1. **`OllamaModelLocator` (E1)** — independent, small, unblocks E2/E4 integration tests.
2. **`GgufReader` (E2)** — foundation. Everything else depends on it.
3. **`BpeTokenizer` (E3)** — depends on `GgufReader`.
4. **`TransformerEmbeddingSource` (E4)** — depends on E1 + E2 + E3.
5. **Cross-validation tests (E5)** — depends on E4 + `OllamaEmbeddingSource`.
6. **Causal model support (E6)** — deferred until E5 validates BERT path.

---

## Deferred from Epic 2 (carried forward)

| Item | Status |
|---|---|
| Phase 8 — BenchmarkDotNet | Deferred until E4 forward pass exists and can be profiled |
| Phase 9 — XML docs | Deferred until E4 API surface stabilizes |
| Generator wiring (`IOptimizedPartitioner` → builder) | Deferred until profiling shows partition is a bottleneck |

---

## Resolved open questions

| ID | Question | Resolution |
|---|---|---|
| OQ-1 | Ollama-first vs ONNX-first | Resolved: GGUF direct (no ONNX runtime). Epic 3 reads GGUF natively. |
| OQ-2 | Target recall@K threshold | Resolved: ≥ 0.90 for lattice KNN recall; ≥ 0.95 cosine for forward-pass fidelity |
| OQ-3 | `SparsityBudget` default | Resolved: null (no cap); tune per-model during E5 experiments |
| OQ-4 | BenchmarkDotNet location | Resolved: `SparseLattice.Test/Benchmarks/` folder, same project |
| OQ-5 | Tokenizer for model path | Resolved: bundled BPE from GGUF vocab — no external library needed |
