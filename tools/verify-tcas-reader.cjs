// Offline checks of the diagnostic reader; no simulator connection or control.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const path = require('node:path');
const source = fs.readFileSync(path.join(__dirname, 'coherent-787-tcas-read.js'), 'utf8');
function snapshot(vector, predictionValid = true, available = true) {
    const contact = {uid:42, name:'Fixture', lat:52, lon:4, altitude:10000, lastContactTime:99000};
    const instrument = {trafficInstrument:{tracked:new Map([[42,contact]]),
        sharedGlobalData:{trafficContactData:new Map([[42,contact]]),isReady:true,updateId:1}},
        tcas:{simTime:{get:()=>100000},operatingModeSub:{get:()=> 'TA/RA'},
            getIntruders:()=>[{contact,isPredictionValid:predictionValid,relativePositionVec:vector,
                position:{lat:52,lon:4},altitude:{number:10000}}]}};
    return JSON.parse(vm.runInNewContext(source, {
        document:{querySelector:()=>available?{fsInstrument:instrument}:null,scripts:[]},
        SimVar:{GetSimVarValue:(name)=>name==='GPS GROUND TRUE TRACK'?90:0}
    }));
}
for (const [vector,bearing] of [[[0,1852,0],0],[[1852,0,0],90],[[0,-1852,0],180],[[-1852,0,0],270]]) {
    const result=snapshot(vector), relative=result.intruders[0].predictedRelative;
    assert.equal(relative.rangeNm,1);
    assert.equal(relative.bearingTrueDegrees,bearing);
    assert.equal(relative.bearingFromOwnTrackDegrees,(bearing-90+360)%360);
    assert.equal(result.contacts[0].contactAgeSeconds,1); // simulation time, not wall time
}
assert.equal(snapshot([0,1852,304.8]).intruders[0].predictedRelative.altitudeDifferenceFeet,1000);
for (const vector of [[NaN,0,0],[Infinity,0,0],[Number.MAX_VALUE,0,0]])
    assert.equal(snapshot(vector).intruders[0].predictedRelative,null);
assert.equal(snapshot([0,1852,0],false).intruders[0].predictedRelative,null);
assert.equal(snapshot([0,1852,0],true,false).instrumentAvailable,false);
console.log('TCAS reader checks passed: cardinal bearings, units, simulation age, invalid predictions and missing instrument.');
