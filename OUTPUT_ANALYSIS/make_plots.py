"""
Generate exploratory charts from simulation_results.csv

Usage:  python make_plots.py [path/to/simulation_results.csv] [output_dir]
Needs:  pandas, numpy, matplotlib
"""
import sys
from pathlib import Path

import matplotlib.pyplot as plt
import numpy as np
import pandas as pd
from matplotlib.colors import LogNorm

CSV = sys.argv[1] if len(sys.argv) > 1 else "simulation_results.csv"
OUT = Path(sys.argv[2] if len(sys.argv) > 2 else "plots")
OUT.mkdir(parents=True, exist_ok=True)

# ---------------------------------------------------------------- styling
ALGS = ["no-stack", "ack-no-routing", "flooding", "distance-vector", "link-state"]
COLORS = dict(zip(ALGS, ["#2a78d6", "#eb6834", "#1baf7a", "#e87ba4", "#6250d6"]))
MARKERS = dict(zip(ALGS, ["o", "s", "^", "D", "v"]))  # secondary encoding (colour-blind safe)
TOPOS = ["complete", "ring", "star", "scale-free", "random"]
INK, MUTED, GRID = "#0b0b0b", "#52514e", "#e6e5e1"

plt.rcParams.update({
    "figure.facecolor": "#fcfcfb", "axes.facecolor": "#fcfcfb",
    "axes.edgecolor": GRID, "axes.labelcolor": MUTED, "axes.titlecolor": INK,
    "axes.titleweight": "bold", "axes.titlesize": 13, "axes.titlelocation": "left",
    "axes.spines.top": False, "axes.spines.right": False,
    "axes.grid": True, "grid.color": GRID, "grid.linewidth": 0.8,
    "xtick.color": MUTED, "ytick.color": MUTED, "text.color": INK,
    "font.size": 10, "legend.frameon": False, "savefig.dpi": 160,
    "savefig.bbox": "tight",
})


def save(fig, name):
    fig.savefig(OUT / name)
    plt.close(fig)
    print("saved", OUT / name)


def legend_handles():
    return [plt.Line2D([], [], color=COLORS[a], marker=MARKERS[a], lw=2, ms=6, label=a) for a in ALGS]


# ---------------------------------------------------------------- load
df = pd.read_csv(CSV)
parts = df.scenario.str.extract(r"^factorial-nodes-(\d+)-delay-([\d.]+)-([\d.]+)(?:-edge-([\d.]+))?$")
df["nodes"] = parts[0].astype(float)
df["delay_min"] = parts[1].astype(float)
df["delay_max"] = parts[2].astype(float)
df["edge_prob"] = parts[3].astype(float)
fact = df[df.nodes.notna()].copy()
ok = df[df.ended_successfully]
fact_ok = fact[fact.ended_successfully]

# ------------------------------------------------ 1. success rate heatmap
rate = (df.pivot_table(index="topology", columns="routing_algorithm",
                       values="ended_successfully", aggfunc="mean")
        .loc[TOPOS, ALGS] * 100)
fig, ax = plt.subplots(figsize=(8, 4.2))
im = ax.imshow(rate.values, cmap="Blues", vmin=0, vmax=100, aspect="auto")
ax.set_xticks(range(len(ALGS)), ALGS, rotation=20, ha="right")
ax.set_yticks(range(len(TOPOS)), TOPOS)
ax.grid(False)
for i in range(rate.shape[0]):
    for j in range(rate.shape[1]):
        v = rate.values[i, j]
        ax.text(j, i, f"{v:.0f}%", ha="center", va="center",
                color="white" if v > 55 else INK, fontsize=11, fontweight="bold")
ax.set_title("Only routed protocols finish on sparse topologies")
ax.text(0, 1.02, "Share of runs that ended successfully", transform=ax.transAxes, color=MUTED)
save(fig, "01_success_rate_heatmap.png")

# -------------------------- 2. median messages by topology x algorithm
med = df.pivot_table(index="topology", columns="routing_algorithm",
                     values="total_messages_sent", aggfunc="median").loc[TOPOS, ALGS]
fig, ax = plt.subplots(figsize=(10, 4.8))
w = 0.16
x = np.arange(len(TOPOS))
for k, a in enumerate(ALGS):
    ax.bar(x + (k - 2) * w, med[a], w * 0.9, color=COLORS[a], label=a)
