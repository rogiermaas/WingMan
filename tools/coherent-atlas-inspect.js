(function() {
    function shape(o) { if(o == null) return o; if(typeof o !== 'object') return typeof o === 'function' ? '[function]' : o; return {type: o.constructor && o.constructor.name, keys:Object.keys(o).slice(0,60)}; }
    var manager = window.g_AtlasMgr;
    var fields = {}; Object.keys(manager || {}).forEach(function(k) { fields[k] = shape(manager[k]); });
    var root = {fields: fields, methods: manager ? Object.getOwnPropertyNames(Object.getPrototypeOf(manager)) : []};
    window.__escortAtlasSources = {};
    ['/Global/atlas.bundle.js','/Global/pkg/base.bundle.js'].forEach(function(url) {
        var xhr = new XMLHttpRequest(); xhr.open('GET',url,true);
        xhr.onload = function() { var text=xhr.responseText; var matches=[]; var re=/nameplate|namePlate|NamePlate|Nameplate/g; var m; while((m=re.exec(text)) && matches.length<15) matches.push(text.slice(Math.max(0,m.index-220),m.index+450)); window.__escortAtlasSources[url]={length:text.length,matches:matches}; }; xhr.send();
    });
    return JSON.stringify(root);
})()
