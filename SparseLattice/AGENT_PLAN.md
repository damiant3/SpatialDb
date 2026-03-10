# AMENDMENT — Agent Plan (applies to this document)

> Note: The text that follows in this file (below this amendment) is archival. Treat the original plan as historical context. New guidance, corrections, and active directives must be added as amendment sections at the top of this document only. Do not modify the archival text except to append further amendments.

Summary of changes authorized by the project owner:

- Scope: All applied changes are restricted to the `SparseLattice` and `SparseLattice.Test` projects. All other projects in the repository are locked and must not be modified.
- Deprecation: The original Phase 6 (dimension-wise map/reduce sharding across embedding dimensions) is deprecated for the current flexible-dimension, sparse-node architecture. Dimension-partitioned map/reduce is brittle and unnecessary under the runtime-N sparse model.
- Replacement guidance: Prefer horizontal (data) sharding (shard by item id via hash or range) for scale-out. For multi-index strategies, use independent ensemble indexes (different quantizers or partitions) and merge candidate lists at query time. Reconsider dimension-wise partitioning only if a future design adopts fixed, small subspaces with a well-defined, lossless projection and specialized generated code per-subspace.
- Editing rule: Future operational changes must be added as new amendment sections at the top of this file. The archival portion below must remain unchanged unless another explicit amendment is added.

Rationale (concise): the codebase now supports runtime-flexible dimensionality and realized-only child nodes. The original map/reduce assumption required dense fixed-dimension sub-lattices and is incompatible with the current architecture. Horizontal sharding or ensemble approaches preserve correctness, simplify testing, and align with the project's immutability model after `Freeze()`.

Actions to follow (immediate):

1. Mark Phase 6 as deprecated in the archival text (this amendment suffices).  
2. Add unit tests under `SparseLattice.Test` to validate horizontal sharding preserves Top-K vs brute-force on small synthetic datasets.  
3. If you want, I will also create a short README section and add the suggested unit-test stubs; confirm and I will apply them.

---

# SparseLattice — Agent Run Plan

## Checkpoint C — Phase 4 deepening, Phase 7 serialization, Phase 9 invariant lockdown

### What was built

**Phase 4 — Query engine deepening**

| Change | Detail |
|---|---|
| `QueryKNearestL1` | New method on `EmbeddingLattice<TPayload>` — KNN by Manhattan distance with max-heap pruning |
| Priority-queue KNN (L2 + L1) | Replaced full-scan + sort with a fixed-capacity max-heap (`KnnHeap`, private inner class). Heap worst-distance is used as a live pruning radius: the far child of a branch is skipped when its split-plane distance exceeds the current worst heap entry, eliminating unnecessary subtree visits once K candidates are found |
| `Freeze()` guard | `Freeze()` now throws `InvalidOperationException` on second call — prevents accidental double-freeze |
| `FromFrozenRoot` / `Root` | Private factory + internal accessor to support the serializer without exposing mutation |

**Phase 7 — Serialization**

| File | Type | Description |
|---|---|---|
| `Lattice/SparseLatticeSerializer.cs` | `SparseLatticeSerializer` | Compact deterministic binary save/load. Magic=`SLAT`, version=1, recursive node encoding. Two consecutive saves of the same frozen lattice produce byte-identical output |
| `Lattice/SparseLatticeSerializer.cs` | `IPayloadSerializer<TPayload>` | Contract for type-specific payload encoding |
| `Lattice/SparseLatticeSerializer.cs` | `StringPayloadSerializer`, `Int32PayloadSerializer` | Built-in serializers for the two most common payload types |

Wire format: `[4B magic][2B version][node…]`. Node: `[1B tag]` then leaf (`[4B count][occupants…]`) or branch (`[2B dim][8B value][1B childMask][children…]`).

**Phase 9 — Recall evaluator + invariant lockdown**

| File | Type | Description |
|---|---|---|
| `Lattice/RecallEvaluator.cs` | `RecallEvaluator` | Brute-force KNN (L2 + L1), `EvaluateL2/L1`, `AggregateL2` for multi-query recall sweeps. Foundation for Phase 10 quantization experiments |
| `Lattice/RecallEvaluator.cs` | `RecallResult`, `AggregateRecallStats` | Typed results with `RecallAtK`, `MeanRecallAtK`, `MinRecallAtK`, `MaxRecallAtK` and `ToString()` |

**Tests added — 47 new (147 total, 0 failures)**

