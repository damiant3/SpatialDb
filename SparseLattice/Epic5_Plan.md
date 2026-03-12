# SparseLattice — Epic 5 Plan

# The Integer Lattice as a Lens into Model Structure

## Scope and editing rules

Same as Epic 4: changes restricted to `SparseLattice`, `SparseLattice.Test`, `SparseLattice.Perf`.

---

## Corrections from Review

- **Machine has 32 GB RAM**, not 9 GB. The Half[] optimization was still useful (reduced
  from ~86 GB to ~21 GB, which fits in 32 GB), but the motivation was overstated.
- **Garbled console output** — the `â•"â•â•...` characters were UTF-8 box-drawing chars
  (`╔══╗`) that got corrupted during encoding round-trips. Fixed: replaced with ASCII.
  These were never intentional edits — just encoding damage from tool copy-paste.
- **Cosine 1.000000 is expected, not surprising** — we inherited all the error already
  baked into the model weights. The float32 training process produced those weights, and
  we loaded them faithfully. Matching float32 output proves our arithmetic is correct,
  but it does NOT prove float epsilon doesn't matter. The error is *inside the weights*.
- **Disk-backed intermediate data** is the right approach for large temporaries, not
  trying to fit everything in RAM. Memory-mapped files or chunked streaming to disk.

---

## Where We Actually Are

### What was built (Epic 4 delivered)

1. **Integer math kernel** — Int128 matmul, LayerNorm, RMS norm, attention, SiLU-gated FFN,
   RoPE, fixed-point softmax/exp/sigmoid. All exact. All deterministic.

2. **Two encoder forward passes** — `IntegerTransformerSource` (nomic-bert, BERT encoder)
   and `IntegerGemmaSource` (embeddinggemma, Gemma3 encoder). Both produce embeddings at
   cosine 1.000000 vs their float32 counterparts. 24 layers, GQA, per-head norms — the
   integer stack handles real architectures.

3. **Causal generation** — `IntegerCausalSource` loads Gemma3 4B (48 layers, 3840-dim,
   262K vocab) on a 32 GB RAM machine using Half[] weight storage with on-the-fly
   quantization. Forward pass completes. Greedy generation works. Determinism verified.

4. **VocabLattice** — KD-tree over the output embedding table for KNN-accelerated token
   prediction. Replaces the [hidden × vocab] matmul with a KNN query + K dot products.

5. **Parallelization** — Parallel.For on matmul columns, Parallel.Invoke on independent
   Q/K/V projections and gate/up FFN projections, parallel attention heads.

6. **Mixed-precision storage** — Half[] weights (2 bytes/element) with int64 on-the-fly
   quantization in the dot product inner loop. 4× memory reduction vs long[].

### What the numbers say

| Measurement | Result | What it means |
|---|---|---|
| Integer vs float cosine (embedding, 12-layer BERT) | **1.000000** | Integer path is indistinguishable from float32 |
| Integer vs float cosine (embedding, 24-layer Gemma3) | **1.000000** | Holds across architectures |
| Determinism (10 runs, bit-identity) | **10/10 identical** | Zero nondeterminism — by construction |
| Gemma3 4B generation output | `"manera"` for `"Hi"` | Forward pass works; output is gibberish |
| Generation speed (embeddinggemma, 768-dim) | ~0.5 tok/s | Usable for testing |
| Generation speed (Gemma3 4B, 3840-dim, 48L) | ~0.14 tok/s | Too slow for interactive use, fine for measurement |

---

## Re-examining the Hypothesis

The original thesis:

> **"Floating-point epsilon is a fundamental tax on every computation in modern AI.
> These errors compound through 12+ transformer layers, and the model has no way to
> distinguish signal from accumulated noise. The hypothesis is that this contributes
> to hallucination."**

### Why cosine 1.000000 does NOT answer the question

The cosine 1.0 result proves our *arithmetic* matches float32. But the key insight
from the review is:

**The error is already in the weights.**

The model was *trained* using float32 (or BF16) arithmetic. Every gradient update,
every loss computation, every forward pass during training accumulated float rounding
error. The resulting weight values are the *product* of billions of imprecise operations.
By loading those weights and reproducing their float32 output exactly, we proved our
math is right. We did NOT prove that the weights are free of float-induced error.

The real question is: **how much of what's stored in the weights is signal, and how much
is error correction for float's imprecision during training?**

Consider: if training had been done in exact integer arithmetic from the start, the
gradient updates would follow a different path. The loss landscape would be explored
differently. The resulting weights would be *different* — not just the same weights at
higher precision, but fundamentally different learned representations. Some of the
structure in current weights may exist specifically to compensate for float rounding
during training (analogous to error-correcting codes).

