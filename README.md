# Distributed Mesh Network Simulation

**Author:** Gabriela Rodrigues da Silva  
**Affiliation:** Faculdade de Engenharia da Universidade do Porto (FEUP)  

---

## Project Overview

This project is an experimental framework designed to evaluate the performance, convergence, and reliability of Conflict-Free Replicated Data Types (CRDTs) operating over decentralized, multi-hop mesh topologies. In decentralized mesh environments, nodes must reach eventual consistency despite variable latency, network churn, and the absence of direct point-to-point links between all replicas. This project simulates various network topologies, including fully connected, ring, star, random mesh, and scale-free networks, and compares multiple communication strategies ranging from naive direct transport and flooding to distance-vector and link-state routing protocols paired with causal tracking and acknowledgment mechanisms.

The system is architected around discrete-event simulation powered by the SimSharp engine, enabling deterministic and reproducible evaluation across broad parameter spaces. Complementing the core simulation libraries is an interactive Windows Presentation Foundation (WPF) desktop application for visualizing topology layouts, packet dissemination, and replica synchronization in real time. For large-scale benchmarking, an automated console runner executes extensive factorial experiments, logging metrics such as message delivery overhead, convergence time, and packet loss, which can then be processed and plotted via an integrated Python analysis pipeline.

---

## Folder Structure

The repository is organized into modular .NET projects and supporting analysis scripts:

```text
DistributedMeshSimulation/
├── CRDTs/                # Core CRDT data structures (e.g., LWW-Register, MV-Register)
├── CRDTApp/              # Node application runtime, schedulers, behaviors, and network layers
├── Simulator/            # Discrete-event (SimSharp) & real-time mesh simulation engines
├── Shared/               # Common abstractions, version vectors, replica IDs, and utilities
├── RunSimulations/       # Console benchmark harness for automated parameter & factorial sweeps
├── VisualizationTest/    # Interactive WPF GUI for configuring and observing live simulations
├── Tests_1/              # MSTest unit and integration test suite
└── OUTPUT_ANALYSIS/      # Python scripts to aggregate metrics, parse results, and generate plots
```

---

## Requirements

### Core (.NET)
- **[.NET 10.0 SDK](https://dotnet.microsoft.com/)** (or later)
- **Windows OS** (required for running the `VisualizationTest` WPF UI; the simulation core, console runner, and tests are cross-platform)

### Output Analysis & Plotting (Python)
- **Python 3.10+**
- Python packages:
  - `pandas`
  - `numpy`
  - `matplotlib`

You can install the required Python packages with:
```bash
pip install pandas numpy matplotlib
```

---

## Launch Instructions

### 1. Run the Interactive GUI (Visualization)
To launch the desktop application to configure network topologies, set latency parameters, and watch simulations run visually:
```bash
dotnet run --project VisualizationTest/VisualizationTest.csproj
```

### 2. Run Automated Batch Simulations
To execute headless benchmark scenarios across all topologies (complete, ring, star, random, scale-free) and output results to a JSON file:
```bash
dotnet run --project RunSimulations/RunSimulations.csproj -c Release -- simulation-results.json
```
*(If no output path argument is provided, it defaults to `simulation-results.json` in the working directory.)*

### 3. Run Unit and Integration Tests
To execute the test suite covering CRDT operations, acknowledgment protocols, causality tracking, and routing logic:
```bash
dotnet test Tests_1/Tests_1.csproj
```

### 4. Generate Analysis Plots and Summaries
Once benchmark results have been generated (e.g., converted or exported to CSV/JSON), switch to the analysis folder to generate charts:
```bash
cd OUTPUT_ANALYSIS
python make_plots.py path/to/simulation_results.csv plots/
```
or run the comprehensive metric analyzer:
```bash
python OUTPUT_ANALYSIS.py
```
