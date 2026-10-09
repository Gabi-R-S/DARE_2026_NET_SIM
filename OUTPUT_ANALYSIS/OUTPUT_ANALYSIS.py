from __future__ import annotations

import argparse
import csv
import json
import math
import re
import sys
from collections import defaultdict
from collections.abc import Sequence
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

import matplotlib

matplotlib.use("Agg")

import matplotlib.pyplot as plt
import numpy as np


TOPOLOGIES = {
    "fully-connected": "complete",
    "scale-free": "scale-free",
    "random": "random",
    "ring": "ring",
    "star": "star",
}
ALGORITHMS = {
    "no-stack": "no-stack",
    "ack-no-routing": "ack-no-routing",
    "basic-hop-flood": "flooding",
    "basic-distance-vector": "distance-vector",
    "basic-link-state": "link-state",
}
METRIC_FIELDS = {
    "ended_successfully": "Value",
    "execution_time": "Value",
    "messages_after_close": "Counter",
}
ROUTING_ALGORITHMS = tuple(ALGORITHMS.values())


@dataclass(frozen=True)
class SimulationResult:
    tag: str
    topology: str
    scenario: str
    routing_algorithm: str
    total_messages_sent: int
    ended_successfully: bool | None
    execution_time: float | None
    messages_after_close: int | None


def parse_tag(tag: str) -> tuple[str, str, str]:
    """Extract topology, scenario, and routing mode from the C# experiment tag."""
    for marker, topology in TOPOLOGIES.items():
        match = re.search(rf"(?:^|-){re.escape(marker)}-", tag)
        if match is None:
            continue

        scenario = tag[: match.start()].rstrip("-") or "baseline"
        experiment_type = tag[match.end() :]
        for suffix, algorithm in ALGORITHMS.items():
            if experiment_type.endswith(suffix):
                return topology, scenario, algorithm
        return topology, scenario, "unknown"

    return "unknown", "unclassified", "unknown"


def _metric_value(metric: dict[str, Any], metric_name: str) -> Any:
    if metric.get("Tag") != metric_name:
        return None
    field = METRIC_FIELDS[metric_name]
    return metric.get(field)


def _extract_metrics(extras: Any, result_index: int) -> dict[str, Any]:
    if not isinstance(extras, list):
        raise ValueError(f"Result {result_index} has no valid 'Extras' array.")

    metrics: dict[str, Any] = {}
    for item in extras:
        if not isinstance(item, dict):
            raise ValueError(f"Result {result_index} contains a malformed metric.")
        metric_name = item.get("Tag")
        if metric_name in METRIC_FIELDS:
            metrics[metric_name] = _metric_value(item, metric_name)

    return metrics


def load_results(input_path: Path) -> list[SimulationResult]:
    with input_path.open(encoding="utf-8-sig") as input_file:
        payload = json.load(input_file)

    if not isinstance(payload, list):
        raise ValueError("The simulation JSON root must be an array of results.")
    if not payload:
        raise ValueError("The simulation JSON contains no result records.")

    results: list[SimulationResult] = []
    for index, item in enumerate(payload):
        if not isinstance(item, dict):
            raise ValueError(f"Result {index} must be a JSON object.")

        tag = item.get("Tag")
        message_count = item.get("TotalMessagesSent")
        if not isinstance(tag, str):
            raise ValueError(f"Result {index} is missing its string 'Tag'.")
        if not isinstance(message_count, int) or isinstance(message_count, bool):
            raise ValueError(f"Result {index} has an invalid 'TotalMessagesSent'.")

        topology, scenario, algorithm = parse_tag(tag)
        metrics = _extract_metrics(item.get("Extras"), index)
        results.append(
            SimulationResult(
                tag=tag,
                topology=topology,
                scenario=scenario,
                routing_algorithm=algorithm,
                total_messages_sent=message_count,
                ended_successfully=metrics.get("ended_successfully"),
                execution_time=metrics.get("execution_time"),
                messages_after_close=metrics.get("messages_after_close"),
            )
        )
    return results


def _result_rows(results: list[SimulationResult]) -> list[dict[str, Any]]:
    return [asdict(result) for result in results]