- `KnnQueryTests` (13): `QueryKNearestL1` empty/ordered/brute-force, KNN L2 brute-force, K > corpus, result ordering, freeze consistency (L1 + L2), high-dimensional sparse recall@10 (L1 + L2), radius search high-dim, aggregate recall smoke test
- `InvariantTests` (9): double-freeze throws, no dense children, occupant count invariant, post-freeze count preserved, sparse entry no-zero, dimensions strictly ascending, split dimension bounds, build determinism, child realization only when data exists
- `SerializerTests` (12): save throws if not frozen, empty/single/multi-item round-trips, tree structure identity, byte-identical consecutive saves, query/KNN results after load, invalid magic/version, loaded lattice is frozen, string payloads preserved
- `RecallEvaluatorTests` (13): brute-force ordering (L2 + L1), perfect/zero/partial recall, K < groundTruth, aggregate empty/single queries, `ToString` format, `EvaluateL2` vs manual ground truth

### Key design decisions

- `KnnHeap` is a private inner sealed class inside `EmbeddingLattice<TPayload>` — it shares the `TPayload` type parameter and avoids boxing. It is a fixed-capacity max-heap; sift operations are standard binary heap sift-up / sift-down over a `ValueTuple` array.
- Pruning condition: `!heap.IsFull || splitPlaneDistance <= heap.WorstDistance`. When the heap is not yet full, always descend; once full, prune only when the split-plane lower bound exceeds the heap's current worst entry.
- `SparseLatticeSerializer` is a `sealed class` with a `private` constructor (not `static`) to conform to the `s_` naming rule for private constants, consistent with `FrozenBranchNode`.
- `RecallEvaluator` lives in `SparseLattice.Lattice` (not `SparseLattice.Math`) because it depends on `SparseOccupant<TPayload>` from the Lattice layer.
- All `RecallEvaluator` brute-force methods are explicitly `O(n)` scans — they are ground-truth utilities, not production query paths.

### Files changed (SparseLattice and SparseLattice.Test only)

- `SparseLattice/Lattice/EmbeddingLattice.cs` — KNN heap, `QueryKNearestL1`, freeze guard, `Root`/`FromFrozenRoot`
- `SparseLattice/Lattice/SparseLatticeSerializer.cs` — new file
- `SparseLattice/Lattice/RecallEvaluator.cs` — new file (moved from Math/ during build to align with namespace)
- `SparseLattice.Test/Lattice/KnnQueryTests.cs` — new file
- `SparseLattice.Test/Lattice/InvariantTests.cs` — new file
- `SparseLattice.Test/Lattice/SerializerTests.cs` — new file
- `SparseLattice.Test/Lattice/RecallEvaluatorTests.cs` — new file

---

# SparseLattice — Agent Run Plan

## Checkpoint B — Phase 2 & Phase 3 complete

### What was built

**Phase 2 — Node solidification and Freeze compaction**

| File | Change |
|---|---|
| `Lattice/Nodes.cs` | Added `FrozenBranchNode` (array-backed compact children, tag-bit mask), `SparseTreeStats` telemetry struct |
| `Lattice/EmbeddingLattice.cs` | Real `Freeze()` converts `SparseBranchNode` → `FrozenBranchNode` recursively; `CollectStats()` telemetry walk; query paths handle both mutable and frozen branch types via shared descend helpers |

**Phase 3 — Roslyn code generator**

| File | Type | Description |
|---|---|---|
| `Lattice/Generator/IOptimizedPartitioner.cs` | `IOptimizedPartitioner`, `PartitionerDescriptor` | Contract interface + descriptor for the generator |
| `Lattice/Generator/SparseLatticeCodeGenerator.cs` | `SparseLatticeCodeGenerator` | Emits C# source for a dimension-specialized unrolled partitioner |
| `Lattice/Generator/GeneratedPartitionerLoader.cs` | `GeneratedPartitionerLoader` | Compiles via Roslyn, loads into a collectible ALC, disposable loader for clean unload |

**Tests added — 24 new (100 total, 0 failures)**

- `FreezeTests` (11): freeze flag, compaction, L2/L1/KNN query consistency pre/post freeze, `FrozenBranchNode` mask invariants, stats telemetry, occupant count invariant, concurrent reads post-freeze
- `SparseLatticeCodeGeneratorTests` (5): compilable source, dimensions property, invalid inputs rejected, FQN helper
- `GeneratedPartitionerLoaderTests` (8): loads small/large dims, partition equivalence vs generic, empty span, all-below/all-above cases, missing-dimension behavior, dispose/unload safety, independent loaders

### Key design decisions

