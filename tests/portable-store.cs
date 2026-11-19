using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

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
                catch (Exception error)
                {
                    using (var output = new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)))
                        output.WriteLine(error);
                    return 1;
                }
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

                string fallbackProfile = Path.Combine(root, "暂存受阻的用户缓存");
                string blockedStaging = BlockStagingPath(fallbackProfile);
                string fallback = Path.Combine(root, "回退副本.exe");
                File.Copy(image, fallback);
                Run(fallback, "fallback-stage", fallbackProfile);
                Check(File.ReadAllBytes(fallback).SequenceEqual(cleanBytes), "回退暂存时提前改写了 EXE。");
                Run(fallback, "fallback-read-staged", fallbackProfile);
                File.Delete(blockedStaging);
                Run(fallback, "commit", fallbackProfile);
                string fallbackCopy = Path.Combine(root, "回退后的可携带副本.exe");
                File.Copy(fallback, fallbackCopy);
                Run(fallbackCopy, "read", otherProfile);
                Console.WriteLine("PASS 暂存目录不可写时可首次保存，原位重开可恢复，正常退出后可复制 EXE");

                string migrationProfile = Path.Combine(root, "旧数据可读但暂存受阻的缓存");
                var migrationSource = new Store(migrationProfile);
                migrationSource.Save(oldState);
                string blockedMigrationStaging = BlockStagingPath(migrationProfile);
                string migration = Path.Combine(root, "迁移回退副本.exe");
                File.Copy(image, migration);
                Run(migration, "stage", migrationProfile);
                Run(migration, "commit", migrationProfile);
                Check(File.ReadAllText(blockedMigrationStaging) == "occupied", "改写了挡住主暂存目录的原文件。");
                Check(migrationSource.Load().Banks.Single().Name == "迁移题库", "回退写入改动了旧数据。");
                Console.WriteLine("PASS 暂存目录不可写时仍可读取并迁移旧题库，原文件完整保留");

                string lockedProfile = Path.Combine(root, "已有暂存但暂时不可读的缓存");
                string lockedImage = Path.Combine(root, "暂存读锁副本.exe");
                File.Copy(image, lockedImage);
                Run(lockedImage, "fallback-stage", lockedProfile);
                var lockedStore = new PortableStore(lockedImage, lockedProfile);
                string lockedState = Path.Combine(lockedStore.DirectoryPath, "state.json");
                byte[] lockedBytes = File.ReadAllBytes(lockedState);
                using (new FileStream(lockedState, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    Run(lockedImage, "unreadable", lockedProfile);
                Check(File.ReadAllBytes(lockedState).SequenceEqual(lockedBytes), "已存在但读取受阻的暂存文件被改写。");
                Check(File.ReadAllBytes(lockedImage).SequenceEqual(cleanBytes), "暂存读取受阻时覆盖了 EXE。");
                Run(lockedImage, "commit", lockedProfile);
                Console.WriteLine("PASS 现有暂存暂时不可读时明确失败，解锁后仍可恢复原快照");

                string readOnlyProfile = Path.Combine(root, "暂存文件写入拒绝的缓存");
                string readOnlyImage = Path.Combine(root, "只读暂存副本.exe");
                File.Copy(image, readOnlyImage);
                Run(readOnlyImage, "fallback-stage", readOnlyProfile);
                string readOnlyDirectory = new PortableStore(readOnlyImage, readOnlyProfile).DirectoryPath;
                string readOnlyState = Path.Combine(readOnlyDirectory, "state.json");
                FileAttributes originalAttributes = File.GetAttributes(readOnlyState);
                byte[] savedState = File.ReadAllBytes(readOnlyState);
                File.SetAttributes(readOnlyState, originalAttributes | FileAttributes.ReadOnly);
                try
                {
                    var direct = new Store(readOnlyDirectory);
                    var directState = direct.Load();
                    bool denied = false;
                    try { direct.Save(directState); }
                    catch (UnauthorizedAccessException) { denied = true; }
                    Check(denied, "普通 Store 未复现访问拒绝。");
                    Run(readOnlyImage, "fallback-update", readOnlyProfile);
                    Check(File.ReadAllBytes(readOnlyState).SequenceEqual(savedState), "回退保存改写了原暂存文件。");
                }
                finally { File.SetAttributes(readOnlyState, originalAttributes); }
                Run(readOnlyImage, "fallback-updated-commit", readOnlyProfile);
                Console.WriteLine("PASS Windows 拒绝更新只读暂存文件时保存至回退位置，重开读到最新数据");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }

        private static void RunChild(string image, string mode, string legacy)
        {
            if (mode == "corrupt" || mode == "unreadable")
            {
                try { new PortableStore(image, legacy).Load(); }
                catch (IOException) { return; }
                catch (UnauthorizedAccessException) { return; }
                throw new InvalidOperationException("不可读快照被当成可继续使用的数据。");
            }
            var store = new PortableStore(image, legacy);
            AppState state = store.Load();
            if (mode == "fallback-stage")
            {
                Check(!store.HasSnapshot && state.Banks.Count == 0, "全新副本出现非空记录。");
                var bank = XmlQuiz.Parse(Sample, "fallback.xml");
                bank.Name = "写入便携版";
                state.Banks.Add(bank);
                state.SelectedBankId = bank.Id;
                state.Dark = true;
                store.Save(state);
                return;
            }
            if (mode == "fallback-read-staged")
            {
                Check(state.Banks.Single().Name == "写入便携版" && state.Dark && store.HasSnapshot, "目录仍不可写时，重开后丢失回退暂存。");
                return;
            }
            if (mode == "fallback-update")
            {
                Check(state.Dark && state.Zoom == 1, "只读暂存的现有数据未加载。");
                state.Zoom = 1.25;
                store.Save(state);
                return;
            }
            if (mode == "fallback-updated-commit")
            {
                Check(state.Dark && state.Zoom == 1.25, "位置恢复可写后错误选择了较早的快照。");
                store.Flush();
                return;
            }
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

        private static string BlockStagingPath(string profile)
        {
            Directory.CreateDirectory(profile);
            string path = Path.Combine(profile, "portable");
            File.WriteAllText(path, "occupied");
            return path;
        }

        private static void Run(string executable, string mode, string legacy)
        {
            var info = new ProcessStartInfo(executable, mode + " \"" + legacy + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                StandardErrorEncoding = Encoding.UTF8
            };
            using (Process child = Process.Start(info))
            {
                var error = child.StandardError.ReadToEndAsync();
                if (!child.WaitForExit(12000)) { child.Kill(); throw new TimeoutException("便携测试超时：" + mode); }
                Check(child.ExitCode == 0, "便携测试失败：" + mode + Environment.NewLine + error.Result.Trim());
            }
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
