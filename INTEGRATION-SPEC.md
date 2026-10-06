# Harmony Pairing API — Integration Spec

This document describes how to integrate with the Harmony pairing API. It covers the source data format (exported from a tournament platform), how to transform that data into an API request, and how to interpret the response.

---

## 1. Overview

The Harmony API generates optimal debate tournament pairings for a given round. It accepts a list of teams with their current tournament state and returns a set of matchups (aff vs neg assignments, plus byes for odd-count fields).

**Endpoint:** `POST /api/pairing/generate`
**Content-Type:** `application/json`
**CORS:** Open (any origin)

---

## 2. Source Data Format

The source data is a JSON export from the tournament platform. You need to transform this into an API request (see Section 4).

```json
{
  "tournament": "The Texas Escalade",
  "events": [
    {
      "event_id": 1715,
      "event_name": "Lincoln Douglas",
      "teams": [
        {
          "entry_id": 55888,
          "mask": "YP-221",
          "history": [
            {
              "round": 1,
              "side": "aff",
              "opponent_mask": "QM-223",
              "result": "win"
            },
            {
              "round": 2,
              "side": "neg",
              "opponent_mask": "PK-202",
              "result": "unknown"
            }
          ],
          "rankings": {
            "1": 3,
            "2": 1
          }
        }
      ]
    }
  ]
}
```

### Source Field Definitions

| Field | Type | Description |
|---|---|---|
| `tournament` | string | Tournament name |
| `events` | array | List of debate events (e.g. Lincoln Douglas, Team Policy) |
| `events[].event_id` | int | Unique event identifier |
| `events[].event_name` | string | Human-readable event name |
| `events[].teams` | array | All entries in this event |
| `teams[].entry_id` | int | Unique entry identifier |
| `teams[].mask` | string | Team code/identifier (e.g. "YP-221"). **Note:** masks are NOT guaranteed unique — duplicate masks can occur across different entries. Use `entry_id` for unique identification. |
| `teams[].history` | array | One record per round the team has participated in |
| `history[].round` | int | Round number (1-indexed) |
| `history[].side` | string | `"aff"`, `"neg"`, or `"bye"` |
| `history[].opponent_mask` | string | Opponent's mask. Present even for byes (may reference a phantom). |
| `history[].result` | string | `"win"`, `"loss"`, `"bye"`, or `"unknown"` |
| `teams[].rankings` | object | Keys are round numbers (as strings), values are the team's rank/seed after that round. E.g. `{"1": 3, "2": 1}` means ranked 3rd after round 1, 1st after round 2. Lower number = better rank. |

### Understanding `"unknown"` results

In the source data, when two teams debate each other, typically only ONE side has a definitive `"win"` or `"loss"` result recorded. The other side (usually the negative) will show `"unknown"`. To resolve an unknown result, look up the opponent's record for the same round and invert it:

- If the opponent's result is `"win"` → this team's result is `"loss"`
- If the opponent's result is `"loss"` → this team's result is `"win"`
- If both are `"unknown"` → the result is genuinely unresolved

---

## 3. Transformation Logic

To generate pairings for round N, you need two configuration values:

| Parameter | Description | Example |
|---|---|---|
| `roundToMatch` | The round you are generating pairings FOR | `3` (generating round 3 pairings) |
| `baseRankingsOnRound` | Which round's rankings to use as seeds. Must be ≤ `roundToMatch - 1`. | `2` (use rankings after round 2) |

### Transformation rules per team

For each team in the selected event, produce one `TeamRequest` object:

#### `name` → `mask`
Use the team's `mask` as the name identifier.

#### `wins` and `losses` — count through `baseRankingsOnRound` only
Iterate over the team's `history`. For each round where `round <= baseRankingsOnRound`:
1. Resolve the actual result (see "unknown" resolution above)
2. Count wins and losses

**Important:** Only count W/L through `baseRankingsOnRound`, NOT through all completed rounds. This allows the algorithm to use rankings that correspond to a consistent W/L snapshot.

#### `seed` — from `rankings[baseRankingsOnRound]`
Look up `team.rankings[String(baseRankingsOnRound)]`. If not present, default to `999`.

Lower seed number = better ranking (seed 1 is the top-ranked team).

#### `affRounds` and `negRounds` — count through `roundToMatch - 1`
Iterate over history. For each round where `round <= roundToMatch - 1`:
- If `side === "aff"` → increment `affRounds`
- If `side === "neg"` → increment `negRounds`
- Byes don't count toward either side