- `FrozenBranchNode` uses a tag-byte bitmask + compact `ISparseNode[]` sized to exactly the realized child count (0, 1 or 2) — no null slots, no dense allocations.
- `const` fields use `s_` prefix per CONTRIBUTING.md (constants are class-level).
- The generator currently targets an `IOptimizedPartitioner` contract; wiring the partitioner to be generic over `TPayload` will be completed when the builder uses generated partitioners (next phase).
- Generated assemblies are loaded into a collectible ALC; `GeneratedPartitionerLoader.Dispose()` unloads the context and triggers GC.

### Files changed only inside SparseLattice and SparseLattice.Test

- `SparseLattice.csproj` — added `Microsoft.CodeAnalysis.CSharp` package.
- `SparseLattice.Test.csproj` — test project updates for generator tests.
- No modifications to `SpatialDbLib` or other locked projects.

---

Checkpoint A Report
What was built
Project infrastructure:
•	SparseLattice.csproj — added TreatWarningsAsErrors=true, LangVersion=latest
•	SparseLattice.Test.csproj — added TreatWarningsAsErrors=true, LangVersion=latest, project reference to SparseLattice
•	.editorconfig — aligned var rules to false:warning per CONTRIBUTING.md
Core types (SparseLattice\Math): | File | Type | Description | |---|---|---| | LongVectorN.cs | readonly struct LongVectorN | N-dimensional integer vector with overflow-safe BigInteger arithmetic, midpoint, operators, equality | | SparseVector.cs | readonly struct SparseVector + SparseEntry | Sorted sparse tuple representation with O(nnz) merge-based L2 and L1 distance, validation invariants enforced in constructor | | HyperRegion.cs | readonly struct HyperRegion | N-dimensional bounding box with midpoint, containment, hypersphere intersection, split halves | | EmbeddingAdapter.cs | EmbeddingAdapter + QuantizationOptions | Float→int64 quantizer with configurable zeroThreshold=0.01, round-trip dequantize |
Lattice types (SparseLattice\Lattice): | File | Type | Description | |---|---|---| | Nodes.cs | SparseBranchNode, SparseLeafNode<T>, SparseOccupant<T> | Sparse decision-tree nodes — branch stores only SplitDimension/SplitValue + two nullable children, leaf stores occupant array | | EmbeddingLatticeBuilder.cs | EmbeddingLatticeBuilder | Builds tree via variance-based pivot selection and in-place partition | | EmbeddingLattice.cs | EmbeddingLattice<TPayload> | Wrapper with Freeze(), QueryWithinDistanceL2, QueryWithinDistanceL1, QueryKNearestL2 |
Tests — 76 total, 0 failures:
•	LongVectorNTests — 19 tests: construction, operators, overflow safety, midpoint, equality, hash
•	SparseVectorTests — 17 tests: construction validation, sorted invariant, merge distance vs brute force, L1/L2, round-trip
•	HyperRegionTests — 11 tests: construction, midpoint, containment, hypersphere intersection, split halves
•	EmbeddingAdapterTests — 7 tests: threshold, sign preservation, round-trip, overflow safety
•	EmbeddingLatticeBuilderTests — 8 tests: empty/single/threshold/branch creation, element preservation, determinism, no dense children invariant, split dimension bounds
•	EmbeddingLatticeTests — 7 tests: empty query, exact neighbor, radius search, L1 search, KNN, brute-force equivalence, concurrent reads
Style compliance
•	All new code uses explicit types (no var)
•	All private fields use m_ prefix
•	File-scoped namespaces with // separator line
•	No empty catches, no block comments, no regions
•	Using order: System.* then project namespace
No files changed outside SparseLattice/SparseLattice.Test
•	.editorconfig updated (shared) to enforce var warnings
•	Zero modifications to SpatialDbLib or any other project
Next phase when ready
Phase 2/3 deepening: refine partition heuristics, add Roslyn code generator contract, add freeze compaction (linked-list→array), and expand invariant tests.


# SparseLattice — Agent Run Plan (revised after user answers)

Purpose
-------
Step-by-step plan for evolving `SparseLattice` and `SparseLattice.Test` into a robust, production-ready, read-only sparse lattice index for embeddings. The plan records decisions made, defers open engineering choices where required, and lists tests that lock invariants. Do not change other projects; use them as style/architecture references.

