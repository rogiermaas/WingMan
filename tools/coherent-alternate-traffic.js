(function () {
    // Read-only comparison of SDK map traffic queries; no camera or aircraft writes.
    var result = window.__escortAlternateTraffic = { startedAt: Date.now(), title: document.title };
    ["GET_AIR_TRAFFIC", "GET_TCAS_PLANES", "GET_FLARM_PLANES"].forEach(function (method) {
        result[method] = { status: "pending" };
        var timeout = setTimeout(function () { result[method] = { status: "timed out" }; }, 4000);
        Coherent.call(method).then(function (data) {
            clearTimeout(timeout);
            result[method] = { status: "received", receivedAt: Date.now(), data: data };
        }).catch(function (error) {
            clearTimeout(timeout);
            result[method] = { status: "error", error: String(error) };
        });
    });
    return JSON.stringify({ started: true });
})()