These counts drive side-balance constraints. The algorithm will try to assign each team to the side they've been on less.

#### `opponentHistory` — through `roundToMatch - 1`
Collect the `opponent_mask` from every round where `round <= roundToMatch - 1`, **excluding byes**. The algorithm uses this to prevent rematches.

#### `isByeEligible`
Set to `true` if the team has NOT had a bye in any round through `roundToMatch - 1`. Set to `false` if they have. This prevents a team from receiving two byes in the same tournament.

A bye is identified by `history[].result === "bye"` (or `history[].side === "bye"`).

#### `club`
Club/school affiliation string. Set to `null` if not available in the source data. When provided, the algorithm penalizes same-club matchups.

---

## 4. API Request

```
POST /api/pairing/generate
Content-Type: application/json
```

### Request Body Schema

```json
{
  "roundNumber": 3,
  "strategy": "powermatch",
  "teams": [
    {
      "name": "YP-221",
      "isByeEligible": true,
      "wins": 2,
      "losses": 0,
      "affRounds": 1,
      "negRounds": 1,
      "seed": 1,
      "club": null,
      "opponentHistory": ["QM-223", "PK-202"]
    }
  ]
}
```

### Request Field Definitions

| Field | Type | Required | Description |
|---|---|---|---|
| `roundNumber` | int | yes | The round being generated |
| `strategy` | string | no | Matching algorithm: `"powermatch"` (default) or `"random"`. See Section 6 for details. |
| `teams` | array | yes | All teams to be paired |
| `teams[].name` | string | yes | Team identifier (source `mask`) |
| `teams[].isByeEligible` | bool | yes | `false` if team already had a bye |
| `teams[].wins` | int | yes | Win count through `baseRankingsOnRound` |
| `teams[].losses` | int | yes | Loss count through `baseRankingsOnRound` |
| `teams[].affRounds` | int | yes | Aff-side count through `roundToMatch - 1` |
| `teams[].negRounds` | int | yes | Neg-side count through `roundToMatch - 1` |
| `teams[].seed` | int | yes | Ranking from `baseRankingsOnRound` (1 = best) |
| `teams[].club` | string? | no | Club/school affiliation (null if unavailable) |
| `teams[].opponentHistory` | string[] | yes | Names of all prior opponents (excluding byes) |

### JSON Serialization

All property names use **camelCase** (e.g., `isByeEligible`, `affRounds`, `opponentHistory`).

---

## 5. API Response

### Success Response (HTTP 200)

```json
{
  "success": true,
  "error": null,
  "matchups": [
    {
      "aff": "YP-221",
      "neg": "CD-131",
      "isBye": false
    },
    {
      "aff": "MB-153",
      "neg": "DP-178",
      "isBye": false
    },
    {
      "aff": "RF-188",
      "neg": null,
      "isBye": true
    }
  ]
}
```

### Response Field Definitions

| Field | Type | Description |
|---|---|---|
| `success` | bool | `true` if pairings were generated successfully |
| `error` | string? | Error message if `success` is `false`, otherwise `null` |
| `matchups` | array | List of pairings for the round |
| `matchups[].aff` | string | Team name assigned to the affirmative side |
| `matchups[].neg` | string? | Team name assigned to the negative side. `null` for bye matchups. |
| `matchups[].isBye` | bool | `true` if this is a bye (team has no opponent this round) |

### Error Response (HTTP 200 with `success: false`)

```json
{
  "success": false,
  "error": "Cannot generate valid pairings: no feasible solution found",
  "matchups": []
}
```

Common error scenarios:
- Odd number of teams but no team is bye-eligible (all have already had byes)
- Constraints are unsatisfiable (e.g., all legal opponents exhausted for some team)

---

## 6. Algorithm Behavior

The API supports two matching strategies, selected via the `strategy` field in the request. Both are solved as an exact minimum-cost perfect matching (see SOLVER-APPROACH.md) and enforce the same hard constraints.

### Strategy: `"powermatch"` (default)

The default strategy. Produces competitive, seeded matchups. If `strategy` is omitted, this is used.

#### Priority order (highest to lowest)

1. **Win balance** — Teams with the same W-L record are strongly preferred to face each other. This is the dominant constraint.

   | Win difference | Cost weight |
   |---|---|
   | 0 (same record) | 0 |
   | 1 | 100,000 |
   | 2 | 10,000,000 |
   | 3 | 100,000,000 |
   | 4+ | 500,000,000 |