Resolved design decisions (user answers)
---------------------------------------
- Dimension representation: Support runtime-N vectors and also a Roslyn-based code generator to emit and hot-compile optimized types for a requested dimensionality.
- Sparsity model: Virtual space with only realized nodes — no eager allocation of full child arrays. This is fundamental; design assumes sparse realization.
- Distance metrics: Support L2 (Euclidean) and Manhattan (L1). Use the project's distance library and produce comparative experiments.
- Quantization: Start with single linear quantization (value * globalScale) with configurable `zeroThreshold = 0.01` (default). Global scale chosen to fit within `long` range. Per-dimension scaling deferred.
- Sparsity budget: No fixed nnz limit; vectors may have arbitrary nonzero counts up to the dimension count. Experiments will report recall vs nnz.
- Leaf and depth policy: Configurable `LeafThreshold` (suggested default 16). Depth not artificially capped — governed by data and builder heuristics.
- Concurrency: Build single-threaded; after `Freeze()` the structure is immutable and must support lock-free concurrent queries. Tests will validate concurrency behavior.
- Persistence: Not important at this stage — format decision deferred.
- Benchmarks & datasets: Deferred; add when research experiments begin.
- CI / runner: You will run builds/tests. Agent must run `dotnet build` and `dotnet test` locally before PRs.

Open / deferred discussion points (need follow-up)
--------------------------------------------------
These items are explicitly deferred and annotated with suggested triggers for revisit:

1. BitKey representation for >64 dims (deferred)
   - Why deferred: user emphasized virtual sparsity; representation details are secondary.
   - When to revisit: before Phase 2/Phase 3 implementation of navigation or when serialization/mmap is required.
   - Options to evaluate: `byte[]` compact bitmask, `BitArray`, `BigInteger`, or generated per-dimension split-path code via Roslyn.

2. Persistence format (deferred)
   - Why deferred: not currently important.
   - When to revisit: Phase 7 (Freeze/serialization) or when mmap-loading performance is required.
   - Options to evaluate: binary compact (mmap-friendly), memory-mapped layout, or simple deterministic binary blob.

3. Per-dimension quantization scaling and more advanced quantizers (deferred)
   - When to revisit: after initial experiments (Phase 10) show need for per-dimension scaling.

4. Benchmarks dataset selection (deferred)
   - When to revisit: before Phase 8; define small canonical workloads and real-code embedding samples.

Plan changes and explicit next actions
-------------------------------------
- The plan phases remain, but with these updates:
  - Phase 1 (Core types): implement runtime `LongVectorN` and `SparseVector` first. Add an internal factory interface to allow plugging in Roslyn-generated optimized types later.
  - Phase 2 (Node types): enforce sparse child realization; do NOT allocate 2^N children. Add tests to assert this invariant.
  - Phase 3 (Builder): implement pivot heuristics (highest variance among nonzero dims). Add deterministic build tests.
  - Phase 4 (Query): produce L2 and L1 query paths (toggle via argument). Add allocation-free after-freeze checks.
  - Phase 5 (Adapter): implement linear quantizer with `zeroThreshold = 0.01` default and expose `QuantizationOptions`. Add round-trip and overflow safety tests.
  - (deprecated) Phase 6 (Sharding): implement shard coordinator using dimension partitions. Allow dynamic shard counts; tests must assert Top-K equivalence vs brute-force for small datasets.
  - Phase 7 (Freeze/Serialization): postpone final format decision; implement freeze that converts linked-lists to arrays and exposes a serialization interface. Add a TODO to finalize binary layout before enabling mmap.
  - Phase 8–10: deferred dataset/benchmark selection and research experiments; add harness to sweep `zeroThreshold` and compare against float-cosine baseline.

Explicit items added to the plan
--------------------------------
- Roslyn generator contract: define small generator API (input: dimension, nnz budget hints, target optimizations) that produces a compiled assembly loaded at runtime. Include tests that the generated types behave identically to the runtime generic versions.
- Style/invariant tests: add Roslyn-based style checks enforcing `CONTRIBUTING.md` rules (no `var` in new code, `m_` prefix, file-scoped namespace, using groups and separator). These tests run in CI and locally.
- Concurrency tests: add deterministic multi-threaded read-only query tests to validate no race conditions on frozen structures.
- Telemetry: add debug counters for realized-node counts and occupancy sizes to enable tests asserting memory vs data scaling.

When to schedule deferred discussions
------------------------------------
- BitKey representation & serialization layout: schedule conversation before Phase 3→Phase 7 merge (recommended trigger: when freeze serialization interface is implemented).
- Per-dimension quantization & learned quantizers: schedule after Phase 10 experiment results are available.
- Benchmark datasets: schedule before Phase 8 benchmark implementation.

