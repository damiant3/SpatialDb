# SparseLattice — Epic 2 Plan

## Scope and editing rules

This document supersedes the active-work sections of `AGENT_PLAN.md` for all new phases.
- All changes are restricted to `SparseLattice` and `SparseLattice.Test`.
- All other projects are locked and may only be read as style/architecture references.
- Amendments to this doc follow the same rule as `AGENT_PLAN.md`: new sections prepend to the top; archival text below is never edited.
- When a phase completes, a Checkpoint entry is prepended above the relevant phase section.

---

## State of the project entering Epic 2

### What exists (all tested, 147 tests, 0 failures)

| Layer | File | Capability |
|---|---|---|
| Math | `LongVectorN.cs` | N-dim int64 vector, overflow-safe BigInteger ops |
| Math | `SparseVector.cs` | Sorted sparse tuple, O(nnz) merge distances (L1 + L2) |
| Math | `HyperRegion.cs` | N-dim bounding box, hypersphere intersection |
| Math | `EmbeddingAdapter.cs` | `float[]` → `SparseVector`/`LongVectorN` quantizer; `QuantizationOptions` |
| Lattice | `Nodes.cs` | `SparseBranchNode`, `FrozenBranchNode` (compact array, tag-mask), `SparseLeafNode<T>`, `SparseOccupant<T>`, `SparseTreeStats` |
| Lattice | `EmbeddingLatticeBuilder.cs` | Variance-pivot build, in-place partition, `LatticeOptions` |
| Lattice | `EmbeddingLattice<TPayload>` | `QueryWithinDistanceL2/L1`, `QueryKNearestL2/L1` (max-heap pruning), `Freeze()` (idempotent guard), `CollectStats()` |
| Lattice | `SparseLatticeSerializer.cs` | Deterministic binary save/load, `IPayloadSerializer<T>`, built-in `string`/`int` serializers |
| Lattice | `RecallEvaluator.cs` | Brute-force KNN (L2+L1), `EvaluateL2/L1`, `AggregateL2`, `RecallResult`, `AggregateRecallStats` |
| Generator | `IOptimizedPartitioner.cs` | Contract + `PartitionerDescriptor` for Roslyn-generated partitioners |
| Generator | `SparseLatticeCodeGenerator.cs` | Emits C# source for dimension-specialized unrolled partitioner |
| Generator | `GeneratedPartitionerLoader.cs` | Compiles via Roslyn, loads into collectible ALC, disposable for clean unload |

### What is missing / deferred entering Epic 2

| Item | Status |
|---|---|
| Phase 5 adapter (full) | Partially done: quantizer exists but no model ingestion, no identity-of-function validation |
| `IEmbeddingSource` contract | Not yet defined — the abstraction that decouples lattice from how embeddings are produced |
| Ollama HTTP embedding source | Not yet implemented |
| ONNX direct embedding source | Not yet implemented |
| Diagnostics / sparsity reporting | Not yet implemented |
| Per-threshold recall sweep harness | `RecallEvaluator` exists but no harness that sweeps `zeroThreshold` or nnz |
| Phase 8 benchmarks | Deferred — no BenchmarkDotNet wiring yet |
| Phase 9 XML docs | Not yet added to public API |
| `IOptimizedPartitioner` wired into builder | Generator exists but builder still uses generic runtime path |
| Phase 6 (original map-reduce) | Deprecated — see `AGENT_PLAN.md` amendment |

---

## Open questions and decisions entering Epic 2

### OQ-1 — Embedding source: Ollama HTTP vs ONNX direct

**Context from original conversation:**
> *"maybe we find code to run the embedding model directly, bypass ollama? I don't know what would be easier."*

**Analysis:**

| Approach | Pros | Cons |
|---|---|---|
| **Ollama HTTP** (`api/embed` endpoint) | No extra NuGet deps; reuses the server already running; model choice is dynamic; works for any model Ollama supports | Requires Ollama running; HTTP round-trip latency; not suitable for unit tests without a mock |
| **ONNX direct** (`Microsoft.ML.OnnxRuntime`) | CPU-only, zero external process; testable offline; exact float output; matches the "bypass GPU" vision from the original conversation | Requires ONNX model file on disk; tokenizer must be provided separately; larger NuGet surface |