def write_csv_reports(results: list[SimulationResult], output_dir: Path) -> None:
    rows = _result_rows(results)
    result_path = output_dir / "simulation_results.csv"
    with result_path.open("w", newline="", encoding="utf-8") as output_file:
        writer = csv.DictWriter(output_file, fieldnames=list(rows[0]))
        writer.writeheader()
        writer.writerows(rows)

    grouped: dict[tuple[str, str, str], list[SimulationResult]] = defaultdict(list)
    for result in results:
        grouped[(result.topology, result.scenario, result.routing_algorithm)].append(result)

    summary_path = output_dir / "scenario_summary.csv"
    summary_fields = [
        "topology",
        "scenario",
        "routing_algorithm",
        "result_count",
        "mean_total_messages_sent",
        "mean_execution_time",
        "completion_rate",
        "mean_messages_after_close",
    ]
    with summary_path.open("w", newline="", encoding="utf-8") as output_file:
        writer = csv.DictWriter(output_file, fieldnames=summary_fields)
        writer.writeheader()
        for (topology, scenario, algorithm), group in sorted(grouped.items()):
            writer.writerow(
                {
                    "topology": topology,
                    "scenario": scenario,
                    "routing_algorithm": algorithm,
                    "result_count": len(group),
                    "mean_total_messages_sent": _mean(
                        [result.total_messages_sent for result in group]
                    ),
                    "mean_execution_time": _mean(
                        [result.execution_time for result in group]
                    ),
                    "completion_rate": _mean(
                        [
                            float(result.ended_successfully)
                            for result in group
                            if result.ended_successfully is not None
                        ]
                    ),
                    "mean_messages_after_close": _mean(
                        [result.messages_after_close for result in group]
                    ),
                }
            )


def _mean(values: Sequence[int | float | None]) -> float | str:
    valid = [float(value) for value in values if value is not None]
    return sum(valid) / len(valid) if valid else ""


def plot_baseline_comparison(
    results: list[SimulationResult], output_path: Path
) -> None:
    baseline = [result for result in results if result.scenario == "baseline"]
    if not baseline:
        raise ValueError("No baseline/control results were found in the experiment tags.")

    topologies = sorted({result.topology for result in baseline})
    algorithms = [
        algorithm
        for algorithm in ROUTING_ALGORITHMS
        if any(result.routing_algorithm == algorithm for result in baseline)
    ]
    colormap = plt.get_cmap("tab10")
    measures = (
        ("total_messages_sent", "Messages sent"),
        ("execution_time", "Execution time (seconds)"),
        ("ended_successfully", "Completion rate"),
    )
    figure, axes = plt.subplots(1, len(measures), figsize=(17, 5))

    for axis, (field, title) in zip(axes, measures):
        width = 0.8 / max(len(algorithms), 1)
        positions = np.arange(len(topologies), dtype=float)
        for index, algorithm in enumerate(algorithms):
            values: list[float] = []
            for topology in topologies:
                matching = [
                    getattr(result, field)
                    for result in baseline
                    if result.topology == topology
                    and result.routing_algorithm == algorithm
                    and getattr(result, field) is not None
                ]
                values.append(float(_mean(matching)) if matching else math.nan)
            axis.bar(
                positions + (index - (len(algorithms) - 1) / 2) * width,
                values,
                width=width,
                color=colormap(index / max(len(algorithms), 1)),
                label=algorithm,
            )
        axis.set_title(title)
        axis.set_xticks(positions, topologies, rotation=25, ha="right")
        axis.grid(axis="y", alpha=0.25)

    axes[0].set_ylabel("Messages")
    axes[1].set_ylabel("Seconds")
    axes[2].set_ylabel("Rate")
    axes[2].set_ylim(0, 1.05)
    axes[-1].legend(title="Routing mode", bbox_to_anchor=(1.04, 1), loc="upper left")
    figure.suptitle("Baseline/control comparison across network topologies")
    figure.tight_layout()
    figure.savefig(output_path, dpi=160, bbox_inches="tight")
    plt.close(figure)


def _scenario_order(scenario: str) -> tuple[str, int, str]:
    match = re.fullmatch(r"(.+)-(low|high|first|last)", scenario)
    if match is None:
        return scenario, 0, scenario
    order = {"low": 0, "first": 0, "high": 1, "last": 1}[match.group(2)]
    return match.group(1), order, scenario


