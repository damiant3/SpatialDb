# SparseLattice — Epic 4 Plan
# The Integer Lattice as a Full Model Container

## Scope and editing rules

This document covers all work from Epic 4 onward.
- All changes are restricted to `SparseLattice`, `SparseLattice.Test`, and `SparseLattice.Perf`.
- All other projects are locked and may only be read as style/architecture references.
- Amendments prepend to the top; archival text below is never edited.
- When a phase completes, a Checkpoint entry is prepended above the relevant phase section.

---

## Executive Summary

**The thesis:** floating-point epsilon is a fundamental tax on every computation in modern AI.
Every matmul, every attention score, every softmax produces values that are approximately
right but never exactly right. These errors compound through 12+ transformer layers, and
the model has no way to distinguish signal from accumulated noise. The hypothesis is that
this contributes to hallucination — the model confuses precision artifacts with learned
knowledge.

**The bet:** if we can run transformer-class computations in exact integer arithmetic —
where 3 × 7 = 21, always, with no rounding, no epsilon, no catastrophic cancellation —
then the representations are *deterministic* and *lossless*. Distance comparisons become
exact. Nearest-neighbor lookups return provably correct results. And if the model's
internal activations are exact, the boundary between "the model knows this" and "the model
is guessing" may become measurable.

**The evidence so far:**

| What we measured | Result |
|---|---|
| Lattice recall@10 at n=500, 75% sparse | **1.0000** (perfect — every query, every neighbour) |
| Semantic separation (similar vs dissimilar pairs) | **2.52×** (5/5 pair wins) |
| Embedding throughput vs Ollama HTTP | **250×** single, **750×** concurrent |
| Document ingestion 1000 docs | 76ms vs 17s |
| Cosine similarity: lattice token-lookup vs full transformer | **0.06** (nearly orthogonal) |

The last number is the critical one. The lattice KNN is perfect. The integer math is
exact. The sparsity works. But the *embeddings* produced by raw token lookup are nothing
like what the transformer produces after 12 layers of self-attention. This is expected:
the transformer's purpose is exactly to remix those raw embeddings through learned
attention patterns. Skipping that computation skips the meaning.

**The plan:** don't skip it. Run the transformer forward pass *in the integer lattice*.
Not as an approximation. Not as a distillation. As exact integer matrix multiplication
through quantized weight tensors, with integer attention scores and integer softmax.
The lattice becomes not just an index, but a *model runtime*.

---

## The Epsilon Problem (and why this might matter)

### What floats actually do

IEEE 754 float32 has 23 bits of mantissa — roughly 7 decimal digits of precision.
When two values are close together, subtraction loses significant bits (catastrophic
cancellation). When values are large, small components vanish entirely.

In a transformer forward pass:
1. **MatMul**: `A[T×768] × W[768×768]` — each output element is a sum of 768 products.
   Each product has ~7 digits of precision. The sum accumulates ~3 digits of rounding
   noise. After 12 layers of this: 36+ digits of compounded rounding.
2. **Softmax**: `exp(x) / sum(exp(x))` — exponentials amplify small differences.
   Two attention scores that differ by 1e-7 (below float32 precision at the scale of
   typical logits) can produce meaningfully different attention weights.
3. **LayerNorm**: `(x - mean) / sqrt(variance)` — subtracting the mean cancels the
   largest bits, amplifying noise in the lower bits. Dividing by sqrt(variance) further
   amplifies the relative error.

This isn't theoretical. Minecraft's far-lands bug is the same phenomenon: at large
coordinates, float32 loses precision and the world tears apart. GPU inference at
`bfloat16` (7 bits of mantissa — 2 decimal digits!) makes this dramatically worse.

### What integers do differently

`long` (int64) has 63 bits of precision — roughly 18 decimal digits. No rounding.
No epsilon. `a + b - b = a` always. `a * b` is exact if the product fits in 64 bits.
When products exceed 64 bits, we use `BigInteger` — arbitrary precision, still exact.

The existing SparseLattice already proves this works for the retrieval layer:
- `SparseVector.DistanceSquaredL2()` uses `BigInteger` accumulation — exact.
- `SparseVector.DistanceSquaredL2Fast()` uses `ulong` when values are bounded — exact.
- The KNN heap prunes on exact comparisons — no approximate nearest neighbors, no
  probability of error. Perfect recall@10 is not an accident; it's a mathematical
  consequence.

