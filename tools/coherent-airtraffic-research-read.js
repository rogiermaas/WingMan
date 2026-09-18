(function () {
    var s = window.__escortAirResearch;
    if (!s) return JSON.stringify({ error: 'Not initialized' });
    if (Date.now() - s.startedAt > 120000) return JSON.stringify({ expired: true });
    var completedReads = JSON.parse(JSON.stringify(s.reads));
    var listeners = {};
    Object.keys(s.listeners).forEach(function (name) {
        var info = s.listeners[name]; listeners[name] = { ready: info.ready, error: info.error, call: typeof info.listener.call };
        if (info.ready) s.query(name, function () { return info.listener.call('GET_AIR_TRAFFIC'); });
    });
    s.query('Coherent.call', function () { return Coherent.call('GET_AIR_TRAFFIC'); });
    return JSON.stringify({ title: s.title, startedAt: s.startedAt, readAt: Date.now(), listeners: listeners, binding: s.binding, reads: completedReads });
})()
