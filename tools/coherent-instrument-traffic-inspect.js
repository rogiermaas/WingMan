(function () {
    function summary(o) {
        if (o == null || typeof o !== 'object') return typeof o;
        var result = { type: o.constructor && o.constructor.name, keys: Object.keys(o).slice(0, 120) };
        Object.keys(o).filter(function (k) { return /traffic|tcas|tracked|contact/i.test(k); }).slice(0, 12).forEach(function (k) {
            var value = o[k];
            if (value instanceof Map) result[k] = { type: 'Map', size: value.size };
            else if (Array.isArray(value)) result[k] = { type: 'Array', length: value.length };
            else if (value && typeof value === 'object') result[k] = { type: value.constructor && value.constructor.name, keys: Object.keys(value).slice(0, 50) };
            else if (typeof value !== 'function') result[k] = value;
        });
        return result;
    }
    var found = [];
    Array.prototype.forEach.call(document.querySelectorAll('*'), function (e) {
        if (e.instrument) found.push({ tag: e.tagName, instance: summary(e.instrument) });
        if (Object.keys(e).some(function (k) { return /trafficInstrument|tcas|trafficSystem/i.test(k); })) found.push({ tag: e.tagName, element: summary(e) });
    });
    return JSON.stringify({ title: document.title, instruments: found.slice(0, 16), scripts: Array.prototype.map.call(document.scripts, function (s) { return s.src; }) });
})()
