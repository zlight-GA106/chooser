using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Chooser
{
    internal sealed class PortableStore : Store
    {
        // An unsigned managed executable can carry an overlay after its PE sections.
        // Layout: original executable, gzip JSON, marker, gzip length, SHA-256 of JSON.
        private static readonly byte[] Marker = Encoding.ASCII.GetBytes("Chooser.Portable.State.1");
        private const int DigestSize = 32;
        private const int BufferSize = 81920;
        private static readonly int FooterSize = Marker.Length + sizeof(long) + DigestSize;
        private readonly string executable;
        private readonly string legacyDirectory;
        private byte[] imageDigest;
        private byte[] snapshotDigest;
        private long imageSize;
        private long compressedSize;

        public PortableStore(string executable, string legacyDirectory)
            : base(legacyDirectory)
        {
            this.executable = Path.GetFullPath(executable);
            this.legacyDirectory = DirectoryPath;
            using (FileStream input = OpenImage())
            {
                imageDigest = Hash(input);
                ReadFooter(input);
            }
            DirectoryPath = StagingDirectory();
        }

        public override bool HasSnapshot { get { return snapshotDigest != null || base.HasSnapshot; } }

        public override AppState Load()
        {
            LoadWarning = "";
            if (base.HasSnapshot)
            {
                AppState staged = base.Load();
                LoadWarning = "已恢复上次尚未写入 EXE 的数据。正常关闭后即可复制 EXE。" + LoadWarning;
                return staged;
            }

            if (snapshotDigest != null)
            {
                using (FileStream input = OpenImage())
                using (var snapshot = new MemoryStream())
                {
                    EnsureOriginalImage(input);
                    input.Position = imageSize;
                    // The gzip decoder stops at the end of its member; the footer follows it.
                    using (var gzip = new GZipStream(input, CompressionMode.Decompress, true))
                    {
                        byte[] buffer = new byte[BufferSize];
                        int count;
                        while ((count = gzip.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            if (snapshot.Length + count > MaxStateBytes)
                                throw new IOException("便携数据超过 256 MB，无法读取。");
                            snapshot.Write(buffer, 0, count);
                        }
                    }
                    snapshot.Position = 0;
                    if (!Hash(snapshot).SequenceEqual(snapshotDigest))
                        throw new IOException("EXE 内的数据校验失败，原文件已保留。请使用完好的 EXE 或 JSON 备份。");
                    snapshot.Position = 0;
                    return ReadSnapshot(snapshot);
                }
            }

            var legacy = new Store(legacyDirectory);
            if (File.Exists(MigrationMarker) || !legacy.HasSnapshot) return new AppState();
            AppState migrated = legacy.Load();
            Save(migrated);
            LoadWarning = "已读取旧版题库与记录。正常关闭后，数据会写入此 EXE。" + legacy.LoadWarning;
            return migrated;
        }

        public override void Flush()
        {
            if (!File.Exists(StatePath)) return;
            string staging = DirectoryPath;
            string temporary = Path.Combine(Path.GetDirectoryName(executable), ".chooser-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                byte[] newDigest;
                byte[] newImageDigest = null;
                long newCompressedSize = compressedSize;
                using (FileStream input = OpenImage())
                using (FileStream snapshot = File.OpenRead(StatePath))
                {
                    EnsureOriginalImage(input);
                    newDigest = Hash(snapshot);
                    if (snapshotDigest == null || !newDigest.SequenceEqual(snapshotDigest))
                    {
                        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                        {
                            input.Position = 0;
                            CopyPrefix(input, output, imageSize);
                            snapshot.Position = 0;
                            using (var gzip = new GZipStream(output, CompressionMode.Compress, true)) snapshot.CopyTo(gzip);
                            newCompressedSize = output.Position - imageSize;
                            using (var writer = new BinaryWriter(output, Encoding.ASCII, true))
                            {
                                writer.Write(Marker);
                                writer.Write(newCompressedSize);
                                writer.Write(newDigest);
                            }
                            output.Flush(true);
                            output.Position = 0;
                            newImageDigest = Hash(output);
                        }
                    }
                }

                if (newImageDigest != null)
                {
                    ReplaceImage(temporary);
                    imageDigest = newImageDigest;
                    snapshotDigest = newDigest;
                    compressedSize = newCompressedSize;
                    DirectoryPath = StagingDirectory();
                }
                FinishStaging(staging);
            }
            catch (IOException error) { throw PortableWriteError(error); }
            catch (UnauthorizedAccessException error) { throw PortableWriteError(error); }
            finally { if (File.Exists(executable)) TryDelete(temporary); }
        }

        private void ReplaceImage(string temporary)
        {
            try { File.Replace(temporary, executable, null); }
            catch (IOException error)
            {
                // ReplaceFile can report a failed final rename after moving the old file.
                // If that happens, restore the path with the already-flushed replacement.
                if (File.Exists(executable) || !File.Exists(temporary)) throw;
                try { File.Move(temporary, executable); }
                catch (IOException moveError) { throw RecoveryError(temporary, error, moveError); }
                catch (UnauthorizedAccessException moveError) { throw RecoveryError(temporary, error, moveError); }
            }
        }

        private static IOException RecoveryError(string temporary, Exception replaceError, Exception moveError)
        {
            return new IOException("已保存含完整数据的可执行文件，请将它重命名为 .exe：\n" + temporary,
                new AggregateException(replaceError, moveError));
        }

        private string MigrationMarker { get { return Path.Combine(legacyDirectory, "portable-v15-migrated"); } }

        private string StagingDirectory()
        {
            string stamp = Convert.ToBase64String(imageDigest);
            byte[] name = Encoding.UTF8.GetBytes(executable.ToUpperInvariant() + "\n" + stamp);
            using (SHA256 sha = SHA256.Create())
                return Path.Combine(legacyDirectory, "portable", BitConverter.ToString(sha.ComputeHash(name)).Replace("-", ""));
        }

        private void FinishStaging(string staging)
        {
            try
            {
                // The marker is machine-local. The portable snapshot has already been committed.
                if (!File.Exists(MigrationMarker)) File.WriteAllText(MigrationMarker, "1");
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            string backup = Path.Combine(staging, "state.json.bak");
            TryDelete(backup);
            if (!File.Exists(backup)) TryDelete(Path.Combine(staging, "state.json"));
            try { Directory.Delete(staging); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private FileStream OpenImage()
        {
            return new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        private void ReadFooter(FileStream input)
        {
            imageSize = input.Length;
            if (input.Length < FooterSize) return;
            input.Position = input.Length - FooterSize;
            using (var reader = new BinaryReader(input, Encoding.ASCII, true))
            {
                if (!reader.ReadBytes(Marker.Length).SequenceEqual(Marker)) return;
                compressedSize = reader.ReadInt64();
                if (compressedSize <= 0 || compressedSize >= input.Length - FooterSize || compressedSize > MaxStateBytes + 1024 * 1024)
                    throw new IOException("EXE 内的便携数据长度无效。请使用完好的 EXE 或 JSON 备份。");
                imageSize -= FooterSize + compressedSize;
                snapshotDigest = reader.ReadBytes(DigestSize);
            }
        }

        private void EnsureOriginalImage(Stream input)
        {
            if (!Hash(input).SequenceEqual(imageDigest))
                throw new IOException("运行期间 EXE 文件已变更，无法写入此文件。");
        }

        private IOException PortableWriteError(Exception error)
        {
            return new IOException("无法把数据写入此 EXE。请检查 EXE 和所在文件夹是否可写，关闭占用它的程序后重试。数据仍可在原位置恢复。\n" + error.Message, error);
        }

        private static byte[] Hash(Stream stream)
        {
            using (SHA256 sha = SHA256.Create()) return sha.ComputeHash(stream);
        }

        private static void CopyPrefix(Stream input, Stream output, long count)
        {
            byte[] buffer = new byte[BufferSize];
            while (count > 0)
            {
                int read = input.Read(buffer, 0, (int)Math.Min(count, buffer.Length));
                if (read == 0) throw new EndOfStreamException("读取 EXE 时文件已截断。");
                output.Write(buffer, 0, read);
                count -= read;
            }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
