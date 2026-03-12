# NeuralNavigator — Design Plan

## What This Is

A 3D interactive model explorer. Load a GGUF model, fly through its embedding space
like a video game, hover over tokens to see what they are, click to see neighbors,
trace how a prompt flows through layers. Built on HelixToolkit.Wpf.SharpDX (same as
SpatialGame, proven working in this solution).

---

## Current State (v0.1 — Scaffolding)

The project builds and runs. It has:
- WPF window with HelixToolkit 3D viewport
- Dark theme UI with side panel for controls
- Load GGUF model (file dialog, reads token_embd.weight)
- Project embeddings to 3D (top-3 variance dimensions)
- Render token embedding point cloud (colored spheres)
- Search for a token by text, fly camera to it
- Show K nearest neighbors in the full embedding space
- Highlight selected token and neighbors
- Color modes: by token ID (golden angle hue), magnitude, first-char cluster

---

## Phased Roadmap

### Phase 1: Make It Navigable (Video Game Controls)

The viewport currently uses HelixToolkit's built-in trackball rotation.
That's fine for orbiting, but the user wants **WASD fly-through** like a game.

**Requirements:**
- WASD keys move the camera forward/back/strafe in the camera's local frame
- Mouse look (hold right-click or always-on) rotates the camera
- Scroll wheel adjusts move speed
- Space = up, Ctrl = down (world-space vertical movement)
- Smooth movement (per-frame updates, not jerky key repeats)
- Double-click a point to select it

**Implementation:**
- Custom `InputController` that hooks `PreviewKeyDown`, `PreviewMouseMove`
- Render loop via `CompositionTarget.Rendering` for smooth per-frame updates
- Accumulate velocity from held keys, apply to camera position each frame
- Mouse delta → yaw/pitch rotation of camera look direction

### Phase 2: Hover QuickInfo

When the mouse hovers over a token sphere in the viewport:
- Show a floating tooltip with the token text, ID, magnitude
- Highlight the hovered sphere (emissive glow)
- Use HelixToolkit's built-in hit-testing (`Viewport3DX.FindHits`)

**Challenge:** With 10K+ spheres rendered as a single mesh, hit-testing
needs to map back from triangle index → token index. Options:
1. Render each token as a separate `MeshGeometryModel3D` (simple but slow for 10K+)
2. Use instanced rendering (`InstancingMeshGeometryModel3D`) with instance ID
3. Use the spatial index: project mouse ray into embedding space, find nearest token
   via the projected 3D positions. This is O(n) but n is small after the visibility filter.

**Chosen approach:** Option 3 — project the mouse ray, find nearest projected token by
distance to ray. Works with any number of visible tokens and doesn't require per-token
scene objects.

### Phase 3: Context Menu and Cross-Dimension Navigation

Right-click a token → context menu:
- "See neighbors" — already built, just expose it here
- "Show in other dimensions" — re-project the neighborhood around this token
  using different dimension pairs
- "Trace through layers" — feed this token through the forward pass, show how
  its hidden state moves through the embedding space layer by layer
- "Find cluster" — find all tokens within radius R in the full embedding space
- "Compare to..." — select two tokens, show the vector between them, find tokens
  along that direction (the "King - Man + Woman = Queen" analogy test)

### Phase 4: Layer-by-Layer Activation Visualization

Load `IntegerCausalSource`, run a prompt through the forward pass, capture
the hidden state at each layer. Show the trajectory as a line/trail through
the 3D space.

**Questions to answer visually:**
- Does the hidden state "converge" toward the correct answer token?
- Which layers cause the biggest moves?
- Do different prompts follow similar trajectories through the space?

This requires instrumenting `IntegerCausalSource.ApplyCausalBlock` to return
intermediate states, or a new `ForwardCausalWithTrace()` method.

### Phase 5: Attention Pattern Heatmap

For a given prompt and layer, visualize the attention matrix:
- 2D heatmap overlay (separate panel or toggle)
- 3D visualization: draw lines between attended positions, thickness = weight
- Per-head breakdown: cycle through heads to see what each one learns

### Phase 6: Weight Explorer

Navigate into individual layer weights:
- Select a layer → show its weight matrices as heatmaps
- Select a weight row → project it into the same embedding space
- Find weight rows that are "near" each other (redundancy detection)
- Prune a dimension interactively: zero it out, re-run a prompt, see the effect

---

## Architecture

