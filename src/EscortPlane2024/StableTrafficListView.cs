namespace EscortPlane2024;

internal sealed class StableTrafficListView : ListView
{
    public StableTrafficListView() { DoubleBuffered = true; }

    internal static void SetText(ListViewItem row, int column, string value)
    {
        if (row.SubItems[column].Text != value) row.SubItems[column].Text = value;
    }

    // Sorting unchanged rows also redraws the native list. Only reorder when a
    // changed value actually affects the currently selected ordering.
    internal bool SortIfNeeded()
    {
        if (ListViewItemSorter is not { } sorter) return false;
        for (var i = 1; i < Items.Count; i++)
        {
            if (sorter.Compare(Items[i-1], Items[i]) <= 0) continue;
            var anchor = IsHandleCreated && TopItem is { Index: > 0 } top ? top : null;
            Sort();
            if (anchor != null) TopItem = anchor;
            return true;
        }
        return false;
    }
}
