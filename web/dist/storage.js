/* Chooser browser storage. No server requests or external dependencies. */
(function (root) {
  'use strict';
  var DATABASE = 'chooser-classroom-web-v1';
  var KEY = 'chooser-classroom-web-state-v1';

  function copy(value) { return JSON.parse(JSON.stringify(value)); }
  function normalize(value) {
    if (!value || !Array.isArray(value.Banks)) { throw new Error('本地缓存缺少题库数据。'); }
    return root.ChooserCore && root.ChooserCore.validateState ? root.ChooserCore.validateState(value) : value;
  }
  function warning(store, message) {
    if (store.warning.indexOf(message) < 0) { store.warning += (store.warning ? ' ' : '') + message; }
  }
  function fail(error) {
    var detail = error && (error.message || error.name);
    return new Error('浏览器缓存操作失败，请检查可用空间或浏览器存储权限。' + (detail ? ' ' + detail : ''));
  }
  function fingerprint(bank) {
    return String(bank.Xml || '') + '\n' + bank.Name + '\n' + bank.ImportedAt + '\n' + JSON.stringify(bank.Questions || []);
  }
  function snapshot(state) { return normalize(copy(state)); }
  function serial(store, action) {
    var result = store._tail.then(action, action);
    store._tail = result.then(function () {}, function () {});
    return result;
  }
  function queuedSave(store, action) {
    return function (state) {
      var saved;
      try { saved = snapshot(state); } catch (error) { return Promise.reject(error); }
      return serial(store, function () { return action(saved); });
    };
  }

  function memoryStore(message) {
    var value = null;
    var store = { kind: 'memory', warning: message || '浏览器不允许保存本地数据，关闭页面后题库和进度将丢失。', _tail: Promise.resolve() };
    store.load = function () { return serial(store, function () { return Promise.resolve(value === null ? null : copy(value)); }); };
    store.save = queuedSave(store, function (state) { value = state; return Promise.resolve(); });
    return store;
  }

  function localStore(storage, message) {
    var lastGood = null, loaded = false;
    var store = { kind: 'localStorage', warning: message || '', _tail: Promise.resolve() };
    function decode(text) { return normalize(JSON.parse(text)); }
    function read() {
      try {
        var primary = storage.getItem(KEY), backup = storage.getItem(KEY + '.bak'), value;
        if (primary === null && backup === null) { loaded = true; return null; }
        if (primary !== null) {
          try { value = decode(primary); lastGood = primary; loaded = true; return value; }
          catch (primaryError) { /* Recover only from a fully valid prior snapshot. */ }
        }
        if (backup !== null) {
          try {
            value = decode(backup); lastGood = backup; loaded = true;
            warning(store, '主缓存损坏，已从上一次保存的备份恢复；最近一次更改可能未恢复。');
            try { storage.setItem(KEY, backup); } catch (restoreError) { warning(store, '恢复后的数据尚未写回磁盘。'); }
            return value;
          } catch (backupError) { /* Keep both damaged values intact for manual recovery. */ }
        }
        throw new Error('本地缓存及备份均无法读取。为保护已有记录，未覆盖原缓存；请先导出可用备份或联系维护者。');
      } catch (error) { throw fail(error); }
    }
    store.load = function () { return serial(store, function () { return Promise.resolve().then(read); }); };
    store.save = queuedSave(store, function (state) {
      return Promise.resolve().then(function () {
        try {
          if (!loaded) { read(); }
          var serialized = JSON.stringify(state);
          if (lastGood !== null) { storage.setItem(KEY + '.bak', lastGood); }
          storage.setItem(KEY, serialized);
          lastGood = serialized;
        } catch (error) { throw fail(error); }
      });
    });
    return store;
  }

  function fallback(reason) {
    var storage, probe = KEY + '.probe-' + String(Math.random()).slice(2);
    try {
      storage = root.localStorage;
      if (!storage) { throw new Error('localStorage unavailable'); }
      storage.getItem(KEY);
      try { storage.setItem(probe, '1'); storage.removeItem(probe); }
      catch (probeError) {
        if (probeError && (probeError.name === 'QuotaExceededError' || probeError.code === 22 || probeError.code === 1014)) {
          return localStore(storage, reason + ' 浏览器存储空间已满，已有缓存仍可读取，新的保存可能失败。');
        }
        throw probeError;
      }
      return localStore(storage, reason);
    } catch (error) {
      return memoryStore('浏览器不允许保存本地数据，当前仅保存在内存中；关闭页面后题库和进度将丢失。');
    }
  }

  function indexedStore(database) {
    var fingerprints = null;
    var store = { kind: 'indexedDB', warning: '', _tail: Promise.resolve() };
    database.onversionchange = function () { database.close(); warning(store, '缓存结构已更新，请刷新页面后继续。'); };
    function read() {
      return new Promise(function (resolve, reject) {
        var transaction, record = null, rows = [], error;
        try {
          transaction = database.transaction(['banks', 'meta'], 'readonly');
          var metaRequest = transaction.objectStore('meta').get('current');
          metaRequest.onsuccess = function () { record = metaRequest.result || null; };
          var cursorRequest = transaction.objectStore('banks').openCursor();
          cursorRequest.onsuccess = function () {
            var cursor = cursorRequest.result;
            if (cursor) { rows.push(cursor.value); cursor.continue(); }
          };
          transaction.oncomplete = function () {
            try {
              if (!record) {
                if (rows.length) { throw new Error('本地缓存题库与进度不完整，未覆盖已有数据。'); }
                fingerprints = Object.create(null); resolve(null); return;
              }
              if (!record.State || !Array.isArray(record.BankOrder)) { throw new Error('本地缓存索引无效。'); }
              var banks = Object.create(null), seen = Object.create(null), next = Object.create(null), state = record.State;
              rows.forEach(function (bank) { banks[bank.Id] = bank; });
              state.Banks = record.BankOrder.map(function (id) {
                if (!banks[id] || seen[id]) { throw new Error('本地缓存中的题库缺失或重复。'); }
                seen[id] = true; next[id] = fingerprint(banks[id]); return banks[id];
              });
              if (rows.length !== state.Banks.length) { throw new Error('本地缓存包含无法识别的题库记录。'); }
              state = normalize(state); fingerprints = next; resolve(state);
            } catch (validationError) { reject(fail(validationError)); }
          };
          transaction.onerror = function () { error = transaction.error; };
          transaction.onabort = function () { reject(fail(error || transaction.error)); };
        } catch (openError) { reject(fail(openError)); }
      });
    }
    function write(state) {
      return new Promise(function (resolve, reject) {
        var transaction, error, next = Object.create(null), meta = {}, order = [];
        try {
          Object.keys(state).forEach(function (key) { if (key !== 'Banks') { meta[key] = state[key]; } });
          transaction = database.transaction(['banks', 'meta'], 'readwrite');
          var bankStore = transaction.objectStore('banks');
          state.Banks.forEach(function (bank) {
            var signature = fingerprint(bank); next[bank.Id] = signature; order.push(bank.Id);
            if (fingerprints[bank.Id] !== signature) { bankStore.put(bank); }
          });
          Object.keys(fingerprints).forEach(function (id) { if (!Object.prototype.hasOwnProperty.call(next, id)) { bankStore.delete(id); } });
          transaction.objectStore('meta').put({ Key: 'current', State: meta, BankOrder: order });
          transaction.oncomplete = function () { fingerprints = next; resolve(); };
          transaction.onerror = function () { error = transaction.error; };
          transaction.onabort = function () { reject(fail(error || transaction.error)); };
        } catch (writeError) {
          if (transaction) { try { transaction.abort(); } catch (abortError) {} }
          reject(fail(writeError));
        }
      });
    }
    store.load = function () { return serial(store, read); };
    store.save = queuedSave(store, function (state) {
      if (fingerprints === null) { return read().then(function () { return write(state); }); }
      return write(state);
    });
    return store;
  }

  function open() {
    return new Promise(function (resolve) {
      var request, settled = false, timer;
      function finish(store) { if (!settled) { settled = true; if (timer) { root.clearTimeout(timer); } resolve(store); } }
      function useFallback(reason) { finish(fallback(reason)); }
      try {
        // A previous blocked/denied IDB open may already have durable fallback data.
        // Keep that backend stable; never make its records disappear when IDB later opens.
        // Even malformed fallback data must reach localStore.load(), which reports damage
        // and preserves the original values rather than treating this as a fresh install.
        var existingLocal;
        try {
          existingLocal = root.localStorage;
          if (existingLocal && (existingLocal.getItem(KEY) !== null || existingLocal.getItem(KEY + '.bak') !== null)) {
            finish(localStore(existingLocal, '继续使用已有兼容缓存，保留原有题库与记录。'));
            return;
          }
        } catch (localError) { /* Denied localStorage must not disable an available IDB. */ }
        if (!root.indexedDB) { useFallback('当前浏览器使用兼容缓存模式。'); return; }
        request = root.indexedDB.open(DATABASE, 1);
        timer = root.setTimeout(function () { useFallback('本地数据库未及时响应，已使用兼容缓存模式。'); }, 5000);
        request.onupgradeneeded = function () {
          var database = request.result;
          if (!database.objectStoreNames.contains('banks')) { database.createObjectStore('banks', { keyPath: 'Id' }); }
          if (!database.objectStoreNames.contains('meta')) { database.createObjectStore('meta', { keyPath: 'Key' }); }
        };
        request.onsuccess = function () {
          if (settled) { request.result.close(); return; }
          finish(indexedStore(request.result));
        };
        request.onerror = function () { useFallback('本地数据库不可用，已使用兼容缓存模式。'); };
        request.onblocked = function () { useFallback('其他页面正在更新本地数据库，已使用兼容缓存模式。'); };
      } catch (error) { useFallback('本地数据库不可用，已使用兼容缓存模式。'); }
    });
  }

  root.ChooserStorage = { open: open };
}(typeof window !== 'undefined' ? window : this));
