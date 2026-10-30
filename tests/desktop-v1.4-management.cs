using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Chooser
{
    public static class DesktopV14Management
    {
        static MainWindow window;
        static AppState state;
        static Store store;
        static string root, temporary;
        static readonly List<string> report = new List<string>();
        static int passed;
        static Exception dispatchError;
        [STAThread]
        public static int Main(string[] args)
        {
            root = args[0]; temporary = Path.Combine(Path.GetTempPath(), "chooser-management-v14-" + Guid.NewGuid().ToString("N"));
            int exit = 0;
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e) { dispatchError = e.Exception; e.Handled = true; };
                store = new Store(temporary); state = new AppState();
                Bank original = XmlQuiz.ParseFile(Path.Combine(root, "assets", "example.xml")); state.Banks.Add(original); state.SelectedBankId = original.Id;
                store.Save(state); window = new MainWindow(store, state);
                Test("管理列表提供新建、查看、编辑、重命名与查询入口", delegate {
                    Call("ShowManage");
                    foreach (string action in new[] { "新建题库", "查看", "编辑 XML", "重命名", "删除", "导出 XML" }) Check(Find<Button>().Any(b => Equals(b.Content, action)), "缺少 " + action);
                    SetField("managementQuery", "地球"); Call("ShowManage"); Check(AllText().Contains(original.Name), "题干查询未匹配题库。");
                    SetField("managementQuery", "无匹配关键词XYZ"); Call("ShowManage"); Check(AllText().Contains("没有匹配的题库"), "空查询结果未显示。");
                    SetField("managementQuery", ""); Call("ShowManage"); Render("desktop-v1.4-management.png");
                });
                Test("真实新建编辑器校验 XML，并原子保存新题库", delegate {
                    string xml = original.Xml.Replace("2 + 2", "3 + 1");
                    RunModal("新建题库", delegate(Window dialog) {
                        var content = (DependencyObject)dialog.Content;
                        Descendants(content).OfType<TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "题库名称").Text = "课堂新增题库";
                        var editor = Descendants(content).OfType<TextBox>().Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "题库 XML 内容");
                        Check(editor.AcceptsReturn && editor.Text.Contains(Environment.NewLine), "XML 编辑器没有多行格式。"); editor.Text = xml;
                        ClickWithin(content, "校验 XML"); Check(Descendants(content).OfType<TextBlock>().Any(t => t.Text.Contains("校验通过，共 3 题")), "校验未返回题数。");
                        RenderElement((FrameworkElement)dialog.Content, dialog.Background, 850, 680, "desktop-v1.4-editor.png");
                        ClickWithin(content, "保存题库");
                    }, delegate { Call("OpenBankEditor", new object[] { null }); });
                    Check(state.Banks.Count == 2 && store.Load().Banks.Count == 2, "新建题库未持久保存。");
                    Check(Field<string>("view") == "bank" && AllText().Contains("课堂新增题库"), "保存后未查看新题库。");
                });
                Test("题库查看支持题干、选项、解析查询及 30 题分页", delegate {
                    var xml = new StringBuilder("<quiz>");
                    for (int i = 1; i <= 31; i++) xml.Append("<question><text>分页题 ").Append(i).Append("</text><option id=\"A\">正确选项</option><option id=\"B\">其他选项</option><answer>A</answer><explanation>解析 ").Append(i).Append("</explanation></question>");
                    xml.Append("</quiz>");
                    var bank = (Bank)CallResult("SaveBankDraft", null, "分页题库", xml.ToString(), true); Call("ShowBank", bank.Id);
                    Check(Find<Expander>().Count() == 30 && Find<Button>().Any(b => Equals(b.Content, "下一页") && b.IsEnabled), "分页数量或下一页错误。");
                    SetField("viewedQuestionPage", 1); Call("ShowBank", bank.Id); Check(Find<Expander>().Count() == 1, "第二页应为 1 题。");
                    SetField("viewedQuestionQuery", "解析 31"); SetField("viewedQuestionPage", 0); Call("ShowBank", bank.Id); Check(Find<Expander>().Count() == 1 && AllText().Contains("分页题 31"), "解析查询失败。");
                    SetField("viewedQuestionQuery", "");
                });
                Test("真实重命名保留错题和待查看评分，并更新导出 XML 标题", delegate {
                    var bank = state.Banks.First(b => b.Id == original.Id);
                    state.ImmediateScoring = true; state.Active = QuizEngine.CreateSession(bank, 1, 1, 1, false, "管理回归"); QuizEngine.Submit(state, state.Active, bank, new List<string> { "A" }); store.Save(state);
                    RunModal("重命名题库", delegate(Window dialog) { var content = (DependencyObject)dialog.Content; Descendants(content).OfType<TextBox>().Single().Text = "重命名后的题库"; ClickWithin(content, "保存名称"); }, delegate { Call("OpenRenameBank", bank); });
                    var renamed = state.Banks.First(b => b.Id == original.Id);
                    Check(renamed.Name == "重命名后的题库" && XmlQuiz.Parse(renamed.Xml, "x.xml").Name == renamed.Name, "导出名称不一致。");
                    Check(state.Wrongs.Single().Count == 1 && state.Active.FeedbackPending && state.Active.BankId == renamed.Id, "重命名丢失记录或评分。");
                });
                Test("编辑未作答题目会重映射题库标识，并保留未变题目记录", delegate {
                    var bank = state.Banks.First(b => b.Id == original.Id);
                    string changed = bank.Xml.Replace("地球", "蓝色星球地球");
                    var saved = (Bank)CallResult("SaveBankDraft", bank.Id, bank.Name, changed, true);
                    Check(saved.Id != original.Id && state.SelectedBankId != original.Id && state.Wrongs.Single().BankId == saved.Id, "编辑后标识未重映射。");
                    Check(state.Wrongs.Single().Count == 1 && state.Active != null && state.Active.FeedbackPending && state.Active.BankId == saved.Id, "无关题目修改损坏旧记录。");
                    Check(store.Load().Active.BankId == saved.Id, "新进度未持久化。");
                });
                Test("非法 XML、重名和重复内容不会改动内存或缓存", delegate {
                    var bank = state.Banks.First(b => b.Name == "重命名后的题库");
                    string before = Snapshot(); string disk = File.ReadAllText(Path.Combine(temporary, "state.json"));
                    Reject(delegate { CallResult("SaveBankDraft", bank.Id, bank.Name, "<quiz><question></quiz>", true); });
                    Reject(delegate { CallResult("RenameManagedBank", bank.Id, "课堂新增题库"); });
                    Reject(delegate { CallResult("SaveBankDraft", null, "重复内容的新名", bank.Xml, true); });
                    Check(Snapshot() == before && File.ReadAllText(Path.Combine(temporary, "state.json")) == disk, "失败操作修改了数据。");
                });
                Test("修改已作答题目会清除相关本轮记录和该题错题，保持缓存有效", delegate {
                    var bank = state.Banks.First(b => b.Name == "重命名后的题库");
                    var saved = (Bank)CallResult("SaveBankDraft", bank.Id, bank.Name, bank.Xml.Replace("2 + 2", "2 加 2"), true);
                    Check(state.Active == null && state.Wrongs.Count == 0 && store.Load().Active == null, "相关记录未清理。");
                    Call("ShowBank", saved.Id); Render("desktop-v1.4-bank-view.png");
                });
                Test("保存失败保留内存状态和原磁盘文件", delegate {
                    var bank = state.Banks.First(); string before = Snapshot();
                    string path = Path.Combine(temporary, "state.json"), backup = path + ".protected"; File.Move(path, backup); Directory.CreateDirectory(path);
                    try { Reject(delegate { CallResult("RenameManagedBank", bank.Id, "不能写入时的名称"); }); Check(Snapshot() == before, "保存失败后内存仍被改动。"); }
                    finally { Directory.Delete(path); File.Move(backup, path); }
                    Check(store.Load().Banks.First().Name == bank.Name, "原磁盘数据未保留。");
                });
                Test("删除题库经事务保存，其他题库保持可用", delegate {
                    var bank = state.Banks.First(b => b.Name == "课堂新增题库"); int count = state.Banks.Count;
                    Check((bool)CallResult("DeleteManagedBank", bank.Id, false), "删除未执行。");
                    Check(state.Banks.Count == count - 1 && !store.Load().Banks.Any(b => b.Id == bank.Id), "删除未持久化。");
                    Call("ShowManage"); Check(AllText().Contains("分页题库"), "删除影响其他题库。");
                });
                Test("发布版本更新至 1.4，帮助与图标保持可用", delegate {
                    string exe = Path.Combine(root, "选择器单文件版v1.4.exe"); Check(AssemblyName.GetAssemblyName(exe).Version.ToString() == "1.4.0.0", "发布版本错误。");
                    var resources = Assembly.ReflectionOnlyLoadFrom(exe).GetManifestResourceNames(); Check(resources.Contains("default.xml") && resources.Contains("xml-guide.json") && resources.Contains("example.xml"), "发布程序缺少内置资源。");
                    Call("ShowHelp"); Check(AllText().Contains("题库 XML 写作指南") && AllText().Contains("选择器 v1.4.0"), "指南版本未同步。");
                    Check(!AllText().Split('\n').Contains("选"), "旧字图标复现。");
                });
            }
            catch (Exception error) { exit = 1; while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException; report.Add("FAIL " + error); }
            finally {
                if (window != null) window.Close();
                if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
                report.Add("结果：" + passed + " 项通过；" + (exit == 0 ? "0 项失败。" : "存在失败。"));
                File.WriteAllLines(Path.Combine(root, "tests", "desktop-v1.4-management-report.txt"), report, new UTF8Encoding(true));
            }
            return exit;
        }
        static void Test(string name, Action action) { action(); Drain(); passed++; report.Add("PASS " + name); }
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        static void Reject(Action action) { bool rejected = false; try { action(); } catch { rejected = true; } Check(rejected, "应拒绝的操作成功。"); }
        static string Snapshot() { var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(AppState)); using (var stream = new MemoryStream()) { serializer.WriteObject(stream, state); return Convert.ToBase64String(stream.ToArray()); } }
        static T Field<T>(string name) { return (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window); }
        static void SetField(string name, object value) { typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(window, value); }
        static void Call(string name, params object[] args) { CallResult(name, args); }
        static object CallResult(string name, params object[] args) { try { var result = typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, args); Drain(); return result; } catch (TargetInvocationException error) { throw error.InnerException; } }
        static IEnumerable<DependencyObject> Descendants(DependencyObject rootElement) { yield return rootElement; foreach (object child in LogicalTreeHelper.GetChildren(rootElement)) { var element = child as DependencyObject; if (element != null) foreach (var value in Descendants(element)) yield return value; } }
        static IEnumerable<T> Find<T>() where T : DependencyObject { return Descendants((DependencyObject)window.Content).OfType<T>(); }
        static string AllText() { return String.Join("\n", Find<TextBlock>().Select(t => t.Text)); }
        static void ClickWithin(DependencyObject content, string label) { Descendants(content).OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Drain(); }
        static void Drain() { Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.Background, new Action(delegate {})); if (dispatchError != null) throw new InvalidOperationException("WPF 调度异常", dispatchError); }
        static void RunModal(string title, Action<Window> body, Action open)
        {
            new WindowInteropHelper(window).EnsureHandle(); Exception failure = null;
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(delegate {
                Window dialog = null; Dictionary<TextBox, string> initial = null;
                try { dialog = Application.Current.Windows.Cast<Window>().Single(w => w != window && w.Title == title); initial = Descendants((DependencyObject)dialog.Content).OfType<TextBox>().ToDictionary(t => t, t => t.Text); body(dialog); Check(!dialog.IsVisible, "保存操作未关闭编辑器。"); }
                catch (Exception error) { failure = error; if (dialog != null) { if (initial != null) foreach (var pair in initial) pair.Key.Text = pair.Value; dialog.Close(); } }
            }));
            open(); if (failure != null) throw failure;
        }
        static void Render(string name) { RenderElement((FrameworkElement)window.Content, window.Background, 1080, 825, name); }
        static void RenderElement(FrameworkElement content, Brush background, int width, int height, string name)
        {
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout(); Drain();
            var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); var canvas = new DrawingVisual(); using (var drawing = canvas.RenderOpen()) drawing.DrawRectangle(background, null, new Rect(0, 0, width, height)); image.Render(canvas); image.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using (var output = File.Create(Path.Combine(root, "tests", name))) encoder.Save(output);
        }
    }
}
