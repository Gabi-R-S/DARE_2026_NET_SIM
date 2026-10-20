#!/usr/bin/env python3
"""
Plots for the RunSimulations parameter sweeps.

Usage:
    python plot_simulations.py simulation-results.json            # results plots (+ design plot)
    python plot_simulations.py --design-only                      # only the sweep-design plot
    python plot_simulations.py results.json --metrics time converged --outdir plots
    python plot_simulations.py results.json --list-columns        # show detected columns

Results mode assumes results is a JSON list of objects. Scenario and topology are
recovered from the text of each result (the C# program prefixes every experiment name
with "<scenario>-"). If detection fails, use --name-field / --topology-field.
Numeric columns become metrics; boolean columns are plotted as rates.
"""
import argparse
import json
import math
import re
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.ticker
import matplotlib.pyplot as plt
import numpy as np
import pandas as pd

TOPOLOGIES = ["complete", "ring", "star", "random", "scale-free"]
COLORS = dict(zip(TOPOLOGIES, ["#1b9e77", "#d95f02", "#7570b3", "#e7298a", "#66a61e"]))

# family -> (baseline value, {level label: value})   (mirrors CreateParameterScenarios)
FAMILIES = {
    "duration":             (200.0, {"low": 20.0, "medium": 100.0, "high": 500.0}),
    "operations":           (5,     {"low": 1, "medium": 6, "high": 10}),
    "send-interval":        (0.4,   {"low": 0.1, "medium": 0.45, "high": 0.8}),
    "operation-interval":   (0.1,   {"low": 0.025, "medium": 0.125, "high": 0.2}),
    "heartbeat":            (0.1,   {"low": 0.05, "medium": 0.2, "high": 0.5}),
    "timeout":              (15.0,  {"low": 2.0, "medium": 16.0, "high": 30.0}),
    "hop-limit":            (10,    {"low": 2, "medium": 11, "high": 20}),
    "forgetting-time":      (60.0,  {"low": 10.0, "medium": 65.0, "high": 120.0}),
    "distance-vector-poll": (10.0,  {"low": 1.0, "medium": 12.0, "high": 20.0}),
    "ack-timeout":          (5.0,   {"8": 8.0, "15": 15.0}),
    "edges-per-node":       (2,     {"low": 1, "medium": 3, "high": 4}),
}


def scenario_names():
    """Map full scenario name (as prefixed by the C# program) -> (family, value)."""
    names = {"baseline": ("baseline", None)}
    for fam, (_, levels) in FAMILIES.items():
        for label, val in levels.items():
            key = f"{fam}-{label}" if fam != "ack-timeout" else f"ack-timeout-{label}"
            if fam == "edges-per-node":
                key = f"edges-per-node-{label}"
            names[key] = (fam, val)
    return names


SCEN = scenario_names()
# longest first so "operation-interval-low" is not mistaken for "operations-low", etc.
SCEN_ORDER = sorted(SCEN, key=len, reverse=True)


# --------------------------------------------------------------------------- design plot
def plot_design(outdir: Path):
    fams = [f for f in FAMILIES]
    fig, ax = plt.subplots(figsize=(9, 6))
    for i, fam in enumerate(fams):
        base, levels = FAMILIES[fam]
        ax.scatter([0], [i], marker="D", color="black", zorder=3)
        for label, val in levels.items():
            r = math.log2(val / base)
            ax.scatter([r], [i], s=70, zorder=3,
                       color={"low": "#3b82f6", "medium": "#f59e0b", "high": "#ef4444"}.get(label, "#6b7280"))
            ax.annotate(f"{val:g}", (r, i), textcoords="offset points", xytext=(0, 8),
                        ha="center", fontsize=8)
        ax.annotate(f"base {base:g}", (0, i), textcoords="offset points", xytext=(0, -14),
                    ha="center", fontsize=7, color="dimgray")
    ax.axvline(0, color="gray", lw=0.8, ls="--")
    ax.set_yticks(range(len(fams)))
    ax.set_yticklabels(fams)
    ax.invert_yaxis()
    ax.set_xlabel("log2(value / baseline)   (0 = baseline; +1 = doubled)")
    ax.set_title("One-factor-at-a-time sweep design: how far each level sits from the baseline")
    ax.grid(axis="x", alpha=0.3)
    fig.tight_layout()
    path = outdir / "design_sweeps.png"
    fig.savefig(path, dpi=150)
    plt.close(fig)
    print(f"wrote {path}")


