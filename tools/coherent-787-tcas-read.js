(function () {
    var element = document.querySelector('wtb78x-mfd');
    var fs = element && element.fsInstrument;
    var ti = fs && fs.trafficInstrument, tcas = fs && fs.tcas;
    function val(x) { return x && typeof x.get === 'function' ? x.get() : x; }
    function num(x) { return x && typeof x.number === 'number' ? x.number : x; }
    function finite(x) { return typeof x === 'number' && isFinite(x) && Math.abs(x) < 1e100; }
    var simTime = tcas && val(tcas.simTime);
    var ownTrack = SimVar.GetSimVarValue('GPS GROUND TRUE TRACK','degrees');
    function age(time) { return finite(simTime) && finite(time) ? (simTime-time)/1000 : null; }
    var shared = ti && ti.sharedGlobalData;
    var contacts = shared && shared.trafficContactData ? Array.from(shared.trafficContactData.values()).slice(0,512).map(function(c) {
        return {uid:c.uid, name:c.name, lat:c.lat, lon:c.lon, altitude:c.altitude, heading:c.heading,
            lastContactTime:c.lastContactTime, contactAgeSeconds:age(c.lastContactTime),
            groundSpeed:c.groundSpeed, groundTrack:c.groundTrack, verticalSpeed:c.verticalSpeed};
    }) : [];
    var intruders = tcas && tcas.getIntruders ? tcas.getIntruders().slice(0,128).map(function(i) {
        var v = i.relativePositionVec;
        var valid = i.isPredictionValid === true && v && v.length === 3 && Array.from(v).every(finite);
        var bearing = valid ? (Math.atan2(v[0],v[1])*180/Math.PI+360)%360 : null;
        return {uid:i.contact && i.contact.uid, name:i.contact && i.contact.name,
            contactTime:i.contact && i.contact.lastContactTime,
            contactAgeSeconds:age(i.contact && i.contact.lastContactTime), isPredictionValid:i.isPredictionValid === true,
            position:i.position && {lat:i.position.lat,lon:i.position.lon}, altitude:num(i.altitude),
            predictedRelative:valid ? {rangeNm:Math.sqrt(v[0]*v[0]+v[1]*v[1])/1852,
                bearingTrueDegrees:bearing, bearingFromOwnTrackDegrees:finite(ownTrack) ? (bearing-ownTrack+360)%360 : null,
                altitudeDifferenceFeet:v[2]/0.3048} : null,
            relativePositionVec:i.relativePositionVec && Array.from(i.relativePositionVec),
            relativeVelocityVec:i.relativeVelocityVec && Array.from(i.relativeVelocityVec), alertLevel:val(i.alertLevel)};
    }) : [];
    return JSON.stringify({at:Date.now(),instrumentAvailable:!!ti,syncRole:ti&&ti.syncRole,
        trackedCount:ti&&ti.tracked&&ti.tracked.size, sharedCount:contacts.length, sharedUpdateId:shared&&shared.updateId,
        isReady:shared&&shared.isReady, tcasMode:tcas&&val(tcas.operatingModeSub),simTime:simTime,
        units:{contactAltitude:'feet',contactGroundSpeed:'knots',contactGroundTrack:'degrees true',contactVerticalSpeed:'feet/minute',
            contactTime:'simulation Unix milliseconds',intruderAltitude:'feet',relativePositionVec:'east/north/up meters',relativeVelocityVec:'east/north/up meters/second'},
        intruderPositionIsPredicted:true,
        tcasCount:intruders.length, contacts:contacts,intruders:intruders,
        own:{lat:SimVar.GetSimVarValue('PLANE LATITUDE','degrees'),lon:SimVar.GetSimVarValue('PLANE LONGITUDE','degrees'),
            altFeet:SimVar.GetSimVarValue('PLANE ALTITUDE','feet'),track:ownTrack},
        scripts:Array.from(document.scripts).map(function(s){return s.src;}).filter(function(s){return /b78|boeing/i.test(s);})});
})();
