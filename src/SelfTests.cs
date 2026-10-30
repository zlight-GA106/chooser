using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Chooser
{
    public static class SelfTests
    {
        private const string TwoQuestions = "<quiz title='测试题库'><question score='999' type='multiple_choice'><text>第一题：选 A</text><option id='A'>甲</option><option id='B'>乙</option><answer>A</answer></question><question score='7'><text>第二题：选 B</text><option id='A'>丙</option><option id='B'>丁</option><answer>B</answer></question></quiz>";

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }

        private static void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); }
            catch { rejected = true; }
            Check(rejected, message);
        }

        private static List<string> Answer(params string[] values)
        {
            return values.ToList();
        }

        private static AppState State(Bank bank)
        {
            AppState state = new AppState();
            state.Banks.Add(bank);
            state.SelectedBankId = bank.Id;
            return state;
        }

        private static Session Session(AppState state, Bank bank, int start, int end)
        {
            Session session = QuizEngine.CreateSession(bank, start, end, end - start + 1, false, "practice");
            state.Active = session;
            return session;
        }

        public static int Run(string reportPath, string samplePath)
        {
            List<string> report = new List<string>();
            string temporary = Path.Combine(Path.GetTempPath(), "ChooserTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporary);
            int passed = 0;
            int failed = 0;
            Action<string, Action> test = delegate(string name, Action body)
            {
                try
                {
                    body();
                    passed++;
                    report.Add("PASS  " + name);
                }
                catch (Exception ex)
                {
                    failed++;
                    report.Add("FAIL  " + name + "\r\n      " + ex.GetType().Name + ": " + ex.Message);
                }
            };

            report.Add("Chooser 核心验证");
            report.Add("执行时间: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.Add("");

            try
            {
                test("实际题库：196 题，161 道四选项题和 35 道 T/F 判断题", delegate
                {
                    Bank bank = XmlQuiz.ParseFile(samplePath);
                    Check(bank.Questions.Count == 196, "实际题库题数不符。");
                    Check(bank.Questions.Count(q => q.Options.Count == 4) == 161, "四选项题数不符。");
                    List<Question> judgments = bank.Questions.Where(q => q.Options.Count == 2).ToList();
                    Check(judgments.Count == 35, "判断题数不符。");
                    Check(judgments.All(q => q.Options.Select(o => o.Id).OrderBy(x => x).SequenceEqual(new[] { "F", "T" })), "T/F 选项丢失。");
                    Check(judgments.All(q => q.Answers.Count == 1 && q.Options.Any(o => o.Id == q.Answers[0])), "判断题答案无效。");
                    Check(bank.Questions.Select(q => q.Id).Distinct().Count() == 196, "题目 ID 发生冲突。");
                });

                test("内容 ID 稳定：相同 XML 改名不重复导入", delegate
                {
                    Bank first = XmlQuiz.Parse(TwoQuestions, "first.xml");
                    Bank second = XmlQuiz.Parse(TwoQuestions, "second.xml");
                    Check(first.Id == second.Id, "题库 ID 依赖文件名。");
                    Check(first.Questions.Select(q => q.Id).SequenceEqual(second.Questions.Select(q => q.Id)), "题目 ID 不稳定。");
                    Check(first.Name == "测试题库", "题库标题未保留。");
                });

                test("重复同内容题目 ID 各不相同，重新导入仍然稳定", delegate
                {
                    string question = "<question><text>完全相同的题目</text><option id='A'>甲</option><option id='B'>乙</option><answer>A</answer></question>";
                    string xml = "<quiz>" + question + question + question + "</quiz>";
                    Bank first = XmlQuiz.Parse(xml, "duplicates.xml");
                    Bank second = XmlQuiz.Parse(xml, "renamed.xml");
                    Check(first.Questions.Count == 3 && first.Questions.Select(q => q.Id).Distinct().Count() == 3, "重复题内容造成 ID 冲突或丢题。");
                    Check(first.Questions.Select(q => q.Id).SequenceEqual(second.Questions.Select(q => q.Id)), "重复题 ID 重新导入后改变。");
                    AppState state = State(first);
                    Session session = Session(state, first, 1, 3);
                    QuizEngine.Submit(state, session, first, Answer("A"));
                    QuizEngine.Submit(state, session, first, Answer("A"));
                    QuizEngine.Submit(state, session, first, Answer("A"));
                    Check(QuizEngine.Score(session) == 3 && session.Completed, "重复题无法分别答题计分。");
                });

                test("UTF-16 文件按声明读取，缓存 XML 可按 UTF-8 导出重导入", delegate
                {
                    string original = Path.Combine(temporary, "utf16.xml");
                    string exported = Path.Combine(temporary, "exported-utf8.xml");
                    File.WriteAllText(original, "<?xml version='1.0' encoding='utf-16'?>" + TwoQuestions, Encoding.Unicode);
                    Bank loaded = XmlQuiz.ParseFile(original);
                    Check(loaded.Questions[0].Text.Contains("第一题"), "UTF-16 中文题干解析错误。");
                    File.WriteAllText(exported, loaded.Xml, new UTF8Encoding(false));
                    Bank reimported = XmlQuiz.ParseFile(exported);
                    Check(loaded.Id == reimported.Id && loaded.Questions.Select(q => q.Text).SequenceEqual(reimported.Questions.Select(q => q.Text)), "缓存按 UTF-8 导出后无法重导入。");
                });

                test("多选按答案集合判分；少选、多选均不得分", delegate
                {
                    Bank bank = XmlQuiz.Parse("<quiz><question score='99'><text>多选题</text><option id='P1'>甲</option><option id='P2'>乙</option><option id='P3'>丙</option><answer>P1,P3</answer><analysis>答案解析</analysis></question></quiz>", "multi.xml");
                    Check(bank.Questions[0].Answers.Count == 2, "任意选项 ID 未正确解析。");
                    Check(bank.Questions[0].Explanation == "答案解析", "analysis 解析未保留。");
                    AppState state = State(bank);
                    Session partial = Session(state, bank, 1, 1);
                    QuizEngine.Submit(state, partial, bank, Answer("P1"));
                    Check(QuizEngine.Score(partial) == 0 && state.Wrongs.Single().Count == 1, "少选误判正确。");
                    Session extra = Session(state, bank, 1, 1);
                    QuizEngine.Submit(state, extra, bank, Answer("P1", "P2", "P3"));
                    Check(QuizEngine.Score(extra) == 0 && state.Wrongs.Single().Count == 2, "多选误判正确。");
                    Session complete = Session(state, bank, 1, 1);
                    QuizEngine.Submit(state, complete, bank, Answer("P3", "P1"));
                    Check(QuizEngine.Score(complete) == 1, "答案顺序影响集合判分。");
                    Check(state.Wrongs.Single().Count == 2, "答对后清零了累计错题次数。");
                });

                test("T/F 答题且每题固定 1 分，忽略 XML 原始分值", delegate
                {
                    Bank bank = XmlQuiz.Parse("<quiz><question score='999'><text>判断</text><option id='T'>正确</option><option id='F'>错误</option><answer>F</answer></question></quiz>", "judgment.xml");
                    AppState state = State(bank);
                    Session session = Session(state, bank, 1, 1);
                    QuizEngine.Submit(state, session, bank, Answer("F"));
                    Check(QuizEngine.Score(session) == 1 && session.Completed, "判断题判分或结束状态错误。");
                    Check(state.Wrongs.Count == 0, "答对记录为错题。");
                    Bank normal = XmlQuiz.Parse(TwoQuestions, "normal.xml");
                    AppState normalState = State(normal);
                    Session normalSession = Session(normalState, normal, 1, 2);
                    QuizEngine.Submit(normalState, normalSession, normal, Answer("A"));
                    QuizEngine.Submit(normalState, normalSession, normal, Answer("B"));
                    Check(QuizEngine.Score(normalSession) == 2, "XML 分值改变了每题 1 分规则。");
                });

                test("非法题库全部拒绝：缺题干、少选项、缺答案、非法答案和重复 ID", delegate
                {
                    string[] invalid = {
                        "<quiz></quiz>",
                        "<quiz><question><option id='A'>甲</option><option id='B'>乙</option><answer>A</answer></question></quiz>",
                        "<quiz><question><text>题目</text><option id='A'>甲</option><answer>A</answer></question></quiz>",
                        "<quiz><question><text>题目</text><option id='A'>甲</option><option id='B'>乙</option></question></quiz>",
                        "<quiz><question><text>题目</text><option id='A'>甲</option><option id='B'>乙</option><answer>Z</answer></question></quiz>",
                        "<quiz><question><text>题目</text><option id='A'>甲</option><option id='A'>乙</option><answer>A</answer></question></quiz>",
                        "<quiz><question><text>题目</text><option id='A'>甲</option><option id='B'>乙</option><answer>A,Z</answer></question></quiz>",
                        "<quiz><question><text>未闭合</text>"
                    };
                    foreach (string xml in invalid)
                        Reject(delegate { XmlQuiz.Parse(xml, "invalid.xml"); }, "非法题目被接受：" + xml);
                });

                test("XML 禁止 DTD 和外部实体，不访问外部资源", delegate
                {
                    string xml = "<!DOCTYPE quiz [<!ENTITY xxe SYSTEM 'file:///C:/Windows/win.ini'>]><quiz><question><text>&xxe;</text><option id='A'>甲</option><option id='B'>乙</option><answer>A</answer></question></quiz>";
                    Reject(delegate { XmlQuiz.Parse(xml, "entity.xml"); }, "带 DTD 的 XML 被接受。");
                });

                test("题号范围校验和随机抽题不重复", delegate
                {
                    Bank bank = XmlQuiz.ParseFile(samplePath);
                    Reject(delegate { QuizEngine.CreateSession(bank, 0, 20, 10, true, "exam"); }, "接受起始题号 0。");
                    Reject(delegate { QuizEngine.CreateSession(bank, 20, 10, 2, true, "exam"); }, "接受反向范围。");
                    Reject(delegate { QuizEngine.CreateSession(bank, 1, 197, 2, true, "exam"); }, "接受越界范围。");
                    Reject(delegate { QuizEngine.CreateSession(bank, 1, 10, 11, true, "exam"); }, "接受超过范围的抽题数量。");
                    for (int i = 0; i < 12; i++)
                    {
                        Session session = QuizEngine.CreateSession(bank, 20, 60, 30, true, "exam");
                        Check(session.QuestionIds.Count == 30 && session.QuestionIds.Distinct().Count() == 30, "随机抽题数量或去重错误。");
                        Check(session.QuestionIds.All(id => bank.Questions.Single(q => q.Id == id).Number >= 20 && bank.Questions.Single(q => q.Id == id).Number <= 60), "抽到了指定范围外的题目。");
                    }
                    Session ordered = QuizEngine.CreateSession(bank, 40, 43, 4, false, "practice");
                    Check(ordered.QuestionIds.SequenceEqual(bank.Questions.Skip(39).Take(4).Select(q => q.Id)), "顺序练习题号不符。");
                });

                test("空答案和非法选项不提交、不推进进度", delegate
                {
                    Bank bank = XmlQuiz.Parse(TwoQuestions, "test.xml");
                    AppState state = State(bank);
                    Session session = Session(state, bank, 1, 2);
                    Reject(delegate { QuizEngine.Submit(state, session, bank, Answer()); }, "空答案被提交。");
                    Reject(delegate { QuizEngine.Submit(state, session, bank, Answer("Z")); }, "未知选项被提交。");
                    Check(session.Position == 0 && session.Attempts.Count == 0 && state.Wrongs.Count == 0, "无效提交改变了进度或错题。");
                });

                test("保存、重启、恢复草稿后答题：错题次数恰好累计一次", delegate
                {
                    string dir = Path.Combine(temporary, "resume");
                    Bank bank = XmlQuiz.Parse(TwoQuestions, "test.xml");
                    AppState state = State(bank);
                    Session session = Session(state, bank, 1, 2);
                    QuizEngine.Submit(state, session, bank, Answer("B"));
                    session.Draft = Answer("A");
                    Store store = new Store(dir);
                    store.Save(state);
                    AppState loaded = new Store(dir).Load();
                    Bank loadedBank = loaded.Banks.Single();
                    Check(loaded.Active.Position == 1 && loaded.Active.Attempts.Count == 1 && loaded.Active.Draft.SequenceEqual(new[] { "A" }), "重启后进度或草稿丢失。");
                    Check(QuizEngine.Current(loadedBank, loaded.Active).Number == 2, "续答题号错误。");
                    QuizEngine.Submit(loaded, loaded.Active, loadedBank, loaded.Active.Draft.ToList());
                    Check(loaded.Active.Completed && loaded.Active.Attempts.Count == 2, "最终提交未结束答题。");
                    Check(loaded.Wrongs.Count == 2 && loaded.Wrongs.All(w => w.Count == 1), "保存/恢复使错题重复计数。");
                    Check(loaded.Active.Draft.Count == 0, "提交后草稿未清理。");
                    Reject(delegate { QuizEngine.Submit(loaded, loaded.Active, loadedBank, Answer("A")); }, "结算后再次提交被接受。");
                    Check(loaded.Wrongs.All(w => w.Count == 1), "结算后重复提交增加了错题次数。");
                    store.Save(loaded);
                    Check(new Store(dir).Load().Active.Completed, "结束状态未持久化。");
                });

                test("同题反复答错累计，后来答对保留次数；不同题库分别统计", delegate
                {
                    Bank bank = XmlQuiz.Parse(TwoQuestions, "one.xml");
                    AppState state = State(bank);
                    for (int i = 0; i < 3; i++)
                    {
                        Session session = Session(state, bank, 1, 1);
                        QuizEngine.Submit(state, session, bank, Answer("B"));
                    }
                    Check(state.Wrongs.Single().Count == 3, "重复错题未累计。");
                    Session correct = Session(state, bank, 1, 1);
                    QuizEngine.Submit(state, correct, bank, Answer("A"));
                    Check(state.Wrongs.Single().Count == 3, "后续答对改变了累计错误次数。");
                    Bank second = new Bank { Id = "independent-bank", Name = "另一个题库", Questions = bank.Questions, Xml = bank.Xml, ImportedAt = bank.ImportedAt };
                    state.Banks.Add(second);
                    Session other = Session(state, second, 1, 1);
                    QuizEngine.Submit(state, other, second, Answer("B"));
                    Check(state.Wrongs.Count == 2 && state.Wrongs.Single(w => w.BankId == second.Id).Count == 1, "不同题库的同题统计串联。");
                });

                test("提前结算持久化：仅统计已答题目，不把未答题目计入成绩", delegate
                {
                    string dir = Path.Combine(temporary, "early-complete");
                    Bank bank = XmlQuiz.Parse(TwoQuestions, "early.xml");
                    AppState state = State(bank);
                    Session session = Session(state, bank, 1, 2);
                    QuizEngine.Submit(state, session, bank, Answer("A"));
                    session.Completed = true;
                    new Store(dir).Save(state);
                    AppState loaded = new Store(dir).Load();
                    Check(loaded.Active.Completed && loaded.Active.Position == 1 && loaded.Active.QuestionIds.Count == 2, "提前结算状态未保存。");
                    Check(loaded.Active.Attempts.Count == 1 && QuizEngine.Score(loaded.Active) == 1, "未答题目进入提前结算成绩。");
                    Check(loaded.Wrongs.Count == 0, "未答题目进入错题本。");
                    Reject(delegate { QuizEngine.Submit(loaded, loaded.Active, loaded.Banks.Single(), Answer("B")); }, "提前结算后仍可提交。");
                });

                test("原子保存和备份恢复：主文件损坏可恢复并提示", delegate
                {
                    string dir = Path.Combine(temporary, "backup");
                    Bank bank = XmlQuiz.Parse(TwoQuestions, "save.xml");
                    AppState state = State(bank);
                    state.Dark = false;
                    Store store = new Store(dir);
                    store.Save(state);
                    state.Dark = true;
                    store.Save(state);
                    Check(File.Exists(Path.Combine(dir, "state.json.bak")), "未生成备份文件。");
                    File.WriteAllText(Path.Combine(dir, "state.json"), "{broken", Encoding.UTF8);
                    Store recovery = new Store(dir);
                    AppState recovered = recovery.Load();
                    Check(recovered.Banks.Count == 1 && !recovered.Dark, "没有从上一个有效备份恢复。");
                    Check(!String.IsNullOrWhiteSpace(recovery.LoadWarning), "备份恢复没有提示。");
                    recovery.Save(recovered);
                    Check(new Store(dir).Load().Banks.Count == 1, "恢复后的保存仍不可读取。");
                });

                test("主文件和备份均损坏时明确失败，保留损坏文件", delegate
                {
                    string dir = Path.Combine(temporary, "corrupt");
                    Directory.CreateDirectory(dir);
                    string primary = Path.Combine(dir, "state.json");
                    string backup = Path.Combine(dir, "state.json.bak");
                    File.WriteAllText(primary, "broken-primary", Encoding.UTF8);
                    File.WriteAllText(backup, "broken-backup", Encoding.UTF8);
                    Reject(delegate { new Store(dir).Load(); }, "双份损坏时被静默重置。");
                    Check(File.ReadAllText(primary).Contains("broken-primary") && File.ReadAllText(backup).Contains("broken-backup"), "损坏数据被覆盖。");
                });
            }
            finally
            {
                try { Directory.Delete(temporary, true); }
                catch (Exception ex) { report.Add("清理测试临时目录失败: " + ex.Message); }
            }

            report.Add("");
            report.Add("结果: " + passed + " 项通过，" + failed + " 项失败。");
            string directory = Path.GetDirectoryName(Path.GetFullPath(reportPath));
            Directory.CreateDirectory(directory);
            File.WriteAllLines(reportPath, report, new UTF8Encoding(true));
            return failed == 0 ? 0 : 1;
        }
    }
}