# --------------------------------------------------------------------------- loading
def load_results(path: Path, name_field, topology_field):
    with open(path, encoding="utf-8") as fh:
        data = json.load(fh)
    if isinstance(data, dict):  # tolerate {"results": [...]}
        data = next((v for v in data.values() if isinstance(v, list)), [data])
    df = pd.json_normalize(data)

    str_cols = [c for c in df.columns
                if not pd.api.types.is_bool_dtype(df[c])
                and not pd.api.types.is_numeric_dtype(df[c])
                and df[c].map(lambda v: isinstance(v, str)).any()]

    # scenario detection
    if name_field is None:
        best, best_hits = None, 0
        for c in str_cols:
            hits = df[c].astype(str).str.lower().map(lambda s: any(s.startswith(n + "-") or s == n for n in SCEN_ORDER)).sum()
            if hits > best_hits:
                best, best_hits = c, hits
        name_field = best
    if name_field is None:
        raise SystemExit("Could not find a column holding the scenario prefix. Use --name-field "
                         f"(string columns seen: {str_cols}).")

    def scen_of(s):
        s = str(s).lower()
        for n in SCEN_ORDER:
            if s.startswith(n + "-") or s == n:
                return n
        return None

    df["_scenario"] = df[name_field].map(scen_of)

    # topology detection
    def topo_of(text):
        t = text.lower()
        if "scale-free" in t or "scalefree" in t or "scale_free" in t:
            return "scale-free"
        for k in ("complete", "ring", "star", "random"):
            if re.search(rf"\b{k}", t) or k in t:
                return k
        return None

    if topology_field:
        df["_topology"] = df[topology_field].astype(str).map(topo_of)
    else:
        joined = df[str_cols].astype(str).agg(" ".join, axis=1) if str_cols else pd.Series([""] * len(df))
        # prefer the name field first, then everything else
        df["_topology"] = df[name_field].astype(str).map(topo_of)
        missing = df["_topology"].isna()
        df.loc[missing, "_topology"] = joined[missing].map(topo_of)

    print(f"scenario column: {name_field!r}; rows: {len(df)}; "
          f"unmatched scenario rows (e.g. factorial): {df['_scenario'].isna().sum()}")
    return df


def pick_metrics(df, requested):
    skip = {"_scenario", "_topology"}
    cols = []
    for c in df.columns:
        if c in skip:
            continue
        s = df[c]
        if pd.api.types.is_bool_dtype(s) or pd.api.types.is_numeric_dtype(s):
            if s.nunique(dropna=True) > 1:
                cols.append(c)
    if requested:
        missing = [m for m in requested if m not in df.columns]
        if missing:
            raise SystemExit(f"Unknown metric(s) {missing}. Available: {cols}")
        return requested
    return cols


