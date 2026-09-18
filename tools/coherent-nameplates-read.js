(function () {
    // Read the existing HUD labels only. Atlas IDs are UI IDs, not aircraft object IDs.
    var manager = window.g_AtlasMgr;
    if (!manager || !manager.m_elements || typeof manager.m_elements.forEach !== 'function')
        throw new Error('MSFS nameplate view is unavailable');
    var contacts = [];
    manager.m_elements.forEach(function (info, element) {
        if (contacts.length >= 512 || element._componentName !== 'hud_airtraffic') return;
        var attributes = element.m_exposedAttributes;
        if (!attributes || typeof attributes.get !== 'function') return;
        function value(key) { var item = attributes.get(key); return item && typeof item.value === 'string' ? item.value : ''; }
        var name = value('Gamertag');
        if (!name) return;
        contacts.push({ labelId: String(element._instanceId || info.id), name: name, model: value('Serie'),
            aircraftType: value('Type'), distance: value('Distance'), altitude: value('Altitude') });
    });
    return JSON.stringify({ version: 1, readAt: Date.now(), contacts: contacts });
})()
