'use strict';
// Test dependency is intentionally outside the application. Native browser testing
// uses management-browser.html and requires no third-party package at all.
var fs = require('fs'), path = require('path'), vm = require('vm'), os = require('os');
var domPath = process.env.CHOOSER_TEST_DOM || path.join(os.tmpdir(), 'chooser-management-test-deps/node_modules/@xmldom/xmldom');
var dom = require(domPath), NativeParser = dom.DOMParser;
var sandbox = { console: console, XMLSerializer: dom.XMLSerializer, DOMParser: function () {} };
sandbox.DOMParser.prototype.parseFromString = function (xml, type) {
  return new NativeParser({ onError: function (level, message) { if (level === 'fatalError' || level === 'error') throw new Error(message); } }).parseFromString(xml, type);
};
vm.createContext(sandbox); vm.runInContext(fs.readFileSync(path.join(__dirname, '../dist/core.js'), 'utf8'), sandbox);
var run = require('./management-tests');
var result = run(sandbox.ChooserCore, fs.readFileSync(path.join(__dirname, '../dist/example.xml'), 'utf8'), JSON.parse(fs.readFileSync(path.join(__dirname, 'management-parity.json'), 'utf8')));
fs.writeFileSync(path.join(__dirname, 'management-node-report.json'), JSON.stringify(result, null, 2), 'utf8');
console.log(JSON.stringify(result, null, 2)); if (result.failures.length) process.exitCode = 1;
