# Hierarchical Spatial Lattice Engine

A high-performance spatial database system for real-time simulations, built from first principles in C# / .NET 8.

## Performance Highlights

- **2+ million objects/second** sustained tick throughput
- **800,000 concurrent moving entities** with near-linear scaling
- **130x faster queries** than naive linear scans for distance searches
- **Automatic pruning** reduces memory footprint in sparse regions
- **Zero external dependencies** - pure C#/.NET implementation

Performance at scale:

| Object Count | Tick Time | Throughput      |
|--------------|-----------|-----------------|
| 50,000       | 25ms      | 2,000,000/sec   |
| 200,000      | 77ms      | 2,597,403/sec   |
| 800,000      | 385ms     | 2,077,922/sec   |

Query Performance:

| Query Type                | Object Count | Queries | Time  | QPS     |
|---------------------------|--------------|---------|-------|---------|
| Global Distance Queries   | 1M           | 10K     | 263ms | 38,023  |
| Local Neighbor Queries    | 100K         | 10K     | 56ms  | 178,571 |

## What Is This?

A hierarchical octree-based spatial database that dynamically creates nested "sublattices" (coordinate frame transformations) as spatial regions become crowded. This enables:

- **Infinite precision**: 64-bit integer coordinates without floating-point error
- **Automatic load balancing**: Hot spots spawn deeper subdivisions
- **Thread-safe operations**: Concurrent inserts, removes, and queries
- **Memory optimization**: Pruning removes empty branches to save space
- **Generic extensibility**: Add custom behaviors without modifying core code

## Key Features

### Core Spatial Engine
- Dynamic sublattice generation for adaptive spatial partitioning
- Proxy-based transactional insertion with two-phase commit
- Multi-object locking with deadlock prevention
- O(log n) spatial queries and neighbor detection (global and local)
- Stack-based position tracking for nested coordinate spaces
- Automatic pruning of empty branches to optimize memory

### Real-Time Simulation System
- Time-based tick system with velocity thresholds
- Handles 260,000+ moving objects at 10 ticks/second
- Automatic tick propagation through sublattice hierarchy
- Boundary crossing with seamless movement between regions
- Local neighbor queries for efficient proximity detection
- Movement prediction and collision detection ready

### Thread Safety
- Reader-writer locks with recursion support
- Lock-free reads via immutable snapshots
- Thread-local depth tracking for tree traversal
- Atomic promotion from read to write locks

## Quick Start

Create a lattice and insert an object:

    var lattice = new SpatialLattice();
    var obj = new SpatialObject(new LongVector3(1000, 2000, 3000));
    var result = lattice.Insert(obj);

Query by position:

    var leaf = lattice.ResolveOccupyingLeaf(obj);

Query objects within a distance:

    var results = lattice.QueryWithinDistance(new LongVector3(1000, 2000, 3000), 1000UL);

Remove when done:

    lattice.Remove(obj);


### Tickable Simulation

Create tickable lattice and insert moving object:

    var lattice = new TickableSpatialLattice();
    var obj = new TickableSpatialObject(new LongVector3(1000, 1000, 1000));
    lattice.Insert(obj);

Register for ticks and set velocity:

    obj.RegisterForTicks();
    obj.Accelerate(new IntVector3(100, 0, 0));

Tick all objects (moves objects based on elapsed time):

    lattice.Tick();


Query local neighbors from a leaf:

    var leaf = lattice.ResolveOccupyingLeaf(obj);
    var neighbors = leaf.QueryNeighbors(obj.LocalPosition, 500UL);

## Architecture

### Type Hierarchy (Inheritance)

The node type system uses abstract base classes and interfaces:

    ISpatialNode (interface)
      |
      +-- SpatialNode (abstract base with locking)
           |
           +-- ParentNode (abstract, has Children array)
           |    |
           |    +-- OctetParentNode (8-way branching)
           |         |
           |         +-- RootNode (top of lattice)
           |         +-- OctetBranchNode (IChildNode)
           |
           +-- LeafNode (abstract, has Parent)
                |
                +-- VenueLeafNode (occupant storage)
                |    |
                |    +-- LargeLeafNode (capacity: 16)
                |
                +-- SubLatticeBranchNode (nested lattice)

### Structural Composition (Runtime Tree)

