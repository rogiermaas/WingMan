(function () {
    var fields = [
        ['AUTOPILOT THROTTLE ARM','bool'], ['AUTOTHROTTLE ACTIVE','bool'],
        ['L:AS01B_AUTO_THROTTLE_ARM_STATE','number']
    ];
    return JSON.stringify({ title: document.title, readAt:Date.now(), values:fields.map(function (v) {
        try { return { name:v[0], value:SimVar.GetSimVarValue(v[0],v[1]) }; }
        catch (e) { return { name:v[0],error:String(e) }; }
    }) });
})()