This means the interesting experiment is NOT "does our integer inference match float
inference?" (answer: yes, trivially). The interesting experiments are:

1. **Can we identify redundancy in the integer representation?** Now that the weights
   are in an exact integer space, can we find dimensions/components that exist only
   to compensate for training noise? This would manifest as weight components that
   can be zeroed or collapsed without affecting output quality.

2. **Can we reduce dimensionality?** Like defragmenting a hard drive — reorganize the
   integer weight space to a more compact representation. If float training forced the
   model to spread information across extra dimensions to survive rounding, an integer
   representation might be compressible.

3. **Can we SEE the structure?** Visualize the weight space, navigate it, find concepts
   like "Country" and "Capital City" and see how they're represented. The integer
   representation is exact, so distances and relationships are provably correct.

---

## Epic 5: The Model as Data

The shift: from "run the model" to "examine the model." We have a complete, exact,
navigable integer representation of a real transformer. Use it as a *data structure*
to be analyzed, not just a function to be called.

### Phase E5-1: Disk-Backed Weight Storage

**Goal:** Large intermediate arrays (token embedding expansion, VocabLattice build data,
activation buffers) go to disk instead of RAM.

**Why:** The 4 GB float[] expansion for ArgmaxBruteForce, the VocabLattice build
requiring all token embeddings as float[] — these are unnecessary RAM pressure. With
32 GB total and ~21 GB for model weights, that leaves only ~11 GB for everything else.

**Approach:**
- `MemoryMappedFile` for the token embedding table — read directly from GGUF on disk,
  decode pages on demand
- Chunked ArgmaxBruteForce: scan the embedding table in chunks of 1K tokens, keep only
  the running argmax, never hold the full float[] in memory
- Disk-backed activation checkpointing: for the 48-layer forward pass, write intermediate
  activations to disk and re-read when needed (trades I/O for RAM)

**Gate:** Gemma3 4B forward pass completes with peak RAM ≤ 24 GB (down from ~25 GB).

### Phase E5-2: Fix Generation Quality

**Goal:** Gemma3 4B produces recognizable English.

**Diagnosis approach:**
- Compare per-layer activations between the embeddinggemma model (known working, long[]
  weights) and the same model loaded through the Half[] path. If they diverge: Half
  precision is the problem. If they match: the problem is elsewhere.
- Check whether the GGUF has a separate `output.weight` tensor (some models have tied
  embeddings, some don't)
- Verify the `resultShift` arithmetic in the mixed-precision MatMul

**Gate:** "The capital of France is" → "Paris" (or a plausible completion).

### Phase E5-3: Model Visualizer (WPF)

**Goal:** Interactive visualization of the model's weight space using the existing WPF
infrastructure in the solution.

**What to show:**
- **Token embedding space** — 3D projection (PCA or t-SNE of the integer embeddings)
  of vocabulary tokens, color-coded by category (nouns, verbs, punctuation, etc.)
- **Concept clusters** — select a token like "France", find its K nearest neighbors in
  the embedding lattice, show the cluster. "Paris", "French", "Europe" should be nearby.
- **Attention patterns** — for a given input sequence, visualize which positions attend
  to which. Heatmap of attention weights per layer per head.
- **Layer-by-layer activation flow** — show how a token's representation changes as it
  passes through layers. The 768-dim (or 3840-dim) vector at each layer, projected to 3D.
- **Weight magnitude heatmap** — per-layer, per-component weight magnitude. Large
  weights = important? Or large weights = error correction?

**Architecture:**
```
SpatialGame (existing WPF app with 3D rendering via HelixToolkit)
  └── New views:
      ├── TokenEmbeddingView    — 3D scatter plot of token embeddings
      ├── AttentionView         — heatmap of attention weights
      ├── LayerFlowView         — 3D trajectory of a token through layers
      └── WeightExplorerView    — navigate layer weights, drill into heads
```

The existing `SpatialGame` project already has 3D rendering infrastructure (HelixToolkit,
SharpDX). The `XkcdColors.cs` file suggests it already does some data visualization.

**Why WPF:** It's already in the solution, it has 3D, and it runs on the dev machine.
No web server, no browser, no extra dependencies.

**Gate:** Can navigate to a token, see its neighbors, see what layers do to it.

### Phase E5-4: Redundancy Analysis

**Goal:** Quantify how much of the weight space is "error correction" vs "signal."

**Approach 1: Pruning sensitivity.**
For each dimension d in the embedding space, zero out that dimension across all weights
and measure the change in output. Dimensions that can be zeroed with minimal output
change are candidates for "noise compensation" from training.

**Approach 2: PCA of weight matrices.**
Compute principal components of each layer's weight matrices (in integer space — exact
SVD via integer arithmetic is hard, so use double for the decomposition, integer for
verification). If the weight matrix has effective rank << full rank, the extra dimensions
are either noise or redundancy.

