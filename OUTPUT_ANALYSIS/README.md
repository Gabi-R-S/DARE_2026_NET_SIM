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
- `parameter_responses/parameter_response_<algorithm>_<parameter>_<metric>_<topology>.png`:
  marginal response curves for one parameter, routing algorithm, metric, and
  topology. Every PNG contains exactly one chart. Factorial network sweeps average each level
  across the other crossed network dimensions; standalone protocol parameter
  variations compare each value with the shared baseline. Exact values and
  descriptive Spearman rho
  (numeric factors only) are annotated. Counts use a symmetric-log y-axis;
  completion is displayed as a binary outcome (0% or 100%). Failed runs are
  omitted from completion-time plots and remain visible in completion plots.
- `parameter_effects/parameter_effect_<algorithm>_<parameter>_<metric>_<topology>.png`:
  companion baseline-centered effect plots, also one chart per PNG, showing each
  variant's percentage, percentage-point, or count change against the matching
  baseline.
- `parameter_response_data.csv`: one row per parameter-variation result, including
  the configured numeric setting, baseline setting, raw outcomes, and paired
  changes.
- `parameter_trends.csv`: descriptive Spearman rank correlations between numeric
  settings and each outcome, separately by topology and routing algorithm. For
  factorial settings, trends use the marginal mean at each level. This is not an
  inferential statistical analysis, so the file contains no p-values or confidence intervals.
  Categorical parameters such as random seed and star-center choice are compared
  as categories, not treated as numeric dose-response variables.
- `analysis_report.txt`: baseline results and the largest paired increases and
  decreases, broken down by topology.
- `paired_baseline_comparison.csv`: each scenario result alongside its exact
  topology/routing baseline and the metric deltas.
- `simulation_results.csv`: one normalized row per simulation.
- `scenario_summary.csv`: grouped results by topology, scenario, and routing mode.

Each JSON record represents one run, and the input does not include repeated
trials. The report therefore describes observed comparisons rather than
statistical significance or run-to-run variability. The analysis maps named
scenario labels and factorial scenario tags to the values declared in
`RunSimulations/Program.cs`. Node count and network delay are crossed; random
networks also cross extra-edge probability. Other protocol settings vary
individually against the same baseline. Timeouts are conservatively bounded by
the simulated network delay and node count (acknowledgement timeout allows 1.25
times the worst-case round trip over a simple path). Delay profiles vary minimum
and maximum delay together for random and scale-free networks; complete, ring,
and star networks use only the minimum as their fixed communication delay. The C# execution-time
metric is set only when all insert operations complete, so a recorded value of
zero indicates an unsuccessful run, not a zero-duration success. Delay profiles
are joint-setting comparisons where both delay bounds are consumed, not isolated
effects of either bound.

Spearman rho values and response curves are descriptive trends, not inferential
evidence; they have no p-values or confidence intervals. Replicated randomized
runs are needed to estimate run-to-run uncertainty and support statistical
inference.
