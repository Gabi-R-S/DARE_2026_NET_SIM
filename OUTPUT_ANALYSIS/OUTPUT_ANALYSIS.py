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
        "label": "Network delay profile",
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
    "duration-medium": ("duration", 100.0, "100"),
    "duration-high": ("duration", 500.0, "500"),
    "operations-low": ("operations", 1, "1"),
    "operations-medium": ("operations", 6, "6"),
    "operations-high": ("operations", 10, "10"),
    "send-interval-low": ("send-interval", 0.1, "0.1"),
    "send-interval-medium": ("send-interval", 0.45, "0.45"),
    "send-interval-high": ("send-interval", 0.8, "0.8"),
    "operation-interval-low": ("operation-interval", 0.025, "0.025"),
    "operation-interval-medium": ("operation-interval", 0.125, "0.125"),
    "operation-interval-high": ("operation-interval", 0.2, "0.2"),
    "heartbeat-low": ("heartbeat", 0.05, "0.05"),
    "heartbeat-medium": ("heartbeat", 0.2, "0.2"),
    "heartbeat-high": ("heartbeat", 0.5, "0.5"),
    "timeout-low": ("timeout", 2.0, "2"),
    "timeout-medium": ("timeout", 16.0, "16"),
    "timeout-high": ("timeout", 30.0, "30"),
    "hop-limit-low": ("hop-limit", 2, "2"),
    "hop-limit-medium": ("hop-limit", 11, "11"),
    "hop-limit-high": ("hop-limit", 20, "20"),
    "forgetting-time-low": ("forgetting-time", 10.0, "10"),
    "forgetting-time-medium": ("forgetting-time", 65.0, "65"),
    "forgetting-time-high": ("forgetting-time", 120.0, "120"),
    "ack-timeout-low": ("ack-timeout", 1.0, "1"),
    "ack-timeout-medium": ("ack-timeout", 8.0, "8"),
    "ack-timeout-high": ("ack-timeout", 15.0, "15"),
    "distance-vector-poll-low": ("distance-vector-poll", 1.0, "1"),
    "distance-vector-poll-medium": ("distance-vector-poll", 12.0, "12"),
    "distance-vector-poll-high": ("distance-vector-poll", 20.0, "20"),
    "seed-low": ("seed", None, "1"),
    "seed-high": ("seed", None, "2026"),
    "minimum-delay-low": ("minimum-delay", 0.001, "0.001"),
    "minimum-delay-medium": ("minimum-delay", 0.025, "0.025"),
    "minimum-delay-high": ("minimum-delay", 0.05, "0.05"),
    "maximum-delay-low": ("maximum-delay", 0.05, "0.05"),
    "maximum-delay-medium": ("maximum-delay", 0.3, "0.3"),
    "maximum-delay-high": ("maximum-delay", 0.5, "0.5"),
    "edge-probability-low": ("edge-probability", 0.0, "0.0"),
    "edge-probability-high": ("edge-probability", 0.5, "0.5"),
    "edges-per-node-low": ("edges-per-node", 1, "1"),
    "edges-per-node-medium": ("edges-per-node", 3, "3"),
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
        replica_match = re.fullmatch(r"replicas-(\d+)", scenario)
        probability_match = re.fullmatch(
            r"edge-probability-(\d+(?:\.\d+)?|\.\d+)", scenario
        )
        delay_match = re.fullmatch(
            r"network-delay-(\d+(?:\.\d+)?)-(\d+(?:\.\d+)?)", scenario
        )
        ack_timeout_match = re.fullmatch(r"ack-timeout-(\d+(?:\.\d+)?)", scenario)
        if replica_match:
            value = int(replica_match.group(1))
            definition = ("replicas", value, str(value))
        elif probability_match:
            value = float(probability_match.group(1))
            definition = ("edge-probability", value, f"{value:g}")
        elif delay_match:
            minimum, maximum = (float(value) for value in delay_match.groups())
            definition = (
                "network-delay",
                None,
                f"min={minimum:g}, max={maximum:g}",
            )
        elif ack_timeout_match:
            value = float(ack_timeout_match.group(1))
            definition = ("ack-timeout", value, f"{value:g}")
    if definition is None:
        return None
    parameter, setting_value, setting_label = definition
    variant = (
        "level"
        if re.fullmatch(r"network-delay-\d+(?:\.\d+)?-\d+(?:\.\d+)?", scenario)
        else str(setting_value)
        if scenario.startswith(("replicas-", "edge-probability-"))
        else scenario.rsplit("-", 1)[-1]
    )
    spec = PARAMETER_SPECS[parameter]
    return {
        "parameter": parameter,
        "parameter_label": spec["label"],
        "variant": variant,
        "setting_value": setting_value,
        "setting_label": setting_label,
        "setting_unit": spec["unit"],
        "setting_kind": "numeric" if spec["baseline"] is not None else "categorical",
        "factorial": False,
    }


