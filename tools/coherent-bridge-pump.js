(function () {
    var state = window.__escortTrafficBridge;
    if (!state || !state.listener) return JSON.stringify({ status: "bridge must be started after CommBus loads" });
    state.pump();
    return JSON.stringify({ status: state.status, samples: state.samples, receivedAt: state.receivedAt, traffic: state.traffic });
})()
