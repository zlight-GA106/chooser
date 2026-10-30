using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Chooser
{
    internal static class ManagementCoreTests
    {
        private static string root, source;
        private static readonly List<string> lines = new List<string>();
        private static int passed;
        private static void Check(bool condition, string text) { if (!condition) throw new Exception(text); }
        private static void Test(string title, Action body) { body(); passed++; lines.Add("PASS " + title); }
        private static string Json(AppState state) { using (var stream = new MemoryStream()) { new DataContractJsonSerializer(typeof(AppState)).WriteObject(stream, state); return Encoding.UTF8.GetString(stream.ToArray()); } }
        private static void Reject(Action action) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, "本应拒绝的操作被接受。"); }
        private static AppState Fresh()
        {
            var state = new AppState(); Bank bank = BankManager.Create(state, "原始题库", source);
            state.ImmediateScoring = true;
            state.Active = QuizEngine.CreateSession(bank, 1, 2, 2, false, "测试");
            QuizEngine.Submit(state, state.Active, bank, new List<string> { "A" });
            QuizEngine.AdvanceFeedback(state.Active);
            QuizEngine.Submit(state, state.Active, bank, new List<string> { "B" });
            return state;
        }
        public static int Main(string[] args)
        {
            root = args.Length > 0 ? args[0] : Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
            source = File.ReadAllText(Path.Combine(root, "assets", "example.xml"), Encoding.UTF8);
            int status = 0;
            try
            {
                Test("新建题库并选择，名称写入可重新导入 XML", delegate {
                    var state = new AppState(); Bank bank = BankManager.Create(state, "新建 & \"标题\"", source);
                    Check(state.Banks.Count == 1 && state.SelectedBankId == bank.Id && bank.Questions.Count == 3, "新建内容或选中状态错误。");
                    Check(XmlQuiz.Parse(bank.Xml, "sample.xml").Name == bank.Name, "名称未安全写入 XML。");
                });
                Test("重命名保留题库/题目标识、错题和未离开的评分", delegate {
                    AppState state = Fresh(); Bank old = state.Banks[0]; string oldId = old.Id, firstQuestion = old.Questions[0].Id;
                    var active = state.Active; Bank bank = BankManager.Rename(state, oldId, "重命名 & \"题库\"");
                    Check(bank.Id == oldId && bank.Questions[0].Id == firstQuestion && state.Wrongs.Count == 2 && state.Wrongs.Sum(w => w.Count) == 2, "重命名改变了身份或错题。");
                    Check(Object.ReferenceEquals(active, state.Active) && state.Active.FeedbackPending && state.Active.Completed, "重命名丢失即时评分。");
                    Check(XmlQuiz.Parse(bank.Xml, "export.xml").Name == bank.Name, "重命名导出失效。");
                });
                Test("编辑未参与本轮的题目，保留本轮进度并原子重映射题库标识", delegate {
                    AppState state = Fresh(); Bank old = state.Banks[0]; string oldId = old.Id; string changed = source.Replace("地球", "火星");
                    Check(!BankManager.RequiresProgressReset(state, oldId, changed), "无关题目误报影响进度。");
                    Bank bank = BankManager.Replace(state, oldId, "修改第三题", changed);
                    Check(bank.Id != oldId && bank.ImportedAt == old.ImportedAt && state.SelectedBankId == bank.Id && state.Active.BankId == bank.Id, "题库重映射/日期错误。");
                    Check(state.Wrongs.Count == 2 && state.Wrongs.All(w => w.BankId == bank.Id) && state.Active.FeedbackPending, "保留错题/评分失败。");
                    Roundtrip(state);
                });
                Test("编辑本轮已答题，清除受影响进度并仅移除已改变题的错题", delegate {
                    AppState state = Fresh(); string oldId = state.Banks[0].Id; string changed = source.Replace("偶数", "正偶数");
                    Check(BankManager.RequiresProgressReset(state, oldId, changed), "未识别已答题变化。");
                    Bank bank = BankManager.Replace(state, oldId, "修改第二题", changed);
                    Check(state.Active == null && state.Wrongs.Count == 1 && state.Wrongs[0].QuestionId == bank.Questions[0].Id && state.Wrongs[0].BankId == bank.Id, "未正确过滤受影响的记录。");
                    Roundtrip(state);
                });
                Test("重复名称、重复内容、非法 XML 和非法名称不会部分修改", delegate {
                    AppState state = Fresh(); Bank old = state.Banks[0];
                    BankManager.Create(state, "Other", source.Replace("地球", "水星"));
                    string before = Json(state);
                    Reject(delegate { BankManager.Rename(state, old.Id, "other"); }); Check(Json(state) == before, "重复名称改变状态。");
                    Reject(delegate { BankManager.Create(state, "内容重复", source); }); Check(Json(state) == before, "重复内容改变状态。");
                    Reject(delegate { BankManager.Replace(state, old.Id, "坏 XML", "<quiz><question></quiz>"); }); Check(Json(state) == before, "无效 XML 改变状态。");
                    Reject(delegate { BankManager.Replace(state, old.Id, "Other", source); }); Check(Json(state) == before, "重复名称替换改变状态。");
                    Reject(delegate { BankManager.Create(state, "", source); });
                    Reject(delegate { BankManager.Create(state, new string('名', 121), source); });
                    Reject(delegate { BankManager.Create(state, "含\n换行", source); });
                    Reject(delegate { BankManager.Create(state, "空题库", "<quiz/>"); });
                    Check(Json(state) == before, "拒绝后的缓存状态变化。");
                });
                Test("名称覆盖安全且保留注释、根属性、未知题目元数据", delegate {
                    string xml = source.Replace("<quiz", "<!-- original note --><quiz custom=\"kept\"").Replace("<answer>B</answer>", "<source>资料 &amp; 引用</source><answer>B</answer>");
                    Bank bank = BankManager.Preview("Guide & \"Name\"", xml);
                    Check(bank.Xml.Contains("original note") && bank.Xml.Contains("custom=\"kept\"") && bank.Xml.Contains("资料 &amp; 引用"), "XML 元数据丢失。");
                    Check(XmlQuiz.Parse(bank.Xml, "x.xml").Name == "Guide & \"Name\"", "标题转义无效。");
                    Bank upper = BankManager.Preview("新名", source.Replace("title=", "TITLE=")); Check(XmlQuiz.Parse(upper.Xml, "x.xml").Name == "新名", "大写 TITLE 未被覆盖。");
                });
                Test("删除选中题库并清除关联记录，其他题库保留", delegate {
                    AppState state = Fresh(); Bank old = state.Banks[0]; Bank second = BankManager.Create(state, "第二库", source.Replace("地球", "木星"));
                    state.SelectedBankId = old.Id; BankManager.Delete(state, old.Id);
                    Check(state.Banks.Count == 1 && state.Banks[0].Id == second.Id && state.Wrongs.Count == 0 && state.Active == null && state.SelectedBankId == second.Id, "删除关联数据错误。");
                    BankManager.Delete(state, second.Id); Check(state.Banks.Count == 0 && state.SelectedBankId == "", "删除最后一库错误。"); Roundtrip(state);
                });
                Test("预览不修改数据，旧备份缺少 XML 时仍可重命名导出", delegate {
                    AppState state = Fresh(); string before = Json(state); BankManager.Preview("预览", source); Check(Json(state) == before, "预览修改状态。");
                    Bank old = state.Banks[0]; old.Xml = ""; string oldId = old.Id; string questionId = old.Questions[0].Id;
                    Bank renamed = BankManager.Rename(state, old.Id, "旧备份新名"); Check(renamed.Id == oldId && renamed.Questions[0].Id == questionId && XmlQuiz.Parse(renamed.Xml, "x.xml").Questions.Count == 3, "旧备份重命名不兼容。");
                });
                // This small fixture lets browser tests compare IDs with the native implementation.
                var parity = BankManager.Preview("跨平台核对", source);
                File.WriteAllText(Path.Combine(root, "tests", "management-parity.json"), "{\"BankId\":\"" + parity.Id + "\",\"QuestionIds\":[" + String.Join(",", parity.Questions.Select(q => "\"" + q.Id + "\"")) + "]}", new UTF8Encoding(false));
            }
            catch (Exception error) { status = 1; lines.Add("FAIL " + error); }
            lines.Add("结果：" + passed + " 项通过；" + (status == 0 ? "0 项失败。" : "存在失败。"));
            File.WriteAllLines(Path.Combine(root, "tests", "management-core-report.txt"), lines, new UTF8Encoding(true));
            Console.WriteLine(String.Join(Environment.NewLine, lines)); return status;
        }
        private static void Roundtrip(AppState state)
        {
            string directory = Path.Combine(Path.GetTempPath(), "ChooserManagement-" + Guid.NewGuid().ToString("N"));
            try { var store = new Store(directory); store.Save(state); var reloaded = store.Load(); Check(reloaded.Banks.Count == state.Banks.Count && reloaded.Wrongs.Count == state.Wrongs.Count && reloaded.SelectedBankId == state.SelectedBankId, "重开数据不一致。"); }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        }
    }
}