**Recommendation:** implement **both** behind a common `IEmbeddingSource` contract. The Ollama client is simpler to start with and exercises the quantization pipeline immediately. The ONNX path is the long-term production path and aligns with the original goal ("keep LLM resident on GPU, do all embedding work on CPU/RAM").

The two are interchangeable because they both produce `float[]` — only the `IEmbeddingSource` implementation changes. Switching is one line of code for the caller.

**Decision needed:** do you want to start with Ollama (simpler, needs server) or ONNX (self-contained, needs model file)? Both are planned below. Leave `OQ-1` open until you confirm the starting order.

### OQ-2 — Identity-of-function validation strategy

**Context:**
> *"we could ingest an encoding model, and should be able to produce identical outputs as original model, basically. the idea being that our system is fully able to produce that identity of function as a drop in replacement."*

**What this means precisely:**
Given the same input text, `EmbeddingLattice.QueryKNearestL2(Quantize(embed(text)), k)` should return the same Top-K neighbors as a cosine-similarity query against the same corpus of raw float embeddings.

This is a **recall@K equivalence** test, not bit-exact equality (quantization is lossy by design). The goal is recall@10 ≥ 0.90 for typical embedding workloads. The existing `RecallEvaluator` is exactly the right harness for this.

**Implementation plan:** build a small `EmbeddingModelValidator` that:
1. Accepts a corpus of `(text, label)` pairs.
2. Uses `IEmbeddingSource` to embed all items.
3. Quantizes via `EmbeddingAdapter`.
4. Builds and freezes an `EmbeddingLattice`.
5. For a set of query texts, compares lattice KNN results against float cosine brute-force.
6. Reports `AggregateRecallStats` for various `zeroThreshold` settings.

### OQ-3 — Diagnostics granularity

**Context:**
> *"some diagnostics, so we can see how sparse the data is, if the max-realized-dimensions or occupants per venue is over/under allocated"*

**Existing:** `SparseTreeStats` captures `TotalNodes`, `BranchNodes`, `LeafNodes`, `TotalOccupants`, `MaxDepth`, `AverageLeafOccupancy`.

**Missing:**
- Sparsity profile: histogram of nnz counts across all occupants
- Dimension coverage: which dimensions are actually populated, how many
- Per-leaf occupancy distribution (min/max/stddev, not just mean)
- Branch balance: ratio of nodes with both children vs single child vs leaf

These belong in `SparseTreeStats` as an extended `DetailedStats` option or a separate `SparsityReport`.

---

## Epic 2 phases

---

### Phase 5 — Model ingestion and identity-of-function (NEW — see above)

**Goal:** Make `SparseLattice` a functional drop-in for a vector similarity query against an embedding model. Given a corpus embedded with any standard model, lattice KNN recall@K should match cosine brute-force at ≥ 0.90.

#### 5a — `IEmbeddingSource` contract

Define the single abstraction that all embedding sources implement.

```
IEmbeddingSource
  string ModelName { get; }
  int Dimensions { get; }
  Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
  Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
```

**File:** `SparseLattice/Embedding/IEmbeddingSource.cs`

**Why `float[][]` batch:** models amortize tokenization cost over batches; single calls in a loop are 5–10× slower for ONNX and incur one HTTP round-trip per item for Ollama.

#### 5b — Ollama HTTP embedding source

```
OllamaEmbeddingSource : IEmbeddingSource
  ctor(string baseUrl, string modelName, HttpClient? httpClient = null)
  // POST api/embed, parses embeddings array
  // Batch: serial loop (Ollama does not expose true batch endpoint as of 2025)
```

**File:** `SparseLattice/Embedding/OllamaEmbeddingSource.cs`

NuGet needed: none — uses `System.Net.Http.HttpClient` (already in `net8.0`) and `System.Text.Json`.

**Tests:** unit tests with a mock `HttpMessageHandler`; integration tests skipped by default (require live Ollama, marked `[TestCategory("Integration")]`).

#### 5c — ONNX direct embedding source

```
OnnxEmbeddingSource : IEmbeddingSource
  ctor(string modelPath, string tokenizerVocabPath, OnnxEmbeddingOptions? options = null)
  // Loads InferenceSession from file
  // Tokenizes text (WordPiece/BPE via BertTokenizerFast or a bundled simple tokenizer)
  // Runs mean-pool of last hidden state
```

**File:** `SparseLattice/Embedding/OnnxEmbeddingSource.cs`

