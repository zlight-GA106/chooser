using System;
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
    public static class DesktopV13Help
    {
        static MainWindow window;
        static AppState state;
        static readonly List<string> report = new List<string>();
        static int passed;
        static Exception dispatchError;
        [STAThread]
        public static int Main(string[] args)
        {
            string root = args[0], temporary = Path.Combine(Path.GetTempPath(), "chooser-help-v13-" + Guid.NewGuid().ToString("N"));
            int result = 0;
            try
            {
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { dispatchError = e.Exception; e.Handled = true; };
                var store = new Store(temporary);
                state = new AppState();
                Bank bank = XmlQuiz.ParseFile(Path.Combine(root, "assets", "example.xml"));
                state.Banks.Add(bank); state.SelectedBankId = bank.Id;
                window = new MainWindow(store, state);
                Test("共享示例 XML 包含单选、多选、判断三题，答案 B / A,C / T", delegate {
                    Check(bank.Questions.Count == 3, "示例题数不为 3。");
                    Check(String.Join(",", bank.Questions[0].Answers) == "B" && String.Join(",", bank.Questions[1].Answers) == "A,C" && String.Join(",", bank.Questions[2].Answers) == "T", "示例答案错误。");
                    Check(bank.Questions.All(q => !String.IsNullOrWhiteSpace(q.Explanation)), "示例缺少解析。");
                });
                Test("页头为青色勾选图标，帮助入口可达，原选字已移除", delegate {
                    Check(Find<Button>().Any(b => Equals(b.Content, "帮助")), "未找到帮助按钮。");
                    Check(!Find<TextBlock>().Any(t => t.Text == "选"), "页头残留选字。");
                    Check(Find<Border>().Any(b => b.Background is SolidColorBrush && ((SolidColorBrush)b.Background).Color.ToString() == "#FF008080" && b.Width == 36), "未找到青色勾选底图。");
                    Check(Find<System.Windows.Shapes.Path>().Any(p => p.Stroke == Brushes.White), "未找到白色勾选路径。");
                    Render(root, "desktop-v1.3-home.png");
                });
                Test("内置帮助与共享指南一致，示例可复制且支持下载", delegate {
                    Click("帮助");
                    Check(Field<string>("view") == "help", "未进入帮助页。");
                    Check(Find<TextBlock>().Any(t => t.Text == "题库 XML 写作指南"), "帮助标题不正确。");
                    Check(Find<TextBlock>().Count(t => t.Text.StartsWith("1. ") || t.Text.StartsWith("2. ") || t.Text.StartsWith("3. ") || t.Text.StartsWith("4. ") || t.Text.StartsWith("5. ") || t.Text.StartsWith("6. ") || t.Text.StartsWith("7. ") || t.Text.StartsWith("8. ") || t.Text.StartsWith("9. ")) == 9, "共享帮助章节不完整。");
                    Check(Find<Button>().Any(b => Equals(b.Content, "下载示例 XML")), "缺少下载示例入口。");
                    Check(Find<TextBox>().All(t => t.IsReadOnly), "帮助代码框应只读且可复制。");
                    Check(Find<TextBox>().Any(t => t.Text.Contains("<![CDATA[")), "缺少 CDATA 示例。");
                    string embedded = (string)typeof(MainWindow).GetMethod("ReadExample", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
                    Check(XmlQuiz.Parse(embedded, "example.xml").Id == bank.Id, "内置下载示例与共享示例不一致。");
                    Render(root, "desktop-v1.3-help.png");
                });
                Test("帮助页支持深色和缩放，离开不会丢失已提交即时评分", delegate {
                    state.ImmediateScoring = true;
                    state.Active = QuizEngine.CreateSession(bank, 1, 1, 1, false, "顺序练习");
                    state.Active.Draft.Add("A");
                    QuizEngine.Submit(state, state.Active, bank, new List<string> { "A" });
                    int count = state.Wrongs.Single().Count;
                    Click("深色"); Call("ChangeZoom", .1);
                    Check(Field<string>("view") == "help", "主题或缩放后离开了帮助页。");
                    Check(Find<Button>().Any(b => Equals(b.Content, "返回答题")), "帮助页缺少返回答题按钮。");
                    Click("返回答题");
                    Check(Field<string>("view") == "quiz" && state.Active.FeedbackPending && state.Wrongs.Single().Count == count, "帮助跳转丢失或重复结算评分。");
                    Call("ShowHelp"); Click("返回首页"); Check(Field<string>("view") == "home", "无法返回首页。");
                });
                Test("发布 EXE 版本及帮助资源已内置，无需外置文件", delegate {
                    string exe = Path.Combine(root, "选择器单文件版v1.3.exe");
                    Check(AssemblyName.GetAssemblyName(exe).Version.ToString() == "1.3.0.0", "发布版本号未更新。");
                    var assembly = Assembly.ReflectionOnlyLoadFrom(exe);
                    string[] names = assembly.GetManifestResourceNames();
                    Check(names.Contains("xml-guide.json") && names.Contains("example.xml") && names.Contains("default.xml"), "单文件 EXE 缺少帮助资源。");
                });
            }
            catch (Exception error)
            {
                result = 1; while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                report.Add("FAIL " + error);
            }
            finally
            {
                if (window != null) window.Close();
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
                report.Add("结果：" + passed + " 项通过；" + (result == 0 ? "0 项失败。" : "存在失败。"));
                File.WriteAllLines(Path.Combine(root, "tests", "desktop-v1.3-help-report.txt"), report, new UTF8Encoding(true));
            }
            return result;
        }
        static void Test(string label, Action test) { test(); Drain(); passed++; report.Add("PASS " + label); }
        static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static T Field<T>(string name) { return (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window); }
        static void Call(string name, params object[] args) { typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, args); Drain(); }
        static void Click(string content) { Find<Button>().Single(b => Equals(b.Content, content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Drain(); }
        static IEnumerable<T> Find<T>() where T : DependencyObject { return Descendants((DependencyObject)window.Content).OfType<T>(); }
        static IEnumerable<DependencyObject> Descendants(DependencyObject root) { yield return root; foreach (object child in LogicalTreeHelper.GetChildren(root)) { var element = child as DependencyObject; if (element != null) foreach (var next in Descendants(element)) yield return next; } }
        static void Drain() { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate {})); if (dispatchError != null) throw new InvalidOperationException("WPF 调度错误", dispatchError); }
        static void Render(string rootPath, string filename)
        {
            FrameworkElement content = (FrameworkElement)window.Content; content.Measure(new Size(1080, 825)); content.Arrange(new Rect(0, 0, 1080, 825)); content.UpdateLayout(); Drain();
            var bitmap = new RenderTargetBitmap(1080, 825, 96, 96, PixelFormats.Pbgra32); var background = new DrawingVisual();
            using (DrawingContext drawing = background.RenderOpen()) drawing.DrawRectangle(window.Background, null, new Rect(0, 0, 1080, 825));
            bitmap.Render(background); bitmap.Render(content); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var output = File.Create(Path.Combine(rootPath, "tests", filename))) encoder.Save(output);
        }
    }
}
