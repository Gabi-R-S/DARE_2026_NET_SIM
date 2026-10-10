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

- `baseline_<topology>_<metric>.png`: one image per topology and baseline metric,
  with larger labels and exact values shown directly on each bar. Message counts
  use logarithmic or symmetrical-log scales where appropriate.
- `parameter_sweep_<algorithm>_<topology>_<metric>.png`: parameter effects within
  one routing algorithm and topology, with a separate graph for each metric.
  Low/high or first/last variants are shown together on the same parameter row,
  each compared against that algorithm/topology's baseline. Percentage and
  percentage-point changes are labeled to two decimal places; count changes are
  labeled as messages. Symmetric-log x-axes retain detail for percentage and
  message-count changes when other parameters have much larger effects; completion
  changes use a bounded linear scale. A time change is shown only when both runs
  completed.
- `analysis_report.txt`: baseline results and the largest paired increases and
  decreases, broken down by topology.
- `paired_baseline_comparison.csv`: each scenario result alongside its exact
  topology/routing baseline and the metric deltas.
- `simulation_results.csv`: one normalized row per simulation.
- `scenario_summary.csv`: grouped results by topology, scenario, and routing mode.

Each JSON record represents one run, and the input does not include repeated
trials or the numeric values assigned to low/high settings. The report therefore
describes observed comparisons rather than statistical significance or
run-to-run variability; for numeric scenario settings, consult the C# scenario
definitions. The C# execution-time metric is set only when all insert operations
complete, so a recorded value of zero indicates an unsuccessful run, not a
zero-duration success. Parameter-sweep images use scenario suffixes (`low`,
`high`, `first`, or `last`) as variant labels; numeric values for those variants
are not embedded in the simulation JSON.
