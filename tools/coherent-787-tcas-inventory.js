(function () {
    var base = document.querySelector('wtb78x-mfd');
    var fs = base && base.fsInstrument;
    var ti = fs && fs.trafficInstrument;
    var tcas = fs && fs.tcas;
    function keys(value) { return value ? Object.keys(value).slice(0, 100) : []; }
    return JSON.stringify({ at: Date.now(), base: !!base, fs: !!fs,
        fsKeys: keys(fs).filter(function (x) { return /traffic|tcas|shared|map|bus/i.test(x); }),
        trafficKeys: keys(ti), tcasKeys: keys(tcas),
        sharedGlobals: Object.keys(window).filter(function (x) { return /trafficInstrumentSync|b78x.*traffic/i.test(x); }),
        trafficCount: ti && ti.contacts && ti.contacts.size,
        tcasCount: tcas && tcas.getIntruders && tcas.getIntruders().length,
        firstContactKeys: ti && ti.contacts && ti.contacts.size ? keys(ti.contacts.values().next().value) : [],
        firstIntruderKeys: tcas && tcas.getIntruders && tcas.getIntruders().length ? keys(tcas.getIntruders()[0]) : [],
        own: {lat: SimVar.GetSimVarValue('PLANE LATITUDE', 'degrees'), lon: SimVar.GetSimVarValue('PLANE LONGITUDE', 'degrees')},
        native: ['TCAS MODE','TCAS INTRUDER NETWORK ID','TCAS INTRUDER BEARING','TCAS INTRUDER DISTANCE','TCAS INTRUDER RELATIVE ALTITUDE'].map(function (key) {
            try { return {name:key, value:SimVar.GetSimVarValue(key, key.indexOf('BEARING')>=0?'degrees':key.indexOf('DISTANCE')>=0?'nautical miles':key.indexOf('ALTITUDE')>=0?'feet':'number')}; }
            catch (e) { return {name:key,error:String(e)}; }
        })
    });
})();
