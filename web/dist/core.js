/* Chooser 1.2 — dependency-free, ES5 quiz rules shared by all browser views. */
(function (root) {
  'use strict';
  var MAX_XML = 32 * 1024 * 1024;
  function fail(message) { throw new Error(message); }
  function list(value) { return Object.prototype.toString.call(value) === '[object Array]'; }
  function integer(value) { return typeof value === 'number' && isFinite(value) && Math.floor(value) === value; }
  function clean(value) { return String(value == null ? '' : value).replace(/\s+/g, ' ').replace(/^\s+|\s+$/g, ''); }
  function trim(value) { return String(value == null ? '' : value).replace(/^\s+|\s+$/g, ''); }
  function dictionary() { return Object.create(null); }
  function unique(values) {
    var seen = dictionary(), output = [], i;
    for (i = 0; i < values.length; i++) if (!seen[values[i]]) { seen[values[i]] = true; output.push(values[i]); }
    return output;
  }
  function sameSet(a, b) {
    if (!list(a) || !list(b) || a.length !== b.length || unique(a).length !== a.length) return false;
    for (var i = 0; i < a.length; i++) if (b.indexOf(a[i]) < 0) return false;
    return true;
  }
  function byId(items) {
    var output = dictionary();
    for (var i = 0; i < items.length; i++) output[items[i].Id] = items[i];
    return output;
  }

  /* UTF-8 SHA-256 keeps XML identities identical to the desktop version. */
  function hash(text) {
    var bytes = [], i, code, next;
    for (i = 0; i < text.length; i++) {
      code = text.charCodeAt(i);
      if (code >= 0xD800 && code <= 0xDBFF) {
        next = text.charCodeAt(i + 1);
        if (next >= 0xDC00 && next <= 0xDFFF) { code = 0x10000 + ((code - 0xD800) << 10) + next - 0xDC00; i++; }
        else code = 0xFFFD;
      } else if (code >= 0xDC00 && code <= 0xDFFF) code = 0xFFFD;
      if (code < 128) bytes.push(code);
      else if (code < 2048) bytes.push(192 | (code >> 6), 128 | (code & 63));
      else if (code < 65536) bytes.push(224 | (code >> 12), 128 | ((code >> 6) & 63), 128 | (code & 63));
      else bytes.push(240 | (code >> 18), 128 | ((code >> 12) & 63), 128 | ((code >> 6) & 63), 128 | (code & 63));
    }
    var bitLength = bytes.length * 8;
    bytes.push(128);
    while ((bytes.length % 64) !== 56) bytes.push(0);
    var high = Math.floor(bitLength / 4294967296), low = bitLength >>> 0;
    for (i = 3; i >= 0; i--) bytes.push((high >>> (i * 8)) & 255);
    for (i = 3; i >= 0; i--) bytes.push((low >>> (i * 8)) & 255);
    var constants = [0x428a2f98,0x71374491,0xb5c0fbcf,0xe9b5dba5,0x3956c25b,0x59f111f1,0x923f82a4,0xab1c5ed5,0xd807aa98,0x12835b01,0x243185be,0x550c7dc3,0x72be5d74,0x80deb1fe,0x9bdc06a7,0xc19bf174,0xe49b69c1,0xefbe4786,0x0fc19dc6,0x240ca1cc,0x2de92c6f,0x4a7484aa,0x5cb0a9dc,0x76f988da,0x983e5152,0xa831c66d,0xb00327c8,0xbf597fc7,0xc6e00bf3,0xd5a79147,0x06ca6351,0x14292967,0x27b70a85,0x2e1b2138,0x4d2c6dfc,0x53380d13,0x650a7354,0x766a0abb,0x81c2c92e,0x92722c85,0xa2bfe8a1,0xa81a664b,0xc24b8b70,0xc76c51a3,0xd192e819,0xd6990624,0xf40e3585,0x106aa070,0x19a4c116,0x1e376c08,0x2748774c,0x34b0bcb5,0x391c0cb3,0x4ed8aa4a,0x5b9cca4f,0x682e6ff3,0x748f82ee,0x78a5636f,0x84c87814,0x8cc70208,0x90befffa,0xa4506ceb,0xbef9a3f7,0xc67178f2];
    var h = [0x6a09e667,0xbb67ae85,0x3c6ef372,0xa54ff53a,0x510e527f,0x9b05688c,0x1f83d9ab,0x5be0cd19], words = [], offset, j;
    function rotate(value, n) { return (value >>> n) | (value << (32 - n)); }
    for (offset = 0; offset < bytes.length; offset += 64) {
      for (j = 0; j < 16; j++) words[j] = (bytes[offset + j * 4] << 24) | (bytes[offset + j * 4 + 1] << 16) | (bytes[offset + j * 4 + 2] << 8) | bytes[offset + j * 4 + 3];
      for (j = 16; j < 64; j++) {
        var x = words[j - 15], y = words[j - 2];
        words[j] = ((rotate(x, 7) ^ rotate(x, 18) ^ (x >>> 3)) + words[j - 16] + (rotate(y, 17) ^ rotate(y, 19) ^ (y >>> 10)) + words[j - 7]) | 0;
      }
      var a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
      for (j = 0; j < 64; j++) {
        var t1 = (hh + (rotate(e, 6) ^ rotate(e, 11) ^ rotate(e, 25)) + ((e & f) ^ (~e & g)) + constants[j] + words[j]) | 0;
        var t2 = ((rotate(a, 2) ^ rotate(a, 13) ^ rotate(a, 22)) + ((a & b) ^ (a & c) ^ (b & c))) | 0;
        hh = g; g = f; f = e; e = (d + t1) | 0; d = c; c = b; b = a; a = (t1 + t2) | 0;
      }
      h[0] = (h[0] + a) | 0; h[1] = (h[1] + b) | 0; h[2] = (h[2] + c) | 0; h[3] = (h[3] + d) | 0;
      h[4] = (h[4] + e) | 0; h[5] = (h[5] + f) | 0; h[6] = (h[6] + g) | 0; h[7] = (h[7] + hh) | 0;
    }
    var output = '';
    for (i = 0; i < 8; i++) output += ('00000000' + (h[i] >>> 0).toString(16)).slice(-8);
    return output;
  }
  function canonical(question) {
    var output = '';
    function append(part) { output += part.length + ':' + part; }
    append(question.Text);
    question.Options.forEach(function (option) { append(option.Id); append(option.Text); });
    question.Answers.forEach(append); append(question.Explanation);
    return output;
  }
  function tag(node) { return String(node.localName || node.nodeName || '').split(':').pop().toLowerCase(); }
  function elements(node) {
    var output = [];
    for (var child = node.firstChild; child; child = child.nextSibling) if (child.nodeType === 1) output.push(child);
    return output;
  }
  function descendants(node, name) {
    var all = node.getElementsByTagName('*'), output = [];
    for (var i = 0; i < all.length; i++) if (tag(all[i]) === name) output.push(all[i]);
    return output;
  }
  function attribute(node, name) {
    for (var i = 0; i < node.attributes.length; i++) if (tag(node.attributes[i]) === name) return trim(node.attributes[i].value);
    return '';
  }
  function childText(node, names) {
    var all = elements(node), i, j;
    for (i = 0; i < names.length; i++) for (j = 0; j < all.length; j++) if (tag(all[j]) === names[i] && trim(all[j].textContent)) return trim(all[j].textContent);
    return '';
  }
  function parseAnswers(raw, options) {
    raw = trim(raw).toUpperCase();
    if (!raw) fail('缺少正确答案 <answer>。');
    var ids = options.map(function (o) { return o.Id; });
    var tokens = raw.split(/[\s,，、;；|/＋+]+/).filter(function (s) { return !!s; }), selected;
    if (tokens.length && tokens.every(function (s) { return ids.indexOf(s) >= 0; })) selected = unique(tokens);
    else if (tokens.length === 1 && ids.every(function (s) { return s.length === 1; }) && raw.split('').every(function (s) { return ids.indexOf(s) >= 0; })) selected = unique(raw.split(''));
    else fail('正确答案必须完整匹配选项 ID；多选可写 A,C 或 AC。');
    return ids.filter(function (id) { return selected.indexOf(id) >= 0; });
  }
  function parseXml(xml, filename) {
    if (typeof xml !== 'string' || !trim(xml)) fail('XML 题库内容为空。');
    if (xml.length > MAX_XML) fail('题库最多支持 32 MB。');
    if (/<!\s*(DOCTYPE|ENTITY)\b/i.test(xml)) fail('题库禁止使用 DTD 或外部实体。');
    if (typeof root.DOMParser !== 'function') fail('此浏览器不支持 XML 解析，请使用 Chrome、Edge 或 Firefox。');
    var document = new root.DOMParser().parseFromString(xml, 'application/xml');
    if (!document.documentElement || tag(document.documentElement) === 'parsererror' || document.getElementsByTagName('parsererror').length) fail('XML 格式错误，请检查标签是否完整以及特殊字符是否正确转义。');
    var element = document.documentElement;
    if (tag(element) !== 'quiz') fail('XML 根节点必须为 <quiz>。');
    var name = attribute(element, 'title') || attribute(element, 'name') || childText(element, ['title']) || String(filename || '题库.xml').replace(/^.*[\\/]/, '').replace(/\.[^.]*$/, '') || '未命名题库';
    var bank = { Id: '', Name: name, ImportedAt: new Date().toISOString(), Xml: '', Questions: [] }, occurrences = dictionary();
    descendants(element, 'question').forEach(function (node) {
      var number = bank.Questions.length + 1;
      try {
        if (attribute(node, 'type').toLowerCase() === 'fill_blank') fail('仅支持选择题和判断题，不支持填空题。');
        if (descendants(node, 'question').length) fail('题目节点不能互相嵌套。');
        var question = { Id: '', Number: number, Text: clean(childText(node, ['text', 'prompt'])), Explanation: childText(node, ['explanation', 'analysis']), Options: [], Answers: [] }, ids = dictionary();
        if (!question.Text) fail('题干 <text> 不能为空。');
        descendants(node, 'option').forEach(function (option) {
          var id = attribute(option, 'id').toUpperCase(), value = clean(option.textContent);
          if (!id || /[\s,，、;；|/+]/.test(id)) fail('选项 ID 不能为空或包含空格、答案分隔符。');
          if (!value) fail('选项 ' + id + ' 的内容不能为空。');
          if (ids[id]) fail('选项 ID 重复：' + id + '。');
          ids[id] = true; question.Options.push({ Id: id, Text: value });
        });
        if (question.Options.length < 2) fail('选择题至少需要两个选项。');
        question.Answers = parseAnswers(childText(node, ['answer', 'correct_answer']), question.Options);
        var baseId = hash(canonical(question)); occurrences[baseId] = (occurrences[baseId] || 0) + 1;
        question.Id = baseId + (occurrences[baseId] > 1 ? '-' + occurrences[baseId] : '');
        bank.Questions.push(question);
      } catch (error) { fail('第 ' + number + ' 题：' + error.message); }
    });
    if (!bank.Questions.length) fail('XML 中没有可导入的题目。');
    bank.Id = hash(bank.Questions.map(function (question) { return question.Id; }).join('\n'));
    bank.Xml = '<?xml version="1.0" encoding="utf-8"?>\n' + new root.XMLSerializer().serializeToString(element);
    return bank;
  }
  function bankName(value) {
    var name = typeof value === 'string' ? trim(value) : '';
    if (!name) fail('题库名称不能为空。');
    if (name.length > 120) fail('题库名称最多 120 个字符。');
    if (/[\x00-\x1f\x7f-\x9f]/.test(name)) fail('题库名称不能包含换行或控制字符。');
    return name;
  }
  function managementState(state) { if (!state || !list(state.Banks) || !list(state.Wrongs)) fail('题库数据不完整。'); }
  function findBank(state, id) {
    managementState(state);
    var bank = state.Banks.filter(function (b) { return b.Id === id; })[0];
    if (!bank) fail('题库不存在或已被删除。');
    return bank;
  }
  function uniqueBankName(state, name, excludedId) {
    if (state.Banks.some(function (b) { return b.Id !== excludedId && b.Name.toUpperCase() === name.toUpperCase(); })) fail('已有同名题库，请使用其他名称。');
  }
  function uniqueBankContent(state, id, excludedId) {
    if (state.Banks.some(function (b) { return b.Id !== excludedId && b.Id === id; })) fail('已有内容相同的题库，请使用已有题库。');
  }
  function xmlText(value) { return String(value == null ? '' : value).replace(/[&<>"']/g, function (ch) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&apos;' }[ch]; }); }
  function currentXml(bank) {
    if (!bank) fail('题库不存在。');
    if (typeof bank.Xml === 'string' && trim(bank.Xml)) return bank.Xml;
    return '<?xml version="1.0" encoding="utf-8"?>\n<quiz title="' + xmlText(bank.Name || '题库') + '">' + bank.Questions.map(function (q) {
      return '<question type="multiple_choice" score="1"><text>' + xmlText(q.Text) + '</text>' + q.Options.map(function (o) { return '<option id="' + xmlText(o.Id) + '">' + xmlText(o.Text) + '</option>'; }).join('') + '<answer>' + xmlText(q.Answers.join(',')) + '</answer>' + (q.Explanation ? '<explanation>' + xmlText(q.Explanation) + '</explanation>' : '') + '</question>';
    }).join('') + '</quiz>';
  }
  function previewBank(name, xml) {
    name = bankName(name);
    // The normal importer is the authority for limits, entities and question validity.
    var bank = parseXml(xml, '题库.xml');
    var document = new root.DOMParser().parseFromString(xml, 'application/xml'), element = document.documentElement, attrs = [], i;
    for (i = 0; i < element.attributes.length; i++) if (tag(element.attributes[i]) === 'title') attrs.push(element.attributes[i]);
    for (i = 0; i < attrs.length; i++) element.removeAttributeNode(attrs[i]);
    element.setAttribute('title', name);
    var body = new root.XMLSerializer().serializeToString(document).replace(/^<\?xml\b[^?]*\?>\s*/i, '');
    var updatedXml = '<?xml version="1.0" encoding="utf-8"?>\n' + body;
    bank.Name = name; bank.Xml = updatedXml;
    return bank;
  }
  function createBank(state, name, xml) {
    managementState(state);
    var bank = previewBank(name, xml);
    uniqueBankName(state, bank.Name, null); uniqueBankContent(state, bank.Id, null);
    state.Banks.push(bank); state.SelectedBankId = bank.Id;
    return bank;
  }
  function renameBank(state, id, name) {
    var old = findBank(state, id); name = bankName(name); uniqueBankName(state, name, id);
    var parsed = previewBank(name, currentXml(old));
    var bank = { Id: old.Id, Name: name, ImportedAt: old.ImportedAt, Xml: parsed.Xml, Questions: old.Questions };
    state.Banks[state.Banks.indexOf(old)] = bank;
    return bank;
  }
  function requiresProgressReset(state, id, xml) {
    var old = findBank(state, id);
    if (!state.Active || state.Active.BankId !== old.Id) return false;
    var ids = byId(parseXml(xml, '题库.xml').Questions);
    return state.Active.QuestionIds.some(function (questionId) { return !ids[questionId]; });
  }
  function replaceBank(state, id, name, xml) {
    var old = findBank(state, id), bank = previewBank(name, xml);
    uniqueBankName(state, bank.Name, id); uniqueBankContent(state, bank.Id, id);
    bank.ImportedAt = old.ImportedAt;
    var ids = byId(bank.Questions), nextWrongs = [], active = state.Active;
    state.Wrongs.forEach(function (w) {
      if (w.BankId !== old.Id) nextWrongs.push(w);
      else if (ids[w.QuestionId]) nextWrongs.push({ BankId: bank.Id, QuestionId: w.QuestionId, Count: w.Count, LastSelected: w.LastSelected.slice(), LastAt: w.LastAt });
    });
    var clearActive = active && active.BankId === old.Id && active.QuestionIds.some(function (questionId) { return !ids[questionId]; });
    state.Banks[state.Banks.indexOf(old)] = bank; state.Wrongs = nextWrongs;
    if (state.SelectedBankId === old.Id) state.SelectedBankId = bank.Id;
    if (clearActive) state.Active = null;
    else if (active && active.BankId === old.Id) active.BankId = bank.Id;
    return bank;
  }
  function deleteBank(state, id) {
    var old = findBank(state, id), wrongs = state.Wrongs.filter(function (w) { return w.BankId !== old.Id; });
    state.Banks.splice(state.Banks.indexOf(old), 1); state.Wrongs = wrongs;
    if (state.Active && state.Active.BankId === old.Id) state.Active = null;
    if (state.SelectedBankId === old.Id) state.SelectedBankId = state.Banks.length ? state.Banks[0].Id : '';
  }
  function createState() {
    return { Banks: [], Wrongs: [], Active: null, SelectedBankId: '', Dark: false, Zoom: 1, ShowAnswers: false, ImmediateScoring: false };
  }
  function randomIndex(length) {
    var crypto = root.crypto || root.msCrypto;
    if (crypto && crypto.getRandomValues && typeof Uint32Array !== 'undefined') {
      var bytes = new Uint32Array(1), bound = 4294967296 - (4294967296 % length), value;
      do { crypto.getRandomValues(bytes); value = bytes[0]; } while (value >= bound);
      return value % length;
    }
    return Math.floor(Math.random() * length);
  }
  function createSession(bank, start, end, count, random, mode) {
    if (!bank || !list(bank.Questions) || !bank.Questions.length) fail('请先选择有题目的题库。');
    if (!integer(start) || !integer(end) || start < 1 || end < start || end > bank.Questions.length) fail('题号范围无效，请输入 1–' + bank.Questions.length + ' 之间的起止题号。');
    if (!integer(count) || count < 1 || count > end - start + 1) fail('抽题数量必须在 1–' + (end - start + 1) + ' 之间。');
    var ids = bank.Questions.slice(start - 1, end).map(function (q) { return q.Id; });
    if (random) for (var i = ids.length - 1; i > 0; i--) { var j = randomIndex(i + 1), temp = ids[i]; ids[i] = ids[j]; ids[j] = temp; }
    return { BankId: bank.Id, Mode: mode || '顺序练习', QuestionIds: ids.slice(0, count), Attempts: [], Position: 0, Draft: [], Completed: false, FeedbackPending: false };
  }
  function current(bank, session) {
    if (!bank || !session || bank.Id !== session.BankId) fail('答题进度与题库不匹配。');
    var position = session.FeedbackPending ? session.Position - 1 : session.Position;
    if (position < 0 || position >= session.QuestionIds.length) fail('当前答题进度已结束。');
    var question = byId(bank.Questions)[session.QuestionIds[position]];
    if (!question) fail('当前题目已不存在。');
    return question;
  }
  function submit(state, bank) {
    var session = state && state.Active;
    if (!session || session.Completed || session.FeedbackPending) fail('此题已提交，请点击下一题。');
    if (session.Attempts.length !== session.Position) fail('答题进度异常，不能重复提交。');
    var question = current(bank, session), selected = unique((session.Draft || []).map(function (id) { return trim(id).toUpperCase(); }));
    if (!selected.length) fail('请先选择答案。');
    var validIds = question.Options.map(function (option) { return option.Id; });
    if (selected.some(function (id) { return validIds.indexOf(id) < 0; })) fail('所选答案中包含无效选项。');
    if (question.Answers.length === 1 && selected.length !== 1) fail('单选题只能选择一个答案。');
    selected = validIds.filter(function (id) { return selected.indexOf(id) >= 0; });
    if (session.Attempts.some(function (attempt) { return attempt.QuestionId === question.Id; })) fail('此题已经提交过。');
    var attempt = { QuestionId: question.Id, Selected: selected.slice(), Correct: sameSet(selected, question.Answers) };
    session.Attempts.push(attempt);
    if (!attempt.Correct) {
      var wrong = null;
      for (var i = 0; i < state.Wrongs.length; i++) if (state.Wrongs[i].BankId === bank.Id && state.Wrongs[i].QuestionId === question.Id) { wrong = state.Wrongs[i]; break; }
      if (!wrong) { wrong = { BankId: bank.Id, QuestionId: question.Id, Count: 0, LastSelected: [], LastAt: '' }; state.Wrongs.push(wrong); }
      wrong.Count = Math.min(2147483647, wrong.Count + 1); wrong.LastSelected = selected.slice(); wrong.LastAt = new Date().toISOString();
    }
    session.Position++; session.Draft = []; session.Completed = session.Position === session.QuestionIds.length; session.FeedbackPending = !!state.ImmediateScoring;
    return attempt;
  }
  function advance(state) {
    if (!state || !state.Active || !state.Active.FeedbackPending) fail('没有待查看的评分。');
    state.Active.FeedbackPending = false;
    return state.Active;
  }
  function score(session) { return !session || !list(session.Attempts) ? 0 : session.Attempts.filter(function (attempt) { return attempt.Correct === true; }).length; }
  function validateState(state) {
    if (!state || !list(state.Banks) || !list(state.Wrongs)) fail('备份缺少题库或错题记录。');
    state.SelectedBankId = typeof state.SelectedBankId === 'string' ? state.SelectedBankId : '';
    state.Dark = state.Dark === true; state.ShowAnswers = state.ShowAnswers === true; state.ImmediateScoring = state.ImmediateScoring === true;
    if (typeof state.Zoom !== 'number' || !isFinite(state.Zoom) || state.Zoom < 0.5 || state.Zoom > 3) state.Zoom = 1;
    var banks = dictionary(), questions = dictionary(), wrongKeys = dictionary();
    state.Banks.forEach(function (bank) {
      if (!bank || typeof bank.Id !== 'string' || !bank.Id || banks[bank.Id] || typeof bank.Name !== 'string' || !trim(bank.Name) || !list(bank.Questions) || !bank.Questions.length) fail('备份题库内容不完整或标识重复。');
      banks[bank.Id] = bank; questions[bank.Id] = dictionary();
      if (typeof bank.Xml !== 'string') bank.Xml = '';
      if (typeof bank.ImportedAt !== 'string') bank.ImportedAt = '';
      bank.Questions.forEach(function (question, index) {
        if (!question || typeof question.Id !== 'string' || !question.Id || questions[bank.Id][question.Id] || typeof question.Text !== 'string' || !trim(question.Text) || !list(question.Options) || question.Options.length < 2 || !list(question.Answers) || !question.Answers.length) fail('备份题目内容不完整或标识重复。');
        var ids = dictionary();
        question.Options.forEach(function (option) {
          if (!option || typeof option.Id !== 'string' || !trim(option.Id) || ids[option.Id] || typeof option.Text !== 'string' || !trim(option.Text)) fail('备份题目选项无效。');
          ids[option.Id] = true;
        });
        if (unique(question.Answers).length !== question.Answers.length || question.Answers.some(function (id) { return typeof id !== 'string' || !ids[id]; })) fail('备份正确答案无效。');
        if (typeof question.Explanation !== 'string') question.Explanation = '';
        if (!integer(question.Number) || question.Number < 1) question.Number = index + 1;
        questions[bank.Id][question.Id] = question;
      });
    });
    state.Wrongs.forEach(function (wrong) {
      if (!wrong || typeof wrong.BankId !== 'string' || !banks[wrong.BankId] || typeof wrong.QuestionId !== 'string' || !questions[wrong.BankId][wrong.QuestionId] || !integer(wrong.Count) || wrong.Count < 1 || wrong.Count > 2147483647) fail('备份错题引用或次数无效。');
      var key = wrong.BankId + ':' + wrong.QuestionId, question = questions[wrong.BankId][wrong.QuestionId], ids = byId(question.Options);
      if (wrongKeys[key]) fail('备份错题记录重复。'); wrongKeys[key] = true;
      if (!list(wrong.LastSelected) || !wrong.LastSelected.length || unique(wrong.LastSelected).length !== wrong.LastSelected.length || wrong.LastSelected.some(function (id) { return typeof id !== 'string' || !ids[id]; })) fail('备份错题的作答选项无效。');
      if (typeof wrong.LastAt !== 'string') wrong.LastAt = '';
    });
    var session = state.Active;
    if (session == null) { state.Active = null; return state; }
    if (!session || !banks[session.BankId] || !list(session.QuestionIds) || !session.QuestionIds.length || !list(session.Attempts) || unique(session.QuestionIds).length !== session.QuestionIds.length) fail('备份答题进度不完整。');
    var available = questions[session.BankId];
    if (session.QuestionIds.some(function (id) { return typeof id !== 'string' || !available[id]; })) fail('备份进度中的题目不存在。');
    session.Completed = session.Completed === true; session.FeedbackPending = session.FeedbackPending === true;
    if (typeof session.Mode !== 'string' || !trim(session.Mode)) session.Mode = '顺序练习';
    if (!integer(session.Position) || session.Position < 0 || session.Position > session.QuestionIds.length || session.Attempts.length !== session.Position || (!session.Completed && session.Position === session.QuestionIds.length) || (session.FeedbackPending && session.Position < 1)) fail('备份答题位置无效。');
    session.Attempts.forEach(function (attempt, index) {
      var question = available[session.QuestionIds[index]], ids = byId(question.Options);
      if (!attempt || attempt.QuestionId !== question.Id || !list(attempt.Selected) || !attempt.Selected.length || unique(attempt.Selected).length !== attempt.Selected.length || attempt.Selected.some(function (id) { return typeof id !== 'string' || !ids[id]; }) || typeof attempt.Correct !== 'boolean' || attempt.Correct !== sameSet(attempt.Selected, question.Answers)) fail('备份作答记录或成绩无效。');
    });
    if (!list(session.Draft)) session.Draft = [];
    if (session.FeedbackPending && session.Draft.length) fail('已提交题目不应保留未提交草稿。');
    if (!session.Completed && !session.FeedbackPending) {
      var currentIds = byId(available[session.QuestionIds[session.Position]].Options);
      if (unique(session.Draft).length !== session.Draft.length || session.Draft.some(function (id) { return typeof id !== 'string' || !currentIds[id]; })) fail('备份草稿答案无效。');
    }
    return state;
  }
  root.ChooserCore = { createState: createState, parseXml: parseXml, createSession: createSession, current: current, submit: submit, advance: advance, score: score, validateState: validateState, hash: hash, currentXml: currentXml, previewBank: previewBank, createBank: createBank, renameBank: renameBank, replaceBank: replaceBank, deleteBank: deleteBank, requiresProgressReset: requiresProgressReset };
}(typeof window !== 'undefined' ? window : this));
