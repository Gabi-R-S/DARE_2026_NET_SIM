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


def _parameter_variant(scenario: str) -> tuple[str, str]:
    match = re.fullmatch(r"(.+)-(low|high|first|last)", scenario)
    if match is None:
        return scenario, "variant"
    return match.group(1), match.group(2)


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


def plot_parameter_sweeps_by_algorithm(
    results: list[SimulationResult], output_dir: Path
) -> None:
    comparison_rows = _baseline_comparison_rows(results)
    measures = (
        (
            "messages_sent_change_pct",
            "Messages sent",
            "change from baseline (%)",
        ),
        (
            "execution_time_change_pct",
            "Time to completion",
            "change from successful baseline (%)",
        ),
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

    topologies = sorted({result.topology for result in results})
    algorithms = [
        algorithm
        for algorithm in ROUTING_ALGORITHMS
        if any(result.routing_algorithm == algorithm for result in results)
    ]
    variant_order = {"low": 0, "high": 1, "first": 2, "last": 3, "variant": 4}
    variant_colors = {
        "low": "#2563eb",
        "high": "#ea580c",
        "first": "#059669",
        "last": "#9333ea",
        "variant": "#64748b",
    }

    for topology in topologies:
        for algorithm in algorithms:
            matching_rows = [
                row
                for row in comparison_rows
                if row["topology"] == topology
                and row["routing_algorithm"] == algorithm
            ]
            if not matching_rows:
                continue

            parameter_variants: dict[str, list[tuple[str, dict[str, Any]]]] = (
                defaultdict(list)
            )
            for row in matching_rows:
                parameter, variant = _parameter_variant(row["scenario"])
                parameter_variants[parameter].append((variant, row))

            for field, metric, change_label in measures:
                sweep_rows = sorted(
                    (
                        (parameter, variant, row)
                        for parameter, variants in parameter_variants.items()
                        for variant, row in variants
                        if row[field] is not None
                    ),
                    key=lambda item: (
                        item[0],
                        variant_order[item[1]],
                    ),
                )
                if not sweep_rows:
                    continue

                figure_height = max(7, 0.4 * len(sweep_rows))
                figure, axis = plt.subplots(figsize=(13, figure_height))
                for y_position, (parameter, variant, row) in enumerate(sweep_rows):
                    change = row[field]
                    color = variant_colors[variant]

                    axis.hlines(
                        y_position,
                        min(0, change),
                        max(0, change),
                        color=color,
                        linewidth=2,
                        alpha=0.75,
                    )
                    axis.scatter(
                        change,
                        y_position,
                        color=color,
                        edgecolor="white",
                        linewidth=0.8,
                        s=64,
                        zorder=3,
                        label=variant,
                    )
                    if field.endswith("_pct"):
                        value_label = f"{change:+.2f}%"
                    elif field == "completion_change_percentage_points":
                        value_label = f"{change:+.2f} pp"
                    else:
                        value_label = f"{change:+,.0f}"
                    axis.annotate(
                        value_label,
                        (change, y_position),
                        xytext=(6, 0),
                        textcoords="offset points",
                        va="center",
                        fontsize=8.5,
                        color="#111827",
                        clip_on=False,
                    )

                axis.axvline(0, color="#111827", linewidth=1, alpha=0.8)
                if field == "completion_change_percentage_points":
                    axis.set_xlim(-105, 105)
                    axis.xaxis.set_major_formatter(StrMethodFormatter("{x:.0f}"))
                elif field == "messages_after_close_change":
                    axis.set_xscale("symlog", linthresh=10)
                    axis.xaxis.set_major_formatter(StrMethodFormatter("{x:,.0f}"))
                else:
                    axis.set_xscale("symlog", linthresh=10)
                    axis.xaxis.set_major_formatter(StrMethodFormatter("{x:,.0f}"))

                axis.set_yticks(
                    np.arange(len(sweep_rows)),
                    [f"{parameter} ({variant})" for parameter, variant, _ in sweep_rows],
                    fontsize=9,
                )
                axis.invert_yaxis()
                axis.set_xlabel(f"{change_label}; negative is less than baseline")
                axis.set_ylabel("Changed parameter")
                axis.set_title(
                    f"{algorithm} — {topology.title()} topology\n"
                    f"{metric}: each variant compared with this algorithm's "
                    "matching baseline",
                    fontsize=14,
                    pad=14,
                )
                axis.grid(axis="x", alpha=0.25, which="both")
                axis.set_axisbelow(True)

                handles, labels = axis.get_legend_handles_labels()
                unique_handles: dict[str, Any] = {}
                for handle, label in zip(handles, labels):
                    unique_handles[label] = handle
                axis.legend(
                    unique_handles.values(),
                    unique_handles.keys(),
                    title="Variant",
                    loc="best",
                    ncols=min(len(unique_handles), 4),
                )

                figure.tight_layout()
                figure.savefig(
                    output_dir
                    / f"parameter_sweep_{algorithm}_{topology}_{field}.png",
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
        plot_parameter_sweeps_by_algorithm(results, args.output_dir)
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