Next immediate steps for the agent (pickable)
--------------------------------------------
1. Implement Phase 1 (LongVectorN, SparseVector) and unit tests; add factory hook for generated types.
2. Add style/invariant Roslyn tests and run them locally.
3. Add `QuantizationOptions` and linear quantizer with default `zeroThreshold = 0.01` and unit tests.
4. Defer bitkey and persistence format decisions until triggered by Phase 3/7 needs.

Notes
-----
- All code must follow `SparseLattice/CONTRIBUTING.md` rules (explicit types, `m_` prefix, file-scoped namespaces, no empty catches, etc.).
- Keep diffs small; add tests concurrently with code changes.
- When you want, I will (a) implement Phase 1 scaffold code and tests, or (b) add the Roslyn generator contract file and tests. Tell me which to start.


# SparseLattice — Agent Run Plan (first cut)

Purpose
-------
This document is a step-by-step plan for an AI agent (and human engineers) to evolve `SparseLattice` and `SparseLattice.Test` from the current prototype to a robust, production-ready, read-only sparse lattice index for embeddings. Each step lists concrete implementation tasks, the tests required to validate the feature and to lock down invariants discovered while coding. Do not change any files outside of the new `SparseLattice` and `SparseLattice.Test` projects — `SpatialDbLib` and other repo projects are read-only reference material for style and architecture.

Open questions (first cut)
--------------------------
These must be answered before particular implementation decisions are finalized. Mark each as resolved in a follow-up iteration.

1. Dimension representation
   - Will we accept arbitrary embedding dimensionality at runtime (preferred), or generate specialized code per-dimension via Roslyn?
   - User: Both.  we will write a code generator, we will compile it, and load it at runtime to produce optimized types for requested dimensions.
   - We are meta-programming!
2. Octant / child index for >64 dims
   - Use `BitArray`, `BigInteger`, `ReadOnlyMemory<byte>` as keys, or a custom compact bitset? Which choice favors serialization, cache locality and speed?
   - I am unsure what this question asks.  There will be no nodes except realized nodes in the virtual space.  That is the point of sparsity.
3. Distance metric
   - Use L2 (Euclidean on quantized ints), Manhattan, or both? Will experiments report recall vs. metric choice?
   - User: we have a distance metric library, so we can support both and produce research results on recall vs metric choice.
4. Quantization policy
   - Single linear quantize (v * long.MaxValue) with threshold? Or per-dimension scaling? What default zeroThreshold?
   - User: start with single linear quantization with a configurable zeroThreshold (e.g., 0.01) and a global scale to fit within long.MaxValue. We can add per-dimension scaling later if needed.
5. Maximum nonzeros (nnz) budget
   - Target sparsity per vector (e.g., 16–64 nonzeros)? Affects buffer sizes and generated code assumptions.
   - User: it shouldn't matter, we are also using sparse vectors internally, so we can support arbitrary nnz up to the dimension count. We will report recall vs nnz budget in experiments.
6. Leaf capacity and tree depth policy
   - Default leaf size (e.g., 8–32 items) and max depth. How to compute depth from dims and data size?
   - User: Definitely a configurable leaf size (e.g., 16) and depth limited only by the data.
7. Persistence format
   - Binary, mmap-friendly layout vs JSON. Must choose for reliable round-trip and freeze performance.
   - User: not important now.
8. Concurrency model
   - Build is single-threaded (preferred); queries must be lock-free. Will we need concurrent read-only access concerns?
   - User: Once the structure is frozen, it is immutable and can be safely read concurrently without locks. During build, we will ensure that no concurrent modifications occur. We should add tests to validate that concurrent queries do not cause issues.
9. Benchmarks & datasets
   - Which small code-focused embedding datasets are canonical for unit/integration tests?
   - User: A topic for future research
10. CI targets
    - Confirm CI will run `.NET 8` builds and run unit tests from `SparseLattice.Test`.
    - User: You and I are the CI engine.  I certainly hope you run builds and unit tests.  I will.

High-level phases
-----------------
Each phase contains tasks and required tests. Implement sequentially, iterate where necessary.

Phase 0 — Project hygiene & baseline
------------------------------------
Tasks
- Add or confirm `SparseLattice/CONTRIBUTING.md` and `.editorconfig` follow repository standards (we previously prepared suggestions).
- Add `docs/` and `benchmarks/` directories to `SparseLattice`.
- Ensure solution builds: `dotnet build` for the solution with `SparseLattice` and test project targeting .NET 8.
Tests
- `Build_Baseline_Succeeds()` — assert `dotnet build` succeeds in CI.
- `Tests_Run_Baseline()` — run `dotnet test` and assert zero failing tests prior to changes (or document failing tests).

