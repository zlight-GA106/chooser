using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Chooser
{
    internal static class PortableStoreTests
    {
        private const string Sample = "<quiz title='迁移题库'><question><text>Test</text><option id='A'>One</option><option id='B'>Two</option><answer>A</answer></question></quiz>";

        private static int Main(string[] args)
        {
            string image = Assembly.GetExecutingAssembly().Location;
            if (args.Length != 0)
            {
                try { RunChild(image, args[0], args[1]); return 0; }
                catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            }

            string root = Path.Combine(Path.GetTempPath(), "chooser-portable-test-" + Guid.NewGuid().ToString("N"));
            string legacy = Path.Combine(root, "旧缓存");
            string portable = Path.Combine(root, "便携副本.exe");
            try
            {
                Directory.CreateDirectory(root);
                File.Copy(image, portable);
                byte[] cleanBytes = File.ReadAllBytes(portable);
                var local = new Store(legacy);
                var oldState = new AppState();
                oldState.Banks.Add(XmlQuiz.Parse(Sample, "legacy.xml"));
                local.Save(oldState);

                Run(portable, "stage", legacy);
                Check(File.ReadAllBytes(portable).SequenceEqual(cleanBytes), "暂存时修改了运行程序。");
                Run(portable, "commit", legacy);
                byte[] committed = File.ReadAllBytes(portable);
                Check(committed.Length > cleanBytes.Length && committed.Take(cleanBytes.Length).SequenceEqual(cleanBytes), "未保留原始可执行文件内容。");
                Check(local.Load().Banks.Single().Name == "迁移题库", "迁移改动了旧数据。");
                Console.WriteLine("PASS 运行中的 EXE 关闭前提交，能恢复先前暂存且保留原 PE 和旧数据");

                string copied = Path.Combine(root, "另一台电脑上的副本.exe");
                string otherProfile = Path.Combine(root, "另一份用户缓存");
                File.Copy(portable, copied);
                Run(copied, "read", otherProfile);
                Check(!Directory.Exists(otherProfile), "打开副本依赖或写入了主机缓存。");
                Run(copied, "empty", otherProfile);
                Run(copied, "read-empty", otherProfile);
                Check(new FileInfo(copied).Length < committed.Length, "再次保存没有替换旧载荷。");
                Console.WriteLine("PASS 重命名并复制 EXE 后，独立缓存下可运行并读出题库和设置，空题库也可保存");

                string blank = Path.Combine(root, "空白副本.exe");
                File.Copy(image, blank);
                Run(blank, "blank", legacy);
                Console.WriteLine("PASS 首次迁移成功后，新副本不会再次迁移旧缓存");

                committed[committed.Length - 1] ^= 1;
                File.WriteAllBytes(portable, committed);
                Run(portable, "corrupt", legacy);
                Check(File.ReadAllBytes(portable).SequenceEqual(committed), "校验失败时覆盖了原文件。");
                Console.WriteLine("PASS 损坏的快照校验失败，原 EXE 保留");

                File.Copy(image, blank, true);
                Run(blank, "read-only", otherProfile);
                Run(blank, "recover-read-only", otherProfile);
                Console.WriteLine("PASS 写入受阻时保留快照，权限恢复后可重试");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void RunChild(string image, string mode, string legacy)
        {
            var store = new PortableStore(image, legacy);
            if (mode == "corrupt")
            {
                try { store.Load(); }
                catch (IOException) { return; }
                throw new InvalidOperationException("未拒绝快照校验失败。");
            }
            AppState state = store.Load();
            if (mode == "stage")
            {
                Check(state.Banks.Single().Name == "迁移题库", "没有读取旧缓存。");
                state.Banks[0].Name = "写入便携版";
                state.Dark = true;
                store.Save(state);
                return;
            }
            if (mode == "commit" || mode == "read" || mode == "empty")
            {
                Check(state.Banks.Single().Name == "写入便携版" && state.Dark, "便携题库或设置丢失。");
                if (mode == "commit")
                {
                    Check(store.LoadWarning.Contains("尚未写入"), "未从暂存恢复。");
                    store.Flush();
                }
                if (mode == "empty")
                {
                    state.Banks.Clear();
                    store.Save(state);
                    store.Flush();
                }
                return;
            }
            if (mode == "read-only")
            {
                state.Dark = true;
                store.Save(state);
                File.SetAttributes(image, File.GetAttributes(image) | FileAttributes.ReadOnly);
                try
                {
                    try { store.Flush(); }
                    catch (IOException) { return; }
                    throw new InvalidOperationException("只读 EXE 上的写入意外成功。");
                }
                finally { File.SetAttributes(image, File.GetAttributes(image) & ~FileAttributes.ReadOnly); }
            }
            if (mode == "recover-read-only")
            {
                Check(state.Dark && store.LoadWarning.Contains("尚未写入"), "只读 EXE 写入失败后丢失暂存。");
                store.Flush();
                return;
            }
            Check(state.Banks.Count == 0 && store.HasSnapshot == (mode == "read-empty"), "无法区分已删除的空题库与全新程序。");
        }

        private static void Run(string executable, string mode, string legacy)
        {
            var info = new ProcessStartInfo(executable, mode + " \"" + legacy + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using (Process child = Process.Start(info))
            {
                if (!child.WaitForExit(12000)) { child.Kill(); throw new TimeoutException("便携测试超时：" + mode); }
                Check(child.ExitCode == 0, "便携测试失败：" + mode);
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
