/* No framework, no analytics, no answer uploads. ES5 syntax for older Android browsers. */
(function () {
  'use strict';
  var C = window.ChooserCore, state, store, view = 'home', ready = false, working = false;
  var app = document.getElementById('app'), notice = document.getElementById('notice'), modal = document.getElementById('modal');
  var ui = { start: 1, end: 0, bank: '', minimum: 1, wrongPage: 0, manageQuery: '', detailId: '', detailQuery: '', detailPage: 0 }, modalReturnFocus = null, modalKind = '', renameId = '', editor = null;
  var EXAMPLE_XML = '<?xml version="1.0" encoding="UTF-8"?>\n<quiz title="新题库">\n  <question type="multiple_choice" score="1">\n    <text>2 + 2 等于多少？</text>\n    <option id="A">3</option>\n    <option id="B">4</option>\n    <option id="C">5</option>\n    <option id="D">6</option>\n    <answer>B</answer>\n    <explanation>2 + 2 = 4。</explanation>\n  </question>\n  <question type="multiple_choice" score="1">\n    <text>以下哪些数字是偶数？</text>\n    <option id="A">2</option>\n    <option id="B">3</option>\n    <option id="C">4</option>\n    <option id="D">5</option>\n    <answer>A,C</answer>\n    <explanation>2 和 4 都是偶数。</explanation>\n  </question>\n  <question type="multiple_choice" score="1">\n    <text>地球是一颗行星。</text>\n    <option id="T">正确</option>\n    <option id="F">错误</option>\n    <answer>T</answer>\n  </question>\n</quiz>';
  function esc(value) { return String(value == null ? '' : value).replace(/[&<>"']/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]; }); }
  function button(text, action, primary, extra) { return '<button type="button" data-act="' + action + '"' + (primary ? ' class="primary"' : '') + (extra || '') + '>' + esc(text) + '</button>'; }
  function byId(id) { return document.getElementById(id); }
  function bankById(id) { return state.Banks.filter(function (b) { return b.Id === id; })[0]; }
  function activeBank() { return state.Active && bankById(state.Active.BankId); }
  function selectedBank() { return bankById(state.SelectedBankId) || state.Banks[0]; }
  function sumWrong() { return state.Wrongs.reduce(function (sum, item) { return sum + item.Count; }, 0); }
  function showNotice(text, error) { notice.textContent = text || ''; notice.className = 'notice' + (error ? ' error' : ''); notice.hidden = !text; }
  function errorText(error) { return error && error.message ? error.message : String(error); }
  function persist() {
    if (!store || !state) return Promise.resolve(false);
    return store.save(state).then(function () { return true; }, function (error) { showNotice('未能保存：' + errorText(error) + ' 请在“管理题库”备份数据。', true); return false; });
  }
  function applyDisplay() {
    document.documentElement.className = state.Dark ? 'dark' : '';
    document.documentElement.style.fontSize = (16 * state.Zoom) + 'px';
    byId('theme-button').textContent = state.Dark ? '浅色' : '深色';
    document.querySelector('meta[name="theme-color"]').setAttribute('content', state.Dark ? '#10141b' : '#0b57d0');
  }
  function checked(flag) { return flag ? ' checked' : ''; }
  function settings() {
    return '<div class="settings" aria-label="答题选项"><label class="check-label"><input type="checkbox" id="show-answers" data-setting="ShowAnswers"' + checked(state.ShowAnswers) + '>显示答案</label><label class="check-label"><input type="checkbox" id="immediate-score" data-setting="ImmediateScoring"' + checked(state.ImmediateScoring) + '>立即评分</label></div>';
  }
  function show(name, keepScroll) {
    if (!state) return;
    if (view === 'editor' && name !== 'editor' && !leaveEditor()) return;
    view = name;
    if (name === 'quiz') renderQuiz();
    else if (name === 'result') renderResult();
    else if (name === 'wrong') renderWrongs();
    else if (name === 'manage') renderManage();
    else if (name === 'bank-detail') renderBankDetail();
    else if (name === 'editor') renderEditor();
    else { view = 'home'; renderHome(); }
    if (!keepScroll) window.scrollTo(0, 0);
  }
  function renderHome() {
    var bank = selectedBank(), s = state.Active;
    if (bank && (!ui.end || ui.bank !== bank.Id)) { ui.start = 1; ui.end = bank.Questions.length; ui.bank = bank.Id; }
    var options = state.Banks.map(function (b) { return '<option value="' + esc(b.Id) + '"' + (bank && bank.Id === b.Id ? ' selected' : '') + '>' + esc(b.Name) + ' · ' + b.Questions.length + ' 题</option>'; }).join('');
    var resume = s && activeBank() ? button(!s.Completed || s.FeedbackPending ? '继续上次进度' : '查看上次成绩', !s.Completed || s.FeedbackPending ? 'resume' : 'result', false) : '';
    app.innerHTML = '<section class="card"><div class="card-title"><h1>题库选择器</h1>' + button('管理题库', 'manage', false) + '</div>' +
      '<div class="field"><label for="bank-picker">选择已有题库</label><select id="bank-picker"' + (bank ? '' : ' disabled') + '>' + (options || '<option>请先导入题库</option>') + '</select></div>' +
      '<button type="button" class="upload" data-act="import" title="选择 XML 文件或拖入窗口">＋ 导入 XML 题库</button>' +
      '<div class="range"><div class="field"><label for="range-start">起始题号</label><input id="range-start" type="number" min="1" max="' + (bank ? bank.Questions.length : 1) + '" step="1" inputmode="numeric" value="' + esc(ui.start) + '"' + (bank ? '' : ' disabled') + '></div>' +
      '<div class="field"><label for="range-end">结束题号</label><input id="range-end" type="number" min="1" max="' + (bank ? bank.Questions.length : 1) + '" step="1" inputmode="numeric" value="' + esc(ui.end || '') + '"' + (bank ? '' : ' disabled') + '></div></div>' +
      '<p class="range-hint">' + (bank ? '共 ' + bank.Questions.length + ' 题' : '请先导入 XML 题库') + '</p>' + settings() +
      '<div class="actions">' + button('开始答题 →', 'start', true, bank ? '' : ' disabled') + button('随机考试', 'exam', false, bank ? '' : ' disabled') + resume + '</div></section>' +
      '<div class="stats-grid"><section class="card stat-card"><p class="stat-label">本机题库</p><p class="stat-number">' + state.Banks.length + ' 个题库 / ' + state.Banks.reduce(function (sum, b) { return sum + b.Questions.length; }, 0) + ' 题</p></section>' +
      '<section class="card stat-card"><p class="stat-label">累计错题</p><p class="stat-number">' + state.Wrongs.length + ' 题 / ' + sumWrong() + ' 次</p><button type="button" class="text-button" data-act="wrong">查看错题本 →</button></section></div>';
  }
  function answers(q, ids) { return q.Options.filter(function (o) { return ids.indexOf(o.Id) >= 0; }).map(function (o) { return o.Id; }).join('、'); }
  function answerPanel(q) {
    return '<div class="answer-panel"><p class="answer">正确答案：' + esc(answers(q, q.Answers)) + '</p>' + (q.Explanation ? '<p class="explanation">' + esc(q.Explanation) + '</p>' : '') + '</div>';
  }
  function renderQuiz() {
    var s = state.Active, bank = activeBank();
    if (!s || !bank) { show('home'); return; }
    if (s.Completed && !s.FeedbackPending) { show('result'); return; }
    var q = C.current(bank, s), pending = s.FeedbackPending, attempt = pending ? s.Attempts[s.Position - 1] : null;
    var chosen = pending ? attempt.Selected : s.Draft, number = pending ? s.Position : s.Position + 1;
    var kind = q.Answers.length > 1 ? '多选' : (q.Options.length === 2 && q.Options[0].Id === 'T' && q.Options[1].Id === 'F' ? '判断' : '单选');
    var options = q.Options.map(function (o) {
      var picked = chosen.indexOf(o.Id) >= 0, correct = q.Answers.indexOf(o.Id) >= 0;
      var cls = 'option' + (picked ? ' selected' : '') + (pending && correct ? ' correct-option' : '') + (pending && picked && !correct ? ' wrong-option' : '');
      var mark = q.Answers.length > 1 ? (picked ? '☑' : '□') : (picked ? '●' : '○');
      return '<button type="button" class="' + cls + '" data-act="option" data-id="' + esc(o.Id) + '" aria-pressed="' + picked + '"' + (pending ? ' disabled' : '') + '><span class="mark" aria-hidden="true">' + mark + '</span><span>' + esc(o.Id) + '. ' + esc(o.Text) + (pending && correct ? ' ✓' : (pending && picked && !correct ? ' ×' : '')) + '</span></button>';
    }).join('');
    var feedback = pending ? '<div class="feedback ' + (attempt.Correct ? 'correct' : 'incorrect') + '" role="status"><p class="feedback-title">' + (attempt.Correct ? '回答正确 · 1 分' : '回答错误 · 0 分') + '</p><p class="small">累计得分 ' + C.score(s) + ' / ' + s.QuestionIds.length + ' 分</p><p class="small">你的选择：' + esc(answers(q, chosen)) + '　正确答案：' + esc(answers(q, q.Answers)) + '</p>' + (q.Explanation ? '<p class="explanation">' + esc(q.Explanation) + '</p>' : '') + '</div>' : (state.ShowAnswers ? answerPanel(q) : '');
    var submitText = pending ? (s.Completed ? '查看成绩 →' : '下一题 →') : (state.ImmediateScoring ? '提交答案' : (s.Position === s.QuestionIds.length - 1 ? '完成并结算 →' : '提交并下一题 →'));
    app.innerHTML = '<section class="card"><p class="quiz-meta">' + esc(s.Mode + ' / ' + bank.Name) + '</p><div class="quiz-heading"><h2>第 ' + number + ' 题 / ' + s.QuestionIds.length + '</h2><span class="badge">' + kind + ' · 1 分</span></div>' +
      '<div class="progress" role="progressbar" aria-label="已提交题数" aria-valuemin="0" aria-valuemax="' + s.QuestionIds.length + '" aria-valuenow="' + s.Position + '"><span style="width:' + (100 * s.Position / s.QuestionIds.length).toFixed(2) + '%"></span></div>' +
      '<h3 class="question-text">' + esc(q.Text) + '</h3><div id="options">' + options + '</div>' + feedback + settings() +
      '<div class="actions">' + button('保存并返回', 'pause', false) + button('提前交卷', 'finish', false) + button(submitText, pending ? 'next' : 'submit', true, !pending && !chosen.length ? ' disabled' : '') + '</div></section><p class="quiz-footer">原题号 ' + q.Number + ' · 已提交 ' + s.Position + ' 题</p>';
  }
  function review(bank, q, selected, meta) {
    return '<details class="review"><summary><span class="review-meta">' + esc(meta) + '</span>' + esc(q.Text) + '</summary><div class="review-body">' + q.Options.map(function (o) {
      var correct = q.Answers.indexOf(o.Id) >= 0, picked = selected.indexOf(o.Id) >= 0;
      return '<p class="review-option' + (correct ? ' success-text' : (picked ? ' error-text' : ' muted')) + '">' + esc(o.Id + '. ' + o.Text) + (correct ? ' ✓' : (picked ? ' ×' : '')) + '</p>';
    }).join('') + '<p class="error-text">你的选择：' + esc(answers(q, selected)) + '</p><p class="success-text">正确答案：' + esc(answers(q, q.Answers)) + '</p>' + (q.Explanation ? '<p class="explanation muted">' + esc(q.Explanation) + '</p>' : '') + '</div></details>';
  }
  function renderResult() {
    var s = state.Active, bank = activeBank();
    if (!s || !bank) { show('home'); return; }
    if (s.FeedbackPending || !s.Completed) { show('quiz'); return; }
    var score = C.score(s), wrongs = s.Attempts.filter(function (a) { return !a.Correct; }), unanswered = s.QuestionIds.length - s.Attempts.length;
    app.innerHTML = '<section class="card"><div class="card-title"><h1>答题结算</h1></div><p class="quiz-meta">' + esc(s.Mode + ' / ' + bank.Name) + '</p>' +
      '<div class="result-summary"><p class="score">' + score + '<small> / ' + s.QuestionIds.length + ' 分</small></p><p class="result-stats">每题 1 分 · 答对 ' + score + ' 题 · 答错 ' + wrongs.length + ' 题 · 未答 ' + unanswered + ' 题</p><p class="small muted">正确率 ' + Math.round(100 * score / s.QuestionIds.length) + '% · 共提交 ' + s.Attempts.length + ' 题</p></div>' +
      '<div class="actions">' + button('返回首页', 'home', true) + (wrongs.length ? button('重练本次错题', 'retry-result', false) : '') + button('查看错题本', 'wrong-current', false) + '</div>' +
      '<h2 class="section-title">本次错题回顾</h2>' + wrongs.map(function (a) {
        var q = bank.Questions.filter(function (x) { return x.Id === a.QuestionId; })[0];
        var record = state.Wrongs.filter(function (w) { return w.BankId === bank.Id && w.QuestionId === q.Id; })[0];
        return review(bank, q, a.Selected, '原题号 ' + q.Number + ' · 累计答错 ' + (record ? record.Count : 1) + ' 次');
      }).join('') + (!wrongs.length ? '<p class="success-text">' + (unanswered ? '已提交的题目均答对。' : '本次全部答对。') + '</p>' : '') + '</section>';
  }
  function filteredWrongs() {
    return state.Wrongs.filter(function (w) { return w.Count >= ui.minimum && (!ui.bankFilter || w.BankId === ui.bankFilter); }).sort(function (a, b) { return b.Count - a.Count || (a.LastAt > b.LastAt ? -1 : 1); });
  }
  function renderWrongs() {
    var list = filteredWrongs(), size = 30;
    ui.wrongPage = Math.max(0, Math.min(ui.wrongPage, Math.max(0, Math.ceil(list.length / size) - 1)));
    app.innerHTML = '<section class="card"><div class="card-title"><h1>我的错题本</h1>' + button('返回首页', 'home', false) + '</div><p class="stat-number">共 ' + state.Wrongs.length + ' 道错题 / 累计答错 ' + sumWrong() + ' 次</p>' +
      '<div class="range section-title"><div class="field"><label for="wrong-bank">题库筛选</label><select id="wrong-bank"><option value="">全部题库</option>' + state.Banks.map(function (b) { return '<option value="' + esc(b.Id) + '"' + (ui.bankFilter === b.Id ? ' selected' : '') + '>' + esc(b.Name) + '</option>'; }).join('') + '</select></div>' +
      '<div class="field"><label for="wrong-minimum">至少答错次数</label><input id="wrong-minimum" type="number" min="1" step="1" inputmode="numeric" value="' + ui.minimum + '"></div></div>' +
      '<div class="actions">' + button('筛选', 'filter', true) + (list.length && (ui.bankFilter || state.Banks.length === 1) ? button('重练筛选错题', 'retry-filter', false) : '') + '</div><p class="small muted section-title">当前筛选 ' + list.length + ' 题 · 按答错次数排序</p>' +
      list.slice(ui.wrongPage * size, (ui.wrongPage + 1) * size).map(function (w) {
        var b = bankById(w.BankId), q = b.Questions.filter(function (x) { return x.Id === w.QuestionId; })[0];
        var date = new Date(w.LastAt), dateText = isNaN(date.getTime()) ? '' : date.toLocaleDateString() + ' ' + date.toLocaleTimeString();
        return review(b, q, w.LastSelected, '答错 ' + w.Count + ' 次 · ' + b.Name + ' · 第 ' + q.Number + ' 题' + (dateText ? ' · ' + dateText : ''));
      }).join('') + (!list.length ? '<p class="muted">暂无符合条件的错题。</p>' : '') +
      (list.length > size ? '<div class="actions pager">' + button('上一页', 'wrong-prev', false, ui.wrongPage ? '' : ' disabled') + '<span>' + (ui.wrongPage + 1) + ' / ' + Math.ceil(list.length / size) + '</span>' + button('下一页', 'wrong-next', false, (ui.wrongPage + 1) * size < list.length ? '' : ' disabled') + '</div>' : '') + '</section>';
  }
  function renderManage() {
    var query = ui.manageQuery.toLowerCase(), banks = state.Banks.filter(function (b) {
      return !query || b.Name.toLowerCase().indexOf(query) >= 0 || b.Questions.some(function (q) { return questionMatches(q, query); });
    });
    app.innerHTML = '<section class="card"><div class="card-title"><h1>题库管理</h1>' + button('返回首页', 'home', false) + '</div><div class="actions manage-tools">' + button('新建题库', 'new-bank', true) + button('导入 XML', 'import', false) + button('备份全部数据', 'backup', false) + button('恢复数据备份', 'restore', false) + '</div>' +
      '<div class="search-row"><div class="field"><label for="bank-search">查询题库</label><input id="bank-search" type="search" value="' + esc(ui.manageQuery) + '" placeholder="题库名称或题目内容" maxlength="200"></div>' + button('搜索', 'search-banks', true) + (ui.manageQuery ? button('清除', 'clear-bank-search', false) : '') + '</div><p class="small muted">共 ' + state.Banks.length + ' 个题库，当前显示 ' + banks.length + ' 个</p>' +
      banks.map(function (b) {
        var id = ' data-id="' + esc(b.Id) + '"';
        return '<article class="bank-row"><h3>' + esc(b.Name) + '</h3><p class="small muted">' + b.Questions.length + ' 题</p><div class="actions">' + button('查看', 'view-bank', true, id) + button('编辑 XML', 'edit-bank', false, id) + button('重命名', 'rename-bank', false, id) + button('使用题库', 'use-bank', false, id) + button('导出 XML', 'export-bank', false, id) + '<button type="button" class="danger" data-act="delete-bank"' + id + '>删除</button></div></article>';
      }).join('') + (!banks.length ? '<p class="section-title muted">' + (state.Banks.length ? '没有符合查询条件的题库。' : '暂无题库，可新建或导入 XML。') + '</p>' : '') +
      '<p class="storage-note">数据保存在当前浏览器。' + (store.kind === 'memory' ? '当前无法持久保存，请及时备份。' : '换设备可使用数据备份。') + '</p></section>';
  }
  function questionMatches(q, query) {
    return String(q.Number).indexOf(query) >= 0 || q.Text.toLowerCase().indexOf(query) >= 0 || q.Explanation.toLowerCase().indexOf(query) >= 0 || q.Options.some(function (o) { return o.Text.toLowerCase().indexOf(query) >= 0; });
  }
  function renderBankDetail() {
    var bank = bankById(ui.detailId); if (!bank) { show('manage'); return; }
    var query = ui.detailQuery.toLowerCase(), questions = bank.Questions.filter(function (q) { return !query || questionMatches(q, query); }), size = 30;
    ui.detailPage = Math.max(0, Math.min(ui.detailPage, Math.max(0, Math.ceil(questions.length / size) - 1)));
    var id = ' data-id="' + esc(bank.Id) + '"';
    app.innerHTML = '<section class="card"><div class="card-title"><h1>查看题库</h1>' + button('返回管理', 'manage', false) + '</div><h2 class="bank-detail-name">' + esc(bank.Name) + '</h2><p class="small muted">共 ' + bank.Questions.length + ' 题</p><div class="actions">' + button('编辑 XML', 'edit-bank', true, id) + button('重命名', 'rename-bank', false, id) + button('使用题库', 'use-bank', false, id) + button('导出 XML', 'export-bank', false, id) + '</div>' +
      '<div class="search-row"><div class="field"><label for="question-search">查询题目</label><input id="question-search" type="search" value="' + esc(ui.detailQuery) + '" placeholder="题号、题干、选项或解析" maxlength="200"></div>' + button('搜索', 'search-questions', true) + (ui.detailQuery ? button('清除', 'clear-question-search', false) : '') + '</div><p class="small muted detail-count">当前显示 ' + questions.length + ' 题</p>' +
      questions.slice(ui.detailPage * size, (ui.detailPage + 1) * size).map(function (q) {
        return '<details class="review"><summary><span class="review-meta">第 ' + q.Number + ' 题 · ' + (q.Answers.length > 1 ? '多选' : '单选 / 判断') + '</span>' + esc(q.Text) + '</summary><div class="review-body">' + q.Options.map(function (o) { var correct = q.Answers.indexOf(o.Id) >= 0; return '<p class="review-option' + (correct ? ' success-text' : ' muted') + '">' + esc(o.Id + '. ' + o.Text) + (correct ? ' ✓' : '') + '</p>'; }).join('') + '<p class="success-text">正确答案：' + esc(answers(q, q.Answers)) + '</p>' + (q.Explanation ? '<p class="explanation muted">' + esc(q.Explanation) + '</p>' : '') + '</div></details>';
      }).join('') + (!questions.length ? '<p class="muted">没有符合条件的题目。</p>' : '') +
      (questions.length > size ? '<div class="actions pager">' + button('上一页', 'detail-prev', false, ui.detailPage ? '' : ' disabled') + '<span>' + (ui.detailPage + 1) + ' / ' + Math.ceil(questions.length / size) + '</span>' + button('下一页', 'detail-next', false, (ui.detailPage + 1) * size < questions.length ? '' : ' disabled') + '</div>' : '') + '</section>';
  }
  function copyState() { return JSON.parse(JSON.stringify(state)); }
  function managementNotice(message) { showNotice(message + (store.kind === 'memory' ? ' 当前仅暂存于本页，请备份数据。' : ''), store.kind === 'memory'); }
  function commitCandidate(candidate, success) {
    working = true; app.className = 'busy';
    var fields = Array.prototype.slice.call(document.querySelectorAll('#app input,#app textarea,#app select,#app button,#modal input,#modal button'));
    var disabled = fields.map(function (field) { var previous = field.disabled; field.disabled = true; return previous; });
    return store.save(candidate).then(function () {
      state = candidate; working = false; app.className = '';
      success();
      return true;
    }, function (error) {
      working = false; app.className = '';
      fields.forEach(function (field, index) { field.disabled = disabled[index]; });
      showNotice('保存失败，原有题库与记录未改变：' + errorText(error), true);
      if (view === 'editor') editorMessage('保存失败：' + errorText(error), true);
      if (modalKind === 'rename' && byId('rename-error')) { byId('rename-error').textContent = errorText(error); byId('rename-error').hidden = false; }
      return false;
    });
  }
  function editorDirty() { return !!editor && (editor.name !== editor.initialName || editor.xml !== editor.initialXml); }
  function leaveEditor() {
    if (!editor) return true;
    if (editorDirty() && !window.confirm('放弃尚未保存的题库修改？')) return false;
    editor = null; return true;
  }
  function openEditor(bank) {
    editor = { bankId: bank ? bank.Id : '', name: bank ? bank.Name : '新题库', xml: bank ? C.currentXml(bank) : EXAMPLE_XML, returnView: view === 'bank-detail' ? 'bank-detail' : 'manage' };
    editor.initialName = editor.name; editor.initialXml = editor.xml;
    show('editor');
  }
  function renderEditor() {
    if (!editor) { show('manage'); return; }
    app.innerHTML = '<section class="card bank-editor"><div class="card-title"><h1>' + (editor.bankId ? '编辑题库' : '新建题库') + '</h1>' + button('取消', 'cancel-editor', false) + '</div>' +
      '<div class="field"><label for="editor-name">题库名称</label><input id="editor-name" type="text" maxlength="120" value="' + esc(editor.name) + '" autocomplete="off"></div>' +
      '<div class="field"><label for="editor-xml">题库 XML</label><textarea id="editor-xml" rows="20" spellcheck="false" autocapitalize="off" autocomplete="off" autocorrect="off">' + esc(editor.xml) + '</textarea></div>' +
      '<p class="small muted">保存时使用上面的题库名称。XML 至少包含一道完整题目。</p><p id="editor-message" class="editor-message" role="status" hidden></p>' +
      '<div class="actions">' + button('校验 XML', 'validate-editor', false) + button('保存题库', 'save-editor', true) + button('取消', 'cancel-editor', false) + '</div></section>';
  }
  function readEditor() {
    if (editor && byId('editor-name')) { editor.name = byId('editor-name').value; editor.xml = byId('editor-xml').value; }
  }
  function editorMessage(message, error) {
    var target = byId('editor-message'); if (!target) return;
    target.textContent = message; target.className = 'editor-message ' + (error ? 'error-text' : 'success-text'); target.hidden = !message;
  }
  function validateEditor() {
    readEditor();
    try { var preview = C.previewBank(editor.name, editor.xml); editorMessage('校验通过，共 ' + preview.Questions.length + ' 题。', false); return preview; }
    catch (error) { editorMessage(errorText(error), true); return null; }
  }
  function saveEditor() {
    if (!editor || working) return;
    readEditor();
    var candidate = copyState(), bank, oldId = editor.bankId;
    try { bank = editor.bankId ? C.replaceBank(candidate, editor.bankId, editor.name, editor.xml) : C.createBank(candidate, editor.name, editor.xml); }
    catch (error) { editorMessage(errorText(error), true); return; }
    var removed = state.Wrongs.length - candidate.Wrongs.length, resets = state.Active && !candidate.Active;
    if ((removed > 0 || resets) && !window.confirm('本次修改' + (removed > 0 ? '将移除 ' + removed + ' 道已修改或删除题目的错题记录' : '') + (removed > 0 && resets ? '，并' : '') + (resets ? '将结束该题库的当前答题进度' : '') + '。其余未改题目的记录会保留。保存修改？')) return;
    commitCandidate(candidate, function () {
      if (ui.bankFilter === oldId) { ui.bankFilter = bank.Id; ui.wrongPage = 0; }
      editor = null; ui.detailId = bank.Id; ui.detailQuery = ''; ui.detailPage = 0; ui.end = 0;
      show('bank-detail'); managementNotice('题库已保存。');
    });
  }
  function openRename(bank) {
    renameId = bank.Id; modalKind = 'rename'; modalReturnFocus = document.activeElement;
    modal.innerHTML = '<section class="modal" role="dialog" aria-modal="true" aria-labelledby="rename-title"><h2 id="rename-title">重命名题库</h2><div class="field section-title"><label for="rename-name">题库名称</label><input id="rename-name" type="text" maxlength="120" value="' + esc(bank.Name) + '" autocomplete="off"></div><p id="rename-error" class="error-text" role="alert" hidden></p><div class="actions">' + button('取消', 'close-modal', false) + button('保存名称', 'confirm-rename', true) + '</div></section>';
    modal.hidden = false; byId('rename-name').focus(); byId('rename-name').select();
  }
  function confirmRename() {
    var candidate = copyState(), bank;
    try { bank = C.renameBank(candidate, renameId, byId('rename-name').value); }
    catch (error) { byId('rename-error').textContent = errorText(error); byId('rename-error').hidden = false; return; }
    commitCandidate(candidate, function () { closeModal(); if (view === 'bank-detail') ui.detailId = bank.Id; show(view); managementNotice('题库名称已保存。'); });
  }
  function canReplace() { return !state.Active || (state.Active.Completed && !state.Active.FeedbackPending) || window.confirm('开始新一轮会替换未完成的进度，已累计的错题次数保留。继续？'); }
  function getRange() {
    var b = selectedBank(), start = Number(ui.start), end = Number(ui.end);
    if (!b || Math.floor(start) !== start || Math.floor(end) !== end || start < 1 || end < start || end > b.Questions.length) throw new Error('请填写有效题号：1 ≤ 起始题号 ≤ 结束题号 ≤ ' + (b ? b.Questions.length : 0) + '。');
    return { bank: b, start: start, end: end };
  }
  function begin(random, count) {
    var r = getRange(); if (!canReplace()) return;
    state.Active = C.createSession(r.bank, r.start, r.end, count || r.end - r.start + 1, random, random ? '随机考试' : '顺序练习');
    state.SelectedBankId = r.bank.Id; persist(); show('quiz');
  }
  function beginQuestions(bank, ids) {
    if (!ids.length || !canReplace()) return;
    state.Active = { BankId: bank.Id, Mode: '错题重练', QuestionIds: ids.slice(), Attempts: [], Position: 0, Draft: [], Completed: false, FeedbackPending: false };
    state.SelectedBankId = bank.Id; persist(); show('quiz');
  }
  function openExam() {
    var range = getRange(), maximum = range.end - range.start + 1;
    modalKind = 'exam';
    modalReturnFocus = document.activeElement;
    modal.innerHTML = '<section class="modal" role="dialog" aria-modal="true" aria-labelledby="exam-title"><h2 id="exam-title">随机考试</h2><p>抽取范围：第 ' + range.start + '–' + range.end + ' 题</p><div class="field"><label for="exam-count">抽取题数（1–' + maximum + '）</label><input id="exam-count" type="number" min="1" max="' + maximum + '" step="1" inputmode="numeric" value="' + Math.min(20, maximum) + '"></div><p id="exam-error" class="error-text" role="alert" hidden></p><div class="actions">' + button('取消', 'close-modal', false) + button('进入考试', 'confirm-exam', true) + '</div></section>';
    modal.hidden = false; byId('exam-count').focus(); byId('exam-count').select();
  }
  function closeModal() { modal.hidden = true; modal.innerHTML = ''; modalKind = ''; renameId = ''; if (modalReturnFocus && document.documentElement.contains(modalReturnFocus)) modalReturnFocus.focus(); }
  function choose(id) {
    var s = state.Active;
    if (!s || s.Completed || s.FeedbackPending) return;
    var q = C.current(activeBank(), s);
    if (!q.Options.some(function (o) { return o.Id === id; })) return;
    if (q.Answers.length === 1) s.Draft = [id];
    else if (s.Draft.indexOf(id) >= 0) s.Draft.splice(s.Draft.indexOf(id), 1); else s.Draft.push(id);
    persist(); show('quiz', true);
  }
  function submit() {
    if (view !== 'quiz' || !state.Active || state.Active.Completed || state.Active.FeedbackPending || !state.Active.Draft.length) return;
    C.submit(state, activeBank()); persist(); show(state.Active.Completed && !state.Active.FeedbackPending ? 'result' : 'quiz');
  }
  function finish() {
    if (!window.confirm('现在交卷？当前已选答案会提交，其余未答题按 0 分计算。')) return;
    var s = state.Active;
    if (!s.FeedbackPending && !s.Completed && s.Draft.length) C.submit(state, activeBank());
    s.Completed = true; s.FeedbackPending = false; s.Draft = []; persist(); show('result');
  }
  function download(filename, text, type) {
    var blob = new Blob([text], { type: type || 'application/octet-stream' }), url = URL.createObjectURL(blob), link = document.createElement('a');
    link.href = url; link.download = filename; document.body.appendChild(link); link.click(); document.body.removeChild(link); setTimeout(function () { URL.revokeObjectURL(url); }, 30000);
  }
  function safeName(text) { return String(text).replace(/[\\/:*?"<>|]/g, '_'); }
  function readFile(file) {
    return new Promise(function (resolve, reject) {
      if (!file || file.size > 32 * 1024 * 1024) { reject(new Error('网页版最多导入 32 MB 文件。')); return; }
      var reader = new FileReader();
      reader.onerror = function () { reject(new Error('无法读取文件。')); };
      reader.onload = function () {
        try {
          var bytes = new Uint8Array(reader.result), encoding = 'utf-8', prefix = '', i;
          if (bytes[0] === 255 && bytes[1] === 254) encoding = 'utf-16le';
          else if (bytes[0] === 254 && bytes[1] === 255) encoding = 'utf-16be';
          else { for (i = 0; i < Math.min(bytes.length, 250); i++) prefix += String.fromCharCode(bytes[i]); var match = /<\?xml[^>]*encoding\s*=\s*["']([^"']+)["']/i.exec(prefix); if (match) encoding = match[1]; }
          if (window.TextDecoder) { resolve(new TextDecoder(encoding, { fatal: true }).decode(bytes).replace(/^\uFEFF/, '')); return; }
          var fallback = new FileReader(); fallback.onerror = reader.onerror; fallback.onload = function () { resolve(String(fallback.result).replace(/^\uFEFF/, '')); }; fallback.readAsText(file, encoding);
        } catch (error) { reject(error); }
      };
      reader.readAsArrayBuffer(file);
    });
  }
  function importFiles(files) {
    if (!files.length || working || !ready) return;
    if (view === 'editor' && !leaveEditor()) return;
    if (view === 'quiz') { showNotice('请先保存并返回首页，再导入题库。'); return; }
    working = true; app.className = 'busy';
    var imported = 0, duplicate = 0, errors = [], chain = Promise.resolve();
    Array.prototype.slice.call(files).forEach(function (file) {
      chain = chain.then(function () {
        if (!/\.xml$/i.test(file.name)) throw new Error('请选择 XML 题库文件。');
        return readFile(file).then(function (xml) {
          var bank = C.parseXml(xml, file.name), exists = bankById(bank.Id);
          if (exists) { state.SelectedBankId = exists.Id; duplicate++; return; }
          var base = bank.Name, suffix = 2;
          while (state.Banks.some(function (b) { return b.Name === bank.Name; })) bank.Name = base + ' (' + suffix++ + ')';
          state.Banks.push(bank); state.SelectedBankId = bank.Id; imported++;
        });
      }).catch(function (error) { errors.push(file.name + '：' + errorText(error)); });
    });
    chain.then(function () { return persist(); }).then(function (saved) {
      working = false; app.className = ''; ui.end = 0; show('home');
      if (saved) showNotice('已导入 ' + imported + ' 个题库' + (duplicate ? '，' + duplicate + ' 个已存在' : '') + (errors.length ? '。' + errors.join('；') : '。'), errors.length > 0);
    });
  }
  function restore(file) {
    if (!file || working) return;
    if (view === 'editor' && !leaveEditor()) return;
    working = true; app.className = 'busy';
    readFile(file).then(function (text) {
      var next = C.validateState(JSON.parse(text));
      if (!window.confirm('备份含 ' + next.Banks.length + ' 个题库、' + next.Wrongs.length + ' 道错题。恢复会替换当前所有题库和记录。继续？')) return;
      return store.save(next).then(function () { state = next; ready = true; ui.end = 0; applyDisplay(); show('home'); showNotice('数据备份已恢复。'); });
    }).then(function () { working = false; app.className = ''; }, function (error) { working = false; app.className = ''; showNotice('恢复失败：' + errorText(error), true); });
  }
  function handle(action, element) {
    var b, s, list, count;
    if (working) return;
    if (action === 'restore') { byId('backup-input').click(); return; }
    if (action === 'reload') { window.location.reload(); return; }
    if (!ready || working) return;
    if (action === 'home') { show('home'); return; }
    if (action === 'manage') { show('manage'); return; }
    if (action === 'new-bank') { openEditor(null); return; }
    if (action === 'view-bank') { ui.detailId = element.getAttribute('data-id'); ui.detailQuery = ''; ui.detailPage = 0; show('bank-detail'); return; }
    if (action === 'edit-bank') { b = bankById(element.getAttribute('data-id')); if (b) openEditor(b); return; }
    if (action === 'rename-bank') { b = bankById(element.getAttribute('data-id')); if (b) openRename(b); return; }
    if (action === 'confirm-rename') { confirmRename(); return; }
    if (action === 'search-banks') { ui.manageQuery = byId('bank-search').value.replace(/^\s+|\s+$/g, ''); show('manage', true); return; }
    if (action === 'clear-bank-search') { ui.manageQuery = ''; show('manage', true); return; }
    if (action === 'search-questions') { ui.detailQuery = byId('question-search').value.replace(/^\s+|\s+$/g, ''); ui.detailPage = 0; show('bank-detail', true); return; }
    if (action === 'clear-question-search') { ui.detailQuery = ''; ui.detailPage = 0; show('bank-detail', true); return; }
    if (action === 'detail-prev' || action === 'detail-next') { ui.detailPage += action === 'detail-prev' ? -1 : 1; show('bank-detail'); return; }
    if (action === 'cancel-editor') { var destination = editor ? editor.returnView : 'manage'; show(destination); return; }
    if (action === 'validate-editor') { validateEditor(); return; }
    if (action === 'save-editor') { saveEditor(); return; }
    if (action === 'import') { byId('xml-input').click(); return; }
    if (action === 'start') { begin(false); return; }
    if (action === 'exam') { openExam(); return; }
    if (action === 'close-modal') { closeModal(); return; }
    if (action === 'confirm-exam') {
      var r = getRange(); count = Number(byId('exam-count').value);
      if (Math.floor(count) !== count || count < 1 || count > r.end - r.start + 1) { byId('exam-error').textContent = '请填写有效的抽取题数。'; byId('exam-error').hidden = false; return; }
      closeModal(); begin(true, count); return;
    }
    if (action === 'resume') { show('quiz'); return; }
    if (action === 'result') { show('result'); return; }
    if (action === 'option') { choose(element.getAttribute('data-id')); return; }
    if (action === 'submit') { submit(); return; }
    if (action === 'next') { if (state.Active.FeedbackPending) { C.advance(state); persist(); show(state.Active.Completed ? 'result' : 'quiz'); } return; }
    if (action === 'pause') { persist(); show('home'); return; }
    if (action === 'finish') { finish(); return; }
    if (action === 'wrong' || action === 'wrong-current') { if (action === 'wrong-current') ui.bankFilter = state.Active.BankId; ui.wrongPage = 0; show('wrong'); return; }
    if (action === 'filter') {
      count = Number(byId('wrong-minimum').value); if (Math.floor(count) !== count || count < 1) throw new Error('次数请填写大于 0 的整数。');
      ui.minimum = count; ui.bankFilter = byId('wrong-bank').value; ui.wrongPage = 0; show('wrong'); return;
    }
    if (action === 'wrong-prev' || action === 'wrong-next') { ui.wrongPage += action === 'wrong-prev' ? -1 : 1; show('wrong'); return; }
    if (action === 'retry-result') { s = state.Active; beginQuestions(activeBank(), s.Attempts.filter(function (a) { return !a.Correct; }).map(function (a) { return a.QuestionId; })); return; }
    if (action === 'retry-filter') { list = filteredWrongs(); b = bankById(ui.bankFilter) || state.Banks[0]; beginQuestions(b, list.filter(function (w) { return w.BankId === b.Id; }).map(function (w) { return w.QuestionId; })); return; }
    if (action === 'theme') { state.Dark = !state.Dark; applyDisplay(); persist(); return; }
    if (action === 'larger' || action === 'smaller') { state.Zoom = Math.max(0.85, Math.min(1.5, Math.round((state.Zoom + (action === 'larger' ? 0.1 : -0.1)) * 100) / 100)); applyDisplay(); persist(); return; }
    if (action === 'fullscreen') {
      var request = document.documentElement.requestFullscreen || document.documentElement.webkitRequestFullscreen;
      if (document.fullscreenElement || document.webkitFullscreenElement) { var exit = document.exitFullscreen || document.webkitExitFullscreen; if (exit) exit.call(document); }
      else if (request) { var result = request.call(document.documentElement); if (result && result.catch) result.catch(function () { showNotice('当前浏览器未允许全屏。'); }); }
      else showNotice('当前浏览器不支持全屏，可横屏或放大字号。');
      return;
    }
    if (action === 'use-bank') { state.SelectedBankId = element.getAttribute('data-id'); ui.end = 0; persist(); show('home'); return; }
    if (action === 'export-bank') { b = bankById(element.getAttribute('data-id')); download(safeName(b.Name) + '.xml', C.currentXml(b), 'application/xml;charset=utf-8'); return; }
    if (action === 'delete-bank') {
      b = bankById(element.getAttribute('data-id'));
      if (!window.confirm('删除「' + b.Name + '」及其错题次数、相关答题进度？')) return;
      var deletion = copyState(); C.deleteBank(deletion, b.Id);
      commitCandidate(deletion, function () { if (ui.bankFilter === b.Id) { ui.bankFilter = ''; ui.wrongPage = 0; } ui.end = 0; show('manage'); managementNotice('题库已删除。'); }); return;
    }
    if (action === 'backup') { C.validateState(state); download('选择器备份-' + new Date().toISOString().slice(0, 10) + '.json', JSON.stringify(state), 'application/json;charset=utf-8'); }
  }
  function actionElement(node) { while (node && node !== document) { if (node.getAttribute && node.getAttribute('data-act')) return node; node = node.parentNode; } return null; }
  function linkElement(node) { while (node && node !== document) { if (node.tagName === 'A' && node.getAttribute('href')) return node; node = node.parentNode; } return null; }
  document.addEventListener('click', function (event) {
    var link = linkElement(event.target);
    if (working) { if (actionElement(event.target) || link) event.preventDefault(); return; }
    if (link && !link.getAttribute('data-act') && view === 'editor' && !leaveEditor()) { event.preventDefault(); return; }
    var element = actionElement(event.target); if (!element || element.disabled) return;
    event.preventDefault(); try { handle(element.getAttribute('data-act'), element); } catch (error) { showNotice(errorText(error), true); }
  });
  document.addEventListener('input', function (event) { if (working) return; if (event.target.id === 'range-start') ui.start = event.target.value; if (event.target.id === 'range-end') ui.end = event.target.value; if (event.target.id === 'editor-name' || event.target.id === 'editor-xml') { readEditor(); editorMessage('', false); } });
  document.addEventListener('change', function (event) {
    if (!ready || working) return;
    var el = event.target, setting = el.getAttribute('data-setting');
    if (setting) { state[setting] = !!el.checked; persist(); if (view === 'quiz') show('quiz', true); }
    if (el.id === 'bank-picker') { state.SelectedBankId = el.value; ui.end = 0; persist(); show('home', true); }
  });
  byId('xml-input').addEventListener('change', function () { importFiles(this.files); this.value = ''; });
  byId('backup-input').addEventListener('change', function () { restore(this.files[0]); this.value = ''; });
  document.addEventListener('dragover', function (event) { if (event.dataTransfer && event.dataTransfer.types && Array.prototype.indexOf.call(event.dataTransfer.types, 'Files') >= 0) event.preventDefault(); });
  document.addEventListener('drop', function (event) { if (event.dataTransfer && event.dataTransfer.files.length) { event.preventDefault(); importFiles(event.dataTransfer.files); } });
  document.addEventListener('keydown', function (event) {
    if (working) { event.preventDefault(); return; }
    if (!modal.hidden) {
      if (event.key === 'Escape' || event.keyCode === 27) { closeModal(); event.preventDefault(); }
      if (event.key === 'Enter' || event.keyCode === 13) { try { handle(modalKind === 'rename' ? 'confirm-rename' : 'confirm-exam'); } catch (error) { showNotice(errorText(error), true); } event.preventDefault(); }
      if ((event.key === 'Tab' || event.keyCode === 9)) {
        var controls = modal.querySelectorAll('input,textarea,button'), first = controls[0], last = controls[controls.length - 1];
        if (event.shiftKey && document.activeElement === first) { last.focus(); event.preventDefault(); }
        else if (!event.shiftKey && document.activeElement === last) { first.focus(); event.preventDefault(); }
      }
      return;
    }
    if ((event.key === 'Enter' || event.keyCode === 13) && event.target.id === 'bank-search') { handle('search-banks'); event.preventDefault(); return; }
    if ((event.key === 'Enter' || event.keyCode === 13) && event.target.id === 'question-search') { handle('search-questions'); event.preventDefault(); return; }
    if (!ready || working || view !== 'quiz' || event.ctrlKey || event.altKey || event.metaKey || /^(INPUT|TEXTAREA|SELECT)$/.test(event.target.tagName)) return;
    var key = String(event.key || String.fromCharCode(event.keyCode)).toUpperCase(), s = state.Active;
    if (key === 'ENTER' || event.keyCode === 13) { event.preventDefault(); if (s.FeedbackPending) handle('next'); else submit(); return; }
    if (s.FeedbackPending || s.Completed) return;
    var q = C.current(activeBank(), s), id = q.Options.filter(function (o) { return o.Id === key; })[0], digit = Number(key);
    if (id) { choose(id.Id); event.preventDefault(); }
    else if (/^[1-9]$/.test(key) && digit <= q.Options.length) { choose(q.Options[digit - 1].Id); event.preventDefault(); }
  });
  window.addEventListener('hashchange', function () { if (ready && !working && location.hash === '#home') { persist(); show('home'); } });
  window.addEventListener('beforeunload', function (event) { if (working || editorDirty()) { event.preventDefault(); event.returnValue = ''; } });
  function getDefault() {
    return new Promise(function (resolve, reject) {
      var request = new XMLHttpRequest(); request.open('GET', 'default.xml', true); request.timeout = 10000;
      request.onload = function () { if (request.status >= 200 && request.status < 300) resolve(request.responseText); else reject(new Error('首次载入题库失败，可联网重试或自行导入 XML。')); };
      request.onerror = request.ontimeout = function () { reject(new Error('未能载入默认题库，可自行导入 XML。')); }; request.send();
    });
  }
  function boot() {
    var initialWarning = '';
    if (!C || !window.ChooserStorage || !window.Promise) { showNotice('当前浏览器过旧，请使用支持 IndexedDB 的 Chrome、Edge 或 Firefox。', true); return; }
    window.ChooserStorage.open().then(function (opened) { store = opened; return store.load(); }).then(function (saved) {
      if (saved) return saved;
      return getDefault().then(function (xml) { var fresh = C.createState(), bank = C.parseXml(xml, '2025新版大信_选择题.xml'); fresh.Banks.push(bank); fresh.SelectedBankId = bank.Id; return fresh; }, function (error) { initialWarning = errorText(error); return C.createState(); });
    }).then(function (loaded) {
      state = C.validateState(loaded); ready = true; applyDisplay(); show('home');
      return persist().then(function (saved) { if (saved && (store.warning || initialWarning)) showNotice([initialWarning, store.warning].filter(function (x) { return !!x; }).join(' '), store.kind === 'memory'); });
    }).catch(function (error) {
      // A broken cache is never silently replaced with an empty one.
      showNotice(errorText(error), true);
      app.innerHTML = '<section class="card"><h1>题库选择器</h1><div class="actions">' + button('重试读取', 'reload', true) + button('恢复数据备份', 'restore', false) + '</div></section>';
    });
    if ('serviceWorker' in navigator && (location.protocol === 'https:' || location.hostname === 'localhost' || location.hostname === '127.0.0.1')) {
      window.addEventListener('load', function () { navigator.serviceWorker.register('./sw.js').catch(function () { /* IndexedDB and ordinary HTTP cache remain available. */ }); });
    }
  }
  boot();
}());
