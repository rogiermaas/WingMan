namespace EscortPlane2024;

internal sealed partial class MainForm
{
    private readonly Button searchMsfs = Button("Search MSFS aircraft");
    private readonly Dictionary<string, NearbyAircraft> nearbyRows = [];
    private DateTimeOffset nextNearbyUi;

    private void SearchMsfsAircraft()
    {
        // Discovery must not deselect or stop an established WingMan lead.
        wantTraffic = true; sim.ScanPositions = true;
        radius.Value = 100; nextScan = default; nextNameplateRead = default; nextBridgeRetry = default;
        nextNearbyUi = default;
        RefreshUi();
    }

    private void RefreshNearbyAircraft(DateTimeOffset now)
    {
        searchMsfs.Enabled = sim.Connected || Program.DemoMode;
        SetText(searchMsfs, wantTraffic ? "Search MSFS again" : "Search MSFS aircraft");
        if (now < nextNearbyUi) return;
        nextNearbyUi = now.AddSeconds(1);
        var local = wantTraffic ? TrafficContacts.Build(sim.CoherentTraffic.Aircraft, sim.Traffic.Aircraft, sim.Own?.Position) : [];
        var labels = wantTraffic && nameplates.ReadAt is { } readAt && now - readAt < TimeSpan.FromSeconds(5) ? nameplates.Contacts : [];
        var rows = NearbyAircraft.Build(members.Values, relayTracks, local, labels, sim.Own?.Position,
            now, networkOptions.Id, relayLead, relay.Connected, sim.Connected);
        if (Program.DemoMode && rows.Count == 0)
            rows = [new("demo:relay", "WingMan pilot (demo)", "Boeing 787-10", "4.00 NM", "WingMan · Live", true, true, null, null, "Offline interface preview"),
                new("demo:local", "ID S421 (demo)", "Airbus A321", "2.30 NM", "MSFS · Live", false, true, null, null, "Offline interface preview"),
                new("demo:label", "Visible pilot (demo)", "Airbus A320", "5.2 km", "MSFS · Name only", false, false, null, null, "Offline interface preview")];
        nearbyRows.Clear(); foreach (var row in rows) nearbyRows[row.Key] = row;
        pilots.BeginUpdate();
        try
        {
            foreach (ListViewItem row in pilots.Items.Cast<ListViewItem>().ToArray())
                if (!nearbyRows.ContainsKey((string)row.Tag!)) pilots.Items.Remove(row);
            foreach (var aircraft in rows)
            {
                var row = pilots.Items.Cast<ListViewItem>().FirstOrDefault(r => (string)r.Tag! == aircraft.Key);
                if (row == null) { row = new ListViewItem(["", "", "", ""]) { Tag = aircraft.Key }; pilots.Items.Add(row); }
                StableTrafficListView.SetText(row, 0, aircraft.Name); StableTrafficListView.SetText(row, 1, aircraft.Model);
                StableTrafficListView.SetText(row, 2, aircraft.Distance); StableTrafficListView.SetText(row, 3, aircraft.Status);
                if (row.BackColor != aircraft.RowColor) row.BackColor = aircraft.RowColor;
                var textColor = aircraft.CanFollow ? Color.FromArgb(34, 54, 75) : Color.DimGray;
                if (row.ForeColor != textColor) row.ForeColor = textColor;
                row.ToolTipText = aircraft.Detail;
            }
            pilots.SortIfNeeded();
        }
        finally { pilots.EndUpdate(); }
    }

    private NearbyAircraft? SelectedNearbyAircraft => pilots.SelectedItems.Count == 1
        ? nearbyRows.GetValueOrDefault((string)pilots.SelectedItems[0].Tag!) : null;

    private void FollowSelectedNearbyAircraft()
    {
        var selected = SelectedNearbyAircraft;
        if (selected?.CanFollow != true) return;
        if (selected.RelayId is { } lead) BeginRelayFollow(lead);
        else if (selected.LocalId is { } id)
        {
            // Resolve by stable source+ID, never by a row index or an ambiguous name.
            contactCatalog = TrafficContacts.Build(sim.CoherentTraffic.Aircraft, sim.Traffic.Aircraft, sim.Own?.Position);
            ChooseContact(id);
        }
    }
}