NuGet needed: `Microsoft.ML.OnnxRuntime` (CPU package; ~30MB).

Compatible models: any ONNX-exported sentence transformer (e.g., `nomic-embed-text`, `all-MiniLM-L6-v2`). The model file is external — not bundled in the repo. The source reads from a file path.

**Tests:** pure unit tests mock the session; integration tests require model file, skipped by default.

#### 5d — `LatticeIndexBuilder` — end-to-end corpus builder

```
LatticeIndexBuilder<TPayload>
  ctor(IEmbeddingSource source, QuantizationOptions? options = null)
  Task<EmbeddingLattice<TPayload>> BuildAsync(
      IReadOnlyList<(string text, TPayload payload)> corpus,
      LatticeOptions? latticeOptions = null,
      CancellationToken ct = default)
```

**File:** `SparseLattice/Embedding/LatticeIndexBuilder.cs`

Embeds all items in batch, quantizes, builds, freezes, and returns the frozen lattice. Single call from consumer code.

#### 5e — `EmbeddingModelValidator` — recall harness

```
EmbeddingModelValidator
  static Task<ValidationReport> ValidateAsync(
      IEmbeddingSource source,
      IReadOnlyList<(string text, string label)> corpus,
      IReadOnlyList<string> queries,
      int k,
      float[] zeroThresholds,        // e.g., [0.001f, 0.005f, 0.01f, 0.02f, 0.05f]
      CancellationToken ct = default)
```

`ValidationReport` contains per-threshold `AggregateRecallStats`, sparsity profile, and dimension coverage — the research output described in Phase 10.

**File:** `SparseLattice/Embedding/EmbeddingModelValidator.cs`

**Tests:** deterministic synthetic corpus (no model required); integration tests with Ollama marked `[TestCategory("Integration")]`.

---

### Phase 5D (diagnostics) — Extended sparsity and tree diagnostics

**Goal:** Answer "is the lattice over/under-allocated relative to the data?" at a glance.

#### New `SparsityReport` type

```
SparsityReport
  // Per-occupant nnz distribution
  int MinNnz
  int MaxNnz
  double MeanNnz
  double StdDevNnz
  int[] NnzHistogram         // bucket counts: 0, 1-4, 5-8, 9-16, 17-32, 33-64, 65+
  
  // Dimension coverage
  int TotalDimensions
  int RealizeddDimensions    // how many distinct dimensions appear across all occupants
  double DimensionCoverage   // RealizeddDimensions / TotalDimensions
  int[] TopActiveDimensions  // top-10 most-populated dimensions by occupant count
  
  // Per-leaf occupancy
  int MinLeafOccupancy
  int MaxLeafOccupancy
  double StdDevLeafOccupancy
  
  // Branch balance
  int BothChildrenRealized   // branch nodes with both Below and Above
  int OneChildRealized       // branch nodes with one side missing
  double BranchBalanceRatio  // BothChildrenRealized / TotalBranchNodes
```

**File:** `SparseLattice/Lattice/SparsityReport.cs`

`EmbeddingLattice<TPayload>` gains a new method:

```
SparsityReport CollectSparsityReport()
```

`SparseTreeStats` stays unchanged (compact summary); `SparsityReport` is the full diagnostic view.

**Tests:** verify histogram bucket boundaries, dimension coverage, branch balance on crafted inputs.

---

### Phase 8 — Benchmarks (deferred but planned)

**Trigger:** implement after Phase 5 embedding sources are working and a real model can produce a corpus.

**Plan:**
- Add `BenchmarkDotNet` to `SparseLattice.Test` (or a new `SparseLattice.Benchmarks` project).
- Benchmark `Build` time vs corpus size (100, 1000, 10000 items).
- Benchmark `QueryKNearestL2` latency vs corpus size and `k`.
- Benchmark `Quantize` throughput for batch embedding.
- Smoke test: assert `QueryKNearestL2` for 10k item corpus with k=10 < 10ms single-threaded.
- Do **not** add BenchmarkDotNet to the main `SparseLattice` library — benchmarks live in test/benchmarks only.

---

### Phase 9 — XML docs (deferred but planned)

**Trigger:** implement after Phase 5 API surface stabilizes.

