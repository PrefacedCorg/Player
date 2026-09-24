namespace Player.Library;

/// <summary>
/// 自然顺序比较：保证 "第2集" 排在 "第10集" 前面，避免纯字符串比较造成列表乱序。
/// </summary>
public static class NaturalComparer
{
    public static int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
            {
                while (i < x.Length && x[i] == '0')
                {
                    i++;
                }

                while (j < y.Length && y[j] == '0')
                {
                    j++;
                }

                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsDigit(x[i]))
                {
                    i++;
                }

                while (j < y.Length && char.IsDigit(y[j]))
                {
                    j++;
                }

                var lengthX = i - startX;
                var lengthY = j - startY;
                if (lengthX != lengthY)
                {
                    return lengthX - lengthY;
                }

                for (var k = 0; k < lengthX; k++)
                {
                    var digit = x[startX + k].CompareTo(y[startY + k]);
                    if (digit != 0)
                    {
                        return digit;
                    }
                }

                continue;
            }

            var ch = char.ToUpperInvariant(x[i]).CompareTo(char.ToUpperInvariant(y[j]));
            if (ch != 0)
            {
                return ch;
            }

            i++;
            j++;
        }

        return x.Length - i - (y.Length - j);
    }
}