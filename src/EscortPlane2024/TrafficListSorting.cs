using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;

namespace EscortPlane2024;

internal enum TrafficColumn { Text, Number, Distance }

internal sealed class TrafficListSorting(ListView list, params TrafficColumn[] columns) : IComparer
{
    private int column;
    private bool descending;
    private readonly string[] headings = list.Columns.Cast<ColumnHeader>().Select(c => c.Text).ToArray();

    public static void Enable(ListView list, params TrafficColumn[] columns)
    {
        var sorter = new TrafficListSorting(list, columns);
        list.ColumnClick += (_, e) =>
        {
            sorter.descending = list.ListViewItemSorter == sorter && sorter.column == e.Column && !sorter.descending;
            sorter.column = e.Column;
            for (var i = 0; i < list.Columns.Count; i++)
                list.Columns[i].Text = sorter.headings[i] + (i == e.Column ? sorter.descending ? " ▼" : " ▲" : "");
            list.ListViewItemSorter = sorter;
            list.Sort();
        };
    }

    public int Compare(object? x, object? y)
    {
        if (x is not ListViewItem a || y is not ListViewItem b) return 0;
        var result = CompareValues(a.SubItems[column].Text, b.SubItems[column].Text, columns[column], descending);
        return result != 0 ? result : StringComparer.OrdinalIgnoreCase.Compare(a.Text, b.Text);
    }

    internal static int CompareValues(string a, string b, TrafficColumn kind, bool descending)
    {
        if (kind == TrafficColumn.Text)
            return (descending ? -1 : 1) * StringComparer.OrdinalIgnoreCase.Compare(a, b);
        var av = NumericValue(a, kind); var bv = NumericValue(b, kind);
        // Unknown values remain at the bottom in either direction.
        if (!av.HasValue || !bv.HasValue) return av.HasValue ? -1 : bv.HasValue ? 1 : 0;
        return (descending ? -1 : 1) * av.Value.CompareTo(bv.Value);
    }

    internal static double? NumericValue(string display, TrafficColumn kind)
    {
        var match = Regex.Match(display, @"^\s*([+-]?\d[\d.,'\s\u00a0\u202f]*)");
        if (!match.Success) return null;
        var token = Regex.Replace(match.Groups[1].Value, @"[\s'\u00a0\u202f]", "");
        var comma = token.LastIndexOf(','); var dot = token.LastIndexOf('.');
        if (comma >= 0 && dot >= 0)
            token = comma > dot ? token.Replace(".", "").Replace(',', '.') : token.Replace(",", "");
        else if (comma >= 0)
            token = token.Length - comma - 1 == 3 && !token.StartsWith("0,") ? token.Replace(",", "") : token.Replace(',', '.');
        else if (dot >= 0 && token.Count(c => c == '.') > 1) token = token.Replace(".", "");
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) return null;
        if (kind == TrafficColumn.Distance)
        {
            var unit = display[match.Length..].Trim().ToLowerInvariant();
            number *= unit switch { "km" => 1 / 1.852, "m" => 1 / 1852.0, "mi" => 1609.344 / 1852, _ => 1 };
        }
        return number;
    }
}