Phase 1 — Core value types (dimension-agnostic runtime N)
---------------------------------------------------------
Goal
- Introduce robust, efficient, bounds-checked runtime-dimension vector types and a sparse tuple vector representation.

Implementation tasks
1. Implement `LongVectorN` (readonly struct):
   - Holds `long[] m_components` (private `m_` prefix).
   - Exposes `int Dimensions { get; }`, indexer `this[int i]`.
   - Implement subtraction, dot product, magnitude squared with overflow-safe arithmetic (use `BigInteger` where necessary).
   - Provide `static LongVectorN FromArray(long[])` and serialization helpers.

2. Implement `SparseVector`:
   - Immutable representation of `(ushort dimension, long value)[] Components`, sorted ascending by `dimension`.
   - Provide `IntersectionsWith(SparseVector other)` style merge iterators for O(nnz) operations.
   - Provide method to compute squared distance to `LongVectorN` or another `SparseVector`.

3. Utility types:
   - `BitKey` abstraction for child indices. Provide two concrete implementations:
     - `SmallBitKey` for dims <= 64 (wraps `ulong`).
     - `BigBitKey` for dims > 64 (wraps `BitArray` or `byte[]` + hash).
   - The lattice uses `IBitKey` at runtime.

Tests
- `LongVectorN_BasicOperations` — assert correct subtraction, dot, magnitude behavior, equality round-trip.
- `LongVectorN_Indexer_ThrowsOnOutOfRange` — verify argument validation.
- `SparseVector_FromDense_RetainsNonzeros` — ensure conversion works and sorts components.
- `SparseVector_MergeDistance_ComplexCase` — validate distance via merge algorithm matches brute force dense result for small dims.
- Invariant tests:
  - `SparseVector_Components_AreSortedAndUnique()` — assert uniqueness and ascending order.

Phase 2 — Sparse node types & decision-tree branch
--------------------------------------------------
Goal
- Replace dense child arrays with sparse realized-child containers and simplify branch nodes to store only `SplitDimension` and `SplitValue`.

Implementation tasks
1. `SparseBranchNode`:
   - Fields: `ushort SplitDimension`, `long SplitValue`, `SparseBranchNode? Below`, `SparseBranchNode? Above`.
   - No pre-allocated children arrays. Use `m_` prefix for private fields.

2. `SparseLeafNode<T>`:
   - Holds `List<Occupant>` or fixed `Occupant[]` until freeze.
   - Occupant = `(LongVectorN Position, T Data)` or `(SparseVector Position, T Data)` depending on chosen internal representation.

3. `SparseHypercubeNode` (root wrapper):
   - Stores bounding `HyperRegion` metadata and single child pointer which is either branch or leaf node.

4. `HyperRegion`:
   - Represents per-dimension bounds (min/max arrays).
   - Provides `Midpoint(dim)` and `IntersectsHypersphere(center, radius)` semantics for integer coordinates.

Tests
- `BranchNode_SimpleInsertPath` — insert vectors that differ on split dimension; validate tree shape.
- `LeafNode_StoresAndEnumerates` — add items, read them back intact.
- `HyperRegion_Midpoint_And_Intersection` — test bounding math for small dimensions.
- Invariants:
  - `No_DenseChildrenAllocated` — reflection-based test to ensure nodes contain no arrays sized `1 << dims`.
  - `BranchNode_SplitDimension_WithinBounds` — validate split dimension < Dimensions.

Phase 3 — Builder and in-place sparse partition
-----------------------------------------------
Goal
- Implement the build-phase partitioning algorithm tuned for sparse vectors, creating a decision-tree based on high-variance dimensions and creating realized child nodes on demand.

Implementation tasks
1. Implement `PartitionSparse(Span<SparseVector> vectors, HyperRegion bounds, int depth)`:
   - Choose pivot dimension by heuristic: highest variance among nonzero dimensions (compute variance on sparse data).
   - Partition in-place by pivot dimension and pivot value (midpoint).
   - Recurse until `LeafThreshold` or max depth reach.

2. `EmbeddingLatticeBuilder`:
   - Public `Build(IEnumerable<SparseVectorWithPayload>)` that performs sorting/partitioning and constructs node graph.
   - Single-threaded, deterministic, configurable seed for any randomized choice (if any).

3. Implement `Freeze()`:
   - Convert any linked lists to compact arrays for read-only queries.
   - Optionally compress node metadata for faster traversal.

