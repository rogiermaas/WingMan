(function () {
    return JSON.stringify({ title: document.title,
        scripts: Array.prototype.map.call(document.scripts, function (s) { return s.src; }),
        resources: performance.getEntriesByType ? performance.getEntriesByType('resource').map(function (r) { return r.name; }).filter(function (n) { return /\.js|worldmap|multiplayer|community/i.test(n); }).slice(-160) : [],
        globals: Object.keys(window).filter(function (n) { return /map|player|traffic|community|listener/i.test(n); }).slice(0, 150),
        tags: Array.prototype.map.call(document.querySelectorAll('*'), function (e) { return e.tagName; }).filter(function (n, i, a) { return /map|player|traffic|community/i.test(n) && a.indexOf(n) === i; }).slice(0, 80)
    });
})()
