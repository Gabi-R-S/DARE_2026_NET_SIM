# Simulation output analysis

This Python project reads the JSON array written by `RunSimulations` and creates
comparison plots and CSV reports using Matplotlib.

Install the dependencies:

```powershell
py -m pip install -r requirements.txt
```

Run the C# simulation project first. Its default output is
`simulation-results.json` in the current working directory. Then run the
analysis from the repository root, passing the JSON path if it is not in one
of the automatically searched locations:

```powershell
py OUTPUT_ANALYSIS\OUTPUT_ANALYSIS.py simulation-results.json
```

Optionally choose a different report directory:

```powershell
py OUTPUT_ANALYSIS\OUTPUT_ANALYSIS.py simulation-results.json --output-dir OUTPUT_ANALYSIS\reports
```

The analysis writes:

- `baseline_comparison.png`: baseline/control results by topology and routing mode.
- `parameter_sensitivity.png`: changes relative to the matching topology/routing baseline.
- `simulation_results.csv`: one normalized row per simulation.
- `scenario_summary.csv`: grouped results by topology, scenario, and routing mode.

Sensitivity bars average paired changes across the topology/routing combinations
available for each parameter scenario. Completion-rate changes are in percentage
points; message-count and execution-time changes are percentages.
