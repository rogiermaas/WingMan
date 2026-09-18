(function () {
    var s = window.__escortAirResearch;
    if (!s || !s.listeners.JS_LISTENER_MAPS.ready) return JSON.stringify({ error: 'Map listener not ready' });
    if (s.binding && s.binding.stop) s.binding.stop();
    s.startedAt = Date.now(); s.reads = {};
    var listener = s.listeners.JS_LISTENER_MAPS.listener;
    var id = 'EscortResearch_' + Date.now();
    var b = s.binding = { id: id, startedAt: Date.now(), status: 'requested' };
    var bound = function (binder, uid) {
        if (!binder || binder.friendlyName !== id || b.stopped) return;
        b.binder = binder; b.uid = uid; b.status = 'bound';
        var pos = new LatLong(SimVar.GetSimVarValue('PLANE LATITUDE', 'degrees'), SimVar.GetSimVarValue('PLANE LONGITUDE', 'degrees'));
        b.center = pos;
        Coherent.call('SET_MAP_PARAMS', uid, pos, 185200).catch(function (e) { b.error = String(e); });
        Coherent.call('SHOW_MAP', uid, true).catch(function (e) { b.error = String(e); });
        if (!b.initialized) { b.initialized = true; Coherent.call('SET_MAP_RESOLUTION', uid, 32, 32).catch(function (e) { b.error = String(e); }); }
    };
    b.stop = function () {
        if (b.stopped) return;
        b.stopped = true; b.status = 'unbound';
        listener.off('MapBinded', bound);
        listener.trigger('JS_UNBIND_BINGMAP', id);
        clearTimeout(b.timeout);
    };
    listener.on('MapBinded', bound);
    listener.trigger('JS_BIND_BINGMAP', id, 0);
    b.timeout = setTimeout(b.stop, 60000);
    return JSON.stringify({ binding: b });
})()