Public types that need XML docs:
- `EmbeddingLattice<TPayload>` (all public methods)
- `EmbeddingAdapter`, `QuantizationOptions`
- `SparseVector`, `SparseEntry`, `LongVectorN`, `HyperRegion`
- `SparseLatticeSerializer`, `IPayloadSerializer<T>`
- `RecallEvaluator`, `RecallResult`, `AggregateRecallStats`
- `IEmbeddingSource`, `LatticeIndexBuilder`, `EmbeddingModelValidator` (Phase 5 additions)

---

### Phase 10 — Research experiments (active after Phase 5)

Recall sweep harness already implemented in `EmbeddingModelValidator` (Phase 5e). When a real model is available:

1. Run `ValidateAsync` against a small code snippet corpus (20–50 functions) with `nomic-embed-text` via Ollama.
2. Sweep `zeroThreshold` from `0.001` to `0.1`.
3. Record: mean recall@10, mean nnz per vector, build time, query latency.
4. Expected result: recall ≥ 0.90 at `zeroThreshold ≤ 0.02` for typical embedding models.
5. If recall drops significantly above `0.01`, consider a top-K nnz budget (retain only the K largest-magnitude dimensions regardless of threshold).

**Top-K nnz budget:** add `int? SparsityBudget` to `QuantizationOptions` — if set, after threshold filtering, keep only the `SparsityBudget` dimensions with the largest absolute values. This is a one-line change to `EmbeddingAdapter.Quantize` and a large practical improvement for very sparse models.

---

### Generator wiring (deferred Phase 3 remainder)

**Status:** `IOptimizedPartitioner`, `SparseLatticeCodeGenerator`, and `GeneratedPartitionerLoader` exist and are tested. The builder still uses the generic runtime partition path.

**Remaining work:** wire `GeneratedPartitionerLoader` into `EmbeddingLatticeBuilder` — when `LatticeOptions.Dimensions` is provided, compile and cache a generated partitioner for that dimensionality, and use it on the hot-path in `PartitionInPlace`. The builder falls back to the generic path when the generated loader is not available.

**Trigger for implementation:** after Phase 5 embeds real vectors and a profiling run shows the partition loop is a measurable bottleneck. Do not optimize prematurely.

---

## Immediate next actions (in order)

1. **Define `IEmbeddingSource` + `OllamaEmbeddingSource` (5a + 5b)** — this unblocks everything else. Tests use a mock handler.
2. **`LatticeIndexBuilder` (5d)** — the end-to-end glue. Tests with a mock source.
3. **`SparsityReport` + `CollectSparsityReport()` (Phase 5D)** — diagnostic tooling. Tests use crafted trees.
4. **`OnnxEmbeddingSource` (5c)** — after Ollama path is working. Confirm NuGet acceptance before adding.
5. **`EmbeddingModelValidator` (5e)** — recall sweep harness. Synthetic tests first, Ollama integration tests optional.
6. **`SparsityBudget` in `QuantizationOptions`** — one-line addition, high-value for sparsity control.

---

## Resolved decisions carried forward from Epic 1

| Decision | Resolution |
|---|---|
| Dimension representation | Runtime-N (`LongVectorN`/`SparseVector`) + Roslyn generator for hot-path optimization |
| Sparsity model | Virtual space, realized-only nodes — never allocate empty branches |
| Distance metrics | L2 (squared) and L1 (Manhattan), both with max-heap KNN pruning |
| Quantization | Single linear scale, configurable `zeroThreshold = 0.01f` default |
| Concurrency | Build single-threaded; frozen lattice is lock-free read-only |
| Phase 6 map-reduce | Deprecated — horizontal sharding or ensemble indexes instead |
| Serialization | Binary `SLAT` format, version 1, deterministic, `IPayloadSerializer<T>` |
| Freeze semantics | `Freeze()` throws on second call; `FromFrozenRoot` is the only deserialization entry point |

---

## Unresolved decisions carried forward

| ID | Question | Trigger to resolve |
|---|---|---|
| OQ-1 | Ollama-first vs ONNX-first for 5b/5c | Confirm before starting Phase 5 implementation |
| OQ-2 | Target recall@K threshold (0.90?) | Confirm before writing `EmbeddingModelValidator` assertions |
| OQ-3 | `SparsityBudget` default value | Resolve during Phase 10 experiments |
| OQ-4 | BenchmarkDotNet: separate project vs test project | Resolve before Phase 8 |
| OQ-5 | Tokenizer for ONNX path: bundled WordPiece vs external library | Resolve when starting 5c |
