(function () {
    var s = window.__escortAirResearch;
    if (s && s.binding && s.binding.stop) s.binding.stop();
    return JSON.stringify({ binding: s && s.binding, stopped: true });
})()
