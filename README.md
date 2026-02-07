# Hierarchical Spatial Lattice Engine

A high-performance spatial database system for real-time simulations, built from first principles in C# / .NET 8.

## Performance Highlights

- **2+ million objects/second** sustained tick throughput
- **800,000 concurrent moving entities** with near-linear scaling
- **2-5x faster** than Unity/Unreal spatial systems for dynamic objects
- **Zero external dependencies** - pure C#/.NET implementation

Performance at scale:

Object Count: 50,000   | Tick Time: 25ms  | Throughput: 2,000,000/sec
Object Count: 200,000  | Tick Time: 77ms  | Throughput: 2,597,403/sec
Object Count: 800,000  | Tick Time: 385ms | Throughput: 2,077,922/sec

## What Is This?

A hierarchical octree-based spatial database that dynamically creates nested "sublattices" (coordinate frame transformations) as spatial regions become crowded. This enables:

- **Infinite precision**: 64-bit integer coordinates without floating-point error
- **Automatic load balancing**: Hot spots spawn deeper subdivisions
- **Thread-safe operations**: Concurrent inserts, removes, and queries
- **Generic extensibility**: Add custom behaviors without modifying core code

## Key Features

### Core Spatial Engine
- Dynamic sublattice generation for adaptive spatial partitioning
- Proxy-based transactional insertion with two-phase commit
- Multi-object locking with deadlock prevention
- O(log n) spatial queries and neighbor detection
- Stack-based position tracking for nested coordinate spaces

### Real-Time Simulation System
- Time-based tick system with velocity thresholds
- Handles 260,000+ moving objects at 10 ticks/second
- Automatic tick propagation through sublattice hierarchy
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

Remove when done:

    lattice.Remove(obj);

### Tickable Simulation

Create tickable lattice and insert moving object:

    var lattice = new TickableSpatialLattice();
    var obj = new TickableSpatialObject(new LongVector3(1000, 1000, 1000));
    lattice.Insert(obj);

Register for ticks:

    var leaf = lattice.ResolveOccupyingLeaf(obj) as TickableVenueLeafNode;
    obj.RegisterForTicks();
    obj.Accelerate(new IntVector3(100, 0, 0));

Tick all objects (moves objects based on elapsed time):

    lattice.Tick();

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
- Performance tests: Scaling validation to 800k objects

## Technical Innovations

### 1. Dynamic Sublattice Generation
Automatically creates nested coordinate spaces when regions become crowded, enabling infinite precision and automatic load balancing.

### 2. Generic Type Families
Zero-overhead polymorphism through compile-time specialization. Add new behaviors (like tick systems) without modifying core code.

### 3. Transactional Semantics
Two-phase commit for insertions: acquire locks → validate → create proxy → commit/rollback. Enables thread-safe bulk operations with atomicity guarantees.

### 4. Shared Thread-Local State
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

Target Rate: 10 ticks/sec  | Budget: 100ms per tick  | Capacity: ~260,000 moving objects
Target Rate: 20 ticks/sec  | Budget: 50ms per tick   | Capacity: ~130,000 moving objects
Target Rate: 60 ticks/sec  | Budget: 16.6ms per tick | Capacity: ~43,000 moving objects

For reference: Most MMORPGs support 1,000-5,000 concurrent players per shard.
This engine supports 50-250x that capacity at standard game tick rates on a home pc.

## Requirements

- .NET 8 SDK
- C# 12 or later
- No external dependencies

## Documentation

- PerformanceReport.txt - Detailed benchmarks and comparative analysis
- LatticeExtensionGuide.txt - How to add custom behaviors
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
