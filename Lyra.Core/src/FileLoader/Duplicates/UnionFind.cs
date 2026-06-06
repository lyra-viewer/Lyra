namespace Lyra.FileLoader.Duplicates;

internal sealed class UnionFind
{
    private readonly int[] _parent;
    private readonly int[] _rank;

    public UnionFind(int count)
    {
        _parent = new int[count];
        _rank = new int[count];
        for (var i = 0; i < count; i++)
            _parent[i] = i;
    }

    public int Find(int x)
    {
        while (_parent[x] != x)
        {
            _parent[x] = _parent[_parent[x]]; // path halving
            x = _parent[x];
        }

        return x;
    }

    public void Union(int a, int b)
    {
        var ra = Find(a);
        var rb = Find(b);
        if (ra == rb)
            return;

        if (_rank[ra] < _rank[rb])
            (ra, rb) = (rb, ra);

        _parent[rb] = ra;
        if (_rank[ra] == _rank[rb])
            _rank[ra]++;
    }
}