### The hypothesis

If we can:
1. Quantize the weight tensors to integer representations at model load time
2. Perform the forward pass (matmul, attention, FFN) in integer arithmetic
3. Quantize activations between layers to bounded integer ranges
4. Replace softmax with an integer-domain equivalent (e.g., fixed-point or rational)

Then:
- Every intermediate activation is exact and deterministic
- The same input always produces bit-identical output (no nondeterminism from float
  accumulation order, no GPU vs CPU divergence)
- Distances between representations are exact — "how confident is the model?" becomes
  a measurable integer quantity
- The precision available (63+ bits) exceeds anything GPU float16/bfloat16 can offer

The open question is whether this precision advantage translates to measurably
better output quality. We won't know until we build it and measure.

---

## What We Have (codebase inventory, post-Epic 3)

### The Integer Math Foundation
| Component | File | Capability |
|---|---|---|
| `LongVectorN` | `Math/LongVectorN.cs` | N-dim int64 vector, BigInteger overflow-safe ops, dot product, add/subtract |
| `SparseVector` | `Math/SparseVector.cs` | Sorted sparse (ushort dim, long value) tuples, O(nnz) merge L1/L2 distances, binary search lookup |
| `SparseEntry` | `Math/SparseVector.cs` | `(ushort Dimension, long Value)` — 10 bytes per nonzero |
| `HyperRegion` | `Math/HyperRegion.cs` | N-dim bounding box, hypersphere intersection for tree pruning |
| `EmbeddingAdapter` | `Math/EmbeddingAdapter.cs` | `float[]` → `SparseVector` quantization, SparsityBudget top-k trim, dequantize back to float |

### The Lattice Index
| Component | File | Capability |
|---|---|---|
| `EmbeddingLattice<T>` | `Lattice/EmbeddingLattice.cs` | KD-tree with variance-pivot split, KNN L2/L1 with max-heap pruning, radius search, freeze/compact |
| `EmbeddingLatticeBuilder` | `Lattice/EmbeddingLatticeBuilder.cs` | In-place partition, configurable leaf threshold |
| `SparseLatticeSerializer` | `Lattice/SparseLatticeSerializer.cs` | Binary save/load, pluggable payload serializers |
| `RecallEvaluator` | `Lattice/RecallEvaluator.cs` | Brute-force ground truth + recall@K aggregation |
| `SparsityReport` | `Lattice/SparsityReport.cs` | NNZ histograms, dimension coverage, branch balance |

### The Model Layer
| Component | File | Capability |
|---|---|---|
| `GgufReader` | `Gguf/GgufReader.cs` | Full GGUF v3 parser, F16/Q4_0/Q4_1/Q5_0/Q5_1/Q8_0/BF16 dequant to float32, metadata + tensor access |
| `WordPieceTokenizer` | `Gguf/WordPieceTokenizer.cs` | BERT/nomic-bert WordPiece tokenizer, matches HuggingFace at cosine ≥ 0.9999 |
| `BpeTokenizer` | `Gguf/BpeTokenizer.cs` | GPT-style BPE tokenizer (for future causal model support) |
| `TransformerEmbeddingSource` | `Gguf/TransformerEmbeddingSource.cs` | Full BERT forward pass: RoPE, fused QKV, SiLU-gated FFN, SIMD matmul, mean pool, L2 normalize. Matches HuggingFace exactly. |
| `LatticeEmbeddingSource` | `Embedding/LatticeEmbeddingSource.cs` | Token-lookup + integer mean-pool + L2 normalize. 250× faster than Ollama, 75% sparse. No transformer layers. |
| `OllamaEmbeddingSource` | `Embedding/OllamaEmbeddingSource.cs` | HTTP client for Ollama `api/embed` endpoint |
| `OllamaModelLocator` | `Gguf/OllamaModelLocator.cs` | Finds GGUF blobs from Ollama's manifest format |

### Test Infrastructure
| Component | Tests |
|---|---|
| Math layer | 60+ unit tests for vector ops, quantization, sparsity budget |
| Lattice layer | 80+ tests for build, freeze, KNN recall, serialization |
| Model layer | 40+ tests for GGUF parsing, tokenization, forward pass fidelity |
| Quality tests | `CorpusQualityTests.cs` — recall@10 at n=100/500, semantic separation, fidelity |
| Perf harness | `SparseLattice.Perf` — vs-Ollama benchmarks, memory diagnostics, batch throughput |