**Approach 3: Integer-space clustering.**
Use the lattice KNN on weight vectors themselves (not just token embeddings). Find
clusters of similar weight rows. If many rows are near-duplicates, the model learned
redundant representations — possibly because float training couldn't reliably distinguish
similar values.

**Gate:** Quantified result: "X% of weight dimensions can be zeroed/collapsed with
< Y% change in output quality."

### Phase E5-5: Compact Integer Representation

**Goal:** If E5-4 finds significant redundancy, produce a smaller model that preserves
output quality.

This is the "defrag" idea: the model's weights, now in exact integer space, may contain
structure that can be factored into a lower-dimensional representation. Not quantization
(which loses precision) — factorization (which preserves exact arithmetic in fewer
dimensions).

**Gate:** Smaller model that produces same or better output than the original.

---

## The Visualizer in More Detail

This is worth expanding because it's the most immediately useful deliverable.

### What "seeing" the model means

A transformer model with 262K vocabulary tokens, each represented as a 3840-dim vector,
is a 262K-point cloud in 3840-dimensional space. The lattice already indexes this cloud
with exact KNN. What we need is a way to:

1. **Project** the 3840-dim space to 3D for display
2. **Navigate** — click on a point, see its token, see its neighbors
3. **Query** — type "France", find it in the embedding space, show what's nearby
4. **Trace** — feed a prompt through the model, watch the hidden state move through
   the embedding space layer by layer
5. **Compare** — overlay two models' embedding spaces (e.g., integer vs original)

### Concrete UI

```
+--------------------------------------------------+
| [Search: ________] [Layer: 0 ▼] [Head: all ▼]   |
|                                                    |
|     3D viewport (HelixToolkit)                    |
|     - points = token embeddings                   |
|     - color = category or attention weight         |
|     - size = magnitude or importance              |
|     - selected point highlighted + label           |
|                                                    |
|  Click a point:                                    |
|    Token: "France"  ID: 12847                      |
|    Nearest: Paris(0.92), French(0.89), EU(0.85)   |
|    Attention from: [the, capital, of, France, is]  |
|                                                    |
+--------------------------------------------------+
| Layer activations for selected token:              |
| L0: ████████░░ L1: █████████░ ... L47: ██████████ |
+--------------------------------------------------+
```

---

## Priority Order

| Phase | Value | Effort | Priority |
|---|---|---|---|
| E5-1: Disk-backed storage | Medium (enables larger models) | Low | 3 |
| E5-2: Fix generation quality | High (blocks quality testing) | Medium | 1 |
| E5-3: Model visualizer | **Very High** (makes the data explorable) | Medium-High | 2 |
| E5-4: Redundancy analysis | High (tests the real hypothesis) | Medium | 4 |
| E5-5: Compact representation | Speculative (depends on E5-4) | High | 5 |

**Start with E5-2** (get generation working), then **E5-3** (build the visualizer).
The visualizer is the single most impactful thing because it turns the abstract
question "is there error correction in the weights?" into something you can *see*.

---

## Dependency Graph

```
E5-2: Fix generation quality
  │
  ├──→ E5-3: Model Visualizer (WPF)
  │      │
  │      └──→ E5-4: Redundancy analysis (use visualizer to explore results)
  │             │
  │             └──→ E5-5: Compact integer representation
  │
  └──→ E5-1: Disk-backed storage (independent, do when RAM is the bottleneck)
```

---

## Success Criteria

### E5-2 gate (generation quality)
- [ ] Gemma3 4B produces recognizable English for known prompts
- [ ] Root cause of gibberish identified and documented

### E5-3 gate (visualizer)
- [ ] Can search for a token and see its neighbors in 3D
- [ ] Can trace a prompt through layers and see the hidden state trajectory
- [ ] Can visualize attention patterns for a given input

### E5-4 gate (redundancy)
- [ ] Quantified: "X% of dimensions contribute < Y% of output variance"
- [ ] Visualized in the WPF tool

### E5-5 gate (compact representation)
- [ ] Reduced-dimension model that preserves output quality
- [ ] Measured compression ratio

---

*Document created: post-Epic 4, post-review corrections.*
*Machine: 32 GB RAM, not 9 GB. Key correction noted.*
*The cosine 1.0 result proves arithmetic correctness, not hypothesis.*
*The real question: what's signal and what's error correction in the weights?*
*The tool to answer it: a visualizer that lets you SEE the model's structure.*
