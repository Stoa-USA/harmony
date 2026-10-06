namespace Harmony.Lib.Algorithms;

// Exact minimum-cost perfect matching on a general (non-bipartite) graph.
//
// Pairing a round is a matching problem: every team gets exactly one opponent and
// the total matchup cost should be as small as possible. On a general graph the
// LP relaxation of that problem is not integral (odd cycles can be "matched" at
// one half each), which is why a generic MIP/CP solver struggles to prove a
// pairing optimal. Edmonds' blossom algorithm handles the odd sets directly and
// runs in O(n^3), so a 200-team round solves in milliseconds with a proof of
// optimality, no time budget, and no chance of returning a worse pairing because
// a timer expired.
//
// This is the primal-dual O(n^3) maximum-weight matching formulation (the dense
// "weighted blossom" algorithm with explicit dual variables, blossom contraction
// and expansion). Costs are turned into weights by w = Offset - cost, with the
// offset large enough that any matching with more edges outweighs any matching
// with fewer, so the maximum-weight matching is a maximum-cardinality one and,
// among those, minimum-cost. If the result is not perfect, no perfect matching
// exists.
//
// Vertices are 0-based for callers and 1-based internally. Indices above n are
// contracted blossoms; there are at most n-1 of them, so arrays are sized 2n.
internal sealed class MinCostPerfectMatching
{
    private struct WeightedEdge
    {
        public int U, V;
        public long W;
    }

    private readonly int _n;
    private int _nx;
    private readonly WeightedEdge[][] _g;
    private readonly long[] _lab;
    private readonly int[] _match, _slack, _st, _pa, _s, _vis;
    private readonly int[][] _flowerFrom;
    private readonly List<int>[] _flower;
    private readonly Queue<int> _q = new();
    private int _visitStamp;

    private MinCostPerfectMatching(int n)
    {
        _n = n;
        var size = 2 * n + 1;
        _g = new WeightedEdge[size][];
        for (var i = 0; i < size; i++) _g[i] = new WeightedEdge[size];
        _lab = new long[size];
        _match = new int[size];
        _slack = new int[size];
        _st = new int[size];
        _pa = new int[size];
        _s = new int[size];
        _vis = new int[size];
        _flowerFrom = new int[size][];
        for (var i = 0; i < size; i++) _flowerFrom[i] = new int[size];
        _flower = new List<int>[size];
        for (var i = 0; i < size; i++) _flower[i] = new List<int>();
        for (var u = 1; u < size; u++)
            for (var v = 1; v < size; v++)
                _g[u][v] = new WeightedEdge { U = u, V = v, W = 0 };
    }

    /// <summary>
    /// Returns partner[i] for every vertex i in 0..n-1, or null if the graph has no
    /// perfect matching. Edges are (u, v, cost) with 0-based endpoints; duplicate
    /// edges keep the cheapest. Costs may be negative.
    /// </summary>
    public static int[]? Solve(int n, IReadOnlyList<(int U, int V, long Cost)> edges)
    {
        if (n == 0) return Array.Empty<int>();
        if (n % 2 != 0 || edges.Count == 0) return null;

        long cmin = long.MaxValue, cmax = long.MinValue;
        foreach (var e in edges)
        {
            cmin = Math.Min(cmin, e.Cost);
            cmax = Math.Max(cmax, e.Cost);
        }
        // Any matching with k+1 edges must outweigh any with k edges:
        // (k+1)(offset - cmax) > k(offset - cmin) for all k < n/2.
        var offset = checked(cmax + 1 + (n / 2) * (cmax - cmin));

        var m = new MinCostPerfectMatching(n);
        foreach (var e in edges)
        {
            if (e.U == e.V) continue;
            var u = e.U + 1;
            var v = e.V + 1;
            var w = checked(offset - e.Cost);
            if (m._g[u][v].W == 0 || w > m._g[u][v].W)
            {
                m._g[u][v].W = w;
                m._g[v][u].W = w;
            }
        }

        var matched = m.Run();
        if (matched * 2 != n) return null;

        var partner = new int[n];
        for (var u = 1; u <= n; u++) partner[u - 1] = m._match[u] - 1;
        return partner;
    }

