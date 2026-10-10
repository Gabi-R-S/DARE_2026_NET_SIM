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
from matplotlib.ticker import StrMethodFormatter


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
PARAMETER_SPECS: dict[str, dict[str, Any]] = {
    "replicas": {"label": "Replica count", "baseline": 10, "unit": "replicas"},
    "network-delay": {
        "label": "Network delay range (minimum and maximum jointly)",
        "baseline": None,
        "unit": "",
        "levels": {
            "low": "min=0.001, max=0.01",
            "baseline": "min=0.01, max=0.2",
            "high": "min=0.05, max=0.5",
        },
    },
    "duration": {"label": "Maximum simulation duration", "baseline": 200.0, "unit": "s"},
    "operations": {"label": "Operations per replica", "baseline": 5, "unit": "operations"},
    "send-interval": {"label": "Delay between sends", "baseline": 0.4, "unit": "s"},
    "operation-interval": {"label": "Delay between operations", "baseline": 0.1, "unit": "s"},
    "heartbeat": {"label": "Heartbeat interval", "baseline": 0.1, "unit": "s"},
    "timeout": {"label": "Timeout", "baseline": 15.0, "unit": "s"},
    "hop-limit": {"label": "Hop limit", "baseline": 10, "unit": "hops"},
    "forgetting-time": {"label": "Forgetting time", "baseline": 60.0, "unit": "s"},
    "ack-timeout": {"label": "Acknowledgement timeout", "baseline": 5.0, "unit": "s"},
    "distance-vector-poll": {
        "label": "Distance-vector polling interval",
        "baseline": 10.0,
        "unit": "s",
    },
    "seed": {
        "label": "Random seed (categorical; not a numeric dose)",
        "baseline": None,
        "unit": "",
        "levels": {"low": "1", "baseline": "42", "high": "2026"},
    },
    "minimum-delay": {"label": "Minimum network delay", "baseline": 0.01, "unit": "s"},
    "maximum-delay": {"label": "Maximum network delay", "baseline": 0.2, "unit": "s"},
    "edge-probability": {"label": "Extra-edge probability", "baseline": 0.15, "unit": "probability"},
    "edges-per-node": {"label": "Edges per new node", "baseline": 2, "unit": "edges"},
    "center": {
        "label": "Star center-node choice (categorical)",
        "baseline": None,
        "unit": "",
        "levels": {"first": "node 0", "baseline": "node 0", "last": "node 9"},
    },
}
SCENARIO_PARAMETERS: dict[str, tuple[str, float | None, str]] = {
    "replicas-low": ("replicas", 5, "5"),
    "replicas-high": ("replicas", 20, "20"),
    "network-delay-low": ("network-delay", None, "min=0.001, max=0.01"),
    "network-delay-high": ("network-delay", None, "min=0.05, max=0.5"),
    "duration-low": ("duration", 20.0, "20"),
    "duration-high": ("duration", 500.0, "500"),
    "operations-low": ("operations", 1, "1"),
    "operations-high": ("operations", 10, "10"),
    "send-interval-low": ("send-interval", 0.1, "0.1"),
    "send-interval-high": ("send-interval", 0.8, "0.8"),
    "operation-interval-low": ("operation-interval", 0.025, "0.025"),
    "operation-interval-high": ("operation-interval", 0.2, "0.2"),
    "heartbeat-low": ("heartbeat", 0.05, "0.05"),
    "heartbeat-high": ("heartbeat", 0.5, "0.5"),
    "timeout-low": ("timeout", 2.0, "2"),
    "timeout-high": ("timeout", 30.0, "30"),
    "hop-limit-low": ("hop-limit", 2, "2"),
    "hop-limit-high": ("hop-limit", 20, "20"),
    "forgetting-time-low": ("forgetting-time", 10.0, "10"),
    "forgetting-time-high": ("forgetting-time", 120.0, "120"),
    "ack-timeout-low": ("ack-timeout", 1.0, "1"),
    "ack-timeout-high": ("ack-timeout", 15.0, "15"),
    "distance-vector-poll-low": ("distance-vector-poll", 1.0, "1"),
    "distance-vector-poll-high": ("distance-vector-poll", 20.0, "20"),
    "seed-low": ("seed", None, "1"),
    "seed-high": ("seed", None, "2026"),
    "minimum-delay-low": ("minimum-delay", 0.001, "0.001"),
    "minimum-delay-high": ("minimum-delay", 0.05, "0.05"),
    "maximum-delay-low": ("maximum-delay", 0.05, "0.05"),
    "maximum-delay-high": ("maximum-delay", 0.5, "0.5"),
    "edge-probability-low": ("edge-probability", 0.0, "0.0"),
    "edge-probability-high": ("edge-probability", 0.5, "0.5"),
    "edges-per-node-low": ("edges-per-node", 1, "1"),
    "edges-per-node-high": ("edges-per-node", 4, "4"),
    "center-first": ("center", None, "node 0"),
    "center-last": ("center", None, "node 9"),
}


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

    comparison_rows = _baseline_comparison_rows(results)
    comparison_path = output_dir / "paired_baseline_comparison.csv"
    comparison_fields = [
        "topology",
        "scenario",
        "routing_algorithm",
        "tag",
        "total_messages_sent",
        "baseline_total_messages_sent",
        "messages_sent_change_pct",
        "execution_time",
        "baseline_execution_time",
        "execution_time_change_pct",
        "ended_successfully",
        "baseline_ended_successfully",
        "completion_change_percentage_points",
        "messages_after_close",
        "baseline_messages_after_close",
        "messages_after_close_change",
    ]
    with comparison_path.open("w", newline="", encoding="utf-8") as output_file:
        writer = csv.DictWriter(output_file, fieldnames=comparison_fields)
        writer.writeheader()
        writer.writerows(comparison_rows)


