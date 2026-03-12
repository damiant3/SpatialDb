# NeuralNavigator — Design Plan

## What This Is

A 3D interactive model explorer. Load a GGUF model, fly through its embedding space
like a video game, hover over tokens to see what they are, click to see neighbors,
trace how a prompt flows through layers. Built on HelixToolkit.Wpf.SharpDX (same as
SpatialGame, proven working in this solution).

---

## Current State (v0.3 — Full Explorer)

The project builds and runs. Implemented:
- WPF window with HelixToolkit 3D viewport (dark theme, compact side panel)
- Load GGUF model (file dialog, reads token_embd.weight, auto-clamps vocab)
- Project embeddings to 3D (top-3 variance dimensions)
- Render token embedding point cloud (colored spheres)
- Search for a token by text, fly camera to it
- Show K nearest neighbors in the full embedding space
- Highlight selected token and neighbors
- Color modes: by token ID (golden angle hue), magnitude, first-char cluster
- **Phase 1: WASD fly-through camera** (FlyCamera.cs)
- **Phase 2: Hover QuickInfo + Double-click select**
- **Phase 3: Context menu** (neighbors, dimensions, cluster, compare)
- **Phase 4: Layer trace** (full forward pass for Gemma, embedding lookup fallback)
- **Phase 5: Attention inspector** (per-layer movement bar chart)
- **Phase 6: Weight explorer** (project weight rows, effective rank analysis)
- **Viewport Options dialog** (⚙ button):
  - Toggle: Model Info HUD, Coordinate System, Camera Info, FPS
  - Toggle: FXAA anti-aliasing, Shadow Mapping
  - Model info displayed as transparent HUD overlay on viewport
  - All HelixToolkit overlays controllable from single options window

---

## Phased Roadmap

### Phase 1: Make It Navigable (Video Game Controls) ✅

Implemented in `FlyCamera.cs`. WASD + mouse look + scroll speed + Space/Ctrl vertical.
Per-frame updates via `CompositionTarget.Rendering`. Right-click-drag for look, left-click
to focus viewport for keyboard. All built-in Viewport3DX camera manipulation left at
defaults (FlyCamera captures its own events via Preview handlers).

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

### Phase 2: Hover QuickInfo ✅

Implemented via `TokenSpatialIndex.cs` ray-cast and `FlyCamera.HoverMove`/`DoubleClick`
events. Manual perspective ray construction from screen coords → camera FOV → 3D ray.
Brute-force nearest-point-to-ray over visible tokens (O(n), n ≤ 50K). Tooltip bound to
`HasHoverText`. Double-click selects token and shows neighbors.

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

### Phase 3: Context Menu and Cross-Dimension Navigation ✅

Implemented via `FlyCamera.RightClick` event (fires on right-button-up when no drag
occurred) and programmatic WPF `ContextMenu` in `MainViewModel.ShowContextMenu()。
Four context menu actions: See Neighbors, Show in Other Dimensions (cycles projection),
Find Cluster (adaptive radius search), Compare To (two-token analogy with vector line).

Right-click a token → context menu:
- "See neighbors" — already built, just expose it here
- "Show in other dimensions" — re-project the neighborhood around this token
  using different dimension pairs
- "Trace through layers" — feed this token through the forward pass, show how
  its hidden state moves through the embedding space layer by layer
- "Find cluster" — find all tokens within radius R in the full embedding space
- "Compare to..." — select two tokens, show the vector between them, find tokens
  along that direction (the "King - Man + Woman = Queen" analogy test)

### Phase 4: Layer-by-Layer Activation Visualization ✅

Implemented via `IntegerCausalSource.ForwardCausalWithTrace()` (new method in
SparseLattice) which captures the last-position hidden state after each transformer
layer. In NeuralNavigator, the "Layer Trace" panel lets you enter a prompt and
visualize the trajectory through the 3D embedding space:

- **Causal models (Gemma):** Full forward pass through all layers, capturing hidden
  state at each layer. Renders as a 3D trail with blue→cyan→green→yellow→red gradient
  showing layer progression. Per-layer movement distance bar chart shows which layers
  cause the biggest hidden-state moves.
- **Encoder models (BERT/nomic):** Falls back to embedding lookup — tokenizes the
  prompt and plots each token's embedding position, showing how the prompt tokens
  are distributed in the space.
- Layer slider flies the camera to each layer's position in the trajectory.
- "Clear Trace" button removes the trace overlay.

New in SparseLattice: `IntegerCausalSource.ForwardCausalWithTrace(int[] tokenIds)`
returns `float[LayerCount+1][dims]` — the hidden state at each layer boundary.

### Phase 5: Attention Inspector ✅ (movement visualization)

Per-layer movement distance bar chart implemented as part of Phase 4. Shows which
layers cause the biggest displacement in the hidden state trajectory. Color-coded
bars (red = large movement, green = small). Layer slider navigates through the trace.

Full attention matrix heatmap (2D per-head visualization) deferred to a future
iteration — requires capturing attention weights during the forward pass.

### Phase 6: Weight Explorer ✅

Weight tensor picker populated from the GGUF file's tensor table (filtered to
2D weight matrices: attn, ffn, embd tensors). "Show" button:
- Reads and dequantizes the selected weight tensor on a background thread
- Projects weight rows to 3D using top-3 variance dimensions
- Renders as a blue point cloud overlaid on the token embedding space
- Computes approximate effective rank (dimensions explaining 90% of variance)
- Reports tensor shape and effective rank in the status area
- Limits to 5000 rows for rendering performance

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