---

## Phase Plan

### Phase E4-1: Integer MatMul Kernel

**Goal:** Implement `long[] × long[]` matrix multiplication that is exact and fast.

**Why first:** MatMul is ~90% of transformer compute. Everything else depends on getting
this right: attention, FFN, projections. If integer matmul is correct and fast enough,
everything else follows.

**Deliverables:**
- `IntegerMatMul.cs` in `SparseLattice/Math/`
  - `long[] MatMul(long[] A, int rowsA, int colsA, long[] B, int colsB)` — dense × dense
  - `long[] MatMulSparse(SparseVector[] A, int rowsA, long[] B, int colsB)` — sparse activations × dense weights
  - Overflow detection: accumulate in `Int128` (available in .NET 8) or `BigInteger`, downscale if needed
  - SIMD path using `Vector<long>` where available
- `IntegerMatMulTests.cs` — correctness against known results, overflow boundary tests
- Benchmark in `SparseLattice.Perf` comparing integer matmul vs float matmul throughput

**Design decisions:**
- Weights are stored as `long[]` (quantized once at load time, same as current `EmbeddingAdapter`)
- Activations are `long[]` between layers (dense) or `SparseVector` (sparse path, optional)
- Scale management: each tensor carries a `scaleExponent` (power-of-2) so we can right-shift
  after multiply to keep values in range without losing the low bits (shift is exact,
  unlike float truncation)

**Key insight:** `Int128` multiplication in .NET 8 is a single CPU instruction on x64
(`mul` produces 128-bit result in rdx:rax). We get 128 bits of exact product per
multiply — more precision than float64's 52-bit mantissa, at comparable speed.

---

### Phase E4-2: Integer LayerNorm

**Goal:** Implement LayerNorm in exact integer arithmetic.

**The challenge:** LayerNorm requires `sqrt(variance)`, which is irrational for most inputs.
We cannot compute it exactly. But we can compute an integer approximation that is
*strictly better* than float32's approximation.

**Approach:**
- Compute mean as `sum / count` using integer division (truncation, not rounding — deterministic)
- Compute variance as `sum_of_squares / count - mean²` in `BigInteger` (exact)
- Compute `isqrt(variance)` — integer square root — which gives the floor value.
  This is exact to within ±1 ULP in the integer domain (vs float32's ±0.5 ULP but with
  the accumulated error from prior computation)
- Apply: `(x[d] - mean) * scale * isqrt_inv / isqrt_norm + bias[d]`
  where all operations are integer multiply + shift

**Deliverables:**
- `IntegerLayerNorm.cs` in `SparseLattice/Math/`
- Tests comparing integer LayerNorm output vs float LayerNorm output on the same input,
  measuring the maximum per-element deviation

**Risk:** The division and isqrt introduce deterministic truncation error. This is acceptable
if the error is bounded and smaller than float32's accumulated rounding. Quantify this in tests.

---

### Phase E4-3: Integer Attention (QKV + Softmax)

**Goal:** Implement multi-head self-attention with integer dot products and integer softmax.

**Attention scores:** `score[t,s] = dot(Q[t], K[s]) / sqrt(d_head)` — the dot product is
exact in integer arithmetic. Division by `sqrt(d_head)` is a fixed constant (e.g., √64 = 8),
so we can absorb it into the quantization scale. No float needed.

**Softmax is the hard part.** `exp(x)` is transcendental; it cannot be computed exactly in
integers. Options:

1. **Fixed-point exp approximation:** Compute `exp(x)` as a Taylor series truncated to N
   terms, with all arithmetic in fixed-point `long`. Error is bounded and deterministic.
   Well-studied in DSP/embedded literature.

2. **Log-domain softmax:** Store log-probabilities instead of probabilities. This avoids
   `exp()` entirely and replaces it with `log-sum-exp`, which can be computed as
   `max(x) + log(sum(exp(x - max(x))))`. The inner `exp` operates on values close to 0,
   where the Taylor series converges fast.