# --------------------------------------------------------------------------- results plots
def plot_metric(df, metric, outdir: Path):
    d = df.dropna(subset=["_scenario", "_topology"]).copy()
    d[metric] = pd.to_numeric(d[metric], errors="coerce").astype(float)  # bool -> 0/1
    fams = [f for f in FAMILIES if (d["_scenario"].map(lambda n: SCEN[n][0]) == f).any()]
    if not fams:
        print(f"  {metric}: no parameter-sweep rows found, skipping")
        return

    ncols = 3
    nrows = math.ceil(len(fams) / ncols)
    fig, axes = plt.subplots(nrows, ncols, figsize=(5 * ncols, 3.6 * nrows), squeeze=False)
    d["_family"] = d["_scenario"].map(lambda n: SCEN[n][0])
    d["_value"] = d["_scenario"].map(lambda n: SCEN[n][1])

    for ax, fam in zip(axes.flat, fams):
        base_val = FAMILIES[fam][0]
        sub = d[d["_family"] == fam]
        for topo in TOPOLOGIES:
            t_sub = sub[sub["_topology"] == topo]
            if t_sub.empty:
                continue
            base = d[(d["_scenario"] == "baseline") & (d["_topology"] == topo)][metric].mean()
            pts = t_sub.groupby("_value")[metric].mean()
            xs = [base_val] + list(pts.index)
            ys = [base] + list(pts.values)
            order = np.argsort(xs)
            xs, ys = np.array(xs)[order], np.array(ys)[order]
            ax.plot(xs, ys, marker="o", color=COLORS[topo], label=topo)
        ax.axvline(base_val, color="gray", ls="--", lw=0.8)
        ax.set_xscale("log")
        ax.xaxis.set_minor_formatter(matplotlib.ticker.NullFormatter())
        ax.set_xticks(sorted(set([base_val] + list(sub["_value"].dropna().unique()))))
        ax.xaxis.set_major_formatter(matplotlib.ticker.FuncFormatter(lambda v, _: f"{v:g}"))
        ax.tick_params(axis="x", labelsize=7)
        ax.set_title(fam, fontsize=10)
        ax.set_xlabel("value (log scale; dashed = baseline)", fontsize=8)
        ax.set_ylabel(metric, fontsize=8)
        ax.grid(alpha=0.3)
    for ax in list(axes.flat)[len(fams):]:
        ax.axis("off")
    handles, labels = axes.flat[0].get_legend_handles_labels()
    fig.legend(handles, labels, loc="lower right", ncol=1, title="topology")
    fig.suptitle(f"{metric}: mean per scenario vs. parameter value", y=1.0)
    fig.tight_layout()
    safe = re.sub(r"[^A-Za-z0-9_.-]+", "_", metric)
    path = outdir / f"sweeps_{safe}.png"
    fig.savefig(path, dpi=150, bbox_inches="tight")
    plt.close(fig)
    print(f"wrote {path}")

    # relative change vs baseline heatmap (family/level x topology)
    rows = {}
    for name in SCEN_ORDER:
        if name == "baseline":
            continue
        s = d[d["_scenario"] == name]
        if s.empty:
            continue
        rows[name] = {}
        for topo in TOPOLOGIES:
            base = d[(d["_scenario"] == "baseline") & (d["_topology"] == topo)][metric].mean()
            cur = s[s["_topology"] == topo][metric].mean()
            rows[name][topo] = (cur - base) / base if base not in (0, np.nan) and base == base and base != 0 else np.nan
    if rows:
        hm = pd.DataFrame(rows).T.reindex(columns=TOPOLOGIES)
        hm = hm.reindex([n for n in SCEN if n in hm.index])
        fig, ax = plt.subplots(figsize=(6.5, 0.28 * len(hm) + 1.5))
        lim = np.nanmax(np.abs(hm.values)) if np.isfinite(hm.values).any() else 1
        im = ax.imshow(hm.values, aspect="auto", cmap="RdBu_r", vmin=-lim, vmax=lim)
        ax.set_xticks(range(len(hm.columns)))
        ax.set_xticklabels(hm.columns, rotation=30, ha="right")
        ax.set_yticks(range(len(hm)))
        ax.set_yticklabels(hm.index, fontsize=7)
        fig.colorbar(im, ax=ax, label="relative change vs baseline")
        ax.set_title(f"{metric}: change vs baseline")
        fig.tight_layout()
        path = outdir / f"heatmap_{safe}.png"
        fig.savefig(path, dpi=150)
        plt.close(fig)
        print(f"wrote {path}")


