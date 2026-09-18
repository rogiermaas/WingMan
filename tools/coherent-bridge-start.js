(function () {
    // Temporary SDK debugger experiment. No camera, flight controls or base-game files are changed.
    if (window.__escortTrafficBridge && window.__escortTrafficBridge.stop) window.__escortTrafficBridge.stop();
    var state = window.__escortTrafficBridge = { status: "loading CommBus", samples: 0, traffic: [], stopped: false, owner: "__ESCORT_OWNER__", startedAt: Date.now(), leaseUntil: Date.now() + 30000 };
    var busy = false;
    state.stop = function () { state.stopped = true; clearInterval(state.timer); clearInterval(state.loader); state.status = "stopped"; };
    function sample() {
        if (Date.now() > state.leaseUntil) { state.stop(); return; }
        if (busy || state.stopped) return;
        busy = true;
        var queryTimeout;
        Promise.race([Coherent.call("GET_AIR_TRAFFIC"), new Promise(function (_, reject) {
            queryTimeout = setTimeout(function () { reject(new Error("Traffic query timed out")); }, 3000);
        })]).then(function (traffic) {
            if (state.stopped) return;
            var now = Date.now();
            if (!Array.isArray(traffic) || traffic.length > 512) throw new Error("Invalid traffic array");
            state.traffic = traffic; state.receivedAt = now; state.samples++; state.status = "active";
            var messages = traffic.map(function (aircraft) {
                return { version: 1, kind: "aircraft", sourceTimestampMs: now, aircraft: aircraft };
            });
            messages.push({ version: 1, kind: "heartbeat", sourceTimestampMs: now, count: traffic.length });
            return Promise.all(messages.map(function (message) {
                return state.listener.callSimConnect("EscortPlane2024.Traffic.v1", JSON.stringify(message));
            }));
        }).catch(function (error) { state.status = "error: " + String(error); }).then(function () { clearTimeout(queryTimeout); busy = false; });
    }
    state.pump = sample;
    function initialize() {
        clearInterval(state.loader);
        state.listener = window.__escortCommBusListener || (window.__escortCommBusListener = RegisterCommBusListener());
        state.timer = setInterval(sample, 1000);
        sample();
    }
    if (typeof RegisterCommBusListener === "function") {
        initialize();
        return JSON.stringify({ status: "bridge started", renewableLeaseSeconds: 30, rateHz: 1 });
    }
    if (typeof RegisterCommBusListener !== "function") Include.addScript("/JS/Services/CommBus.js");
    var loadDeadline = Date.now() + 10000;
    state.loader = setInterval(function () {
        if (typeof RegisterCommBusListener === "function") {
            initialize();
        } else if (Date.now() > loadDeadline) { state.stop(); state.status = "CommBus script did not load"; }
    }, 100);
    return JSON.stringify({ status: "bridge starting", renewableLeaseSeconds: 30, rateHz: 1 });
})()