3. **Rational softmax:** Replace `exp(x)` with a rational polynomial approximation
   `p(x)/q(x)` where p and q have integer coefficients. The output is a ratio of
   two integers — exact in the rational number sense. Attention weights become rationals.

4. **Hardmax / sparsemax:** Replace soft attention with hard (argmax) or sparse (keep top-k
   values, zero the rest). This is already used in some efficient attention variants and
   maps naturally to `SparseVector` — the attention pattern itself becomes sparse.

**Recommendation:** Start with option 1 (fixed-point exp) for correctness validation, then
explore option 4 (sparsemax) for the sparse lattice path where we want sparse activations.

**RoPE:** Rotary position embeddings require `sin(θ)` and `cos(θ)`. These can be
precomputed to arbitrary precision at load time (they depend only on position and dimension
index, not on input data) and stored as quantized `long` values. The rotation itself is
two integer multiplies and an add — exact.

**Deliverables:**
- `IntegerAttention.cs` — QKV projection, score computation, softmax, output projection
- `IntegerSoftmax.cs` — fixed-point exp with configurable precision
- `IntegerRoPE.cs` — precomputed sin/cos tables in integer domain
- Tests comparing integer attention output vs float attention output

---

### Phase E4-4: Integer FFN (SiLU-Gated)

**Goal:** Implement the feed-forward network layer in integer arithmetic.

nomic-bert uses a SiLU-gated FFN:
```
gate = matmul(x, W_gate)        → integer matmul
up   = matmul(x, W_up)          → integer matmul
y    = gate * silu(gate) * up    → need integer silu
down = matmul(y, W_down)        → integer matmul
```

**SiLU** = `x * sigmoid(x)` = `x / (1 + exp(-x))`. Same `exp()` challenge as softmax.
Same solution: fixed-point Taylor series, precomputed to bounded precision.

**Deliverables:**
- `IntegerFFN.cs` — gated FFN with integer SiLU
- `IntegerSigmoid.cs` — fixed-point sigmoid (shared with softmax exp)
- Tests and benchmarks

---

### Phase E4-5: Integer Transformer Block (Integration)

**Goal:** Wire E4-1 through E4-4 into a complete integer transformer block and validate
end-to-end on real model weights.

**Architecture:**
```
IntegerTransformerSource : IEmbeddingSource
├── Load(ggufPath)               // reads GGUF, quantizes all weights to long[]
├── Forward(text) → long[]       // full forward pass in integer domain
│   ├── Tokenize(text) → int[]
│   ├── BuildEmbeddings() → long[T × d]     // token + type + position embeddings
│   ├── IntegerLayerNorm()
│   ├── for each layer:
│   │   ├── IntegerAttention()    // QKV, RoPE, scores, softmax, output proj
│   │   ├── Residual add
│   │   ├── IntegerLayerNorm()
│   │   ├── IntegerFFN()          // gate, SiLU, up, down
│   │   ├── Residual add
│   │   └── IntegerLayerNorm()
│   ├── MeanPool()               // integer mean — exact
│   └── L2Normalize()            // integer L2 norm — isqrt
└── EmbedSparse(text) → SparseVector   // quantize output to sparse
```

**The critical measurement:**
Cosine similarity between `IntegerTransformerSource.Forward(text)` (dequantized to float
for comparison) and `TransformerEmbeddingSource.Forward(text)` (current float32 impl).

- If cosine ≥ 0.95: the integer path faithfully reproduces the transformer computation.
  The precision hypothesis is confirmed for this architecture.
- If cosine ≥ 0.80: the integer path captures most of the structure, with quantization
  noise that may or may not matter for downstream tasks. Investigate which layer diverges.
- If cosine < 0.80: the quantization introduces too much error. Increase precision
  (scale factors), investigate accumulation strategies, or accept that some operations
  need higher precision.

**Deliverables:**
- `IntegerTransformerSource.cs` — full integer forward pass
- Fidelity test: integer vs float cosine on the 200-line solution corpus
- Per-layer activation comparison: find where divergence happens
- Memory profile: integer weights vs float weights footprint

---

### Phase E4-6: Causal Model Support (Generate/Think)

**Goal:** Extend the integer transformer to causal (autoregressive) architectures like
Gemma and LLaMA. This is the "move the thinker to the lattice" phase.

