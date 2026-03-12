namespace Lyra.PathUtils;

public sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null)
            return y is null ? 0 : -1;

        if (y is null)
            return 1;

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            if (char.IsDigit(x[ix]) && char.IsDigit(y[iy]))
            {
                // Skip leading zeros and extract numeric spans
                int nx = ix, ny = iy;
                while (nx < x.Length && x[nx] == '0') nx++;
                while (ny < y.Length && y[ny] == '0') ny++;

                int startX = nx, startY = ny;
                while (nx < x.Length && char.IsDigit(x[nx])) nx++;
                while (ny < y.Length && char.IsDigit(y[ny])) ny++;

                int lenX = nx - startX, lenY = ny - startY;
                if (lenX != lenY)
                    return lenX.CompareTo(lenY); // fewer significant digits = smaller number

                for (var i = 0; i < lenX; i++)
                {
                    var cmp = x[startX + i].CompareTo(y[startY + i]);
                    if (cmp != 0) return cmp;
                }

                ix = nx;
                iy = ny;
            }
            else
            {
                var cmp = char.ToLowerInvariant(x[ix]).CompareTo(char.ToLowerInvariant(y[iy]));
                if (cmp != 0) return cmp;
                ix++;
                iy++;
            }
        }

        return x.Length.CompareTo(y.Length);
    }
}