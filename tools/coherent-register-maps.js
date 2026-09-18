(function () {
    if (!window.__escortMapsListener) {
        window.__escortMapsListener = RegisterViewListener("JS_LISTENER_MAPS", function () { window.__escortMapsReady = true; });
    }
    return JSON.stringify({ registered: true, ready: !!window.__escortMapsReady, title: document.title });
})()