**What changes vs encoder (BERT):**
- Causal attention mask (lower-triangular) — trivial in integer: just skip upper-triangle scores
- KV cache — store K and V from previous positions as `long[]` arrays
- Next-token prediction: instead of mean-pool + embedding output, compute logits
  over the vocabulary and sample/argmax
- BPE tokenizer (already implemented: `BpeTokenizer.cs`)

**The integer advantage for generation:**
In autoregressive generation, each new token depends on all previous tokens' KV cache.
Small rounding errors in the KV cache compound over the sequence. At 1000+ tokens,
float16 models measurably degrade — this is why "context window quality" drops off
even within the stated context length.

With integer KV cache, position 1000 has *exactly* the same precision as position 1.
There is no degradation over sequence length. This could be the single most impactful
advantage of integer inference for generation quality.

**Deliverables:**
- `IntegerCausalTransformerSource.cs` — autoregressive integer forward pass
- KV cache management in `long[]` arrays
- Token sampling: greedy (argmax), temperature, top-k, top-p — all implementable in integer
- Integration test: generate text from a small causal model, compare output vs float path
- Benchmark: tokens/second for small (1B) and medium (3B) causal models

**Architecture support:** Start with Gemma 3 (already in TestData as a GGUF). The
`GgufReader` already handles the tensor layout; we just need the causal attention mask
and the generation loop.

---

### Phase E4-7: Lattice-Accelerated Generation

**Goal:** Use the lattice's KNN capability to accelerate token prediction.

**The idea:** Instead of computing a full `[hidden_dim × vocab_size]` matmul for the output
logits (the most expensive single operation in generation), use the lattice to find the
K nearest vocabulary embeddings to the final hidden state. This is a KNN query — exactly
what the lattice was built for.

**How it works:**
1. At model load time, insert all vocabulary token embeddings (output embedding table)
   into an `EmbeddingLattice<int>` where the payload is the token ID.
2. After the final transformer layer, mean-pool or take the last position's hidden state
   as a `SparseVector`.
3. `lattice.QueryKNearestL2(hidden, k=32)` → top-32 candidate tokens.
4. Score only those 32 candidates with the full dot product (32 × 768 multiplies instead
   of 30522 × 768).
5. Sample from the 32-candidate distribution.

**Expected speedup:** For a 30K vocabulary, this replaces 30K dot products with a KNN query
(typically visiting ~100-200 leaf nodes) plus 32 dot products. Roughly 100-300× fewer
FLOPs for the output layer.

**Risk:** The KNN query may miss the true top-1 token if the lattice tree doesn't visit
the right branch. This is acceptable if k is large enough (k=32-64) and we verify recall
on the output embedding table. Our current lattice achieves perfect recall@10 at n=500
with 75% sparse vectors — at n=30K the recall may drop, but k=32 gives much more room.

**Deliverables:**
- `VocabLattice` — `EmbeddingLattice<int>` built from the output embedding table
- Integration into the generation loop
- Recall test: lattice top-32 vs brute-force top-32 on the full vocabulary
- Benchmark: generation speed with and without lattice-accelerated output

---

### Phase E4-8: Validation and Quality Measurement

**Goal:** Answer the question "does integer precision reduce hallucination?"

**Experimental design:**
1. Select a benchmark task with known-correct answers (e.g., factual QA, code completion
   with verifiable output, math word problems).
2. Run the same model through three paths:
   - Float32 (`TransformerEmbeddingSource` / standard generation)
   - Integer (`IntegerTransformerSource` / integer generation)
   - Float16/BF16 (Ollama with quantized model, as a baseline for "GPU-like" precision)
