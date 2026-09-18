(function () {
    var b=document.querySelector('wtb78x-mfd'), f=b&&b.fsInstrument;
    return JSON.stringify({baseKeys:b?Object.keys(b).filter(function(k){return /xml|config|logic/i.test(k);}):[],
        handlers:b&&b.xmlConfig?Array.from(b.xmlConfig.getElementsByTagName('Handler')).map(function(e){return e.textContent;}):[],
        sharedKeys:f&&f.trafficInstrument&&f.trafficInstrument.sharedGlobalData?Object.keys(f.trafficInstrument.sharedGlobalData):[],
        scripts:Array.from(document.scripts).map(function(s){return s.src;}).filter(Boolean)});
})();