ax.set_yscale("log")
ax.set_xticks(x, TOPOS)
ax.set_ylabel("Median total messages sent (log)")
ax.set_title("Flooding costs orders of magnitude more messages")
ax.grid(axis="x", visible=False)
ax.legend(ncol=5, loc="upper center", bbox_to_anchor=(0.5, -0.1))
save(fig, "02_median_messages_by_topology.png")

# ------------- 3. message growth with network size (small multiples)
fig, axes = plt.subplots(1, 5, figsize=(17, 3.9), sharey=True)
for ax, t in zip(axes, TOPOS):
    sub = fact[fact.topology == t]
    for a in ALGS:
        g = sub[sub.routing_algorithm == a].groupby("nodes").total_messages_sent
        m, lo, hi = g.median(), g.quantile(0.25), g.quantile(0.75)
        ax.fill_between(m.index, lo, hi, color=COLORS[a], alpha=0.12, lw=0)
        ax.plot(m.index, m, color=COLORS[a], marker=MARKERS[a], ms=4, lw=1.8)
    ax.set_yscale("log")
    ax.set_title(t, fontsize=11)
    ax.set_xlabel("nodes")
axes[0].set_ylabel("Total messages (median, IQR band)")
fig.suptitle("How message cost scales with network size", x=0.01, ha="left",
             fontsize=14, fontweight="bold", y=1.04)
fig.legend(handles=legend_handles(), ncol=5, loc="lower center", bbox_to_anchor=(0.5, -0.12))
save(fig, "03_messages_vs_nodes.png")

# ------ 4. wasted traffic: messages after close vs total (scatter)
sc = ok[(ok.messages_after_close > 0) & (ok.total_messages_sent > 0)]
fig, ax = plt.subplots(figsize=(8, 6))
for a in ["flooding", "distance-vector", "link-state"]:
    s = sc[sc.routing_algorithm == a]
    ax.scatter(s.total_messages_sent, s.messages_after_close, s=14, alpha=0.45,
               color=COLORS[a], marker=MARKERS[a], edgecolor="#fcfcfb", linewidth=0.4, label=a)
lim = [sc.messages_after_close.min() * 0.7, sc.total_messages_sent.max() * 1.5]
ax.plot(lim, lim, color=MUTED, lw=1, ls="--")
ax.text(1e4, 1.4e4, "all traffic after close (y = x)", color=MUTED,
        rotation=38, rotation_mode="anchor", fontsize=9, ha="left", va="bottom")
ax.set_xlim(sc.total_messages_sent.min() * 0.7, lim[1])
ax.set_ylim(lim[0], lim[1])
ax.set_xscale("log"); ax.set_yscale("log")
ax.set_xlabel("Total messages sent"); ax.set_ylabel("Messages still sent after close")
ax.set_title("Stragglers: traffic that outlives the session")
ax.legend(loc="upper left")
save(fig, "04_messages_after_close.png")

# ---------- 5. fraction of traffic sent after close, per algorithm
frac = ok.assign(f=ok.messages_after_close / ok.total_messages_sent)
fig, ax = plt.subplots(figsize=(8, 4.5))
data = [frac[frac.routing_algorithm == a].f * 100 for a in ALGS]
bp = ax.boxplot(data, vert=False, patch_artist=True, widths=0.55, showfliers=False,
                medianprops=dict(color=INK, lw=1.6), whiskerprops=dict(color=MUTED),
                capprops=dict(color=MUTED))
for patch, a in zip(bp["boxes"], ALGS):
    patch.set(facecolor=COLORS[a], alpha=0.8, edgecolor="#fcfcfb")
ax.set_yticks(range(1, len(ALGS) + 1), ALGS)
ax.set_xlabel("% of total messages sent after close")
ax.set_title("Share of traffic wasted after the session ends")
ax.grid(axis="y", visible=False)
save(fig, "05_wasted_traffic_share.png")

# -------- 6. execution time vs network delay (successful runs)
d = fact_ok[fact_ok.execution_time > 0]
fig, axes = plt.subplots(1, 2, figsize=(12, 4.5), sharey=True)
for ax, key, lab in [(axes[0], "nodes", "Number of nodes"),
                     (axes[1], "delay_min", "Minimum link delay (s)")]:
    for a in ["flooding", "distance-vector", "link-state"]:
        m = d[d.routing_algorithm == a].groupby(key).execution_time.median()
        ax.plot(m.index, m, color=COLORS[a], marker=MARKERS[a], lw=2, ms=6, label=a)
    ax.set_xlabel(lab)
    if key == "delay_min":
        ax.set_xscale("log")
