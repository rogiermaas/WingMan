(function () {
    // Run after the bounded listener comparison. Only invokes traffic read calls.
    var s = window.__escortAirResearch;
    if (!s) return JSON.stringify({ error: 'Run coherent-airtraffic-research-start.js first' });
    if (!s.alternatives) {
        s.alternatives = {};
        Object.keys(s.listeners).forEach(function (name) {
            var info = s.listeners[name];
            if (!info.ready) return;
            ['GET_TCAS_PLANES', 'GET_FLARM_PLANES'].forEach(function (method) {
                var r = s.alternatives[name + ':' + method] = { status: 'pending', startedAt: Date.now() };
                try {
                    info.listener.call(method).then(function (data) { r.status = 'received'; r.data = data; })
                        .catch(function (e) { r.status = 'error'; r.error = String(e); });
                } catch (e) { r.status = 'error'; r.error = String(e); }
            });
        });
    }
    return JSON.stringify({ title: document.title, readAt: Date.now(), results: s.alternatives });
})()
