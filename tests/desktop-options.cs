using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Chooser
{
    public static class DesktopOptions
    {
        static readonly List<string> report = new List<string>();
        static MainWindow window;
        static AppState state;
        static Store store;
        static string directory, root;
        static int count;
        static Exception dispatcherError;

        [STAThread]
        public static int Main(string[] args)
        {
            root = args[0]; directory = Path.Combine(Path.GetTempPath(), "ChooserOptions-" + Guid.NewGuid().ToString("N"));
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { dispatcherError = e.Exception; e.Handled = true; };
            int code = 0;
            try
            {
                var bank = XmlQuiz.ParseFile(Path.Combine(root, "tests", "ui-fixture.xml"));
                state = new AppState(); state.Banks.Add(bank); state.SelectedBankId = bank.Id;
                store = new Store(directory); store.Save(state);
                Test("旧缓存兼容：缺少新字段时，两项选项和评分状态默认关闭", delegate {
                    string file = Path.Combine(directory, "state.json");
                    string json = File.ReadAllText(file);
                    json = Regex.Replace(json, @"""(?:ShowAnswers|ImmediateScoring)"":false,?", "");
                    json = json.Replace(",}", "}"); File.WriteAllText(file, json, new UTF8Encoding(false));
                    state = store.Load(); Check(!state.ShowAnswers && !state.ImmediateScoring, "旧缓存默认选项不为关闭。");
                    state.Active = QuizEngine.CreateSession(state.Banks[0], 1, 1, 1, false, "兼容检查");
                    store.Save(state); json = File.ReadAllText(file);
                    json = Regex.Replace(json, @"""FeedbackPending"":false,?", "").Replace(",}", "}");
                    File.WriteAllText(file, json, new UTF8Encoding(false)); state = store.Load();
                    Check(!state.Active.FeedbackPending, "旧进度默认评分状态不为关闭。"); state.Active = null; store.Save(state);
                    window = new MainWindow(store, state);
                });
                Test("首页两个选项可独立勾选并持久保存", delegate {
                    Find<CheckBox>().Single(c => (string)c.Content == "显示答案").IsChecked = true;
                    Check(state.ShowAnswers && store.Load().ShowAnswers && !state.ImmediateScoring, "显示答案未独立保存。");
                    Find<CheckBox>().Single(c => (string)c.Content == "立即评分").IsChecked = true;
                    Check(state.ImmediateScoring && store.Load().ImmediateScoring, "立即评分未保存。"); Render("desktop-home-options");
                });
                Test("显示答案：提交前显示标准答案及解析，不增加得分或错题", delegate {
                    Field<TextBox>("rangeEnd").Text = "3"; Call("Start", false);
                    Check(AllText().Contains("正确答案：B") && AllText().Contains("2 + 2 = 4"), "题目未展示答案或解析。");
                    Check(state.Active.Position == 0 && state.Active.Attempts.Count == 0 && state.Wrongs.Count == 0, "仅展示答案就被计分。");
                    Check((string)Field<Button>("nextButton").Content == "提交答案  →", "立即评分的提交按钮不符。");
                });
                Test("立即评分：同题提交一次后锁定，显示 0 分及正确答案", delegate {
                    Call("SelectOption", "A"); Call("SubmitCurrent");
                    Check(state.Active.FeedbackPending && state.Active.Position == 1 && !state.Active.Completed, "未停留于本题评分。");
                    Check(QuizEngine.Displayed(state.Banks[0], state.Active).Number == 1 && AllText().Contains("回答错误  0 分") && AllText().Contains("正确答案：B"), "反馈题目或分数不符。");
                    Check(Find<Button>().Where(b => Field<Dictionary<string, Button>>("optionButtons").Values.Contains(b)).All(b => !b.IsHitTestVisible && !b.Focusable), "反馈选项未锁定。");
                    Call("SelectOption", "B"); Check(state.Active.Draft.Count == 0 && state.Wrongs.Single().Count == 1, "反馈期间可修改答案。");
                    bool rejected = false; try { QuizEngine.Submit(state, state.Active, state.Banks[0], new List<string> { "B" }); } catch (InvalidOperationException) { rejected = true; }
                    Check(rejected && state.Active.Attempts.Count == 1, "引擎接受评分期间再次提交。"); Render("desktop-feedback-wrong");
                });
                Test("关闭重开后恢复同一评分；关闭立即评分选项也不丢失已提交反馈", delegate {
                    Call("ShowHome"); window.Close(); state = store.Load(); window = new MainWindow(store, state);
                    Find<CheckBox>().Single(c => (string)c.Content == "立即评分").IsChecked = false;
                    Call("ShowQuestion"); Check(state.Active.FeedbackPending && AllText().Contains("回答错误  0 分"), "重开或关选项丢失本题反馈。");
                    Call("SubmitCurrent"); Check(!state.Active.FeedbackPending && state.Active.Position == 1 && state.Active.Attempts.Count == 1, "下一题重复计分。");
                    Call("SubmitCurrent"); Check(state.Active.Position == 1 && state.Active.Attempts.Count == 1 && state.Wrongs.Single().Count == 1, "无草稿连点后重复作答。");
                });
                Test("关闭立即评分保留旧流程；多选正确按 1 分，显示答案可独立关闭", delegate {
                    state.ShowAnswers = false; Call("ShowQuestion"); Check(!AllText().Contains("正确答案："), "关闭显示答案后仍在未提交题显示答案。");
                    Call("SelectOption", "A"); Call("SelectOption", "C"); Call("SubmitCurrent");
                    Check(state.Active.Position == 2 && !state.Active.FeedbackPending && QuizEngine.Score(state.Active) == 1, "关闭立即评分的多选判分或跳题不符。");
                    Check(!AllText().Contains("正确答案："), "普通答题泄露标准答案。");
                });
                Test("最后一题完成后仍显示反馈，重开可续看，Enter 查看成绩且不重复计分", delegate {
                    state.ImmediateScoring = true; Call("SelectOption", "T"); Call("SubmitCurrent");
                    Check(state.Active.Completed && state.Active.FeedbackPending && (string)Field<Button>("nextButton").Content == "查看成绩  →", "最后题没有评分停留。");
                    Check(AllText().Contains("回答正确  +1 分") && AllText().Contains("当前得分 2 / 3"), "最后题即时分数不符。"); Render("desktop-feedback-last");
                    window.Close(); state = store.Load(); window = new MainWindow(store, state);
                    Check(Find<Button>().Any(b => b.Content is string && (string)b.Content == "继续上次进度"), "已完成待评分没有续看入口。");
                    Call("ShowQuestion"); Check(AllText().Contains("第 3 题 / 3") && state.Active.FeedbackPending, "恢复不是最后一道题。");
                    var handle = new WindowInteropHelper(window).EnsureHandle();
                    var key = new KeyEventArgs(Keyboard.PrimaryDevice, HwndSource.FromHwnd(handle), 0, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
                    Call("OnKey", window, key);
                    Check(key.Handled && !state.Active.FeedbackPending && Field<string>("view") == "result", "最后反馈 Enter 未进入结果。");
                    Call("SubmitCurrent"); Check(state.Active.Attempts.Count == 3 && state.Wrongs.Single().Count == 1 && QuizEngine.Score(state.Active) == 2, "结果再次提交重复评分。");
                });
                Test("无效评分缓存被拒绝，已保存数据保持有效", delegate {
                    AppState invalid = store.Load(); invalid.Active = QuizEngine.CreateSession(invalid.Banks[0], 1, 3, 3, false, "验证"); invalid.Active.FeedbackPending = true;
                    Reject(invalid); invalid.Active.Position = 1; invalid.Active.Attempts.Add(new Attempt { QuestionId = invalid.Active.QuestionIds[0], Selected = new List<string> { "B" }, Correct = true }); invalid.Active.Draft.Add("A"); Reject(invalid);
                    Check(store.Load().Active.Completed && store.Load().Active.Attempts.Count == 3, "无效缓存破坏原记录。");
                });
            }
            catch (Exception e) { code = 1; while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException; report.Add("FAIL " + e); }
            finally
            {
                if (window != null) window.Close();
                if (Directory.Exists(directory)) Directory.Delete(directory, true);
                report.Add("结果：" + count + " 项通过；" + (code == 0 ? "0 项失败。" : "存在失败。"));
                File.WriteAllLines(Path.Combine(root, "tests", "desktop-options-report.txt"), report, new UTF8Encoding(true));
            }
            return code;
        }
        static void Test(string name, Action test) { test(); Drain(); count++; report.Add("PASS " + name); }
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static void Reject(AppState invalid) { bool rejected = false; try { store.Save(invalid); } catch (System.Runtime.Serialization.SerializationException) { rejected = true; } Check(rejected, "无效评分状态未拒绝。"); }
        static T Field<T>(string name) { return (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window); }
        static void Call(string name, params object[] args) { typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, args); Drain(); }
        static void Drain() { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { })); if (dispatcherError != null) throw dispatcherError; }
        static IEnumerable<DependencyObject> Descendants(DependencyObject parent) { yield return parent; foreach (object value in LogicalTreeHelper.GetChildren(parent)) { DependencyObject child = value as DependencyObject; if (child != null) foreach (var descendant in Descendants(child)) yield return descendant; } }
        static IEnumerable<T> Find<T>() where T : DependencyObject { return Descendants((DependencyObject)window.Content).OfType<T>(); }
        static string AllText() { return String.Join("\n", Find<TextBlock>().Select(t => t.Text)); }
        static void Render(string name)
        {
            FrameworkElement content = (FrameworkElement)window.Content; content.Measure(new Size(1080, 950)); content.Arrange(new Rect(0, 0, 1080, 950)); content.UpdateLayout(); Drain();
            RenderTargetBitmap bitmap = new RenderTargetBitmap(1080, 950, 96, 96, PixelFormats.Pbgra32); DrawingVisual bg = new DrawingVisual();
            using (var drawing = bg.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 1080, 950)); bitmap.Render(bg); bitmap.Render(content);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(root, "tests", name + ".png"))) encoder.Save(output);
        }
    }
}