axes[0].set_ylabel("Median execution time (s)")
axes[0].set_title("Execution time vs. size and link delay")
axes[0].legend()
save(fig, "06_execution_time.png")

# ------ 7. flooding overhead vs link-state on random graphs (heatmap)
r = fact[(fact.topology == "random")]
piv = (r.pivot_table(index="nodes", columns="edge_prob", values="total_messages_sent",
                     aggfunc="median", observed=True))
cols = {a: r[r.routing_algorithm == a].pivot_table(index="nodes", columns="edge_prob",
        values="total_messages_sent", aggfunc="median") for a in ["flooding", "link-state"]}
ratio = cols["flooding"] / cols["link-state"]
fig, ax = plt.subplots(figsize=(9, 4.8))
im = ax.imshow(ratio.values, cmap="Purples", norm=LogNorm(), aspect="auto", origin="lower")
ax.set_xticks(range(len(ratio.columns)), [f"{c:g}" for c in ratio.columns])
ax.set_yticks(range(len(ratio.index)), [f"{int(n)}" for n in ratio.index])
ax.set_xlabel("Edge probability"); ax.set_ylabel("Nodes")
ax.grid(False)
for i in range(ratio.shape[0]):
    for j in range(ratio.shape[1]):
        v = ratio.values[i, j]
        if np.isfinite(v):
            ax.text(j, i, f"{v:.0f}×", ha="center", va="center", fontsize=9,
                    color="white" if v > ratio.values[np.isfinite(ratio.values)].max() ** 0.5 else INK)
fig.colorbar(im, ax=ax, label="flooding ÷ link-state messages (log)")
ax.set_title("How much worse flooding gets on random graphs")
save(fig, "07_flooding_vs_linkstate_heatmap.png")

# ------------- 8. sensitivity to protocol parameters (non-factorial)
sens = df[df.nodes.isna() & df.scenario.str.contains(r"-(?:low|medium|high)$")].copy()
sens["param"] = sens.scenario.str.replace(r"-(low|medium|high)$", "", regex=True)
sens["level"] = sens.scenario.str.extract(r"-(low|medium|high)$")[0]
base = (df[(df.scenario == "baseline")].groupby("routing_algorithm").total_messages_sent.median())
sens = sens[sens.routing_algorithm.isin(["flooding", "distance-vector", "link-state"])]
rel = (sens.groupby(["param", "routing_algorithm", "level"]).total_messages_sent.median()
       .reset_index())
rel["vs_base"] = rel.total_messages_sent / rel.routing_algorithm.map(base)
params = sorted(rel.param.unique())
fig, axes = plt.subplots(1, 3, figsize=(15, 5), sharey=True)
for ax, a in zip(axes, ["flooding", "distance-vector", "link-state"]):
    p = (rel[rel.routing_algorithm == a].pivot(index="param", columns="level", values="vs_base")
         .reindex(params)[["low", "medium", "high"]])
    for i, name in enumerate(p.index):
        ax.plot([0, 1, 2], p.loc[name], marker=MARKERS[a], color=COLORS[a], alpha=0.35, lw=1.3)
    # highlight the most sensitive parameter
    spread = (p.max(axis=1) / p.min(axis=1)).sort_values(ascending=False)
    top = list(spread.index[:3])
    for name in top:
        ax.plot([0, 1, 2], p.loc[name], marker=MARKERS[a], color=COLORS[a], lw=2.4)
    # direct labels at the right edge, nudged apart in log space so they don't collide
    ys = sorted((np.log10(p.loc[n].iloc[-1]), n) for n in top)
    min_gap, placed = 0.12, []
    for y, n in ys:
        if placed and y - placed[-1][0] < min_gap:
            y = placed[-1][0] + min_gap
        placed.append((y, n))
    for y, n in placed:
        ax.text(2.08, 10 ** y, n, fontsize=8, va="center", color=INK)
    ax.axhline(1, color=MUTED, lw=1, ls="--")
    ax.set_yscale("log")
    ax.set_xticks([0, 1, 2], ["low", "medium", "high"])
    ax.set_xlim(-0.1, 2.9)
    ax.set_title(a, fontsize=11)
axes[0].set_ylabel("Messages relative to baseline (log)")
fig.suptitle("Which parameters actually move message counts?", x=0.01, ha="left",
             fontsize=14, fontweight="bold", y=1.02)
save(fig, "08_parameter_sensitivity.png")

print("done")