```
NeuralNavigator/
├── NeuralNavigator.csproj          .NET 8 WPF + HelixToolkit.Wpf.SharpDX
├── App.xaml / App.xaml.cs          Application entry
├── MainWindow.xaml / .cs           Window with viewport + side panel
├── MainViewModel.cs                Core VM: load, project, render, search, select
├── ViewModelBase.cs                INPC base + RelayCommand
├── NeighborInfo.cs                 Neighbor display model
├── NeuralNavigator_Plan.md         This file
│
├── Input/                          (Phase 1)
│   └── FlyCamera.cs               WASD + mouse look camera controller
│
├── Projection/                     (Phase 1-3)
│   ├── PcaProjector.cs            Real PCA via power iteration
│   └── TsneProjector.cs           t-SNE for better cluster separation
│
├── Visualization/                  (Phase 4-6)
│   ├── LayerTraceRenderer.cs       Hidden state trajectory rendering
│   ├── AttentionHeatmap.cs         2D attention matrix view
│   └── WeightExplorer.cs           Layer weight navigation
│
└── Data/                           (shared)
    ├── ModelSession.cs             Loaded model state (reader, embeddings, tokenizer)
    └── TokenSpatialIndex.cs        Fast 3D lookup for hover/click hit testing
```

---

## Dependencies

- **HelixToolkit.Wpf.SharpDX 3.1.2** — proven working in SpatialGame
- **SparseLattice** — GGUF reader, tokenizer, forward pass, lattice KNN

No new NuGet packages needed. Everything builds on what's already in the solution.

---

## UI Color Scheme

Dark theme matching VS Code's default dark:
- Background: `#1e1e1e`
- Panel: `#252526`
- Borders: `#3e3e42`
- Text: `#d4d4d4`
- Keywords: `#569cd6`
- Strings: `#ce9178`
- Numbers: `#b5cea8`
- Comments: `#6a9955`

---

## Ideas for Future Consideration

*From the developer's brainstorming session — captured here for later evaluation:*

### 1. Full Realized Lattice for Training

> "Is there ever a use for the full realized lattice, like maybe during training?
> Maybe you use one of those to handle updates and such then bulk insert into
> sparselattice."

The `EmbeddingLattice<T>` is currently read-only after `Freeze()`. A mutable lattice
that supports insert/delete/update would be useful for:
- **Incremental training:** As weight updates arrive during SGD, insert updated vectors
  into the lattice. The lattice maintains spatial indexing for instant KNN queries.
- **Training-time nearest neighbor lookup:** Instead of full attention over the vocab,
  use the lattice to find the K most relevant tokens during training. This is the
  lattice-accelerated generation idea applied to the training loop.
- **Batch insert after training step:** Collect all weight updates from a training batch,
  then bulk-insert into the lattice. This amortizes the tree-rebalancing cost.

The `EmbeddingLattice` currently uses a KD-tree. For mutable operations, consider:
- B-tree style splitting for inserts (like the spatial DB in this same solution)
- Copy-on-write subtree replacement for lock-free reads during training
- The lattice as a *write-ahead log* that periodically compacts into a frozen tree

This connects to the original SparseLattice vision: the lattice as a database, not
just an index. Updates, transactions, versioning — all the things SpatialDb already has.

### 2. Error Correction Detection via Visualization

> "The actual data in them is... bad. The training also did all this floating point
> garbage. There is error correction going on in there I bet."

The visualizer could help detect this:
- Find token clusters that are "suspiciously tight" — tokens jammed together in
  embedding space that shouldn't be semantically similar
- Find weight rows with nearly identical values across different layers (redundant
  computation = error correction?)
- Compare the effective rank of weight matrices across layers. If later layers have
  lower rank, they may be doing less "real work" and more cleanup.

### 3. Dimensionality Reduction as "Defrag"

> "Now that the data is in an integer space, it could be transformed to a smaller
> dimensionality. Basically like defrag the hard drive."

Steps to test:
1. Compute SVD of the token embedding matrix (262K × 3840)
2. Plot singular values — where's the knee? If the effective rank is 500, the other
   3340 dimensions are noise/redundancy.
3. Project the embeddings into the top-K singular dimensions
4. Run the model with the reduced embeddings — does output quality change?

This is measurable once the forward pass produces coherent output (E5-2).

---

*Created with NeuralNavigator v0.1 scaffolding.*
*304 tests passing. All projects build.*
