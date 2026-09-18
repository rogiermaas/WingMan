(function () {
    var state = window.__escortTrafficBridge;
    return JSON.stringify(state ? { status: state.status, samples: state.samples, receivedAt: state.receivedAt, traffic: state.traffic } : { status: "not started" });
})()