    private int Run()
    {
        Array.Clear(_match, 0, _match.Length);
        _nx = _n;
        for (var u = 1; u <= _n; u++)
            for (var v = 1; v <= _n; v++)
                _flowerFrom[u][v] = u == v ? u : 0;

        long wMax = 0;
        for (var u = 1; u <= _n; u++)
            for (var v = 1; v <= _n; v++)
                wMax = Math.Max(wMax, _g[u][v].W);
        for (var u = 1; u <= _n; u++) _lab[u] = wMax;
        for (var u = 1; u <= _n; u++) _st[u] = u;

        var matches = 0;
        while (Matching()) matches++;
        return matches;
    }

    private long EDelta(in WeightedEdge e) => _lab[e.U] + _lab[e.V] - _g[e.U][e.V].W * 2;

    private void UpdateSlack(int u, int x)
    {
        if (_slack[x] == 0 || EDelta(_g[u][x]) < EDelta(_g[_slack[x]][x])) _slack[x] = u;
    }

    private void SetSlack(int x)
    {
        _slack[x] = 0;
        for (var u = 1; u <= _n; u++)
            if (_g[u][x].W > 0 && _st[u] != x && _s[_st[u]] == 0) UpdateSlack(u, x);
    }

    private void QPush(int x)
    {
        if (x <= _n) _q.Enqueue(x);
        else foreach (var i in _flower[x]) QPush(i);
    }

    private void SetSt(int x, int b)
    {
        _st[x] = b;
        if (x > _n) foreach (var i in _flower[x]) SetSt(i, b);
    }

    private int GetPr(int b, int xr)
    {
        var pr = _flower[b].IndexOf(xr);
        if (pr % 2 == 1)
        {
            _flower[b].Reverse(1, _flower[b].Count - 1);
            return _flower[b].Count - pr;
        }
        return pr;
    }

    private void SetMatch(int u, int v)
    {
        _match[u] = _g[u][v].V;
        if (u <= _n) return;
        var e = _g[u][v];
        var xr = _flowerFrom[u][e.U];
        var pr = GetPr(u, xr);
        for (var i = 0; i < pr; i++) SetMatch(_flower[u][i], _flower[u][i ^ 1]);
        SetMatch(xr, v);
        Rotate(_flower[u], pr);
    }

    private static void Rotate(List<int> list, int k)
    {
        if (k == 0) return;
        var rotated = list.Skip(k).Concat(list.Take(k)).ToList();
        list.Clear();
        list.AddRange(rotated);
    }

    private void Augment(int u, int v)
    {
        while (true)
        {
            var xnv = _st[_match[u]];
            SetMatch(u, v);
            if (xnv == 0) return;
            SetMatch(xnv, _st[_pa[xnv]]);
            u = _st[_pa[xnv]];
            v = xnv;
        }
    }

    private int GetLca(int u, int v)
    {
        _visitStamp++;
        while (u != 0 || v != 0)
        {
            if (u != 0)
            {
                if (_vis[u] == _visitStamp) return u;
                _vis[u] = _visitStamp;
                u = _st[_match[u]];
                if (u != 0) u = _st[_pa[u]];
            }
            (u, v) = (v, u);
        }
        return 0;
    }

    private void AddBlossom(int u, int lca, int v)
    {
        var b = _n + 1;
        while (b <= _nx && _st[b] != 0) b++;
        if (b > _nx) _nx++;
        _lab[b] = 0;
        _s[b] = 0;
        _match[b] = _match[lca];
        _flower[b].Clear();
        _flower[b].Add(lca);
        for (int x = u, y; x != lca; x = _st[_pa[y]])
        {
            _flower[b].Add(x);
            _flower[b].Add(y = _st[_match[x]]);
            QPush(y);
        }
        _flower[b].Reverse(1, _flower[b].Count - 1);
        for (int x = v, y; x != lca; x = _st[_pa[y]])
        {
            _flower[b].Add(x);
            _flower[b].Add(y = _st[_match[x]]);
            QPush(y);
        }
        SetSt(b, b);
        for (var x = 1; x <= _nx; x++)
        {
            _g[b][x].W = 0;
            _g[x][b].W = 0;
        }
        for (var x = 1; x <= _n; x++) _flowerFrom[b][x] = 0;
        foreach (var xs in _flower[b])
        {
            for (var x = 1; x <= _nx; x++)
            {
                if (_g[b][x].W == 0 || EDelta(_g[xs][x]) < EDelta(_g[b][x]))
                {
                    _g[b][x] = _g[xs][x];
                    _g[x][b] = _g[x][xs];
                }
            }
            for (var x = 1; x <= _n; x++)
                if (_flowerFrom[xs][x] != 0) _flowerFrom[b][x] = xs;
        }
        SetSlack(b);
    }