Tests
- `Partition_PreservesElementsAndCreatesLeaves` — assert no elements lost and leaves sizes within threshold.
- `Partition_PivotDimensionSelection` — with crafted inputs, assert pivot chosen is expected high-variance dim.
- Invariants:
  - `Build_IsDeterministic` — repeated `Build()` on same input yields identical serialized trees (byte-for-byte if using deterministic serialization).

Phase 4 — Query engine (distance search and pruning)
----------------------------------------------------
Goal
- Implement allocation-free, lock-free query codepaths for `QueryWithinDistance(center, radius)` over the frozen index.

Implementation tasks
1. `EmbeddingLattice.QueryWithinDistance(LongVectorN center, long radius)`:
   - Traverse nodes; branches make decisions based only on `SplitDimension` and `SplitValue`.
   - When traversing, only iterate realized children.
   - Leaf scanning calculates exact distance using sparse merge or dense long vector.

2. Top-K / thresholded retrieval:
   - Provide `QueryKNN(center, k)` combining map-reduce candidate set and final sort by exact integer distance.

Tests
- `Query_FindExactNeighbor` — known small dataset where nearest neighbor is known; verify returned top1.
- `Query_EmptyIndex` — returns empty.
- `Query_SparseEarlyPrune` — assert that missing child causes early termination (test that no further nodes visited).
- Invariants:
  - `Query_AllocationFreeAfterFreeze` — use performance instrumentation or unit tests that assert no allocations on cold path (or minimal known allocations for enumerators).

Phase 5 — Adapter / Quantizer / Dequantizer
-------------------------------------------
Goal
- Create `EmbeddingAdapter` that maps float[] embeddings to `SparseVector` or `LongVectorN` with tunable threshold and scaling.

Implementation tasks
1. Implement `Quantize(float[] embedding, QuantizationOptions)`:
   - Options: `zeroThreshold`, `perDimensionScale?`, `sparsityBudget` (max nnz), `normalize` toggle.
   - Default quantize: kill small values `abs(v) < zeroThreshold` → 0; otherwise `long value = (long)(v * (long)MaxScale)` (scale chosen to avoid overflow in distance computation).

2. Implement `Dequantize(LongVectorN)` (if needed for debug/visualization).

3. Provide test helpers to generate sparse vectors from dense float input and vice versa.

Tests
- `Quantize_KillsNoiseUnderThreshold` — vectors near zero become sparse.
- `Quantize_RoundTripReproducible` — quantize→dequantize→quantize yields same key within configured threshold.
- Invariants:
  - `Quantize_NoOverflow` — ensure quantized long values are within safe bounds for distance math (test with extremes).

~~Phase 6 — Map-Reduce sharding (multi-lattice approach)~~
-----------------------------------------------------
**Goal**
- ~~Implement sharding over partitions of the embedding dimensions (e.g., 12 shards × 64 dims) and a simple map-reduce merging of candidate lists.~~

**Implementation tasks**
1. `LatticeShard` wrapper class — owns individual `SparseLattice` for its subspace.
2. `ShardCoordinator`:
   - Builds shards by partitioning embedding dims into groups (configurable).
   - `QueryAcrossShards` fans out queries to each shard, aggregates candidate sets, computes exact distances in the full space, and returns Top-K.
   - Make shard operations parallel at the CPU core level (e.g., `Parallel.ForEach`) for performance; ensure builder and freeze remain single-threaded.

Tests
- `ShardCoordinator_Aggregation_PreservesTopK` — small synthetic dataset where shard-based method returns same Top-K as full-space brute force.
- `ShardCoordinator_ParallelConsistency` — parallel vs sequential produce same results (deterministic).

Phase 7 — Freeze, serialization and immutability
------------------------------------------------
Goal
- Implement `Freeze()` producing a compact, immutable index and serialization that supports memory-mapped loading for fast startup.

Implementation tasks
1. Implement deterministic binary serialization for nodes and leaves.
2. Provide `LoadFromStream` and `SaveToStream`.
3. Implement `Freeze()` to convert linked lists to arrays and run optional compacting passes.

Tests
- `Freeze_QueryConsistency` — query results before and after freeze identical.
- `Serialize_RoundTrip` — serialized file binary-equals post-load serialized file.
- `Load_MemoryMapped_ReadOnly` — if memory-mapped format chosen, verify that load is read-only and queries work.

Phase 8 — Benchmarks & performance baseline
-------------------------------------------
Goal
- Provide reproducible microbenchmarks and representative workloads to evaluate recall and latency.

