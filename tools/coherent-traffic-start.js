(function () {
    // Diagnostic scratch state only; the Coherent calls below read traffic.
    window.__escortTrafficProbe = { startedAt: Date.now(), status: "pending" };
    Coherent.call("GET_AIR_TRAFFIC").then(function (traffic) {
        window.__escortTrafficProbe = { receivedAt: Date.now(), status: "received", traffic: traffic };
    }).catch(function (error) {
        window.__escortTrafficProbe = { receivedAt: Date.now(), status: "error", error: String(error) };
    });
    return JSON.stringify({ title: document.title, status: "traffic query requested" });
})()