def _mean(values: Sequence[int | float | None]) -> float | str:
    valid = [float(value) for value in values if value is not None]
    return sum(valid) / len(valid) if valid else ""


def plot_baseline_comparison(
    results: list[SimulationResult], output_dir: Path
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
    algorithm_colors = {
        algorithm: plt.get_cmap("tab10")(index)
        for index, algorithm in enumerate(ROUTING_ALGORITHMS)
    }
    measures = (
        ("total_messages_sent", "Messages sent"),
        ("execution_time", "Time to successful completion"),
        ("ended_successfully", "Successful completion (%)"),
        ("messages_after_close", "Messages after close"),
    )
    for topology in topologies:
        for field, title in measures:
            figure, axis = plt.subplots(figsize=(10, 7))
            values = []
            for algorithm in algorithms:
                matching = [
                    getattr(result, field)
                    for result in baseline
                    if result.topology == topology
                    and result.routing_algorithm == algorithm
                    and getattr(result, field) is not None
                ]
                value = _mean(matching)
                numeric_value = float(value) if value != "" else math.nan
                if field == "ended_successfully" and not math.isnan(numeric_value):
                    numeric_value *= 100
                values.append(numeric_value)

            positions = np.arange(len(algorithms))
            axis.bar(
                positions,
                values,
                color=[algorithm_colors[algorithm] for algorithm in algorithms],
                width=0.72,
            )
            if field == "total_messages_sent":
                axis.set_yscale("log")
                axis.set_ylabel("Messages sent (log scale)")
                axis.yaxis.set_major_formatter(StrMethodFormatter("{x:,.0f}"))
            elif field == "messages_after_close":
                axis.set_yscale("symlog", linthresh=10)
                axis.set_ylabel("Messages after close (symlog scale)")
                axis.yaxis.set_major_formatter(StrMethodFormatter("{x:,.0f}"))
            elif field == "ended_successfully":
                axis.set_ylabel("Successful completion (%)")
                axis.set_ylim(0, 110)
            else:
                axis.set_ylabel("Time to successful completion (seconds)")

            for position, value in zip(positions, values):
                if math.isnan(value):
                    continue
                if field in ("total_messages_sent", "messages_after_close"):
                    label = f"{value:,.0f}"
                elif field == "ended_successfully":
                    label = f"{value:.1f}%"
                else:
                    label = f"{value:.4f}"
                axis.annotate(
                    label,
                    (position, value),
                    xytext=(0, 7),
                    textcoords="offset points",
                    ha="center",
                    va="bottom",
                    fontsize=10,
                    clip_on=False,
                )

            axis.set_xticks(positions, algorithms, rotation=25, ha="right", fontsize=10)
            axis.tick_params(axis="y", labelsize=10)
            axis.grid(axis="y", alpha=0.3, which="both")
            axis.set_axisbelow(True)
            axis.set_title(f"{topology.title()} topology — {title}", fontsize=15, pad=14)
            axis.set_xlabel("Routing mode", fontsize=11)
            figure.tight_layout()
            figure.savefig(
                output_dir / f"baseline_{topology}_{field}.png",
                dpi=220,
                bbox_inches="tight",
            )
            plt.close(figure)


def _baseline_comparison_rows(
    results: list[SimulationResult],
) -> list[dict[str, Any]]:
    baselines: dict[tuple[str, str], SimulationResult] = {}
    for result in results:
        if result.scenario != "baseline":
            continue
        key = (result.topology, result.routing_algorithm)
        if key in baselines:
            raise ValueError(
                "More than one baseline result exists for "
                f"topology {result.topology!r} and routing mode "
                f"{result.routing_algorithm!r}."
            )
        baselines[key] = result

    rows: list[dict[str, Any]] = []
    for result in results:
        if result.scenario == "baseline":
            continue
        baseline = baselines.get((result.topology, result.routing_algorithm))
        if baseline is None:
            continue

        messages_sent_change = None
        if baseline.total_messages_sent != 0:
            messages_sent_change = (
                (result.total_messages_sent - baseline.total_messages_sent)
                / abs(baseline.total_messages_sent)
                * 100
            )

        execution_time_change = None
        if (
            result.execution_time is not None
            and baseline.execution_time is not None
            and result.ended_successfully is True
            and baseline.ended_successfully is True
            and baseline.execution_time != 0
        ):
            execution_time_change = (
                (result.execution_time - baseline.execution_time)
                / abs(baseline.execution_time)
                * 100
            )

        completion_change = None
        if (
            result.ended_successfully is not None
            and baseline.ended_successfully is not None
        ):
            completion_change = (
                float(result.ended_successfully)
                - float(baseline.ended_successfully)
            ) * 100

        messages_after_close_change = None
        if (
            result.messages_after_close is not None
            and baseline.messages_after_close is not None
        ):
            messages_after_close_change = (
                result.messages_after_close - baseline.messages_after_close
            )

        rows.append(
            {
                "topology": result.topology,
                "scenario": result.scenario,
                "routing_algorithm": result.routing_algorithm,
                "tag": result.tag,
                "total_messages_sent": result.total_messages_sent,
                "baseline_total_messages_sent": baseline.total_messages_sent,
                "messages_sent_change_pct": messages_sent_change,
                "execution_time": result.execution_time,
                "baseline_execution_time": baseline.execution_time,
                "execution_time_change_pct": execution_time_change,
                "ended_successfully": result.ended_successfully,
                "baseline_ended_successfully": baseline.ended_successfully,
                "completion_change_percentage_points": completion_change,
                "messages_after_close": result.messages_after_close,
                "baseline_messages_after_close": baseline.messages_after_close,
                "messages_after_close_change": messages_after_close_change,
            }
        )
    return rows


def _parameter_metadata(scenario: str) -> dict[str, Any] | None:
    definition = SCENARIO_PARAMETERS.get(scenario)
    if definition is None:
        return None
    parameter, setting_value, setting_label = definition
    variant = scenario.rsplit("-", 1)[-1]
    spec = PARAMETER_SPECS[parameter]
    return {
        "parameter": parameter,
        "parameter_label": spec["label"],
        "variant": variant,
        "setting_value": setting_value,
        "setting_label": setting_label,
        "setting_unit": spec["unit"],
        "setting_kind": "numeric" if spec["baseline"] is not None else "categorical",
    }


def _parameter_response_rows(
    comparison_rows: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    response_rows = []
    for row in comparison_rows:
        metadata = _parameter_metadata(row["scenario"])
        if metadata is None:
            continue
        parameter = metadata["parameter"]
        spec = PARAMETER_SPECS[parameter]
        baseline_label = (
            spec["levels"]["baseline"]
            if spec["baseline"] is None
            else str(spec["baseline"])
        )
        response_rows.append(
            {
                **row,
                **metadata,
                "baseline_setting_value": spec["baseline"],
                "baseline_setting_label": baseline_label,
            }
        )
    return response_rows


def _average_ranks(values: list[float]) -> list[float]:
    order = sorted(range(len(values)), key=values.__getitem__)
    ranks = [0.0] * len(values)
    start = 0
    while start < len(order):
        end = start + 1
        while end < len(order) and values[order[end]] == values[order[start]]:
            end += 1
        average_rank = (start + 1 + end) / 2
        for index in order[start:end]:
            ranks[index] = average_rank
        start = end
    return ranks


def _spearman_rho(x_values: list[float], y_values: list[float]) -> float | None:
    if len(x_values) < 3 or len(x_values) != len(y_values):
        return None
    x_ranks = np.asarray(_average_ranks(x_values), dtype=float)
    y_ranks = np.asarray(_average_ranks(y_values), dtype=float)
    if np.std(x_ranks) == 0 or np.std(y_ranks) == 0:
        return None
    return float(np.corrcoef(x_ranks, y_ranks)[0, 1])


def _result_metric(result: SimulationResult, metric: str) -> float | None:
    if metric == "total_messages_sent":
        return float(result.total_messages_sent)
    if metric == "execution_time":
        if result.ended_successfully is not True or result.execution_time is None:
            return None
        return float(result.execution_time)
    if metric == "completion_rate_pct":
        if result.ended_successfully is None:
            return None
        return 100.0 if result.ended_successfully else 0.0
    if metric == "messages_after_close":
        if result.messages_after_close is None:
            return None
        return float(result.messages_after_close)
    raise ValueError(f"Unknown analysis metric {metric!r}.")


def parameter_trend_rows(
    results: list[SimulationResult],
) -> list[dict[str, Any]]:
    baselines = {
        (result.topology, result.routing_algorithm): result
        for result in results
        if result.scenario == "baseline"
    }
    variations: dict[tuple[str, str, str], list[SimulationResult]] = defaultdict(list)
    for result in results:
        metadata = _parameter_metadata(result.scenario)
        if metadata is not None:
            variations[
                (result.topology, result.routing_algorithm, metadata["parameter"])
            ].append(result)

    metrics = (
        ("total_messages_sent", "Messages sent"),
        ("execution_time", "Time to successful completion"),
        ("completion_rate_pct", "Successful completion"),
        ("messages_after_close", "Messages after close"),
    )
    trend_rows: list[dict[str, Any]] = []
    for (topology, algorithm, parameter), variant_results in variations.items():
        spec = PARAMETER_SPECS[parameter]
        if spec["baseline"] is None:
            continue
        baseline = baselines.get((topology, algorithm))
        if baseline is None:
            continue

        observations: list[tuple[float, SimulationResult]] = [
            (float(spec["baseline"]), baseline)
        ]
        for result in variant_results:
            metadata = _parameter_metadata(result.scenario)
            if metadata is not None and metadata["setting_value"] is not None:
                observations.append((float(metadata["setting_value"]), result))

        for metric, metric_label in metrics:
            measured = [
                (x_value, value)
                for x_value, result in observations
                if (value := _result_metric(result, metric)) is not None
            ]
            rho = _spearman_rho(
                [item[0] for item in measured],
                [item[1] for item in measured],
            )
            trend_rows.append(
                {
                    "topology": topology,
                    "routing_algorithm": algorithm,
                    "parameter": parameter,
                    "parameter_label": spec["label"],
                    "metric": metric,
                    "metric_label": metric_label,
                    "observation_count": len(measured),
                    "spearman_rho_descriptive": rho,
                    "inferential_test_performed": False,
                }
            )
    return sorted(
        trend_rows,
        key=lambda row: (
            row["routing_algorithm"],
            row["topology"],
            row["parameter"],
            row["metric"],
        ),
    )


def write_parameter_analysis_csv(
    response_rows: list[dict[str, Any]],
    trend_rows: list[dict[str, Any]],
    output_dir: Path,
) -> None:
    response_fields = [
        "topology",
        "routing_algorithm",
        "parameter",
        "parameter_label",
        "variant",
        "setting_value",
        "setting_label",
        "setting_unit",
        "setting_kind",
        "baseline_setting_value",
        "baseline_setting_label",
        "scenario",
        "total_messages_sent",
        "baseline_total_messages_sent",
        "messages_sent_change_pct",
        "execution_time",
        "baseline_execution_time",
        "execution_time_change_pct",
        "ended_successfully",
        "baseline_ended_successfully",
        "completion_change_percentage_points",
        "messages_after_close",
        "baseline_messages_after_close",
        "messages_after_close_change",
    ]
    with (output_dir / "parameter_response_data.csv").open(
        "w", newline="", encoding="utf-8"
    ) as output_file:
        writer = csv.DictWriter(output_file, fieldnames=response_fields)
        writer.writeheader()
        writer.writerows(
            {field: row.get(field) for field in response_fields}
            for row in response_rows
        )

    trend_fields = [
        "topology",
        "routing_algorithm",
        "parameter",
        "parameter_label",
        "metric",
        "metric_label",
        "observation_count",
        "spearman_rho_descriptive",
        "inferential_test_performed",
    ]
    with (output_dir / "parameter_trends.csv").open(
        "w", newline="", encoding="utf-8"
    ) as output_file:
        writer = csv.DictWriter(output_file, fieldnames=trend_fields)
        writer.writeheader()
        writer.writerows(trend_rows)


def _format_metric_value(metric: str, value: float) -> str:
    if metric in ("total_messages_sent", "messages_after_close"):
        return f"{value:,.0f}"
    if metric == "completion_rate_pct":
        return f"{value:.0f}%"
    return f"{value:.4g} s"


def _numeric_setting_value(
    point: tuple[str, str, float | None, float, str],
) -> float:
    value = point[2]
    if value is None:
        raise ValueError("A numeric parameter response is missing its setting value.")
    return value


def plot_parameter_responses(
    results: list[SimulationResult],
    response_rows: list[dict[str, Any]],
    trend_rows: list[dict[str, Any]],
    output_dir: Path,
) -> None:
    metric_definitions = (
        ("total_messages_sent", "Messages sent", "symlog"),
        ("execution_time", "Time to successful completion", "linear"),
        ("completion_rate_pct", "Successful completion", "completion"),
        ("messages_after_close", "Messages after close", "symlog"),
    )
    variant_markers = {
        "baseline": "o",
        "low": "v",
        "high": "^",
        "first": "s",
        "last": "D",
    }
    variant_colors = {
        "baseline": "#111827",
        "low": "#2563eb",
        "high": "#ea580c",
        "first": "#059669",
        "last": "#9333ea",
    }
    baselines = {
        (result.topology, result.routing_algorithm): result
        for result in results
        if result.scenario == "baseline"
    }
    by_parameter: dict[tuple[str, str], list[dict[str, Any]]] = defaultdict(list)
    for row in response_rows:
        by_parameter[(row["routing_algorithm"], row["parameter"])].append(row)
    response_dir = output_dir / "parameter_responses"
    effect_dir = output_dir / "parameter_effects"
    response_dir.mkdir(parents=True, exist_ok=True)
    effect_dir.mkdir(parents=True, exist_ok=True)
    trends_by_group = {
        (
            row["routing_algorithm"],
            row["topology"],
            row["parameter"],
            row["metric"],
        ): row
        for row in trend_rows
    }

    for (algorithm, parameter), parameter_rows in sorted(by_parameter.items()):
        spec = PARAMETER_SPECS[parameter]
        topologies = sorted({row["topology"] for row in parameter_rows})
        for metric, metric_label, axis_scale in metric_definitions:
            figure, axes = plt.subplots(2, 3, figsize=(18, 11), squeeze=False)
            flattened_axes = list(axes.flat)
            has_points = False

            for axis, topology in zip(flattened_axes, topologies):
                topology_rows = [
                    row for row in parameter_rows if row["topology"] == topology
                ]
                baseline = baselines.get((topology, algorithm))
                points: list[tuple[str, str, float | None, float, str]] = []
                baseline_value = _result_metric(baseline, metric) if baseline else None
                if baseline_value is not None:
                    baseline_label = (
                        spec["levels"]["baseline"]
                        if spec["baseline"] is None
                        else str(spec["baseline"])
                    )
                    points.append(
                        (
                            "baseline",
                            baseline_label,
                            spec["baseline"],
                            baseline_value,
                            "baseline",
                        )
                    )

                variant_order = {"low": 0, "first": 1, "high": 2, "last": 3}
                for row in sorted(
                    topology_rows,
                    key=lambda item: variant_order.get(item["variant"], 4),
                ):
                    value = row.get(metric)
                    if metric == "completion_rate_pct":
                        value = (
                            None
                            if row["ended_successfully"] is None
                            else 100.0
                            if row["ended_successfully"]
                            else 0.0
                        )
                    elif metric == "execution_time" and row["ended_successfully"] is not True:
                        value = None
                    if value is None:
                        continue
                    points.append(
                        (
                            row["variant"],
                            row["setting_label"],
                            row["setting_value"],
                            float(value),
                            row["scenario"],
                        )
                    )

                if not points:
                    axis.set_visible(False)
                    continue
                has_points = True
                is_categorical = spec["baseline"] is None
                if is_categorical:
                    category_order = ["baseline", "low", "first", "high", "last"]
                    category_positions = {
                        category: index for index, category in enumerate(category_order)
                    }
                    points.sort(key=lambda point: category_positions[point[0]])
                    x_values = [category_positions[point[0]] for point in points]
                    visible_categories = [point[0] for point in points]
                    axis.set_xticks(
                        [category_positions[category] for category in visible_categories],
                        [
                            f"{point[0]}\n{point[1]}"
                            for point in points
                        ],
                    )
                else:
                    points.sort(key=_numeric_setting_value)
                    x_values = [_numeric_setting_value(point) for point in points]
                    axis.set_xticks(
                        x_values,
                        [f"{point[0]}\n{point[1]}" for point in points],
                    )

                y_values = [point[3] for point in points]
                if len(points) >= 2 and not is_categorical:
                    axis.plot(x_values, y_values, color="#94a3b8", linewidth=1.5, zorder=1)

                for x_value, point in zip(x_values, points):
                    variant = point[0]
                    y_value = point[3]
                    axis.scatter(
                        x_value,
                        y_value,
                        marker=variant_markers[variant],
                        color=variant_colors[variant],
                        s=78,
                        edgecolor="white",
                        linewidth=0.8,
                        zorder=3,
                    )
                    axis.annotate(
                        _format_metric_value(metric, y_value),
                        (x_value, y_value),
                        xytext=(5, 5),
                        textcoords="offset points",
                        fontsize=8,
                        color="#111827",
                    )

                if axis_scale == "symlog":
                    axis.set_yscale("symlog", linthresh=1000 if metric == "total_messages_sent" else 10)
                    axis.yaxis.set_major_formatter(StrMethodFormatter("{x:,.0f}"))
                elif axis_scale == "completion":
                    axis.set_ylim(-8, 108)
                    axis.set_yticks([0, 25, 50, 75, 100])
                    axis.yaxis.set_major_formatter(StrMethodFormatter("{x:.0f}%"))

                if is_categorical:
                    axis.set_xlabel("Setting variant (categorical)")
                else:
                    unit = f" ({spec['unit']})" if spec["unit"] else ""
                    axis.set_xlabel(f"{spec['label']}{unit}")
                    trend = trends_by_group.get(
                        (algorithm, topology, parameter, metric)
                    )
                    if trend and trend["spearman_rho_descriptive"] is not None:
                        axis.text(
                            0.03,
                            0.97,
                            f"Spearman ρ={trend['spearman_rho_descriptive']:.2f}, "
                            f"n={trend['observation_count']} (descriptive)",
                            transform=axis.transAxes,
                            va="top",
                            fontsize=8,
                            bbox={"facecolor": "white", "alpha": 0.8, "edgecolor": "none"},
                        )
                axis.set_title(topology, fontsize=12)
                axis.set_ylabel(metric_label)
                axis.grid(axis="y", alpha=0.25, which="both")
                axis.set_axisbelow(True)

            for axis in flattened_axes[len(topologies):]:
                axis.set_visible(False)
            if not has_points:
                plt.close(figure)
                continue

            figure.suptitle(
                f"{algorithm} — isolated parameter response: {spec['label']}\n"
                f"{metric_label}; separate panels are separate topologies",
                fontsize=15,
            )
            figure.tight_layout(rect=(0, 0, 1, 0.94))
            figure.savefig(
                response_dir
                / f"parameter_response_{algorithm}_{parameter}_{metric}.png",
                dpi=220,
                bbox_inches="tight",
            )
            plt.close(figure)

    effect_definitions = (
        ("messages_sent_change_pct", "Messages sent", "change from baseline (%)"),
        ("execution_time_change_pct", "Time to successful completion", "change from successful baseline (%)"),
        (
            "completion_change_percentage_points",
            "Successful completion",
            "change from baseline (percentage points)",
        ),
        (
            "messages_after_close_change",
            "Messages after close",
            "change from baseline (messages)",
        ),
    )
    variant_order = {"low": 0, "first": 1, "high": 2, "last": 3}
    for (algorithm, parameter), parameter_rows in sorted(by_parameter.items()):
        spec = PARAMETER_SPECS[parameter]
        topologies = sorted({row["topology"] for row in parameter_rows})
        for field, metric_label, change_label in effect_definitions:
            figure, axes = plt.subplots(2, 3, figsize=(18, 11), squeeze=False)
            flattened_axes = list(axes.flat)
            has_points = False

            for axis, topology in zip(flattened_axes, topologies):
                topology_rows = sorted(
                    (
                        row
                        for row in parameter_rows
                        if row["topology"] == topology and row[field] is not None
                    ),
                    key=lambda row: variant_order.get(row["variant"], 4),
                )
                if not topology_rows:
                    axis.set_visible(False)
                    continue
                has_points = True

                for position, row in enumerate(topology_rows):
                    change = float(row[field])
                    variant = row["variant"]
                    axis.vlines(
                        position,
                        min(0, change),
                        max(0, change),
                        color=variant_colors[variant],
                        linewidth=2,
                        alpha=0.8,
                    )
                    axis.scatter(
                        position,
                        change,
                        marker=variant_markers[variant],
                        color=variant_colors[variant],
                        edgecolor="white",
                        linewidth=0.8,
                        s=78,
                        zorder=3,
                    )
                    if field.endswith("_pct"):
                        value_label = f"{change:+.2f}%"
                    elif field == "completion_change_percentage_points":
                        value_label = f"{change:+.2f} pp"
                    else:
                        value_label = f"{change:+,.0f}"
                    axis.annotate(
                        value_label,
                        (position, change),
                        xytext=(4, 5 if change >= 0 else -10),
                        textcoords="offset points",
                        fontsize=8,
                    )

                axis.axhline(0, color="#111827", linewidth=1)
                axis.set_xticks(
                    range(len(topology_rows)),
                    [
                        f"{row['variant']}\n{row['setting_label']}"
                        for row in topology_rows
                    ],
                )
                if field == "completion_change_percentage_points":
                    axis.set_ylim(-108, 108)
                else:
                    axis.set_yscale("symlog", linthresh=1 if field.endswith("_pct") else 10)
                axis.set_title(topology, fontsize=12)
                axis.set_ylabel(change_label)
                axis.grid(axis="y", alpha=0.25, which="both")
                axis.set_axisbelow(True)

            for axis in flattened_axes[len(topologies):]:
                axis.set_visible(False)
            if not has_points:
                plt.close(figure)
                continue

            figure.suptitle(
                f"{algorithm} — isolated parameter effect: {spec['label']}\n"
                f"{metric_label}; changes are relative to the same topology/algorithm baseline",
                fontsize=15,
            )
            figure.tight_layout(rect=(0, 0, 1, 0.94))
            figure.savefig(
                effect_dir
                / f"parameter_effect_{algorithm}_{parameter}_{field}.png",
                dpi=220,
                bbox_inches="tight",
            )
            plt.close(figure)


def write_analysis_report(
    results: list[SimulationResult],
    comparison_rows: list[dict[str, Any]],
    output_path: Path,
) -> None:
    topologies = sorted({result.topology for result in results})
    algorithms = [
        algorithm
        for algorithm in ROUTING_ALGORITHMS
        if any(result.routing_algorithm == algorithm for result in results)
    ]
    scenario_count = len({result.scenario for result in results})
    successful_results = [
        result for result in results if result.ended_successfully is True
    ]
    report_lines = [
        "Simulation analysis report",
        "==========================",
        "",
        f"Results analyzed: {len(results)}",
        f"Scenarios: {scenario_count}",
        f"Topologies: {', '.join(topologies)}",
        f"Routing modes: {', '.join(algorithms)}",
        (
            f"Successful results: {len(successful_results)}/{len(results)} "
            f"({100 * len(successful_results) / len(results):.1f}%)"
        ),
        "",
        "Baseline results by topology and routing mode",
        "---------------------------------------------",
        "Topology | Routing mode | Messages sent | Execution time | Completed | Messages after close",
    ]
    baselines = sorted(
        (result for result in results if result.scenario == "baseline"),
        key=lambda result: (result.topology, result.routing_algorithm),
    )
    for result in baselines:
        report_lines.append(
            f"{result.topology} | {result.routing_algorithm} | "
            f"{result.total_messages_sent} | "
            f"{_format_optional(result.execution_time)} | "
            f"{_format_optional(result.ended_successfully)} | "
            f"{_format_optional(result.messages_after_close)}"
        )

    report_lines.extend(
        [
            "",
            "Largest paired changes within each topology",
            "--------------------------------------------",
            "Metric | Topology | Largest increase | Largest decrease",
            "Changes are relative to the same topology and routing mode's baseline. "
            "Messages after close use absolute count changes.",
        ]
    )
    effect_fields = (
        ("messages_sent_change_pct", "Messages sent (%)"),
        ("execution_time_change_pct", "Execution time (%)"),
        ("completion_change_percentage_points", "Completion (pp)"),
        ("messages_after_close_change", "Messages after close (count)"),
    )
    for field, label in effect_fields:
        for topology in topologies:
            matching = [
                row
                for row in comparison_rows
                if row["topology"] == topology and row[field] is not None
            ]
            if not matching:
                report_lines.append(f"{label} | {topology} | no comparable data")
                continue
            largest_increase = max(matching, key=lambda row: row[field])
            largest_decrease = min(matching, key=lambda row: row[field])
            report_lines.append(
                f"{label} | {topology} | "
                f"{largest_increase['scenario']} / "
                f"{largest_increase['routing_algorithm']} "
                f"({largest_increase[field]:+.2f}) | "
                f"{largest_decrease['scenario']} / "
                f"{largest_decrease['routing_algorithm']} "
                f"({largest_decrease[field]:+.2f})"
            )

    report_lines.extend(
        [
            "",
            "Interpretation notes",
            "--------------------",
            "Each input record is one run; the input contains no repeated trials for a "
            "configuration, so these comparisons do not estimate run-to-run "
            "variability or statistical significance.",
            "Parameter effects compare each scenario only with the baseline having "
            "the same topology and routing mode. Execution-time percentage changes "
            "are included only when both runs complete; the C# metric remains zero "
            "when a run does not complete. "
            "Messages-after-close changes are absolute counts.",
            "The network-delay-low/high scenarios change minimum and maximum delay "
            "together, so they are joint-setting comparisons rather than isolated "
            "effects of either delay parameter.",
            "Each parameter level has one simulation run. Spearman coefficients in "
            "parameter_trends.csv are descriptive rank trends (at most three "
            "observations per topology/algorithm/factor), with no p-values, "
            "confidence intervals, or causal claims.",
            "Scenario tags identify the setting variant (for example, low/high); "
            "the JSON does not include the numeric setting values.",
        ]
    )
    output_path.write_text("\n".join(report_lines) + "\n", encoding="utf-8")


def _format_optional(value: Any) -> str:
    if value is None:
        return "not recorded"
    if isinstance(value, bool):
        return "yes" if value else "no"
    if isinstance(value, float):
        return f"{value:.4g}"
    return str(value)


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
        plot_baseline_comparison(results, args.output_dir)
        comparison_rows = _baseline_comparison_rows(results)
        response_rows = _parameter_response_rows(comparison_rows)
        trend_rows = parameter_trend_rows(results)
        write_parameter_analysis_csv(response_rows, trend_rows, args.output_dir)
        plot_parameter_responses(
            results,
            response_rows,
            trend_rows,
            args.output_dir,
        )
        for obsolete_chart in args.output_dir.glob("parameter_sweep_*.png"):
            obsolete_chart.unlink()
        write_analysis_report(
            results,
            comparison_rows,
            args.output_dir / "analysis_report.txt",
        )
    except (OSError, json.JSONDecodeError, ValueError) as error:
        print(f"Error: {error}", file=sys.stderr)
        return 1

    print(f"Analyzed {len(results)} results from {input_path.resolve()}")
    print(f"Plots and CSV reports saved to {args.output_dir.resolve()}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