2. **High-low seed spread** — Within the same win bracket, the algorithm pairs the highest-seeded team against the lowest-seeded, second-highest vs second-lowest, etc. This produces competitive matchups.

3. **Same-club avoidance** — Small penalty (100) for pairing teams from the same club. Can be overridden by stronger constraints above.

### Strategy: `"random"`

Produces a random valid matching. All legal matchups are assigned random costs, so the solver picks an arbitrary feasible pairing. Useful for early rounds (e.g. round 1) where seeded matchups are not desired.

The `wins`, `losses`, `seed`, and `club` fields are still accepted but are ignored for cost purposes — only the hard constraints (no rematches, side balance, bye eligibility) apply.

### Hard constraints (never violated, both strategies)

- **No rematches:** A team cannot face an opponent already in their `opponentHistory`.
- **Side balance:** A team can only go aff if `affRounds <= negRounds`, and neg if `negRounds <= affRounds`. The difference between aff and neg round counts must be ≤ 1.
- **Bye eligibility:** Only teams with `isByeEligible: true` can receive a bye.
- **Single assignment:** Every team appears in exactly one matchup (or bye).

### Bye allocation

When there's an odd number of teams, one team receives a bye. In `powermatch` mode, the algorithm prefers to give byes to teams with fewer wins (cost = `wins * 2^20`), so lower-ranked teams are more likely to receive a bye. In `random` mode, bye assignment is random among eligible teams.

---

## 7. Worked Example

**Scenario:** Generate round 3 pairings for an event with `baseRankingsOnRound = 2`.

**Source team:**
```json
{
  "entry_id": 55888,
  "mask": "YP-221",
  "history": [
    { "round": 1, "side": "aff", "opponent_mask": "QM-223", "result": "win" },
    { "round": 2, "side": "neg", "opponent_mask": "PK-202", "result": "unknown" }
  ],
  "rankings": { "1": 3, "2": 1 }
}
```

**Step 1 — Resolve unknown results:**
Round 2 result is "unknown". Look up PK-202's round 2 history. If PK-202 shows `result: "loss"` against YP-221, then YP-221's actual result is `"win"`.

**Step 2 — Count W/L through `baseRankingsOnRound` (2):**
- Round 1: win → wins = 1
- Round 2: win (resolved) → wins = 2
- Result: `wins: 2, losses: 0`

**Step 3 — Count aff/neg through `roundToMatch - 1` (2):**
- Round 1: aff → affRounds = 1
- Round 2: neg → negRounds = 1
- Result: `affRounds: 1, negRounds: 1`

**Step 4 — Collect opponents through round 2:**
- Round 1: QM-223 (not a bye) → include
- Round 2: PK-202 (not a bye) → include
- Result: `opponentHistory: ["QM-223", "PK-202"]`

**Step 5 — Seed:**
`rankings["2"]` = 1 → `seed: 1`

**Step 6 — Bye eligibility:**
No bye in rounds 1–2 → `isByeEligible: true`

**Transformed output:**
```json
{
  "name": "YP-221",
  "isByeEligible": true,
  "wins": 2,
  "losses": 0,
  "affRounds": 1,
  "negRounds": 1,
  "seed": 1,
  "club": null,
  "opponentHistory": ["QM-223", "PK-202"]
}
```

---

## 8. Edge Cases & Notes

- **Duplicate masks:** Multiple entries can share the same `mask` value (different `entry_id`). The current system uses `mask` as the team `name`, so duplicate masks in the same event could cause incorrect opponent history resolution. Verify uniqueness within each event or use `entry_id` as a fallback identifier.

- **Missing rankings:** If a team has no ranking entry for `baseRankingsOnRound`, seed defaults to `999` (effectively unranked, will sort to the bottom).

- **`baseRankingsOnRound` vs `roundToMatch`:** These are intentionally separate. Rankings may lag behind the current round (e.g., you might pair round 6 using round 4 rankings if rounds 5's results aren't fully tabulated). W/L counts align with `baseRankingsOnRound`, while side counts and opponent history extend through `roundToMatch - 1`.

- **Unknown results for both sides:** If both teams show `"unknown"` for the same round, the result is genuinely unresolved. The system will count neither a win nor a loss for that round.

- **Bye detection:** Check `result === "bye"` in history entries. When a team has a bye, `opponent_mask` may still contain a value — always check the result field, not the presence of an opponent.
