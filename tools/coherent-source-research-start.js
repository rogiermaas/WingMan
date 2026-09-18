(function () {
    var s = window.__escortSourceResearch = { startedAt: Date.now(), sources: {} };
    ['/JS/Services/WorldMap.js', '/JS/Services/Community.js', '/JS/Services/Nameplate.js',
     '/Pages/InGamePanels/VFRMap/VFRMap.js', '/Pages/VCockpit/Instruments/Shared/Map/MapInstrument.js',
     '/Global/mainUi.bundle.js', '/Global/pkg/base.bundle.js'].forEach(function (url) {
        var xhr = new XMLHttpRequest(); xhr.open('GET', url, true); xhr.timeout = 10000;
        xhr.onload = function () {
            var text = xhr.responseText;
            var matches = [], re = /GET_[A-Z_]*(?:TRAFFIC|PLAYER|MULTIPLAYER)[A-Z_]*|JS_LISTENER_[A-Z_]*(?:TRAFFIC|MARKER|MULTIPLAYER|WORLDMAP)[A-Z_]*|[a-zA-Z_]*(?:PlayerPosition|playerPosition|WorldPosition|worldPosition|AirTraffic|airTraffic|nameplate|NamePlate)[a-zA-Z_]*/g;
            var m; while ((m = re.exec(text)) && matches.length < 80) matches.push(text.slice(Math.max(0, m.index - 120), m.index + 220));
            s.sources[url] = { status: xhr.status, length: text.length, matches: matches };
        };
        xhr.onerror = xhr.ontimeout = function () { s.sources[url] = { error: 'unavailable' }; };
        xhr.send();
    });
    return JSON.stringify({ started: true });
})()