At runtime, a lattice is a tree of nodes connected by parent-child relationships:

    SpatialLattice
      owns: RootNode (parent node, subdivides space, top of tree)
        Children[8]: array of IChildNode
          |
          +-- OctetBranchNode (parent node, subdivides space)
          |    Children[8]: more branches or leaves
          |
          +-- VenueLeafNode (leaf, stores objects)
          |
          +-- SubLatticeBranchNode (leaf containing nested lattice)
               owns: SpatialLattice (depth + 1)
                 owns: RootNode...

**Key distinctions:**
- LeafNode and OctetParentNode are siblings in the type hierarchy (both extend SpatialNode)
- RootNode IS AN OctetParentNode (inheritance)
- SpatialLattice HAS A RootNode (composition)
- SubLatticeBranchNode IS A LeafNode but CONTAINS a nested SpatialLattice
- VenueLeafNode and SubLatticeBranchNode are both leaf types, but only VenueLeafNode stores occupants directly

This design allows leaves and parent nodes to be treated uniformly as ISpatialNode while maintaining type safety for their distinct responsibilities.

Extending the system (e.g., adding tick behavior) requires:
1. Define interfaces for new behavior
2. Extend node types
3. Override factory methods
4. Inherit from SpatialLattice<YourRoot>

See LatticeExtensionGuide.txt for detailed instructions.

## Testing

Comprehensive test coverage with many test methods:
- Unit tests: Core functionality and invariant verification
- Integration tests: Multi-lattice coordination and deep insertions
- Stress tests: Concurrent operations with 8+ threads
- Performance tests: Scaling validation to 800k objects, query benchmarks
- Simulation tests: Tickable systems, boundary crossing, pruning, local queries

## Technical Innovations

### 1. Dynamic Sublattice Generation
Automatically creates nested coordinate spaces when regions become crowded, enabling infinite precision and automatic load balancing.

### 2. Generic Type Families
Zero-overhead polymorphism through compile-time specialization. Add new behaviors (like tick systems) without modifying core code.

### 3. Transactional Semantics
Two-phase commit for insertions: acquire locks → validate → create proxy → commit/rollback. Enables thread-safe bulk operations with atomicity guarantees.

### 4. Automatic Pruning
Empty branches are pruned after operations to reduce memory usage and traversal depth in sparse areas.

### 5. Efficient Spatial Queries
O(log n + k) distance-based searches with sphere-octree intersection pruning, plus local neighbor queries starting from leaves.

### 6. Shared Thread-Local State
Worked around critical C# generic type system bug where [ThreadStatic] fields create separate instances per closed generic type. Solution: non-generic holder class for shared state.

## Use Cases

- MMO game servers (player spatial management)
- Real-time strategy games (unit simulation)
- Large-scale agent-based modeling
- Traffic simulation (autonomous vehicles)
- IoT sensor networks (spatial queries)
- GIS applications (real-time updates)

## Real-World Capacity

Based on measured performance:

| Target Rate | Budget per Tick | Capacity                |
|-------------|------------------|-------------------------|
| 10 ticks/sec| 100ms            | ~260,000 moving objects  |
| 20 ticks/sec| 50ms             | ~130,000 moving objects  |
| 60 ticks/sec| 16.6ms           | ~43,000 moving objects   |

Query Capacity:
- Global distance queries: ~38,000 QPS on 1M objects
- Local neighbor queries: Efficient for proximity detection

For reference: Most MMORPGs support 1,000-5,000 concurrent players per shard.
This engine supports 50-250x that capacity at standard game tick rates on a home pc.

## Requirements

- .NET 8 SDK
- C# 12 or later
- No external dependencies

## Documentation

- PerformanceReport.txt - Detailed benchmarks and comparative analysis
- LatticeExtensionGuide.txt - How to add custom behaviors
- FuturePlansWithMichaelValentine.txt - Roadmap and phase progress
- ReadMe.txt - Development history and design decisions

## Performance Notes

All benchmarks measured on:
- CPU: Intel Core i7-12700KF (12 cores, 20 threads)
- RAM: 32GB
- Platform: .NET 8, x64, Debug mode

Performance claims are reproducible via included test suite.

## License

Copyright 2026 Damian Tedrow

Licensed under the Apache License, Version 2.0. See LICENSE file for details.

## Author

**Damian Tedrow**  
Systems Engineer | Performance-Critical Software

Built from first principles in 5 weeks as a demonstration of spatial algorithm expertise and systems programming capability.

Contact: damian.tedrow@gmail.com  
GitHub: @damiant3

---

*This is production software, not a prototype.*
