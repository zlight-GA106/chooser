/* Local QA only; production is the static dist directory on any HTTP(S) server. */
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const base = __dirname;
const port = Number(process.argv[2]) || 8777;
const requests = {};
const types = { '.html': 'text/html; charset=utf-8', '.js': 'application/javascript; charset=utf-8', '.css': 'text/css; charset=utf-8', '.xml': 'application/xml; charset=utf-8', '.json': 'application/json', '.webmanifest': 'application/manifest+json', '.ico': 'image/x-icon', '.png': 'image/png' };
http.createServer((req, res) => {
  const url = new URL(req.url, 'http://localhost');
  if (url.pathname === '/__stats') { res.setHeader('Content-Type', 'application/json'); res.end(JSON.stringify(requests)); return; }
  let name;
  try { name = decodeURIComponent(url.pathname); } catch (_) { res.writeHead(400); res.end(); return; }
  if (name === '/') { res.writeHead(302, { Location: '/dist/' }); res.end(); return; }
  if (name.endsWith('/')) name += 'index.html';
  const target = path.resolve(base, '.' + name);
  if (!target.startsWith(base + path.sep) || (!name.startsWith('/dist/') && !name.startsWith('/tests/'))) { res.writeHead(403); res.end(); return; }
  fs.stat(target, (error, stat) => {
    if (error || !stat.isFile()) { res.writeHead(404); res.end('Not found'); return; }
    requests[url.pathname] = (requests[url.pathname] || 0) + 1;
    res.setHeader('Content-Type', types[path.extname(target)] || 'application/octet-stream');
    res.setHeader('X-Content-Type-Options', 'nosniff');
    res.setHeader('Cache-Control', /\/(sw\.js|index\.html)$/.test(name) || name.startsWith('/tests/') ? 'no-cache' : 'public, max-age=86400');
    res.setHeader('Content-Length', stat.size);
    fs.createReadStream(target).pipe(res);
  });
}).listen(port, '127.0.0.1', () => console.log('Local: http://127.0.0.1:' + port + '/dist/'));
