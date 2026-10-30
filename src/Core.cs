using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Chooser
{
    [DataContract]
    public sealed class Option
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Text { get; set; }
        public Option() { Id = ""; Text = ""; }
    }

    [DataContract]
    public sealed class Question
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Text { get; set; }
        [DataMember] public string Explanation { get; set; }
        [DataMember] public int Number { get; set; }
        [DataMember] public List<Option> Options { get; set; }
        [DataMember] public List<string> Answers { get; set; }
        public Question() { Id = Text = Explanation = ""; Options = new List<Option>(); Answers = new List<string>(); }
    }

    [DataContract]
    public sealed class Bank
    {
        [DataMember] public string Id { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string ImportedAt { get; set; }
        [DataMember] public string Xml { get; set; }
        [DataMember] public List<Question> Questions { get; set; }
        public Bank() { Id = Name = ImportedAt = Xml = ""; Questions = new List<Question>(); }
    }

    [DataContract]
    public sealed class Attempt
    {
        [DataMember] public string QuestionId { get; set; }
        [DataMember] public List<string> Selected { get; set; }
        [DataMember] public bool Correct { get; set; }
        public Attempt() { QuestionId = ""; Selected = new List<string>(); }
    }

    [DataContract]
    public sealed class Session
    {
        [DataMember] public string BankId { get; set; }
        [DataMember] public string Mode { get; set; }
        [DataMember] public List<string> QuestionIds { get; set; }
        [DataMember] public List<Attempt> Attempts { get; set; }
        [DataMember] public int Position { get; set; }
        [DataMember] public List<string> Draft { get; set; }
        [DataMember] public bool Completed { get; set; }
        [DataMember] public bool FeedbackPending { get; set; }
        public Session() { BankId = Mode = ""; QuestionIds = new List<string>(); Attempts = new List<Attempt>(); Draft = new List<string>(); }
    }

    [DataContract]
    public sealed class WrongRecord
    {
        [DataMember] public string BankId { get; set; }
        [DataMember] public string QuestionId { get; set; }
        [DataMember] public int Count { get; set; }
        [DataMember] public List<string> LastSelected { get; set; }
        [DataMember] public string LastAt { get; set; }
        public WrongRecord() { BankId = QuestionId = LastAt = ""; LastSelected = new List<string>(); }
    }

    [DataContract]
    public sealed class AppState
    {
        [DataMember] public List<Bank> Banks { get; set; }
        [DataMember] public List<WrongRecord> Wrongs { get; set; }
        [DataMember] public Session Active { get; set; }
        [DataMember] public string SelectedBankId { get; set; }
        [DataMember] public bool Dark { get; set; }
        [DataMember] public double Zoom { get; set; }
        [DataMember] public bool ShowAnswers { get; set; }
        [DataMember] public bool ImmediateScoring { get; set; }
        public AppState() { Banks = new List<Bank>(); Wrongs = new List<WrongRecord>(); SelectedBankId = ""; Zoom = 1; }
    }

    public static class XmlQuiz
    {
        private const long MaxXmlCharacters = 32L * 1024L * 1024L;

        public static Bank ParseFile(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("请选择 XML 题库文件。");
            if (new FileInfo(path).Length > MaxXmlCharacters) throw new FormatException("题库文件过大，最多支持 32 MB。");
            XDocument document;
            using (FileStream stream = File.OpenRead(path))
            using (XmlReader reader = XmlReader.Create(stream, ReaderSettings()))
                document = ReadDocument(reader);
            // Canonical UTF-8 XML keeps re-export correct even when the input was UTF-16/GBK.
            document.Declaration = new XDeclaration("1.0", "utf-8", null);
            string xml = document.Declaration.ToString() + Environment.NewLine + document.ToString(SaveOptions.DisableFormatting);
            return ParseDocument(document, xml, Path.GetFileName(path));
        }

        public static Bank Parse(string xml, string filename)
        {
            if (String.IsNullOrWhiteSpace(xml)) throw new FormatException("XML 题库内容为空。");
            if (xml.Length > MaxXmlCharacters) throw new FormatException("题库内容过大，最多支持 32 MB。");
            XDocument document;
            using (StringReader input = new StringReader(xml))
            using (XmlReader reader = XmlReader.Create(input, ReaderSettings())) document = ReadDocument(reader);
            // String input already consists of decoded characters, so normalize the declaration.
            document.Declaration = new XDeclaration("1.0", "utf-8", null);
            string normalizedXml = document.Declaration.ToString() + Environment.NewLine + document.ToString(SaveOptions.DisableFormatting);
            return ParseDocument(document, normalizedXml, filename);
        }

        private static XmlReaderSettings ReaderSettings()
        {
            return new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxXmlCharacters,
                MaxCharactersFromEntities = 1024,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            };
        }

        private static XDocument ReadDocument(XmlReader reader)
        {
            try { return XDocument.Load(reader, LoadOptions.SetLineInfo); }
            catch (XmlException error)
            {
                throw new FormatException(String.Format(CultureInfo.InvariantCulture,
                    "XML 格式错误（第 {0} 行，第 {1} 列）：{2}", error.LineNumber, error.LinePosition, error.Message), error);
            }
        }

        private static bool Is(XElement element, string name)
        {
            return String.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);
        }

        private static string Attribute(XElement element, string name)
        {
            XAttribute attribute = element.Attributes().FirstOrDefault(a => String.Equals(a.Name.LocalName, name, StringComparison.OrdinalIgnoreCase));
            return attribute == null ? "" : attribute.Value.Trim();
        }

        private static string ChildText(XElement element, params string[] names)
        {
            foreach (string name in names)
            {
                XElement child = element.Elements().FirstOrDefault(e => Is(e, name));
                if (child != null && !String.IsNullOrWhiteSpace(child.Value)) return child.Value.Trim();
            }
            return "";
        }

        private static string CleanText(string text)
        {
            return Regex.Replace(text ?? "", @"\s+", " ").Trim();
        }

        private static Bank ParseDocument(XDocument document, string xml, string filename)
        {
            if (document.Root == null || !Is(document.Root, "quiz")) throw new FormatException("XML 根节点必须为 <quiz>。");
            Bank bank = new Bank();
            bank.Name = Attribute(document.Root, "title");
            if (String.IsNullOrWhiteSpace(bank.Name)) bank.Name = Attribute(document.Root, "name");
            if (String.IsNullOrWhiteSpace(bank.Name)) bank.Name = ChildText(document.Root, "title");
            if (String.IsNullOrWhiteSpace(bank.Name)) bank.Name = Path.GetFileNameWithoutExtension(filename ?? "题库.xml");
            if (String.IsNullOrWhiteSpace(bank.Name)) bank.Name = "未命名题库";
            bank.ImportedAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            bank.Xml = xml;
            Dictionary<string, int> occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (XElement node in document.Root.Descendants().Where(e => Is(e, "question")))
            {
                int number = bank.Questions.Count + 1;
                try
                {
                    string type = Attribute(node, "type");
                    if (String.Equals(type, "fill_blank", StringComparison.OrdinalIgnoreCase))
                        throw new FormatException("桌面版仅支持选择题和判断题，不支持填空题。");
                    Question question = new Question();
                    question.Number = number;
                    question.Text = CleanText(ChildText(node, "text", "prompt"));
                    question.Explanation = ChildText(node, "explanation", "analysis");
                    if (question.Text.Length == 0) throw new FormatException("题干 <text> 不能为空。");
                    foreach (XElement option in node.Descendants().Where(e => Is(e, "option")))
                    {
                        string id = Attribute(option, "id").ToUpperInvariant();
                        string text = CleanText(option.Value);
                        if (id.Length == 0) throw new FormatException("每个选项必须有非空的 id。");
                        if (Regex.IsMatch(id, @"[\s,，、;；|/+]")) throw new FormatException("选项 id 不能包含空格或答案分隔符。");
                        if (text.Length == 0) throw new FormatException("选项 " + id + " 的内容不能为空。");
                        if (question.Options.Any(o => o.Id == id)) throw new FormatException("选项 id 重复：" + id + "。");
                        question.Options.Add(new Option { Id = id, Text = text });
                    }
                    if (question.Options.Count < 2) throw new FormatException("选择题至少需要两个选项。");
                    string answer = ChildText(node, "answer", "correct_answer").ToUpperInvariant();
                    question.Answers = ParseAnswers(answer, question.Options);
                    string canonical = CanonicalQuestion(question);
                    string baseId = Hash(canonical);
                    int count;
                    occurrences.TryGetValue(baseId, out count);
                    occurrences[baseId] = ++count;
                    question.Id = baseId + (count > 1 ? "-" + count.ToString(CultureInfo.InvariantCulture) : "");
                    bank.Questions.Add(question);
                }
                catch (FormatException error)
                {
                    IXmlLineInfo line = node;
                    throw new FormatException(String.Format(CultureInfo.InvariantCulture,
                        "第 {0} 题{1}：{2}", number, line.HasLineInfo() ? "（XML 第 " + line.LineNumber + " 行）" : "", error.Message), error);
                }
            }
            if (bank.Questions.Count == 0) throw new FormatException("XML 中没有可导入的 <question> 题目。");
            // The identity follows question content, independent of filename/title/indentation.
            bank.Id = Hash(String.Join("\n", bank.Questions.Select(q => q.Id).ToArray()));
            return bank;
        }

        private static List<string> ParseAnswers(string raw, List<Option> options)
        {
            if (String.IsNullOrWhiteSpace(raw)) throw new FormatException("缺少正确答案 <answer>。");
            HashSet<string> ids = new HashSet<string>(options.Select(o => o.Id), StringComparer.Ordinal);
            string[] tokens = Regex.Split(raw.Trim(), @"[\s,，、;；|/＋+]+").Where(t => t.Length > 0).ToArray();
            IEnumerable<string> result;
            if (tokens.Length > 0 && tokens.All(ids.Contains)) result = tokens;
            else if (tokens.Length == 1 && ids.All(id => id.Length == 1) && raw.All(ch => ids.Contains(ch.ToString())))
                result = raw.Select(ch => ch.ToString());
            else throw new FormatException("正确答案必须完整匹配选项 id；多选可使用 A,B 或 AB。当前答案：" + raw);
            HashSet<string> answers = new HashSet<string>(result, StringComparer.Ordinal);
            if (answers.Count == 0) throw new FormatException("缺少有效的正确答案。");
            return options.Where(o => answers.Contains(o.Id)).Select(o => o.Id).ToList();
        }

        private static string CanonicalQuestion(Question question)
        {
            StringBuilder text = new StringBuilder();
            Action<string> append = delegate(string part) { text.Append(part.Length).Append(':').Append(part); };
            append(question.Text);
            foreach (Option option in question.Options) { append(option.Id); append(option.Text); }
            foreach (string answer in question.Answers) append(answer);
            append(question.Explanation);
            return text.ToString();
        }

        private static string Hash(string text)
        {
            using (SHA256 hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
    }

    // Bank edits are validated completely before any live state is changed.
    // Question identities remain content-based so unchanged history can be retained.
    public static class BankManager
    {
        private static string Name(string value)
        {
            string name = (value ?? "").Trim();
            if (name.Length == 0) throw new ArgumentException("题库名称不能为空。");
            if (name.Length > 120) throw new ArgumentException("题库名称最多 120 个字符。");
            if (name.Any(Char.IsControl)) throw new ArgumentException("题库名称不能包含换行或控制字符。");
            return name;
        }

        private static void State(AppState state)
        {
            if (state == null || state.Banks == null || state.Wrongs == null) throw new ArgumentException("题库数据不完整。");
        }

        private static Bank Find(AppState state, string bankId)
        {
            State(state);
            Bank bank = state.Banks.FirstOrDefault(b => b.Id == bankId);
            if (bank == null) throw new ArgumentException("题库不存在或已被删除。");
            return bank;
        }

        private static void UniqueName(AppState state, string name, string excludedId)
        {
            if (state.Banks.Any(b => b.Id != excludedId && String.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("已有同名题库，请使用其他名称。");
        }

        private static void UniqueContent(AppState state, string id, string excludedId)
        {
            if (state.Banks.Any(b => b.Id != excludedId && b.Id == id))
                throw new ArgumentException("已有内容相同的题库，请使用已有题库。");
        }

        // Existing imported XML keeps its extra metadata. The fallback supports older
        // backups that contain parsed questions but no exportable XML text.
        public static string CurrentXml(Bank bank)
        {
            if (bank == null) throw new ArgumentException("题库不存在。");
            if (!String.IsNullOrWhiteSpace(bank.Xml)) return bank.Xml;
            var root = new XElement("quiz", new XAttribute("title", bank.Name ?? "题库"));
            foreach (Question question in bank.Questions)
            {
                var item = new XElement("question", new XAttribute("type", "multiple_choice"), new XAttribute("score", "1"), new XElement("text", question.Text));
                foreach (Option option in question.Options) item.Add(new XElement("option", new XAttribute("id", option.Id), option.Text));
                item.Add(new XElement("answer", String.Join(",", question.Answers)));
                if (!String.IsNullOrEmpty(question.Explanation)) item.Add(new XElement("explanation", question.Explanation));
                root.Add(item);
            }
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + root.ToString(SaveOptions.DisableFormatting);
        }

        public static Bank Preview(string name, string xml)
        {
            name = Name(name);
            // Parse first to enforce the exact same limits, DTD prohibition and quiz rules.
            Bank bank = XmlQuiz.Parse(xml, "题库.xml");
            XDocument document;
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32L * 1024 * 1024 };
            using (var input = new StringReader(xml))
            using (var reader = XmlReader.Create(input, settings)) document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            foreach (XAttribute attribute in document.Root.Attributes().Where(a => String.Equals(a.Name.LocalName, "title", StringComparison.OrdinalIgnoreCase)).ToList()) attribute.Remove();
            document.Root.SetAttributeValue("title", name);
            document.Declaration = new XDeclaration("1.0", "utf-8", null);
            string updatedXml = document.Declaration.ToString() + "\n" + document.ToString(SaveOptions.DisableFormatting);
            // A title edit cannot change any question identity; reuse the validated
            // question model instead of parsing every question a third time.
            bank.Name = name;
            bank.Xml = updatedXml;
            return bank;
        }

        public static Bank Create(AppState state, string name, string xml)
        {
            State(state);
            Bank bank = Preview(name, xml);
            UniqueName(state, bank.Name, null);
            UniqueContent(state, bank.Id, null);
            state.Banks.Add(bank);
            state.SelectedBankId = bank.Id;
            return bank;
        }

        public static Bank Rename(AppState state, string bankId, string name)
        {
            Bank old = Find(state, bankId);
            name = Name(name);
            UniqueName(state, name, bankId);
            Bank parsed = Preview(name, CurrentXml(old));
            Bank bank = new Bank { Id = old.Id, Name = name, ImportedAt = old.ImportedAt, Xml = parsed.Xml, Questions = old.Questions };
            state.Banks[state.Banks.IndexOf(old)] = bank;
            return bank;
        }

        public static bool RequiresProgressReset(AppState state, string bankId, string xml)
        {
            Bank old = Find(state, bankId);
            if (state.Active == null || state.Active.BankId != old.Id) return false;
            Bank parsed = XmlQuiz.Parse(xml, "题库.xml");
            var ids = new HashSet<string>(parsed.Questions.Select(q => q.Id), StringComparer.Ordinal);
            return state.Active.QuestionIds.Any(id => !ids.Contains(id));
        }

        public static Bank Replace(AppState state, string bankId, string name, string xml)
        {
            Bank old = Find(state, bankId);
            Bank bank = Preview(name, xml);
            UniqueName(state, bank.Name, bankId);
            UniqueContent(state, bank.Id, bankId);
            bank.ImportedAt = old.ImportedAt;
            var ids = new HashSet<string>(bank.Questions.Select(q => q.Id), StringComparer.Ordinal);
            var wrongs = new List<WrongRecord>();
            foreach (WrongRecord wrong in state.Wrongs)
            {
                if (wrong.BankId != old.Id) { wrongs.Add(wrong); continue; }
                if (ids.Contains(wrong.QuestionId)) wrongs.Add(new WrongRecord { BankId = bank.Id, QuestionId = wrong.QuestionId, Count = wrong.Count, LastSelected = new List<string>(wrong.LastSelected), LastAt = wrong.LastAt });
            }
            bool clearActive = state.Active != null && state.Active.BankId == old.Id && state.Active.QuestionIds.Any(id => !ids.Contains(id));
            state.Banks[state.Banks.IndexOf(old)] = bank;
            state.Wrongs = wrongs;
            if (state.SelectedBankId == old.Id) state.SelectedBankId = bank.Id;
            if (clearActive) state.Active = null;
            else if (state.Active != null && state.Active.BankId == old.Id) state.Active.BankId = bank.Id;
            return bank;
        }

        public static void Delete(AppState state, string bankId)
        {
            Bank old = Find(state, bankId);
            var wrongs = state.Wrongs.Where(w => w.BankId != old.Id).ToList();
            state.Banks.Remove(old);
            state.Wrongs = wrongs;
            if (state.Active != null && state.Active.BankId == old.Id) state.Active = null;
            if (state.SelectedBankId == old.Id) state.SelectedBankId = state.Banks.Count > 0 ? state.Banks[0].Id : "";
        }
    }

    public static class QuizEngine
    {
        public static Session CreateSession(Bank bank, int start, int end, int count, bool random, string mode)
        {
            if (bank == null || bank.Questions == null || bank.Questions.Count == 0) throw new ArgumentException("请先选择有题目的题库。");
            if (start < 1 || end < start || end > bank.Questions.Count) throw new ArgumentOutOfRangeException("start", "题号范围无效，请填写 1 至 " + bank.Questions.Count + " 之间的起止题号。");
            int available = end - start + 1;
            if (count < 1 || count > available) throw new ArgumentOutOfRangeException("count", "抽题数量必须为 1 至 " + available + "。");
            List<string> ids = bank.Questions.Skip(start - 1).Take(available).Select(q => q.Id).ToList();
            if (random)
            {
                // Cryptographic rejection sampling avoids repeated random seeds and modulo bias.
                using (RandomNumberGenerator generator = RandomNumberGenerator.Create())
                {
                    for (int i = ids.Count - 1; i > 0; i--)
                    {
                        int j = RandomIndex(generator, i + 1);
                        string temporary = ids[i]; ids[i] = ids[j]; ids[j] = temporary;
                    }
                }
            }
            return new Session { BankId = bank.Id, Mode = mode ?? "顺序练习", QuestionIds = ids.Take(count).ToList() };
        }

        private static int RandomIndex(RandomNumberGenerator generator, int length)
        {
            byte[] bytes = new byte[4];
            uint value;
            uint bound = UInt32.MaxValue - (UInt32.MaxValue % (uint)length);
            do { generator.GetBytes(bytes); value = BitConverter.ToUInt32(bytes, 0); } while (value >= bound);
            return (int)(value % (uint)length);
        }

        public static Question Current(Bank bank, Session session)
        {
            if (bank == null || session == null || bank.Id != session.BankId) throw new InvalidOperationException("答题进度与题库不匹配。");
            if (session.QuestionIds == null || session.Position < 0 || session.Position >= session.QuestionIds.Count)
                throw new InvalidOperationException("当前答题进度已结束。");
            Question question = bank.Questions.FirstOrDefault(q => q.Id == session.QuestionIds[session.Position]);
            if (question == null) throw new InvalidOperationException("答题进度中的题目已不存在。");
            return question;
        }

        public static void Submit(AppState state, Session session, Bank bank, List<string> selected)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (session != null && session.FeedbackPending) throw new InvalidOperationException("请先查看本题评分，再进入下一题。");
            if (session == null || session.Completed) throw new InvalidOperationException("本次答题已结算，不能重复提交。");
            if (session.Attempts == null || session.Attempts.Count != session.Position) throw new InvalidOperationException("答题进度异常，不能重复提交。");
            Question question = Current(bank, session);
            if (session.Attempts.Any(a => a.QuestionId == question.Id)) throw new InvalidOperationException("此题已经提交过。");
            HashSet<string> choices = new HashSet<string>((selected ?? new List<string>()).Select(s => (s ?? "").Trim().ToUpperInvariant()), StringComparer.Ordinal);
            if (choices.Count == 0) throw new ArgumentException("请先选择答案。");
            if (choices.Any(id => !question.Options.Any(o => o.Id == id))) throw new ArgumentException("所选答案中包含无效选项。");
            if (question.Answers.Count == 1 && choices.Count != 1) throw new ArgumentException("单选题只能选择一个答案。");
            List<string> ordered = question.Options.Where(o => choices.Contains(o.Id)).Select(o => o.Id).ToList();
            bool correct = choices.SetEquals(question.Answers);
            session.Attempts.Add(new Attempt { QuestionId = question.Id, Selected = ordered, Correct = correct });
            if (!correct)
            {
                WrongRecord wrong = state.Wrongs.FirstOrDefault(w => w.BankId == bank.Id && w.QuestionId == question.Id);
                if (wrong == null)
                {
                    wrong = new WrongRecord { BankId = bank.Id, QuestionId = question.Id };
                    state.Wrongs.Add(wrong);
                }
                if (wrong.Count < Int32.MaxValue) wrong.Count++;
                wrong.LastSelected = ordered.ToList();
                wrong.LastAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            }
            session.Position++;
            session.Draft = new List<string>();
            session.Completed = session.Position == session.QuestionIds.Count;
            session.FeedbackPending = state.ImmediateScoring;
        }

        public static Question Displayed(Bank bank, Session session)
        {
            if (session == null || !session.FeedbackPending) return Current(bank, session);
            if (bank == null || bank.Id != session.BankId || session.Position < 1 || session.Attempts == null || session.Attempts.Count != session.Position)
                throw new InvalidOperationException("本题评分进度无效。");
            string id = session.QuestionIds[session.Position - 1];
            Question question = bank.Questions.FirstOrDefault(q => q.Id == id);
            if (question == null) throw new InvalidOperationException("本题评分中的题目已不存在。");
            return question;
        }

        public static void AdvanceFeedback(Session session)
        {
            if (session == null || !session.FeedbackPending || session.Position < 1) throw new InvalidOperationException("当前没有待查看的评分。");
            session.FeedbackPending = false;
        }

        public static int Score(Session session)
        {
            return session == null || session.Attempts == null ? 0 : session.Attempts.Count(a => a.Correct);
        }
    }

    public sealed class Store
    {
        private const long MaxStateBytes = 256L * 1024L * 1024L;
        public string DirectoryPath { get; private set; }
        public string LoadWarning { get; private set; }
        private string StatePath { get { return Path.Combine(DirectoryPath, "state.json"); } }
        private string BackupPath { get { return StatePath + ".bak"; } }

        public Store(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory)) throw new ArgumentException("缓存目录不能为空。");
            DirectoryPath = Path.GetFullPath(directory);
            LoadWarning = "";
        }

        public AppState Load()
        {
            LoadWarning = "";
            if (!File.Exists(StatePath) && !File.Exists(BackupPath)) return new AppState();
            Exception primaryError = null;
            if (File.Exists(StatePath))
            {
                try { return Read(StatePath); }
                catch (Exception error) { primaryError = error; }
            }
            if (File.Exists(BackupPath))
            {
                try
                {
                    AppState recovered = Read(BackupPath);
                    if (File.Exists(StatePath))
                    {
                        string damaged = StatePath + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);
                        File.Copy(StatePath, damaged, false);
                    }
                    File.Copy(BackupPath, StatePath, true);
                    LoadWarning = "本地缓存无法读取，已从上一次保存的备份恢复。最近一次保存的更改可能未恢复；损坏文件已保留。";
                    return recovered;
                }
                catch (Exception backupError)
                {
                    throw new IOException("本地缓存及备份均无法恢复。为保护题库与错题记录，程序未覆盖原文件。缓存位置：" + DirectoryPath,
                        new AggregateException(primaryError ?? new IOException("主缓存文件不存在。"), backupError));
                }
            }
            throw new IOException("本地缓存无法读取，且没有可用备份。为保护数据，程序未覆盖原文件。缓存位置：" + DirectoryPath, primaryError);
        }

        public void Save(AppState state)
        {
            Validate(state);
            Directory.CreateDirectory(DirectoryPath);
            string temporary = Path.Combine(DirectoryPath, "state-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    Serializer().WriteObject(stream, state);
                    if (stream.Length > MaxStateBytes)
                        throw new IOException("本地缓存超过 256 MB，无法保存。原缓存及备份未被覆盖，请先导出并清理不再使用的题库。");
                    stream.Flush(true);
                }
                // Replace is atomic on the local Windows filesystem and preserves the prior snapshot.
                if (File.Exists(StatePath)) File.Replace(temporary, StatePath, BackupPath, true);
                else File.Move(temporary, StatePath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static DataContractJsonSerializer Serializer()
        {
            return new DataContractJsonSerializer(typeof(AppState), new DataContractJsonSerializerSettings { MaxItemsInObjectGraph = 5000000 });
        }

        private static AppState Read(string path)
        {
            if (new FileInfo(path).Length > MaxStateBytes) throw new SerializationException("缓存文件超过 256 MB，无法读取。");
            AppState state;
            using (FileStream stream = File.OpenRead(path)) state = Serializer().ReadObject(stream) as AppState;
            Validate(state);
            return state;
        }

        private static void Validate(AppState state)
        {
            if (state == null || state.Banks == null || state.Wrongs == null) throw new SerializationException("缓存缺少题库或错题记录。");
            if (Double.IsNaN(state.Zoom) || Double.IsInfinity(state.Zoom) || state.Zoom < .5 || state.Zoom > 3) state.Zoom = 1;
            if (state.SelectedBankId == null) state.SelectedBankId = "";
            if (state.Banks.Any(b => b == null || String.IsNullOrEmpty(b.Id)) || state.Banks.Select(b => b.Id).Distinct().Count() != state.Banks.Count)
                throw new SerializationException("缓存的题库标识无效或重复。");
            foreach (Bank bank in state.Banks)
            {
                if (String.IsNullOrWhiteSpace(bank.Name) || bank.Questions == null || bank.Questions.Count == 0) throw new SerializationException("缓存题库内容不完整。");
                if (bank.Questions.Any(q => q == null || String.IsNullOrEmpty(q.Id)) || bank.Questions.Select(q => q.Id).Distinct().Count() != bank.Questions.Count)
                    throw new SerializationException("缓存题目标识无效或重复。");
                foreach (Question question in bank.Questions)
                {
                    if (String.IsNullOrWhiteSpace(question.Text) || question.Options == null || question.Options.Count < 2 || question.Answers == null || question.Answers.Count == 0)
                        throw new SerializationException("缓存题目内容不完整。");
                    if (question.Options.Any(o => o == null || String.IsNullOrWhiteSpace(o.Id) || String.IsNullOrWhiteSpace(o.Text)) || question.Options.Select(o => o.Id).Distinct().Count() != question.Options.Count)
                        throw new SerializationException("缓存选项无效。");
                    if (question.Answers.Any(a => !question.Options.Any(o => o.Id == a)) || question.Answers.Distinct().Count() != question.Answers.Count)
                        throw new SerializationException("缓存正确答案无效。");
                    if (question.Explanation == null) question.Explanation = "";
                }
            }
            foreach (WrongRecord wrong in state.Wrongs)
            {
                if (wrong == null || String.IsNullOrEmpty(wrong.BankId) || String.IsNullOrEmpty(wrong.QuestionId) || wrong.Count < 1)
                    throw new SerializationException("缓存错题次数无效。");
                Bank wrongBank = state.Banks.FirstOrDefault(b => b.Id == wrong.BankId);
                Question wrongQuestion = wrongBank == null ? null : wrongBank.Questions.FirstOrDefault(q => q.Id == wrong.QuestionId);
                if (wrongQuestion == null) throw new SerializationException("缓存错题引用的题库或题目不存在。");
                if (wrong.LastSelected == null || wrong.LastSelected.Count == 0 || wrong.LastSelected.Any(a => !wrongQuestion.Options.Any(o => o.Id == a)) || wrong.LastSelected.Distinct().Count() != wrong.LastSelected.Count)
                    throw new SerializationException("缓存错题中的最近作答选项无效。");
            }
            if (state.Wrongs.GroupBy(w => w.BankId + ":" + w.QuestionId).Any(g => g.Count() > 1)) throw new SerializationException("缓存错题记录重复。");
            Session session = state.Active;
            if (session == null) return;
            if (String.IsNullOrWhiteSpace(session.Mode)) session.Mode = "顺序练习";
            Bank activeBank = state.Banks.FirstOrDefault(b => b.Id == session.BankId);
            if (activeBank == null || session.QuestionIds == null || session.QuestionIds.Count == 0 || session.QuestionIds.Distinct().Count() != session.QuestionIds.Count || session.Attempts == null)
                throw new SerializationException("缓存答题进度不完整。");
            if (session.Position < 0 || session.Position > session.QuestionIds.Count || session.Attempts.Count != session.Position || (!session.Completed && session.Position == session.QuestionIds.Count))
                throw new SerializationException("缓存答题位置无效。");
            if (session.QuestionIds.Any(id => !activeBank.Questions.Any(q => q.Id == id))) throw new SerializationException("缓存进度中的题目不存在。");
            for (int i = 0; i < session.Attempts.Count; i++)
            {
                Attempt attempt = session.Attempts[i];
                if (attempt == null || attempt.QuestionId != session.QuestionIds[i] || attempt.Selected == null || attempt.Selected.Count == 0)
                    throw new SerializationException("缓存作答记录顺序无效。");
                Question question = activeBank.Questions.First(q => q.Id == attempt.QuestionId);
                if (attempt.Selected.Any(a => !question.Options.Any(o => o.Id == a)) || attempt.Selected.Distinct().Count() != attempt.Selected.Count)
                    throw new SerializationException("缓存作答选项无效。");
                if (attempt.Correct != new HashSet<string>(attempt.Selected).SetEquals(question.Answers)) throw new SerializationException("缓存成绩校验失败。");
            }
            if (session.Draft == null) session.Draft = new List<string>();
            if (session.FeedbackPending && (session.Position < 1 || session.Draft.Count != 0)) throw new SerializationException("缓存的本题评分进度无效。");
            if (!session.Completed)
            {
                Question current = activeBank.Questions.First(q => q.Id == session.QuestionIds[session.Position]);
                if (session.Draft.Any(a => !current.Options.Any(o => o.Id == a))) throw new SerializationException("缓存草稿答案无效。");
            }
        }
    }
}
