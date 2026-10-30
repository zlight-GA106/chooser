using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Chooser
{
    // In-process integration tests for this application's own controls; no OS input or UI Automation.
    public static class UiIntegration
    {
        private static readonly List<string> Report = new List<string>();
        private static readonly List<string> Images = new List<string>();
        private static MainWindow window;
        private static AppState state;
        private static Store store;
        private static string shots;
        private static Exception dispatchError;
        private static int passed;

        [STAThread]
        public static int Main(string[] args)
        {
            string root = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            string testDirectory = Path.Combine(root, "tests");
            shots = Path.Combine(testDirectory, "screenshots");
            Directory.CreateDirectory(shots);
            string reportPath = Path.Combine(testDirectory, "ui-integration-report.txt");
            string temporary = Path.Combine(Path.GetTempPath(), "ChooserUiIntegration-" + Guid.NewGuid().ToString("N"));
            int exitCode = 0;
            Report.Add("课堂题库 · WPF 控件集成验证");
            Report.Add("运行时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Report.Add("方式：STA 进程内调用生产 MainWindow 控件；离屏渲染；不操作桌面输入。");
            Report.Add("测试数据：" + temporary);
            Report.Add("");
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { dispatchError = e.Exception; e.Handled = true; };
                Bank fixture = XmlQuiz.ParseFile(Path.Combine(testDirectory, "ui-fixture.xml"));
                var seed = new AppState { SelectedBankId = fixture.Id };
                seed.Banks.Add(fixture);
                new Store(Path.Combine(testDirectory, "ui-data-fixture")).Save(seed);
                Report.Add("独立界面测试种子已保存：" + Path.Combine(testDirectory, "ui-data-fixture"));
                if (args.Contains("--seed-only")) return 0;
                store = new Store(temporary);
                store.Save(seed);
                state = store.Load();
                window = new MainWindow(store, state);

                Test("首页选择题库、默认题号范围和轻量离线入口", delegate
                {
                    Check(Field<string>("view") == "home", "初始页面不是首页。");
                    Check(Field<TextBox>("rangeStart").Text == "1" && Field<TextBox>("rangeEnd").Text == "3", "题号范围未自动填入。");
                    Check(Field<Button>("startButton").IsEnabled, "已有题库时开始按钮不可用。");
                    Check(!AllText().Contains("UID") && !AllText().Contains("多端同步"), "界面残留 UID 或同步入口。");
                    Render("integration-home");
                });

                Test("Start(false) 使用首页 1–3 范围；单选草稿自动持久化", delegate
                {
                    Field<TextBox>("rangeStart").Text = "1";
                    Field<TextBox>("rangeEnd").Text = "3";
                    Call("Start", false);
                    Check(Field<string>("view") == "quiz" && state.Active.QuestionIds.Count == 3, "首页开始未创建三题练习。");
                    Check(AllText().Contains("单选  ·  1 分"), "单选题型或一题一分标记缺失。");
                    Check(!Field<Button>("nextButton").IsEnabled, "未选答案时应禁用提交。");
                    Choose("A");
                    Check(Field<Button>("nextButton").IsEnabled && state.Active.Draft.SequenceEqual(new[] { "A" }), "单选选择未更新。");
                    Check(store.Load().Active.Draft.SequenceEqual(new[] { "A" }), "选择后草稿未写入磁盘。");
                    Check(state.Wrongs.Count == 0, "尚未提交便累计错题。");
                    Render("integration-quiz-single");
                    Call("SubmitCurrent");
                    Check(state.Active.Position == 1 && state.Wrongs.Single().Count == 1, "提交单选后错题次数或位置不符。");
                });

                Test("多选控件、保存返回、重开窗口后恢复同题草稿", delegate
                {
                    Check(AllText().Contains("多选  ·  1 分"), "多选题型未识别。");
                    Choose("A"); Choose("C");
                    Check(new HashSet<string>(state.Active.Draft).SetEquals(new[] { "A", "C" }), "多选未保留多个答案。");
                    Render("integration-quiz-multiple");
                    Click(Button("保存并返回"));
                    Check(Field<string>("view") == "home", "保存返回未进入首页。");
                    Check(state.Wrongs.Single().Count == 1, "暂停使错题重复累计。");
                    window.Close();
                    state = store.Load();
                    window = new MainWindow(store, state);
                    Check(Button("继续上次进度") != null, "重开窗口后无续答入口。");
                    Click(Button("继续上次进度"));
                    Check(state.Active.Position == 1 && new HashSet<string>(state.Active.Draft).SetEquals(new[] { "A", "C" }), "续答题号或草稿不符。");
                    Choose("A");
                    Check(state.Active.Draft.SequenceEqual(new[] { "C" }), "多选再次点击不能取消。");
                    Choose("A");
                    Call("SubmitCurrent");
                    Check(state.Active.Position == 2 && QuizEngine.Score(state.Active) == 1, "正确多选未得一分。");
                });

                Test("T/F 判断提交后统一结算为 2/3 分，错题只累计一次", delegate
                {
                    Check(AllText().Contains("判断  ·  1 分"), "判断题型未识别。");
                    Check(Field<Dictionary<string, Button>>("optionButtons").Keys.OrderBy(x => x).SequenceEqual(new[] { "F", "T" }), "T/F 选项控件丢失。");
                    Choose("T"); Call("SubmitCurrent");
                    Check(state.Active.Completed && state.Active.Position == 3 && QuizEngine.Score(state.Active) == 2, "最终成绩不是两题正确。");
                    Check(Field<string>("view") == "result" && AllText().Contains(" / 3 分") && AllText().Contains("答对 2 题  ·  答错 1 题  ·  未答 0 题"), "结算页面成绩文案不符。");
                    Check(state.Wrongs.Single().Count == 1, "错误次数不是一次。");
                    Render("integration-result");
                });

                Test("重复打开结算和点击提交不会再次累计错题", delegate
                {
                    string lastAt = state.Wrongs.Single().LastAt;
                    Call("ShowResult"); Call("ShowResult"); Call("SubmitCurrent");
                    Check(state.Wrongs.Single().Count == 1 && state.Wrongs.Single().LastAt == lastAt && state.Active.Attempts.Count == 3, "结果页面操作重复记录了答题。");
                });

                Test("错题本按次数筛选、选择题库并重练；答对保留历史次数", delegate
                {
                    Call("ShowWrongs");
                    Check(AllText().Contains("累计答错 1 次") && AllText().Contains("答错 1 次"), "错题本未显示累计次数。");
                    Render("integration-wrongs");
                    Find<TextBox>().Single().Text = "2";
                    Click(Button("筛选"));
                    Check(AllText().Contains("没有符合当前筛选条件的错题"), "次数筛选未生效。");
                    Find<TextBox>().Single().Text = "1";
                    var picker = Find<ComboBox>().Single();
                    picker.SelectedItem = picker.Items.Cast<ComboBoxItem>().Single(i => (string)i.Tag == state.Banks.Single().Id);
                    Click(Button("筛选"));
                    Check(Field<string>("wrongBank") == state.Banks.Single().Id && Field<int>("wrongMinimum") == 1, "题库/次数筛选状态未保留。");
                    Click(Button("重练筛选出的 1 道错题"));
                    Check(state.Active.Mode == "错题重练" && state.Active.QuestionIds.Count == 1, "未从筛选结果创建重练。");
                    Choose("B"); Call("SubmitCurrent");
                    Check(QuizEngine.Score(state.Active) == 1 && state.Active.Completed && state.Wrongs.Single().Count == 1, "重练答对清除了历史错误次数。");
                });

                Test("深色主题、字号缩放、重绘及重新载入设置", delegate
                {
                    Click(Button("深色"));
                    Check(state.Dark && ((SolidColorBrush)window.Resources["Canvas"]).Color.ToString() == "#FF10141B", "深色资源未切换。");
                    Call("ChangeZoom", .2);
                    Check(Math.Abs(state.Zoom - 1.2) < .001, "字号未放大到 1.2。");
                    Check(Find<TextBlock>().Any(t => t.Text == "1" && Math.Abs(t.FontSize - 74.4) < .01), "分数文本未应用字号缩放。");
                    Render("integration-result-dark");
                    Call("ShowHome");
                    Render("integration-home-dark-large");
                    AppState reloaded = store.Load();
                    Check(reloaded.Dark && Math.Abs(reloaded.Zoom - 1.2) < .001 && reloaded.Banks.Single().Questions.Count == 3 && reloaded.Wrongs.Single().Count == 1, "重新读取丢失题库/次数/主题/字号。");
                });

                Test("损坏备份的错题引用和选项被拒绝，原缓存保持有效", delegate
                {
                    AppState invalid = store.Load();
                    invalid.Wrongs.Single().BankId = "missing-bank";
                    RejectSave(invalid);
                    invalid = store.Load(); invalid.Wrongs.Single().QuestionId = "missing-question";
                    RejectSave(invalid);
                    invalid = store.Load(); invalid.Wrongs.Single().LastSelected = new List<string> { "Z" };
                    RejectSave(invalid);
                    invalid = store.Load(); invalid.Wrongs.Single().LastSelected = new List<string> { "A", "A" };
                    RejectSave(invalid);
                    Check(store.Load().Wrongs.Single().Count == 1, "损坏备份覆盖了原缓存。");
                });

                Test("随机考试真实模态控件：默认 3，拒绝 4，确认 2 返回有效数量", delegate
                {
                    // Give the owner a hidden HWND; no desktop input, activation, or OS UI Automation.
                    new System.Windows.Interop.WindowInteropHelper(window).EnsureHandle();
                    Exception modalError = null;
                    Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate
                    {
                        Window dialog = null;
                        try
                        {
                            dialog = Application.Current.Windows.Cast<Window>().Single(w => w != window && w.Title == "随机考试");
                            Check(dialog.IsVisible, "随机考试模态窗口未打开。");
                            var content = (FrameworkElement)dialog.Content;
                            TextBox input = Descendants(content).OfType<TextBox>().Single();
                            Button confirm = Descendants(content).OfType<Button>().Single(b => b.Content is string && (string)b.Content == "进入考试");
                            Check(input.Text == "3", "三题范围内默认抽题数量不是 3。");
                            input.Text = "4";
                            confirm.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                            Check(dialog.IsVisible && Descendants(content).OfType<TextBlock>().Any(t => t.Text.Contains("请输入 1–3 之间的整数")), "非法抽题数量未保留模态并显示错误。");
                            RenderElement(content, dialog.Background, Math.Max(1, (int)Math.Ceiling(dialog.ActualWidth)), Math.Max(1, (int)Math.Ceiling(dialog.ActualHeight)), "integration-exam-modal-invalid");
                            input.Text = "2";
                            confirm.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        }
                        catch (Exception error)
                        {
                            modalError = error;
                            if (dialog != null) dialog.Close();
                            else foreach (Window remaining in Application.Current.Windows.Cast<Window>().Where(w => w != window && w.Title == "随机考试").ToList()) remaining.Close();
                        }
                    }));
                    object result = typeof(MainWindow).GetMethod("AskCount", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, new object[] { 1, 3, 3 });
                    if (modalError != null) throw new InvalidOperationException("随机考试模态控件验证失败", modalError);
                    Check(result != null && (int)result == 2, "确认有效抽题数量后未返回 2。");
                });

                Report.Add("SKIP  提前交卷确认由真实 Windows 界面验收；提前结算计分与持久化已有核心测试覆盖。");
            }
            catch (Exception error)
            {
                exitCode = 1;
                while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                Report.Add("FAIL  " + error);
            }
            finally
            {
                try { if (window != null) window.Close(); }
                catch (Exception error) { Report.Add("窗口清理：" + error.Message); exitCode = 1; }
                try { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
                catch (Exception error) { Report.Add("临时目录清理：" + error.Message); }
                Report.Add("");
                Report.Add("结果：" + passed + " 项通过；" + (exitCode == 0 ? "无失败。" : "存在失败。"));
                Report.Add("离屏图像："); Report.AddRange(Images);
                File.WriteAllLines(reportPath, Report, new UTF8Encoding(true));
                Console.WriteLine(String.Join(Environment.NewLine, Report));
            }
            return exitCode;
        }

        private static void Test(string name, Action body) { body(); Drain(); passed++; Report.Add("PASS  " + name); }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void RejectSave(AppState invalid)
        {
            bool rejected = false;
            try { store.Save(invalid); } catch (System.Runtime.Serialization.SerializationException) { rejected = true; }
            Check(rejected, "非法错题备份未被拒绝。");
        }
        private static T Field<T>(string name) { return (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window); }
        private static void Call(string name, params object[] args)
        {
            typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, args);
            Drain();
        }
        private static void Drain()
        {
            Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate { }));
            if (dispatchError != null) throw new InvalidOperationException("WPF 调度异常", dispatchError);
        }
        private static void Choose(string id) { Click(Field<Dictionary<string, Button>>("optionButtons")[id]); }
        private static Button Button(string content) { return Find<Button>().Single(b => b.Content is string && (string)b.Content == content); }
        private static void Click(Button button)
        {
            Check(button.IsEnabled, "测试试图点击禁用按钮。");
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Drain();
        }
        private static IEnumerable<T> Find<T>() where T : DependencyObject { return Descendants((DependencyObject)window.Content).OfType<T>(); }
        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            yield return root;
            foreach (object child in LogicalTreeHelper.GetChildren(root))
            {
                DependencyObject dependency = child as DependencyObject;
                if (dependency != null)
                    foreach (DependencyObject descendant in Descendants(dependency)) yield return descendant;
            }
        }
        private static string AllText() { return String.Join("\n", Find<TextBlock>().Select(t => t.Text).ToArray()); }
        private static void Render(string name)
        {
            FrameworkElement root = (FrameworkElement)window.Content;
            root.Measure(new Size(1080, 825));
            root.Arrange(new Rect(0, 0, 1080, 825));
            root.UpdateLayout(); Drain();
            RenderElement(root, window.Background, 1080, 825, name);
        }
        private static void RenderElement(FrameworkElement root, Brush canvas, int width, int height, string name)
        {
            root.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (DrawingContext drawing = background.RenderOpen()) drawing.DrawRectangle(canvas, null, new Rect(0, 0, width, height));
            bitmap.Render(background);
            bitmap.Render(root);
            PngBitmapEncoder encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = Path.Combine(shots, name + ".png");
            using (FileStream output = File.Create(path)) encoder.Save(output);
            Images.Add(path);
        }
    }
}
