/* Test-only management regressions, reusable with native browser DOMParser. */
(function (root) {
  'use strict';
  function run(C, source, parity) {
    var output = { done: false, passed: 0, failures: [], tests: [] };
    function check(condition, text) { if (!condition) throw new Error(text); }
    function test(name, body) { try { body(); output.passed++; output.tests.push('PASS ' + name); } catch (error) { output.failures.push(name + ': ' + error.message); } }
    function reject(action) { var rejected = false; try { action(); } catch (error) { rejected = true; } check(rejected, '本应拒绝的操作成功了'); }
    function clone(state) { return JSON.parse(JSON.stringify(state)); }
    function fresh() {
      var state = C.createState(), bank = C.createBank(state, '原始题库', source);
      state.ImmediateScoring = true; state.Active = C.createSession(bank, 1, 2, 2, false, '测试');
      state.Active.Draft = ['A']; C.submit(state, bank); C.advance(state);
      state.Active.Draft = ['B']; C.submit(state, bank);
      return state;
    }
    test('创建和预览：标题转义有效且预览不修改状态', function () {
      var state = C.createState(), bank = C.createBank(state, '新建 & "题库"', source), before = JSON.stringify(state);
      check(state.SelectedBankId === bank.Id && bank.Questions.length === 3, '创建题目或选中错误');
      check(C.parseXml(bank.Xml, 'x.xml').Name === bank.Name, '标题转义错误');
      C.previewBank('预览', source); check(JSON.stringify(state) === before, '预览修改了状态');
    });
    test('重命名保留原题库、问题 ID、错题次数及待查看评分', function () {
      var state = fresh(), old = state.Banks[0], active = state.Active;
      var renamed = C.renameBank(state, old.Id, '重命名 & "标题"');
      check(renamed.Id === old.Id && renamed.Questions[0].Id === old.Questions[0].Id, '重命名改变 ID');
      check(state.Wrongs.length === 2 && state.Wrongs[0].Count === 1 && state.Active === active && active.FeedbackPending, '重命名丢失记录');
      check(C.parseXml(renamed.Xml, 'x.xml').Name === renamed.Name, '重命名导出无效'); C.validateState(clone(state));
    });
    test('替换无关题目保留评分，题库 ID 与关联记录一起重映射', function () {
      var state = fresh(), old = state.Banks[0], xml = source.replace('地球', '火星');
      check(!C.requiresProgressReset(state, old.Id, xml), '无关问题误判');
      var changed = C.replaceBank(state, old.Id, '修改第三题', xml);
      check(changed.Id !== old.Id && changed.ImportedAt === old.ImportedAt && state.SelectedBankId === changed.Id, '日期/选中/ID 不正确');
      check(state.Active.BankId === changed.Id && state.Active.FeedbackPending && state.Wrongs.length === 2 && state.Wrongs.every(function (w) { return w.BankId === changed.Id; }), '进度或错题未重映射'); C.validateState(clone(state));
    });
    test('替换已答题仅移除变化题目的错题并结束受影响进度', function () {
      var state = fresh(), old = state.Banks[0], xml = source.replace('偶数', '正偶数');
      check(C.requiresProgressReset(state, old.Id, xml), '未识别受影响进度');
      var changed = C.replaceBank(state, old.Id, '修改第二题', xml);
      check(state.Active === null && state.Wrongs.length === 1 && state.Wrongs[0].QuestionId === changed.Questions[0].Id && state.Wrongs[0].BankId === changed.Id, '变化题过滤错误'); C.validateState(clone(state));
    });
    test('重复名称/内容、非法 XML、空题库与名称错误全部保持原状态', function () {
      var state = fresh(), old = state.Banks[0]; C.createBank(state, 'Other', source.replace('地球', '水星'));
      var before = JSON.stringify(state);
      reject(function () { C.renameBank(state, old.Id, 'other'); });
      reject(function () { C.createBank(state, '重复内容', source); });
      reject(function () { C.replaceBank(state, old.Id, '坏 XML', '<quiz><question></quiz>'); });
      reject(function () { C.replaceBank(state, old.Id, 'Other', source); });
      reject(function () { C.createBank(state, '', source); });
      reject(function () { C.createBank(state, Array(122).join('名'), source); });
      reject(function () { C.createBank(state, '有\n换行', source); });
      reject(function () { C.createBank(state, '空题库', '<quiz/>'); });
      check(JSON.stringify(state) === before, '失败操作部分修改状态');
    });
    test('XML 注释、根属性、未知元数据保留，大小写标题不会干扰重命名', function () {
      var xml = source.replace('<quiz', '<!-- original note --><quiz custom="kept"').replace('<answer>B</answer>', '<source>资料 &amp; 引用</source><answer>B</answer>');
      var bank = C.previewBank('Guide & "Name"', xml);
      check(bank.Xml.indexOf('original note') >= 0 && bank.Xml.indexOf('custom="kept"') >= 0 && bank.Xml.indexOf('资料 &amp; 引用') >= 0, '元数据丢失');
      check(C.parseXml(bank.Xml, 'x.xml').Name === 'Guide & "Name"', '特殊标题无效');
      check(C.previewBank('新名', source.replace('title=', 'TITLE=')).Name === '新名', 'TITLE 未覆盖');
    });
    test('删除关联记录，保留其他题库；旧备份缺 XML 也可重命名', function () {
      var state = fresh(), old = state.Banks[0]; old.Xml = '';
      var renamed = C.renameBank(state, old.Id, '兼容旧备份');
      check(renamed.Id === old.Id && C.parseXml(renamed.Xml, 'x.xml').Questions.length === 3, '旧备份兼容失败');
      var second = C.createBank(state, '第二题库', source.replace('地球', '木星')); state.SelectedBankId = old.Id;
      C.deleteBank(state, old.Id); check(state.Banks.length === 1 && state.Banks[0].Id === second.Id && state.Wrongs.length === 0 && state.Active === null && state.SelectedBankId === second.Id, '删除关联数据失败');
      C.deleteBank(state, second.Id); check(state.Banks.length === 0 && state.SelectedBankId === '', '最后题库删除失败'); C.validateState(clone(state));
    });
    test('管理预览的 SHA-256 题库和题目标识与 C# 实现一致', function () {
      var bank = C.previewBank('跨平台核对', source);
      check(parity && bank.Id === parity.BankId, '题库 ID 不一致');
      check(JSON.stringify(bank.Questions.map(function (q) { return q.Id; })) === JSON.stringify(parity.QuestionIds), '题目 ID 不一致');
    });
    output.done = true; return output;
  }
  if (typeof module !== 'undefined' && module.exports) module.exports = run;
  else root.runManagementTests = run;
}(typeof window !== 'undefined' ? window : this));
