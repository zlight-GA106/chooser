/* Static app shell stays on device; user banks and answers never leave IndexedDB. */
var VERSION = 'chooser-shell-1.4.1';
var FILES = ['./index.html', './help.html', './example.xml?v=1.4.1', './style.css?v=1.4.1', './core.js?v=1.4.1', './storage.js?v=1.4.1', './app.js?v=1.4.1', './icon.svg?v=1.4.1', './icon.ico?v=1.4.1', './manifest.webmanifest?v=1.4.1', './icon-192.png?v=1.4.1', './icon-512.png?v=1.4.1'];
self.addEventListener('install', function (event) {
  event.waitUntil(caches.open(VERSION).then(function (cache) { return cache.addAll(FILES); }).then(function () { return self.skipWaiting(); }));
});
self.addEventListener('activate', function (event) {
  event.waitUntil(caches.keys().then(function (keys) {
    return Promise.all(keys.filter(function (key) { return key.indexOf('chooser-shell-') === 0 && key !== VERSION; }).map(function (key) { return caches.delete(key); }));
  }).then(function () { return self.clients.claim(); }));
});
self.addEventListener('fetch', function (event) {
  var request = event.request;
  if (request.method !== 'GET' || new URL(request.url).origin !== self.location.origin) return;
  var scope = self.registration.scope;
  if (request.url.indexOf(scope) !== 0) return;
  var path = new URL(request.url).pathname;
  if (request.mode === 'navigate') {
    var base = new URL(scope).pathname;
    if (path !== base && path !== base + 'index.html' && path !== base + 'help.html') return;
    var document = path === base + 'help.html' ? './help.html' : './index.html';
    event.respondWith(caches.open(VERSION).then(function (cache) { return cache.match(document); }).then(function (response) { return response || fetch(request); }));
    return;
  }
  // Only cache our own static assets. No APIs, authentication, or unrelated host responses.
  if (!/\/(?:index\.html|help\.html|style\.css|core\.js|storage\.js|app\.js|default\.xml|example\.xml|guide\.json|icon(?:-192\.png|-512\.png|\.ico|\.svg)|manifest\.webmanifest)$/.test(path)) return;
  event.respondWith(caches.open(VERSION).then(function (cache) {
    return cache.match(request).then(function (response) {
      if (response) return response;
      return fetch(request).then(function (fresh) { if (fresh.ok && fresh.type === 'basic') cache.put(request, fresh.clone()); return fresh; });
    });
  }));
});
