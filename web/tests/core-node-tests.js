'use strict';
var fs = require('fs'), path = require('path'), vm = require('vm'), crypto = require('crypto'), assert = require('assert');
var sandbox = { crypto: crypto.webcrypto, Uint32Array: Uint32Array, console: console };
vm.createContext(sandbox);
vm.runInContext(fs.readFileSync(path.join(__dirname, '../dist/core.js'), 'utf8'), sandbox);
var C = sandbox.ChooserCore, passed = 0, report = [];
function test(name, body) { body(); passed++; report.push('PASS ' + name); }
function clone(value) { return JSON.parse(JSON.stringify(value)); }
function bank() {
  return { Id: 'bank', Name: '验证题库', ImportedAt: '', Xml: '', Questions: [
    { Id: 'q1', Number: 1, Text: '单选', Explanation: '', Options: [{Id:'A',Text:'甲'}, {Id:'B',Text:'乙'}], Answers: ['B'] },
    { Id: 'q2', Number: 2, Text: '多选', Explanation: '', Options: [{Id:'A',Text:'甲'}, {Id:'B',Text:'乙'}, {Id:'C',Text:'丙'}], Answers: ['A', 'C'] },
    { Id: 'q3', Number: 3, Text: '判断', Explanation: '', Options: [{Id:'T',Text:'正确'}, {Id:'F',Text:'错误'}], Answers: ['T'] }
  ] };
}
test('SHA-256 matches native crypto for ASCII, Chinese, supplementary Unicode and multi-block strings', function () {
  ['', 'abc', '选择器 by zlight106', '🚀网络🛰️', Array(500).join('题库缓存123')].forEach(function (text) {
    assert.strictEqual(C.hash(text), crypto.createHash('sha256').update(text).digest('hex'));
  });
});
test('Legacy state options default false and retain banks', function () {
  var old = { Banks: [bank()], Wrongs: [], Active: null };
  C.validateState(old); assert.strictEqual(old.ShowAnswers, false); assert.strictEqual(old.ImmediateScoring, false); assert.strictEqual(old.Banks.length, 1);
});
test('Immediate grading locks submission and resumes feedback without recounting', function () {
  var state = C.createState(), b = bank(); state.Banks.push(b); state.ImmediateScoring = true;
  state.Active = C.createSession(b, 1, 3, 3, false, '练习'); state.Active.Draft = ['A'];
  C.submit(state, b); assert.strictEqual(state.Active.FeedbackPending, true); assert.strictEqual(state.Wrongs[0].Count, 1); assert.strictEqual(C.current(b, state.Active).Id, 'q1');
  assert.throws(function () { C.submit(state, b); });
  state = C.validateState(clone(state)); assert.strictEqual(state.Active.Position, 1); assert.strictEqual(C.current(state.Banks[0], state.Active).Id, 'q1');
  C.advance(state); assert.strictEqual(C.current(b, state.Active).Id, 'q2'); assert.strictEqual(state.Wrongs[0].Count, 1);
});
test('Multi-select exact sets and T/F total two of three; final feedback remains visible until advance', function () {
  var state = C.createState(), b = bank(); state.Banks.push(b); state.ImmediateScoring = true; state.Active = C.createSession(b, 1, 3, 3, false);
  state.Active.Draft = ['A']; C.submit(state, b); C.advance(state);
  state.Active.Draft = ['C', 'A']; C.submit(state, b); assert.strictEqual(C.score(state.Active), 1); C.advance(state);
  state.Active.Draft = ['T']; C.submit(state, b);
  assert.strictEqual(C.score(state.Active), 2); assert.strictEqual(state.Active.Completed, true); assert.strictEqual(state.Active.FeedbackPending, true); assert.strictEqual(C.current(b, state.Active).Id, 'q3');
  state = C.validateState(clone(state)); C.advance(state); assert.strictEqual(state.Active.FeedbackPending, false); assert.throws(function () { C.submit(state, b); });
});
test('Later correct answer retains historical wrong count and deferred mode advances immediately', function () {
  var state = C.createState(), b = bank(); state.Banks.push(b);
  state.Active = C.createSession(b, 1, 1, 1, false); state.Active.Draft = ['A']; C.submit(state, b);
  state.Active = C.createSession(b, 1, 1, 1, false); state.Active.Draft = ['B']; C.submit(state, b);
  assert.strictEqual(state.Wrongs[0].Count, 1); assert.strictEqual(C.score(state.Active), 1); assert.strictEqual(state.Active.Completed, true); assert.strictEqual(state.Active.FeedbackPending, false);
});
test('Random draw never repeats and range/count failures reject', function () {
  var b = bank();
  for (var i = 0; i < 25; i++) { var s = C.createSession(b, 1, 3, 3, true); assert.strictEqual(new Set(s.QuestionIds).size, 3); }
  assert.throws(function () { C.createSession(b, 0, 3, 2, true); }); assert.throws(function () { C.createSession(b, 1, 3, 4, true); });
});
test('Corrupt score, dangling wrong reference, and invalid pending state reject', function () {
  var state = C.createState(), b = bank(); state.Banks.push(b); state.Active = C.createSession(b, 1, 3, 3, false); state.Active.Draft = ['A']; C.submit(state, b);
  var wrong = clone(state); wrong.Active.Attempts[0].Correct = true; assert.throws(function () { C.validateState(wrong); });
  wrong = clone(state); wrong.Wrongs[0].QuestionId = 'gone'; assert.throws(function () { C.validateState(wrong); });
  wrong = C.createState(); wrong.Banks.push(b); wrong.Active = C.createSession(b, 1, 3, 3, false); wrong.Active.FeedbackPending = true; assert.throws(function () { C.validateState(wrong); });
});
var output = report.join('\n') + '\n\n' + passed + ' passed, 0 failed.\n';
fs.writeFileSync(path.join(__dirname, 'core-node-report.txt'), output, 'utf8'); console.log(output);
