(function() {
    function safe(x,depth) {
        if(x==null || typeof x==='string' || typeof x==='number' || typeof x==='boolean') return x;
        if(typeof x==='function') return '[function]';
        if(depth<=0) return {type:x.constructor&&x.constructor.name,keys:Object.keys(x).slice(0,30)};
        if(x instanceof Map) { var a=[]; x.forEach(function(v,k) { if(a.length<30) a.push([String(k),safe(v,depth-1)]); }); return a; }
        var out={};Object.keys(x).slice(0,40).forEach(function(k) {out[k]=safe(x[k],depth-1);});return out;
    }
    var out=[]; if(window.g_AtlasMgr) window.g_AtlasMgr.m_elements.forEach(function(v,k) {
        if(out.length>=40) return;
        out.push({id:v.id,path:k._resourcePath,component:k._componentName,instance:k._instanceId,text:k.textContent,values:safe(k.m_exposedValues,3),attributes:safe(k.m_exposedAttributes,2),metadata:safe(k.metadata,2)});
    });return JSON.stringify(out);
})()