# --------------------------------------------------------------------------- ranking
def plot_ranking(df, metric, outdir: Path, exclude=("operations",)):
    """Which parameter produces the largest INCREASE in `metric` vs baseline?

    For each family and topology, take the level with the largest mean value and report
    it as % above the topology's baseline mean. Families are sorted by the median across
    topologies. Also writes a CSV with the winning level for each cell.
    """
    d = df.dropna(subset=["_scenario", "_topology"]).copy()
    d[metric] = pd.to_numeric(d[metric], errors="coerce").astype(float)
    d["_family"] = d["_scenario"].map(lambda n: SCEN[n][0])
    d["_value"] = d["_scenario"].map(lambda n: SCEN[n][1])
    recs = []
    for fam in FAMILIES:
        if fam in exclude:
            continue
        for topo in TOPOLOGIES:
            base = d[(d["_scenario"] == "baseline") & (d["_topology"] == topo)][metric].mean()
            sub = d[(d["_family"] == fam) & (d["_topology"] == topo)]
            if sub.empty or not base or base != base:
                continue
            means = sub.groupby("_value")[metric].mean()
            best_val = means.idxmax()
            recs.append({"parameter": fam, "topology": topo, "baseline": base,
                         "max_level_value": best_val, "max_level_mean": means.max(),
                         "pct_increase": 100 * (means.max() / base - 1)})
    if not recs:
        print(f"  {metric}: nothing to rank")
        return
    r = pd.DataFrame(recs)
    safe = re.sub(r"[^A-Za-z0-9_.-]+", "_", metric)
    r.to_csv(outdir / f"ranking_{safe}.csv", index=False)
    order = r.groupby("parameter")["pct_increase"].median().sort_values().index.tolist()
    piv = r.pivot(index="parameter", columns="topology", values="pct_increase").reindex(order)

    fig, ax = plt.subplots(figsize=(8, 0.55 * len(order) + 1.8))
    h = 0.8 / len(TOPOLOGIES)
    for j, topo in enumerate(TOPOLOGIES):
        if topo not in piv:
            continue
        ys = np.arange(len(order)) + (j - (len(TOPOLOGIES) - 1) / 2) * h
        ax.barh(ys, piv[topo].values, height=h, color=COLORS[topo], label=topo)
    ax.set_yticks(range(len(order)))
    ax.set_yticklabels(order)
    ax.axvline(0, color="black", lw=0.8)
    ax.set_xlabel(f"largest increase in mean {metric} vs. baseline (%)")
    ax.set_title(f"Which parameter increases {metric} most? ({', '.join(exclude)} excluded)")
    ax.grid(axis="x", alpha=0.3)
    ax.legend(title="topology", fontsize=8)
    fig.tight_layout()
    path = outdir / f"ranking_{safe}.png"
    fig.savefig(path, dpi=150)
    plt.close(fig)
    print(f"wrote {path}")
    print(f"\nRanking for {metric} (median % increase across topologies, top first):")
    for p in order[::-1]:
        print(f"  {p:22s} {piv.loc[p].median():8.1f}%   (max {piv.loc[p].max():.1f}% on {piv.loc[p].idxmax()})")


# --------------------------------------------------------------------------- main
def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("results", nargs="?", default="simulation-results.json")
    ap.add_argument("--outdir", default="plots")
    ap.add_argument("--metrics", nargs="*", help="columns to plot (default: all varying numeric/bool columns)")
    ap.add_argument("--name-field", help="column holding the '<scenario>-' prefixed experiment name")
    ap.add_argument("--topology-field", help="column identifying the topology")
    ap.add_argument("--rank", nargs="+", metavar="COLUMN",
                    help="rank parameters by their largest increase in these columns "
                         "(e.g. --rank MessagesSent SimulationTime); skips the other plots")
    ap.add_argument("--rank-exclude", nargs="*", default=["operations"],
                    help="families to leave out of the ranking (default: operations)")
    ap.add_argument("--design-only", action="store_true")
    ap.add_argument("--list-columns", action="store_true")
    args = ap.parse_args()

    outdir = Path(args.outdir)
    outdir.mkdir(parents=True, exist_ok=True)
    plot_design(outdir)
    if args.design_only:
        return

    df = load_results(Path(args.results), args.name_field, args.topology_field)
    if args.list_columns:
        print(df.dtypes)
        return
    if args.rank:
        for m in pick_metrics(df, args.rank):
            plot_ranking(df, m, outdir, tuple(args.rank_exclude))
        return
    for m in pick_metrics(df, args.metrics):
        plot_metric(df, m, outdir)


if __name__ == "__main__":
    main()