    private void ExpandBlossom(int b)
    {
        foreach (var i in _flower[b]) SetSt(i, i);
        var xr = _flowerFrom[b][_g[b][_pa[b]].U];
        var pr = GetPr(b, xr);
        for (var i = 0; i < pr; i += 2)
        {
            var xs = _flower[b][i];
            var xns = _flower[b][i + 1];
            _pa[xs] = _g[xns][xs].U;
            _s[xs] = 1;
            _s[xns] = 0;
            _slack[xs] = 0;
            SetSlack(xns);
            QPush(xns);
        }
        _s[xr] = 1;
        _pa[xr] = _pa[b];
        for (var i = pr + 1; i < _flower[b].Count; i++)
        {
            var xs = _flower[b][i];
            _s[xs] = -1;
            SetSlack(xs);
        }
        _st[b] = 0;
    }

    private bool OnFoundEdge(in WeightedEdge e)
    {
        var u = _st[e.U];
        var v = _st[e.V];
        if (_s[v] == -1)
        {
            _pa[v] = e.U;
            _s[v] = 1;
            var nu = _st[_match[v]];
            _slack[v] = 0;
            _slack[nu] = 0;
            _s[nu] = 0;
            QPush(nu);
        }
        else if (_s[v] == 0)
        {
            var lca = GetLca(u, v);
            if (lca == 0)
            {
                Augment(u, v);
                Augment(v, u);
                return true;
            }
            AddBlossom(u, lca, v);
        }
        return false;
    }

    private bool Matching()
    {
        for (var x = 1; x <= _nx; x++)
        {
            _s[x] = -1;
            _slack[x] = 0;
        }
        _q.Clear();
        for (var x = 1; x <= _nx; x++)
        {
            if (_st[x] == x && _match[x] == 0)
            {
                _pa[x] = 0;
                _s[x] = 0;
                QPush(x);
            }
        }
        if (_q.Count == 0) return false;

        while (true)
        {
            while (_q.Count > 0)
            {
                var u = _q.Dequeue();
                if (_s[_st[u]] == 1) continue;
                for (var v = 1; v <= _n; v++)
                {
                    if (_g[u][v].W > 0 && _st[u] != _st[v])
                    {
                        if (EDelta(_g[u][v]) == 0)
                        {
                            if (OnFoundEdge(_g[u][v])) return true;
                        }
                        else
                        {
                            UpdateSlack(u, _st[v]);
                        }
                    }
                }
            }

            var d = long.MaxValue;
            for (var b = _n + 1; b <= _nx; b++)
                if (_st[b] == b && _s[b] == 1) d = Math.Min(d, _lab[b] / 2);
            for (var x = 1; x <= _nx; x++)
            {
                if (_st[x] == x && _slack[x] != 0)
                {
                    if (_s[x] == -1) d = Math.Min(d, EDelta(_g[_slack[x]][x]));
                    else if (_s[x] == 0) d = Math.Min(d, EDelta(_g[_slack[x]][x]) / 2);
                }
            }
            for (var u = 1; u <= _n; u++)
            {
                if (_s[_st[u]] == 0)
                {
                    if (_lab[u] <= d) return false;
                    _lab[u] -= d;
                }
                else if (_s[_st[u]] == 1)
                {
                    _lab[u] += d;
                }
            }
            for (var b = _n + 1; b <= _nx; b++)
            {
                if (_st[b] == b)
                {
                    if (_s[_st[b]] == 0) _lab[b] += d * 2;
                    else if (_s[_st[b]] == 1) _lab[b] -= d * 2;
                }
            }
            _q.Clear();
            for (var x = 1; x <= _nx; x++)
            {
                if (_st[x] == x && _slack[x] != 0 && _st[_slack[x]] != x && EDelta(_g[_slack[x]][x]) == 0)
                {
                    if (OnFoundEdge(_g[_slack[x]][x])) return true;
                }
            }
            for (var b = _n + 1; b <= _nx; b++)
                if (_st[b] == b && _s[b] == 1 && _lab[b] == 0) ExpandBlossom(b);
        }
    }
}
