# Harmony Solver — How It Works

This document explains how the Harmony pairing algorithm generates debate tournament matchups. It's intended for someone who wants to understand *why* the solver produces the results it does.

---

## The Problem

Given N teams with varying win-loss records, seeds, side histories, and prior opponents, assign each team to exactly one matchup (or bye) for the next round such that:

- The pairings are **fair** (teams face opponents with similar records)
- The pairings are **competitive** (top seeds face bottom seeds within each bracket)
- No team faces a **rematch**
- Each team's **aff/neg balance** stays within 1
- At most one team receives a **bye**, and only if they haven't had one already

This is a constrained optimization problem. The solution space is enormous — for 86 teams there are trillions of possible pairings — so brute force is out. It is, however, exactly a **minimum-cost perfect matching** problem, which has a polynomial exact algorithm (Edmonds' blossom algorithm). Harmony uses that to find the provably best answer in milliseconds.

---

## The Approach: Graph + Exact Matching

The algorithm models the problem as a **minimum-cost perfect matching** on a general graph: teams are vertices, every legal matchup is an edge with a cost, and a pairing is a set of edges that touches every team exactly once. It is solved exactly by the O(n³) weighted blossom algorithm in `MinCostPerfectMatching`.

The graph is *not* bipartite. `CanGoAff`/`CanGoNeg` are a legality filter, not a partition: a team with equal aff and neg rounds may take either side, which is every team in an odd-numbered round. That matters because min-cost matching on a bipartite graph is an easy linear program, while on a general graph the linear relaxation is not integral (odd cycles can be "half matched") and a generic MIP/CP solver has to branch its way through every odd set. Blossom handles the odd sets directly.

### Step 1: Build the candidate graph

The solver constructs a list of **edges**, where each edge represents a potential matchup. There are two types:

**Match edges** — one for every unordered pair of teams that can legally meet:
- At least one side assignment must be legal: (A aff, B neg) needs `A.affRounds <= A.negRounds` and `B.negRounds <= B.affRounds`, or the reverse
- The two teams must not have faced each other before
- Matchup cost is symmetric, so one undirected edge per pair is enough; which side each team takes is decided after the matching (`PairRules.AssignSides`): the only legal direction if there is one, otherwise the better-seeded team goes aff

**Bye edges** — one for each team eligible for a bye:
- Only created when the field has an odd number of teams
- Only for teams that haven't already had a bye in this tournament

Each edge gets a boolean decision variable (`IsSelected`) and an integer cost.

### Step 2: Assign costs

Every edge gets a cost. The solver will minimize total cost, so lower cost = more desirable matchup.

#### Match edge cost

The cost function has three components, evaluated in `Team.MatchupCost()`:

```
cost = winCost + seedCost + clubPenalty
```

**Win cost (dominant)** — penalizes mismatched records:

| Win difference | Cost |
|---|---|
| 0 | 0 |
| 1 | 100,000 |
| 2 | 10,000,000 |
| 3 | 100,000,000 |
| 4+ | 500,000,000 |

The exponential jumps mean the solver will exhaust all same-record pairings before pulling up a team. A 2-win-gap matchup costs 100x more than a 1-win-gap — it will only happen if there's truly no alternative.

**Seed cost (tiebreaker within bracket)** — rewards larger seed spread:

```
seedCost = 10,000 - (seedSpread²)
```

Where `seedSpread = |aff.seed - neg.seed|`.

This produces a **high-low fold** pattern. In a bracket of 6 same-record teams seeded 1–6, the optimal pairing is (1 vs 6), (2 vs 5), (3 vs 4) — maximizing every individual spread.

The squaring is key: it makes spreading seeds as far as possible within *each* matchup more valuable than just maximizing total spread. Without squaring, (1v6, 2v3, 4v5) would score the same as (1v4, 2v5, 3v6) — both sum to 12. With squaring, the first option costs 9991 + 9999 + 9999 = 29989 while the second costs 9991 + 9991 + 9991 = 29973, so the first wins.

**Club penalty (minor)** — adds 100 to same-club matchups. This is small enough that it never overrides win balance or seed spread, but breaks ties in favor of cross-club pairings.

#### Bye edge cost

```
byeCost = wins << 20    (i.e., wins × 1,048,576)
```

This means a 0-win team's bye costs 0, a 1-win team's bye costs ~1M, and a 2-win team's bye costs ~2M. The effect: lower-ranked teams get byes first. A bye for a 2-win team costs more than a 1-win-gap pullup (100K), so the solver will prefer to give the bye to a weaker team even if it forces a mild pullup elsewhere.

### Step 3: Constrain

The only constraint is the definition of a perfect matching: every team is in exactly one selected edge. Rematch avoidance, side balance and bye eligibility are enforced **structurally** — illegal edges are simply never created in Step 1. There is no constraint for "don't rematch"; instead, the edge (A vs B) is never added if A has hit B.

### Step 4: Solve

`MinCostPerfectMatching` finds the perfect matching of minimum total cost:

```
minimize: sum(edge.Cost) over selected edges
```

The algorithm is exact and polynomial (O(n³) in the number of teams), so there is no time limit and no "best feasible so far": a 200-team round returns the optimal pairing in milliseconds. If no perfect matching exists — for example, every legal opponent for some team has been exhausted — `SolveMatching` throws a `CannotPairException`.

The Lambda `--timeout` in `scripts/deploy-lambda.sh` only bounds the function itself; pairing never gets near it.

### Step 5: Extract results

The selected edges become matchups. Each selected match edge becomes an aff/neg pairing; each selected bye edge becomes a bye assignment.

---

## Visual Example

Consider 7 teams after 2 rounds:

```
Team   Record   Seed
A      2-0      1
B      2-0      2
C      1-1      3
D      1-1      4
E      1-1      5
F      0-2      6
G      0-2      7
```

Assume no prior matchups between any pair and balanced aff/neg counts.

**Candidate edges (simplified — ignoring directional duplicates):**

The solver builds ~42 match edges (every pair in both aff/neg directions) plus 7 bye edges.

**Costs:**

| Edge | Win diff | Seed spread | Win cost | Seed cost | Total |
|---|---|---|---|---|---|
| A vs B | 0 | 1 | 0 | 9,999 | 9,999 |
| A vs G | 2 | 6 | 10,000,000 | 9,964 | 10,009,964 |
| A vs F | 2 | 5 | 10,000,000 | 9,975 | 10,009,975 |
| C vs E | 0 | 2 | 0 | 9,996 | 9,996 |
| C vs D | 0 | 1 | 0 | 9,999 | 9,999 |
| D vs E | 0 | 1 | 0 | 9,999 | 9,999 |
| F vs G | 0 | 1 | 0 | 9,999 | 9,999 |
| F bye | — | — | 0 | — | 0 |
| G bye | — | — | 0 | — | 0 |
| A bye | — | — | 2,097,152 | — | 2,097,152 |

**Optimal solution:**

```
A (seed 1) vs B (seed 2)  — 2-0 bracket, only option
C (seed 3) vs E (seed 5)  — 1-1 bracket, high-low
D (seed 4) vs F (seed 6)  — pullup: D is 1-1, F is 0-2 (cost 100K)
G (seed 7) bye             — 0-2, cheapest bye
```

Why D vs F instead of D vs something else? There are only two 0-2 teams (F and G). One must get a bye. The remaining 0-2 team (F) can't pair with the other 0-2 team, so F must pull up. The solver picks D (the lowest-seeded 1-1 team) because that leaves C vs E with a better seed spread.

---

## Why an exact matching algorithm?

Earlier versions solved the same model with Google OR-Tools' CP-SAT solver. That worked for small fields but scaled badly for a reason that has nothing to do with the cost function: CP-SAT relaxes the problem to a linear program, and the LP for matching on a *general* graph is only half-integral. Every odd cycle among teams that could all meet each other is a place where the LP bound lies, and CP-SAT has to branch through them one by one to prove optimality. A 31-team round 6 took 3–15 seconds; a 202-team round 3 took ~150 seconds, and when the time limit hit first the API returned a non-optimal pairing without saying so. Random costs, tighter encodings and hand-written cuts did not change that; it is the wrong tool for this shape of problem.

Blossom is the right tool: it is the exact algorithm for weighted matching on general graphs, its running time depends only on the number of teams, and it either returns the optimum or proves that no legal pairing exists. The cost function is unchanged — all the intelligence is still there — and CP-SAT is kept as a test-only dependency, where it acts as an independent oracle that the blossom implementation is checked against on hundreds of random instances.

---

## Summary

```
Tournament state (teams, records, seeds, history)
    ↓
Build candidate edges (filter out illegal matchups structurally)
    ↓
Assign costs (win balance >> seed spread >> club avoidance)
    ↓
Constrain (exactly one edge per team)
    ↓
Minimum-cost perfect matching (blossom algorithm, exact)
    ↓
Selected edges → matchups + byes
```

The key insight is that **all the intelligence is in the cost function**. The constraints ensure a valid pairing; the costs ensure a *good* one. By making win-balance costs orders of magnitude larger than seed-spread costs, the solver always prioritizes fairness over competitiveness — but within the fairness constraint, it finds the most competitive pairings possible.