def _baseline_relative_changes(
    results: list[SimulationResult],
) -> dict[str, dict[str, list[float]]]:
    baselines = {
        (result.topology, result.routing_algorithm): result
        for result in results
        if result.scenario == "baseline"
    }
    changes: dict[str, dict[str, list[float]]] = defaultdict(
        lambda: defaultdict(list)
    )
    for result in results:
        if result.scenario == "baseline":
            continue
        baseline = baselines.get((result.topology, result.routing_algorithm))
        if baseline is None:
            continue

        for field in ("total_messages_sent", "execution_time"):
            value = getattr(result, field)
            baseline_value = getattr(baseline, field)
            if value is not None and baseline_value is not None and baseline_value != 0:
                changes[field][result.scenario].append(
                    (float(value) - float(baseline_value))
                    / abs(float(baseline_value))
                    * 100
                )

        if (
            result.ended_successfully is not None
            and baseline.ended_successfully is not None
        ):
            changes["ended_successfully"][result.scenario].append(
                (float(result.ended_successfully) - float(baseline.ended_successfully))
                * 100
            )
    return changes


def plot_parameter_sensitivity(
    results: list[SimulationResult], output_path: Path
) -> None:
    changes = _baseline_relative_changes(results)
    measure_titles = (
        ("total_messages_sent", "Messages sent (% change from baseline)"),
        ("execution_time", "Execution time (% change from baseline)"),
        ("ended_successfully", "Completion rate (percentage-point change)"),
    )
    figure, axes = plt.subplots(1, len(measure_titles), figsize=(20, 6))

    for axis, (field, title) in zip(axes, measure_titles):
        scenarios = sorted(changes[field], key=_scenario_order)
        values = [_mean(changes[field][scenario]) for scenario in scenarios]
        if scenarios:
            positions = np.arange(len(scenarios))
            axis.bar(positions, values, color="#3b82a0")
            axis.set_xticks(positions, scenarios, rotation=75, ha="right", fontsize=8)
            axis.axhline(0, color="black", linewidth=0.8)
            axis.grid(axis="y", alpha=0.25)
        else:
            axis.text(
                0.5,
                0.5,
                "No parameter variations to compare",
                ha="center",
                va="center",
                transform=axis.transAxes,
            )
        axis.set_title(title)

    figure.suptitle(
        "Parameter sweeps relative to the matching topology/routing baseline\n"
        "Bars average paired changes across available topology and routing combinations"
    )
    figure.tight_layout()
    figure.savefig(output_path, dpi=160, bbox_inches="tight")
    plt.close(figure)


def _default_input_path() -> Path | None:
    project_root = Path(__file__).resolve().parents[1]
    candidates = (
        project_root / "simulation-results.json",
        project_root / "RunSimulations" / "simulation-results.json",
        Path.cwd() / "simulation-results.json",
    )
    return next((path for path in candidates if path.is_file()), None)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Summarize and plot results from the C# RunSimulations project."
    )
    parser.add_argument(
        "input",
        nargs="?",
        type=Path,
        help="C# simulation-results.json (auto-detected in the repository if omitted)",
    )
    parser.add_argument(
        "-o",
        "--output-dir",
        type=Path,
        default=Path(__file__).resolve().parent / "analysis_output",
        help="directory for plots and CSV summaries",
    )
    args = parser.parse_args(argv)

    input_path = args.input or _default_input_path()
    if input_path is None:
        parser.error(
            "simulation-results.json was not found; pass the JSON path as the first argument."
        )

    try:
        results = load_results(input_path)
        args.output_dir.mkdir(parents=True, exist_ok=True)
        write_csv_reports(results, args.output_dir)
        plot_baseline_comparison(results, args.output_dir / "baseline_comparison.png")
        plot_parameter_sensitivity(
            results, args.output_dir / "parameter_sensitivity.png"
        )
    except (OSError, json.JSONDecodeError, ValueError) as error:
        print(f"Error: {error}", file=sys.stderr)
        return 1

    print(f"Analyzed {len(results)} results from {input_path.resolve()}")
    print(f"Plots and CSV reports saved to {args.output_dir.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