3. Measure:
   - Exact match rate on known answers
   - Confidence calibration (does the model's probability assignment predict correctness?)
   - Consistency: run the same prompt 100 times — float may vary due to non-deterministic
     accumulation order; integer must be bit-identical every time

**What would confirm the hypothesis:**
- Integer path produces correct answers more often than float16/BF16 on factual tasks
- Integer path is perfectly deterministic (100 runs = 100 identical outputs)
- Float path shows measurable variation across runs (even at float32)

**What would refute the hypothesis:**
- Integer and float produce identical quality (meaning epsilon isn't the bottleneck;
  the model's training data is)
- Integer path produces worse quality (meaning the quantization noise from our scale
  choices exceeds float's rounding noise)

Either outcome is valuable. We learn something either way.

**Deliverables:**
- `QualityBenchmark.cs` — automated benchmark runner with JSON result output
- Benchmark results on at least one factual QA dataset
- Determinism test: 100 runs of the same prompt through integer path, verify bit-identity
- Written analysis of results

---

## Risk Register

| Risk | Impact | Mitigation |
|---|---|---|
| Integer matmul too slow on CPU | Generation unusable | SIMD Vector<long>, Int128, explore SIMD intrinsics. Compare against float matmul which is also CPU-only. |
| Integer softmax too imprecise | Attention weights meaningfully wrong | Start with high-precision (128-bit) fixed-point, relax only when tests confirm quality. |
| Quantization scale management complex | Overflow or precision loss between layers | Explicit `ScaledTensor` type carrying value + scale exponent. All operations track scale algebraically. |
| Memory footprint: int64 weights = 2× float32 | May not fit | Use int32 for weights where range allows (most embedding weights are small), int64 for activations only. |
| Hypothesis is wrong (floats are fine) | Work has no quality advantage | Even if quality is identical, the determinism, exact KNN, and vocabulary lattice acceleration are independently valuable. The infrastructure is useful regardless. |
| Causal model architectures vary widely | Gemma/Llama/Mistral each have quirks | Start with one architecture (nomic-bert for embeddings, Gemma for generation). Generalize after validation. |

---

## Success Criteria

### Phase E4-5 gate (embeddings — the first proof point)
- [ ] Integer forward pass produces embeddings with cosine ≥ 0.95 vs float32 forward pass
- [ ] Memory ≤ 2× float32 path (currently ~1 GB for nomic-embed-text)
- [ ] Throughput ≥ 50% of float32 path (it's okay to be slower if we're more precise)
- [ ] 270+ existing tests still pass

### Phase E4-6 gate (generation — the real test)
- [ ] Can generate coherent text from a 2B+ parameter causal model
- [ ] Output is bit-identical across 100 runs of the same prompt
- [ ] Token generation rate ≥ 1 token/sec on CPU (usable for dev/test)

### Phase E4-8 gate (quality — the hypothesis test)
- [ ] Measurable quality comparison between integer and float paths
- [ ] Written conclusion: "integer precision helps / doesn't help / helps for X but not Y"

---

## Dependency Graph

```
E4-1: Integer MatMul ──────────────────────────┐
E4-2: Integer LayerNorm ────────────────────────┤
E4-3: Integer Attention (QKV + Softmax) ────────┼──→ E4-5: Integer Transformer Block
E4-4: Integer FFN (SiLU-Gated) ────────────────┘         │
                                                          ├──→ E4-6: Causal Model Support
                                                          │         │
                                                          │         ├──→ E4-7: Lattice-Accelerated Generation
                                                          │         │
                                                          └─────────┴──→ E4-8: Quality Measurement
```

E4-1 through E4-4 are independent and can be developed in parallel.
E4-5 integrates them and is the first end-to-end validation point.
E4-6 and E4-7 depend on E4-5.
E4-8 depends on E4-6 (needs generation to test hallucination hypothesis).

---

## Starting Point: E4-1

Begin with `IntegerMatMul` — it's the most constrained, most measurable, and most
reusable component. Once we can multiply two integer matrices exactly and know the
performance envelope, every subsequent phase has a foundation to build on.

The key design question for E4-1: **what is the quantization scale for weights?**

Current `LatticeEmbeddingSource` uses `GlobalScale = 1_000_000_000` (1e9). This means
each weight component fits in ~30 bits. A product of two such values fits in ~60 bits —
safely within `long`. A dot product over 768 dimensions: 768 × 60-bit values.
The sum could reach ~70 bits, requiring `Int128` or `BigInteger` for the accumulator.

Proposal: use `Int128` accumulators (native in .NET 8) for dot products, then right-shift
the result back to `long` range. The shift amount is deterministic (sum of the two input
scale exponents). This gives us exact products with controlled, predictable range reduction.

---

*Document created: post-Epic 3, post-perf harness, post-quality tests.*
*Test suite: 269 passing, 1 expected-fail (Q1 fidelity — the very problem this epic solves).*
*Decision: Build it. Measure it. See if the integer hypothesis holds.*
