(function() {
    var out=[];
    if(window.g_AtlasMgr) window.g_AtlasMgr.m_elements.forEach(function(v,k) {
        if(out.length>=60) return;
        var fields={}; Object.keys(v).forEach(function(p) {
            var x=v[p]; fields[p]=x==null || typeof x==='string' || typeof x==='number' || typeof x==='boolean' ? x : {type:x.constructor&&x.constructor.name,keys:Object.keys(x).slice(0,25)};
        });
        out.push({key:typeof k==='object' ? {type:k.constructor&&k.constructor.name,keys:Object.keys(k).slice(0,25)} : k,type:v.constructor&&v.constructor.name,fields:fields});
    });
    return JSON.stringify(out);
})()