Implementation tasks
1. Add `SparseLattice.Test/Benchmarks` with deterministic benchmark inputs:
   - Random sparse vectors of varying nnz (8, 16, 32).
   - Clustered vectors around centers (to test recall).
   - Worst-case dense vectors (to evaluate degradation).
2. Record baseline metrics (build time, query latency, memory footprint).

Tests
- Benchmarks are not pass/fail but produce artifacts. Add a simple assertion that query latency is within a generous upper bound for CI smoke tests (`QueryLatency_Smoke`).

Phase 9 — Integration with reference style & invariant lockdown
---------------------------------------------------------------
Goal
- Ensure all public APIs and behaviors conform to expectations inspired by `SpatialDbLib`. Lock invariants with tests and docs.

Tasks
1. Add XML docs to all public types and methods.
2. Add `SparseLattice/Test/Invariants` tests that encode repository invariants (e.g., `NoMutationsAfterFreeze`, `SparseChildrenOnly`, `QuantizerReversibility`).
3. Add `docs/architecture.md` describing design rationale, trade-offs and known limitations. Refer to `SpatialDbLib` for naming, `m_` prefixes, and `readonly` preferences.

Tests
- `NoMutationsAfterFreeze()` — attempt modifications and assert exceptions or no effect.
- `ChildRealizationOnlyWhenDataExists()` — assert memory usage correlates with element count, not full space.

Phase 10 — Research experiments & model fine-tuning validation
--------------------------------------------------------------
Goal
- Provide tooling to validate the integer quantization and sparsity experiments (QAT path, thresholds, and recall vs. float baseline).

Tasks
1. Implement a small harness that:
   - Accepts dense float embeddings (from real model or synthesized).
   - Produces quantized sparse vectors with various `zeroThreshold` values.
   - Compares nearest neighbor sets for float-cosine baseline (using brute-force) vs integer-lattice retrieval.
   - Reports precision/recall and stability metrics.

2. Add scripts (Python or .NET) for invoking PyTorch QAT flow documentation (outside repo code but documented in `docs/`).

Tests
- `Experiment_RecordRecall_PerThreshold` — assert recall at reasonable thresholds (document target recall numbers).
- `Sparsity_StabilityUnit` — ensure sparseness distribution is stable across runs.

Phase 11 — Release, CI and PR checklist
---------------------------------------
Tasks
1. CI config:
   - `dotnet build` and `dotnet test` for .NET 8.
   - Optional benchmark job (manual or scheduled).
2. PR Template:
   - List of changed files, tests added, invariant checks, performance summary.
3. Labeling:
   - `area: index`, `area: adapter`, `type: benchmark`, `type: docs`.
   - 
Agent runtime rules & operational checklist
-------------------------------------------
- Always run `dotnet build` and `dotnet test` locally before proposing any change.
- Follow `SparseLattice/CONTRIBUTING.md` for commit message conventions and branch naming.
- No modifications to `SpatialDbLib` or any locked project.
- All behavior-affecting changes must include unit tests and invariant tests.
- Tests must be deterministic; if randomness used for tests, seed must be fixed and recorded.
- Prefer conservative changes with small surface area; break big features into multiple PRs.
- When in doubt open an issue documenting the choices and include the proposed test matrix.

Example test names (conventions)
-------------------------------
- `Unit_<Type>_<Behavior>`, e.g. `Unit_LongVectorN_Subtract_Correct`
- `Invariant_<Name>`, e.g. `Invariant_NoDenseChildren`
- `Integration_<Scenario>`, e.g. `Integration_ShardCoordinator_TopKMatchesBruteForce`
- `Benchmark_<Scenario>` (stored in `benchmarks/`)

Deliverables per milestone
--------------------------
For each phase the PR must include:
- Implementation code (compiles).
- Unit tests that exercise edge cases and invariants.
- One integration test that demonstrates intended behavior for that phase.
- Benchmarks (when applicable).
- Small `docs/` entry explaining API and design choices made.

Final notes & next steps
------------------------
- Resolve the open questions at top before implementing Phase 3 pivot logic and Phase 6 sharding policies.
- After the first implementation pass (Phases 1–4), run the experiment harness (Phase 10) with a few models' sample embeddings to validate quantization choices.
- Iterate: adjust `zeroThreshold`, leaf sizes, shard counts, pivot heuristics based on real recall/latency results.

This is the initial, detailed plan. Once you answer the open questions above, I will produce a finalized checklist and a minimal seed implementation plan, including suggested unit-test templates (xUnit) and a deterministic sample dataset for immediate tests.
