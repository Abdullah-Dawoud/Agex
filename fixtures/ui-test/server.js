const http = require('http');
const fs = require('fs');
const path = require('path');
const body = fs.readFileSync(path.join(__dirname, 'index.html'));
http.createServer((request, response) => {
  if (request.url === '/' || request.url === '/index.html') {
    response.writeHead(200, {'Content-Type': 'text/html; charset=utf-8'});
    response.end(body);
    return;
  }
  if (request.url === '/favicon.ico') {
    response.writeHead(204);
    response.end();
    return;
  }
  response.writeHead(404);
  response.end('Not found');
}).listen(4173, '127.0.0.1');