def _scenario_parameter_metadata(
    scenario: str, topology: str
) -> list[dict[str, Any]]:
    match = re.fullmatch(
        r"factorial-nodes-(\d+)-delay-"
        r"(\d+(?:\.\d+)?)-(\d+(?:\.\d+)?)"
        r"(?:-edge-(\d+(?:\.\d+)?))?",
        scenario,
    )
    if match is None:
        metadata = _parameter_metadata(scenario)
        return [metadata] if metadata is not None else []

    replicas = int(match.group(1))
    minimum, maximum = float(match.group(2)), float(match.group(3))
    replica_metadata = _parameter_metadata(f"replicas-{replicas}")
    delay_metadata = _parameter_metadata(f"network-delay-{minimum:g}-{maximum:g}")
    if replica_metadata is None or delay_metadata is None:
        raise ValueError(f"Unable to decode factorial settings from {scenario!r}.")
    factor_metadata = [
        {
            **replica_metadata,
            "factorial": True,
        },
        {
            **delay_metadata,
            "factorial": True,
        },
    ]
    if topology not in ("random", "scale-free"):
        delay = minimum
        factor_metadata[1].update(
            setting_value=delay,
            setting_label=f"{delay:g} s",
            setting_kind="numeric",
            baseline_setting_label="0.01 s",
        )
    if match.group(4) is not None:
        probability = float(match.group(4))
        probability_metadata = _parameter_metadata(
            f"edge-probability-{probability:g}"
        )
        if probability_metadata is None:
            raise ValueError(f"Unable to decode edge probability from {scenario!r}.")
        factor_metadata.append(
            {
                **probability_metadata,
                "factorial": True,
            }
        )
    return factor_metadata


