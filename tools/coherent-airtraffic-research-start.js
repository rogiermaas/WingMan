(function () {
    // Bounded read-only comparison. No aircraft, camera, or existing map writes.
    var s = window.__escortAirResearch = { startedAt: Date.now(), title: document.title, reads: {}, listeners: {} };
    ['JS_LISTENER_AIR_TRAFFIC', 'JS_LISTENER_MAPS'].forEach(function (name) {
        var info = s.listeners[name] = { ready: false };
        try {
            info.listener = RegisterViewListener(name, function () { info.ready = true; info.readyAt = Date.now(); });
        } catch (e) { info.error = String(e); }
    });
    s.query = function (key, invoke) {
        if (s.reads[key] && s.reads[key].status === 'pending' && Date.now() - s.reads[key].startedAt < 4000) return;
        var r = s.reads[key] = { startedAt: Date.now(), status: 'pending' };
        try { invoke().then(function (data) { r.status = 'received'; r.at = Date.now(); r.data = data; })
            .catch(function (e) { r.status = 'error'; r.error = String(e); }); }
        catch (e) { r.status = 'error'; r.error = String(e); }
    };
    return JSON.stringify({ started: true, title: s.title });
})()
