using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Threading;

[assembly: AssemblyTitle("选择器")]
[assembly: AssemblyDescription("离线选择题练习与课堂考试")]
[assembly: AssemblyCompany("zlight106")]
[assembly: AssemblyProduct("选择器")]
[assembly: AssemblyVersion("1.4.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.8")]

namespace Chooser
{
    internal static class Program
    {
        internal static string DefaultXml()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("default.xml"))
            using (var reader = new StreamReader(stream, Encoding.UTF8)) return reader.ReadToEnd();
        }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
            {
                string report = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "chooser-tests.txt");
                string sample = args.Length > 2 ? args[2] : null;
                string temporary = null;
                if (sample == null)
                {
                    temporary = Path.Combine(Path.GetTempPath(), "chooser-sample-" + Guid.NewGuid().ToString("N") + ".xml");
                    File.WriteAllText(temporary, DefaultXml(), new UTF8Encoding(false));
                    sample = temporary;
                }
                try { return SelfTests.Run(report, sample); }
                finally { if (temporary != null) File.Delete(temporary); }
            }

            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChooserClassroom");
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "--data-dir") directory = Path.GetFullPath(args[i + 1]);
            string mutexName;
            using (var sha = System.Security.Cryptography.SHA256.Create())
                mutexName = "Local\\ChooserClassroom-" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(directory.ToLowerInvariant()))).Replace("-", "");
            bool created;
            using (var single = new Mutex(true, mutexName, out created))
            {
                if (!created)
                {
                    MessageBox.Show("选择器已在运行，请切换到已打开的窗口。", "选择器", MessageBoxButton.OK, MessageBoxImage.Information);
                    return 0;
                }
                try
                {
                    var app = new Application();
                    app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
                    {
                        MessageBox.Show("操作未完成：\n" + e.Exception.Message, "选择器", MessageBoxButton.OK, MessageBoxImage.Warning);
                        e.Handled = true;
                    };
                    var store = new Store(directory);
                    var state = store.Load();
                    // Only a genuinely new data directory gets the built-in bank. An empty library stays empty.
                    if (state.Banks.Count == 0 && !File.Exists(Path.Combine(directory, "state.json")))
                    {
                        var bank = XmlQuiz.Parse(DefaultXml(), "2025新版大信_选择题.xml");
                        state.Banks.Add(bank);
                        state.SelectedBankId = bank.Id;
                        store.Save(state);
                    }
                    var window = new MainWindow(store, state);
                    app.Run(window);
                    return 0;
                }
                catch (Exception e)
                {
                    MessageBox.Show("无法启动选择器：\n" + e.Message + "\n\n数据目录：" + directory, "选择器", MessageBoxButton.OK, MessageBoxImage.Error);
                    return 1;
                }
                finally { single.ReleaseMutex(); }
            }
        }
    }
}