def _parameter_response_rows(
    comparison_rows: list[dict[str, Any]],
) -> list[dict[str, Any]]:
    response_rows = []
    for row in comparison_rows:
        for metadata in _scenario_parameter_metadata(
            row["scenario"], row["topology"]
        ):
            parameter = metadata["parameter"]
            spec = PARAMETER_SPECS[parameter]
            baseline_label = metadata.get(
                "baseline_setting_label",
                spec["levels"]["baseline"]
                if spec["baseline"] is None
                else str(spec["baseline"]),
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
    variations: dict[
        tuple[str, str, str], list[tuple[SimulationResult, dict[str, Any]]]
    ] = defaultdict(list)
    for result in results:
        for metadata in _scenario_parameter_metadata(result.scenario, result.topology):
            variations[
                (result.topology, result.routing_algorithm, metadata["parameter"])
            ].append((result, metadata))

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

        factorial_rows = [
            (result, metadata)
            for result, metadata in variant_results
            if metadata["factorial"]
        ]
        for metric, metric_label in metrics:
            if factorial_rows:
                grouped_metrics: dict[float, list[float]] = defaultdict(list)
                for result, metadata in factorial_rows:
                    value = metadata["setting_value"]
                    metric_value = _result_metric(result, metric)
                    if value is not None and metric_value is not None:
                        grouped_metrics[float(value)].append(metric_value)
                measured = [
                    (value, sum(values) / len(values))
                    for value, values in grouped_metrics.items()
                ]
            else:
                measured = [
                    (float(spec["baseline"]), baseline_value)
                    for baseline_value in [_result_metric(baseline, metric)]
                    if baseline_value is not None
                ]
                measured.extend(
                    (float(metadata["setting_value"]), metric_value)
                    for result, metadata in variant_results
                    if metadata["setting_value"] is not None
                    and (metric_value := _result_metric(result, metric)) is not None
                )
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


def _factorial_marginal_values(
    rows: list[dict[str, Any]], metric: str
) -> dict[tuple[float | None, str], float]:
    grouped: dict[tuple[float | None, str], list[float]] = defaultdict(list)
    for row in rows:
        if metric == "completion_rate_pct":
            value = (
                None
                if row["ended_successfully"] is None
                else 100.0
                if row["ended_successfully"]
                else 0.0
            )
        elif metric == "execution_time":
            value = (
                row["execution_time"]
                if row["ended_successfully"] is True
                else None
            )
        else:
            value = row[metric]
        if value is not None:
            grouped[(row["setting_value"], row["setting_label"])].append(
                float(value)
            )
    return {
        key: sum(values) / len(values)
        for key, values in grouped.items()
        if values
    }


def _numeric_setting_value(
    point: tuple[str, str, float | None, float],
) -> float:
    value = point[2]
    if value is None:
        raise ValueError("A numeric parameter response is missing its setting value.")
    return value


def plot_parameter_responses(
    results: list[SimulationResult],
    response_rows: list[dict[str, Any]],
    output_dir: Path,
) -> None:
    metric_definitions = (
        ("total_messages_sent", "Messages sent", "symlog"),
        ("execution_time", "Successful completion time (s)", "linear"),
        ("completion_rate_pct", "Successful completion (%)", "completion"),
        ("messages_after_close", "Messages after close", "symlog"),
    )
    variant_colors = {
        "baseline": "#334155",
        "low": "#2563eb",
        "medium": "#0f766e",
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

    for (algorithm, parameter), parameter_rows in sorted(by_parameter.items()):
        spec = PARAMETER_SPECS[parameter]
        for topology in sorted({row["topology"] for row in parameter_rows}):
            topology_rows = [
                row for row in parameter_rows if row["topology"] == topology
            ]
            factorial_rows = [row for row in topology_rows if row["factorial"]]
            baseline = baselines.get((topology, algorithm))

            for metric, metric_label, axis_scale in metric_definitions:
                points: list[tuple[str, str, float | None, float]] = []
                if factorial_rows:
                    marginal = _factorial_marginal_values(factorial_rows, metric)
                    points.extend(
                        (
                            str(setting_value)
                            if setting_value is not None
                            else setting_label,
                            setting_label,
                            setting_value,
                            value,
                        )
                        for (setting_value, setting_label), value in marginal.items()
                    )
                else:
                    baseline_value = (
                        _result_metric(baseline, metric) if baseline else None
                    )
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
                            )
                        )
                    for row in topology_rows:
                        value = row.get(metric)
                        if metric == "completion_rate_pct":
                            value = (
                                None
                                if row["ended_successfully"] is None
                                else 100.0
                                if row["ended_successfully"]
                                else 0.0
                            )
                        elif (
                            metric == "execution_time"
                            and row["ended_successfully"] is not True
                        ):
                            value = None
                        if value is not None:
                            points.append(
                                (
                                    row["variant"],
                                    row["setting_label"],
                                    row["setting_value"],
                                    float(value),
                                )
                            )

                if not points:
                    continue
                is_categorical = any(point[2] is None for point in points)
                if is_categorical:
                    points.sort(
                        key=lambda point: (
                            0 if point[0] == "baseline" else 1,
                            point[1],
                        )
                    )
                    x_values = list(range(len(points)))
                else:
                    points.sort(key=_numeric_setting_value)
                    x_values = [_numeric_setting_value(point) for point in points]

                figure, axis = plt.subplots(figsize=(10, 6))
                figure.patch.set_facecolor("white")
                axis.set_facecolor("#fbfdff")
                axis.grid(axis="y", alpha=0.22, which="both")
                axis.set_axisbelow(True)
                axis.spines[["top", "right"]].set_visible(False)
                axis.spines[["left", "bottom"]].set_color("#cbd5e1")

                y_values = [point[3] for point in points]
                axis.plot(
                    x_values,
                    y_values,
                    color="#94a3b8",
                    linewidth=2,
                    zorder=1,
                )
                for index, (x_value, point) in enumerate(zip(x_values, points)):
                    variant, _, _, y_value = point
                    color = variant_colors.get(variant, "#2563eb")
                    axis.scatter(
                        x_value,
                        y_value,
                        color=color,
                        s=76,
                        edgecolor="white",
                        linewidth=1.2,
                        zorder=3,
                    )
                    if len(points) <= 9:
                        axis.annotate(
                            _format_metric_value(metric, y_value),
                            (x_value, y_value),
                            xytext=(0, 9 if index % 2 == 0 else -16),
                            textcoords="offset points",
                            ha="center",
                            fontsize=8,
                            color="#334155",
                        )

                if is_categorical:
                    axis.set_xticks(
                        x_values,
                        [point[1].replace(", max=", "\nmax=") for point in points],
                    )
                    axis.set_xlabel(spec["label"])
                else:
                    axis.set_xticks(
                        x_values,
                        [point[1] for point in points],
                    )
                    unit = f" ({spec['unit']})" if spec["unit"] else ""
                    axis.set_xlabel(f"{spec['label']}{unit}")

                if axis_scale == "symlog":
                    threshold = (
                        1000
                        if metric == "total_messages_sent"
                        else 10
                    )
                    axis.set_yscale("symlog", linthresh=threshold)
                    axis.yaxis.set_major_formatter(StrMethodFormatter("{x:,.0f}"))
                elif axis_scale == "completion":
                    axis.set_ylim(-8, 108)
                    axis.set_yticks([0, 25, 50, 75, 100])
                    axis.yaxis.set_major_formatter(StrMethodFormatter("{x:.0f}%"))
                axis.set_ylabel(metric_label)
                axis.set_title(
                    f"{topology.replace('-', ' ').title()} — "
                    f"{algorithm.replace('-', ' ').title()}",
                    loc="left",
                    fontsize=13,
                    fontweight="bold",
                    color="#0f172a",
                    pad=12,
                )
                axis.tick_params(axis="both", colors="#475569", labelsize=9)
                axis.margins(x=0.08, y=0.18)
                figure.suptitle(
                    f"{spec['label']} response",
                    x=0.1,
                    y=0.99,
                    ha="left",
                    fontsize=17,
                    fontweight="bold",
                    color="#0f172a",
                )
                figure.tight_layout(rect=(0, 0, 1, 0.94))
                figure.savefig(
                    response_dir
                    / f"parameter_response_{algorithm}_{parameter}_{metric}_{topology}.png",
                    dpi=180,
                    bbox_inches="tight",
                    facecolor=figure.get_facecolor(),
                )
                plt.close(figure)

            effect_definitions = (
                ("messages_sent_change_pct", "Messages sent", "change from baseline (%)"),
                ("execution_time_change_pct", "Successful completion time", "change from baseline (%)"),
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
            effect_metrics = {
                "messages_sent_change_pct": "total_messages_sent",
                "execution_time_change_pct": "execution_time",
                "completion_change_percentage_points": "completion_rate_pct",
                "messages_after_close_change": "messages_after_close",
            }
            for field, metric_label, change_label in effect_definitions:
                if factorial_rows:
                    metric = effect_metrics[field]
                    marginal = _factorial_marginal_values(factorial_rows, metric)
                    if parameter == "network-delay":
                        baseline_label = (
                            "min=0.01, max=0.2"
                            if topology in ("random", "scale-free")
                            else "0.01 s"
                        )
                        baseline_key = (
                            (None, baseline_label)
                            if topology in ("random", "scale-free")
                            else (0.01, baseline_label)
                        )
                    else:
                        baseline_key = (
                            spec["baseline"],
                            str(spec["baseline"]),
                        )
                    baseline_value = marginal.get(baseline_key)
                    effect_rows = []
                    if baseline_value is not None:
                        for (setting_value, setting_label), value in marginal.items():
                            if field.endswith("_pct"):
                                change = (
                                    None
                                    if baseline_value == 0
                                    else (value - baseline_value)
                                    / abs(baseline_value)
                                    * 100
                                )
                            else:
                                change = value - baseline_value
                            if change is not None:
                                effect_rows.append(
                                    (setting_label, float(change), setting_value)
                                )
                else:
                    effect_rows = [
                        (
                            row["setting_label"],
                            float(row[field]),
                            row["setting_value"],
                        )
                        for row in topology_rows
                        if row[field] is not None
                    ]
                if not effect_rows:
                    continue
                effect_rows.sort(
                    key=lambda row: (
                        row[2] is None,
                        row[2] if row[2] is not None else row[0],
                    )
                )

                figure, axis = plt.subplots(figsize=(10, 5.5))
                figure.patch.set_facecolor("white")
                axis.set_facecolor("#fbfdff")
                axis.grid(axis="y", alpha=0.22, which="both")
                axis.set_axisbelow(True)
                axis.spines[["top", "right"]].set_visible(False)
                axis.spines[["left", "bottom"]].set_color("#cbd5e1")
                positions = np.arange(len(effect_rows))
                changes = [row[1] for row in effect_rows]
                axis.vlines(
                    positions,
                    0,
                    changes,
                    color="#93c5fd",
                    linewidth=2,
                    zorder=1,
                )
                axis.scatter(
                    positions,
                    changes,
                    color=["#dc2626" if value < 0 else "#2563eb" for value in changes],
                    edgecolor="white",
                    linewidth=1.2,
                    s=70,
                    zorder=3,
                )
                axis.axhline(0, color="#334155", linewidth=1)
                if len(effect_rows) <= 9:
                    for position, change in zip(positions, changes):
                        label = (
                            f"{change:+.1f}%"
                            if field.endswith("_pct")
                            else f"{change:+.1f} pp"
                            if field == "completion_change_percentage_points"
                            else f"{change:+,.0f}"
                        )
                        axis.annotate(
                            label,
                            (position, change),
                            xytext=(0, 7 if change >= 0 else -15),
                            textcoords="offset points",
                            ha="center",
                            fontsize=8,
                            color="#334155",
                        )
                axis.set_xticks(
                    positions,
                    [row[0] for row in effect_rows],
                    rotation=25,
                    ha="right",
                )
                if field == "completion_change_percentage_points":
                    axis.set_ylim(-108, 108)
                else:
                    axis.set_yscale(
                        "symlog",
                        linthresh=1 if field.endswith("_pct") else 10,
                    )
                axis.set_ylabel(change_label)
                axis.set_title(
                    f"{topology.replace('-', ' ').title()} — "
                    f"{algorithm.replace('-', ' ').title()}",
                    loc="left",
                    fontsize=13,
                    fontweight="bold",
                    color="#0f172a",
                    pad=12,
                )
                axis.tick_params(axis="both", colors="#475569", labelsize=9)
                figure.suptitle(
                    f"{spec['label']} effect",
                    x=0.1,
                    y=0.99,
                    ha="left",
                    fontsize=17,
                    fontweight="bold",
                    color="#0f172a",
                )
                figure.tight_layout(rect=(0, 0, 1, 0.94))
                figure.savefig(
                    effect_dir
                    / f"parameter_effect_{algorithm}_{parameter}_{field}_{topology}.png",
                    dpi=180,
                    bbox_inches="tight",
                    facecolor=figure.get_facecolor(),
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
            "Network-delay profiles vary minimum and maximum delay together for "
            "random and scale-free topologies. Complete, ring, and star topologies "
            "use the minimum as their fixed communication delay; their maximum "
            "setting is not consumed by those network constructors.",
            "The network-factorial response curves and effects average across all "
            "other crossed factor levels; one-factor parameter variations compare "
            "directly with the shared baseline. Spearman coefficients in "
            "parameter_trends.csv are descriptive rank trends across the available "
            "marginal setting means, with no p-values, "
            "confidence intervals, or causal claims.",
            "Scenario tags identify each setting level. Numeric node-count and "
            "edge-probability tags encode their value; network-delay tags encode "
            "the minimum and maximum delay. The analysis extracts those values "
            "from the tags.",
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
            args.output_dir,
        )
        for obsolete_chart in args.output_dir.glob("parameter_sweep_*.png"):
            obsolete_chart.unlink()
        for chart_directory, prefix in (
            (args.output_dir / "parameter_responses", "parameter_response_"),
            (args.output_dir / "parameter_effects", "parameter_effect_"),
        ):
            if chart_directory.is_dir():
                for composite_chart in chart_directory.glob(f"{prefix}*.png"):
                    if not any(
                        composite_chart.stem.endswith(f"_{topology}")
                        for topology in TOPOLOGIES.values()
                    ):
                        composite_chart.unlink()
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
