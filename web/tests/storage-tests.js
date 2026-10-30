'use strict';
var fs = require('fs');
var vm = require('vm');
var assert = require('assert');
var script = fs.readFileSync(require('path').join(__dirname, '../dist/storage.js'), 'utf8');
var count = 0;
function localMemory() {
  var rows = Object.create(null);
  return {
    rows: rows,
    getItem: function (key) { return Object.prototype.hasOwnProperty.call(rows, key) ? rows[key] : null; },
    setItem: function (key, value) { rows[key] = String(value); },
    removeItem: function (key) { delete rows[key]; }
  };
}
function open(storage, indexedDB) {
  var context = { Promise: Promise, setTimeout: setTimeout, clearTimeout: clearTimeout, localStorage: storage,
    ChooserCore: { validateState: function (state) { if (!state.Banks || !state.Wrongs) { throw new Error('bad snapshot'); } return state; } } };
  if (indexedDB) { context.indexedDB = indexedDB; }
  context.window = context;
  vm.runInNewContext(script, context);
  return context.ChooserStorage.open();
}
function fresh() { return { Banks: [{ Id: 'b', Name: 'bank', Xml: '<quiz/>', Questions: [] }], Wrongs: [], Active: null, Dark: false }; }
function test(name, action) {
  return Promise.resolve().then(action).then(function () { count++; console.log('PASS ' + name); });
}
var memory = localMemory();
var key = 'chooser-classroom-web-state-v1';
Promise.resolve().then(function () {
  return test('localStorage cache roundtrip and immutable save snapshot', function () {
    return open(memory).then(function (store) {
      assert.strictEqual(store.kind, 'localStorage');
      var state = fresh(), save = store.save(state); state.Banks[0].Name = 'mutated after save';
      return save.then(function () { return store.load(); }).then(function (loaded) { assert.strictEqual(loaded.Banks[0].Name, 'bank'); });
    });
  });
}).then(function () {
  return test('serialized writes retain submission order and prior backup', function () {
    return open(memory).then(function (store) {
      var a = fresh(), b = fresh(); a.Dark = false; b.Dark = true;
      return Promise.all([store.save(a), store.save(b)]).then(function () {
        assert.strictEqual(JSON.parse(memory.rows[key]).Dark, true);
        assert.strictEqual(JSON.parse(memory.rows[key + '.bak']).Dark, false);
      });
    });
  });
}).then(function () {
  return test('corrupt primary recovers valid backup and warns', function () {
    memory.rows[key] = 'broken JSON';
    return open(memory).then(function (store) {
      return store.load().then(function (state) { assert.strictEqual(state.Dark, false); assert.ok(store.warning.indexOf('备份恢复') >= 0); assert.doesNotThrow(function () { JSON.parse(memory.rows[key]); }); });
    });
  });
}).then(function () {
  return test('corrupt primary and backup reject load/save without overwriting either', function () {
    memory.rows[key] = 'bad primary'; memory.rows[key + '.bak'] = 'bad backup';
    return open(memory).then(function (store) {
      return store.load().then(function () { throw new Error('load should fail'); }, function () {
        return store.save(fresh()).then(function () { throw new Error('save should fail'); }, function () {
          assert.strictEqual(memory.rows[key], 'bad primary'); assert.strictEqual(memory.rows[key + '.bak'], 'bad backup');
        });
      });
    });
  });
}).then(function () {
  return test('quota failures reject saves while allowing existing cache reads', function () {
    var storage = localMemory(); storage.rows[key] = JSON.stringify(fresh());
    storage.setItem = function () { var error = new Error('full'); error.name = 'QuotaExceededError'; throw error; };
    return open(storage).then(function (store) {
      assert.strictEqual(store.kind, 'localStorage');
      return store.load().then(function (loaded) {
        assert.strictEqual(loaded.Banks[0].Name, 'bank');
        return store.save(loaded).then(function () { throw new Error('quota save should fail'); }, function (error) { assert.ok(error.message.indexOf('缓存操作失败') >= 0); });
      });
    });
  });
}).then(function () {
  return test('blocked IDB fallback remains authoritative when IDB becomes available later', function () {
    var storage = localMemory(), calls = 0;
    var blocked = { open: function () {
      var request = {};
      setTimeout(function () { request.onblocked(); }, 0);
      return request;
    } };
    return open(storage, blocked).then(function (store) {
      assert.strictEqual(store.kind, 'localStorage');
      var state = fresh(); state.Active = { Position: 7 }; state.Wrongs = [{ Count: 3 }];
      return store.save(state);
    }).then(function () {
      return open(storage, { open: function () { calls++; throw new Error('must preserve fallback backend'); } });
    }).then(function (store) {
      assert.strictEqual(calls, 0); assert.strictEqual(store.kind, 'localStorage');
      return store.load();
    }).then(function (state) {
      assert.strictEqual(state.Active.Position, 7); assert.strictEqual(state.Wrongs[0].Count, 3);
    });
  });
}).then(function () {
  return test('existing corrupt fallback never silently switches to a fresh IDB', function () {
    var storage = localMemory(), calls = 0; storage.rows[key] = 'damaged old cache';
    return open(storage, { open: function () { calls++; throw new Error('should not open'); } }).then(function (store) {
      assert.strictEqual(calls, 0); assert.strictEqual(store.kind, 'localStorage');
      return store.load().then(function () { throw new Error('damaged fallback must be reported'); }, function () {
        assert.strictEqual(storage.rows[key], 'damaged old cache');
      });
    });
  });
}).then(function () {
  return test('denied persistent storage exposes memory-only warning and copies on load', function () {
    return open({ getItem: function () { throw new Error('denied'); } }).then(function (store) {
      assert.strictEqual(store.kind, 'memory'); assert.ok(store.warning.indexOf('关闭页面后') >= 0);
      return store.save(fresh()).then(function () { return store.load(); }).then(function (state) {
        state.Banks[0].Name = 'local mutation'; return store.load();
      }).then(function (state) { assert.strictEqual(state.Banks[0].Name, 'bank'); });
    });
  });
}).then(function () { console.log(count + ' storage tests passed.'); }, function (error) { console.error(error.stack); process.exitCode = 1; });
