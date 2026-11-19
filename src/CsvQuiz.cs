using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Chooser
{
    public static class CsvQuiz
    {
        private const int MaxFileSize = 32 * 1024 * 1024;
        private const string EncodingError = "无法识别 CSV 编码，请在 Excel 中另存为「CSV UTF-8（逗号分隔）」后重新导入。";
        private static readonly string[] Headings = { "题目", "A", "B", "C", "D", "答案", "解析" };
        private static readonly string[] RequiredHeadings = { "题目", "A", "B", "答案" };
        private static readonly string[] OptionIds = { "A", "B", "C", "D" };

        public static Bank ParseFile(string path)
        {
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("请选择 CSV 题库文件。");
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length > MaxFileSize) throw new FormatException("CSV 文件过大，最多支持 32 MB。");
                return Parse(ReadCsvText(stream), Path.GetFileName(path));
            }
        }

        private static string ReadCsvText(Stream stream)
        {
            int first = stream.ReadByte(), second = stream.ReadByte(), third = stream.ReadByte(), fourth = stream.ReadByte();
            bool utf8Bom = first == 0xEF && second == 0xBB && third == 0xBF;
            bool otherBom = (first == 0xFF && second == 0xFE) || (first == 0xFE && second == 0xFF) ||
                (first == 0 && second == 0 && third == 0xFE && fourth == 0xFF);
            if (otherBom) throw new FormatException(EncodingError);
            try
            {
                return ReadText(stream, new UTF8Encoding(false, true));
            }
            catch (DecoderFallbackException utf8Error)
            {
                if (utf8Bom) throw new FormatException(EncodingError, utf8Error);
            }

            // Older Chinese Excel versions commonly write GBK. GB18030 also covers GBK.
            try
            {
                return ReadText(stream, Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback));
            }
            catch (DecoderFallbackException legacyError)
            {
                throw new FormatException(EncodingError, legacyError);
            }
        }

        private static string ReadText(Stream stream, Encoding encoding)
        {
            stream.Position = 0;
            using (var reader = new StreamReader(stream, encoding, false, 4096, true))
                return reader.ReadToEnd();
        }

        public static Bank Parse(string csv, string fileName)
        {
            if (String.IsNullOrWhiteSpace(csv)) throw new FormatException("CSV 题库内容为空。");
            if (csv.Length > MaxFileSize) throw new FormatException("CSV 题库内容过大，最多支持 32 MB。");

            using (var rows = ReadRows(csv).GetEnumerator())
            {
                if (!rows.MoveNext()) throw new FormatException("CSV 题库内容为空。");
                Row header = rows.Current;
                var columns = ReadHeadings(header);
                var xml = new StringBuilder("<quiz>");
                int questions = 0;
                while (rows.MoveNext())
                {
                    Row row = rows.Current;
                    if (row.Fields.Count > header.Fields.Count)
                        throw Error(row.Line, "这一行的列比表头多。正文中的英文逗号需放在双引号内。");
                    xml.Append(QuestionXml(row, columns));
                    if (xml.Length > MaxFileSize - "</quiz>".Length)
                        throw Error(row.Line, "转成题库后超过 32 MB，请拆成多个文件。");
                    questions++;
                }
                if (questions == 0) throw Error(header.Line, "表头下面至少需要一道题。");
                xml.Append("</quiz>");
                return XmlQuiz.Parse(xml.ToString(), fileName ?? "题库.csv");
            }
        }

        private static Dictionary<string, int> ReadHeadings(Row header)
        {
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Fields.Count; i++)
            {
                string name = header.Fields[i].Trim();
                if (!Headings.Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw Error(header.Line, "无法识别第 " + (i + 1) + " 列表头。请使用：题目,A,B,C,D,答案,解析。");
                if (columns.ContainsKey(name)) throw Error(header.Line, "表头「" + name + "」重复。");
                columns.Add(name, i);
            }
            string[] missing = RequiredHeadings.Where(heading => !columns.ContainsKey(heading)).ToArray();
            if (missing.Length > 0) throw Error(header.Line, "缺少列：" + String.Join("、", missing) + "。");
            return columns;
        }

        private static string QuestionXml(Row row, Dictionary<string, int> columns)
        {
            string text = Cell(row, columns, "题目");
            if (text.Length == 0) throw Error(row.Line, "题目不能为空。");
            var question = new XElement("question", new XElement("text", text));
            var options = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in OptionIds)
            {
                string option = Cell(row, columns, id);
                if (option.Length == 0)
                {
                    if (id == "A" || id == "B") throw Error(row.Line, "选项 " + id + " 不能为空。");
                    continue;
                }
                options.Add(id);
                question.Add(new XElement("option", new XAttribute("id", id), option));
            }

            string answer = Cell(row, columns, "答案");
            if (answer.Length == 0) throw Error(row.Line, "答案不能为空。");
            if (answer.Length > 64) throw Error(row.Line, "答案只需填写选项字母，例如 B 或 AC。");
            answer = answer.ToUpperInvariant();
            string[] answers = answer.Contains(",")
                ? answer.Split(',').Select(item => item.Trim()).ToArray()
                : answer.Select(letter => letter.ToString()).ToArray();
            foreach (string id in answers)
            {
                if (!OptionIds.Contains(id))
                    throw Error(row.Line, "答案请填写 A、B、C、D 中的选项字母。多选可以写成 AC。");
                if (!options.Contains(id)) throw Error(row.Line, "答案引用了空选项 " + id + "。");
            }
            if (answers.Distinct().Count() != answers.Length) throw Error(row.Line, "答案中的选项字母不能重复。");
            question.Add(new XElement("answer", String.Join(",", answers)));

            string explanation = Cell(row, columns, "解析");
            if (explanation.Length > 0) question.Add(new XElement("explanation", explanation));
            try { return question.ToString(SaveOptions.DisableFormatting); }
            catch (ArgumentException error)
            {
                throw Error(row.Line, "题目内容包含无法导入的控制字符。", error);
            }
        }

        private static string Cell(Row row, Dictionary<string, int> columns, string key)
        {
            int index;
            return columns.TryGetValue(key, out index) && index < row.Fields.Count ? row.Fields[index].Trim() : "";
        }

        private sealed class Row
        {
            public readonly int Line;
            public readonly List<string> Fields = new List<string>(Headings.Length);

            public Row(int line) { Line = line; }
        }

        private static IEnumerable<Row> ReadRows(string csv)
        {
            int position = csv[0] == '\uFEFF' ? 1 : 0;
            int line = 1;
            while (position < csv.Length)
            {
                var row = new Row(line);
                while (true)
                {
                    if (row.Fields.Count == Headings.Length)
                        throw Error(row.Line, "最多支持 7 列。正文中的英文逗号需放在双引号内。");
                    row.Fields.Add(ReadField(csv, ref position, ref line));
                    if (position == csv.Length) break;
                    if (csv[position] != ',')
                    {
                        ReadLineBreak(csv, ref position, ref line);
                        break;
                    }
                    position++;
                }
                if (row.Fields.Any(field => !String.IsNullOrWhiteSpace(field))) yield return row;
            }
        }

        private static string ReadField(string csv, ref int position, ref int line)
        {
            if (position == csv.Length) return "";
            if (csv[position] != '"')
            {
                int start = position;
                while (position < csv.Length && !IsSeparator(csv[position]))
                {
                    if (csv[position] == '"') throw Error(line, "双引号必须围住整个字段，字段里的双引号写成两个。");
                    position++;
                }
                return csv.Substring(start, position - start);
            }

            int firstLine = line;
            position++;
            var field = new StringBuilder();
            while (position < csv.Length)
            {
                char character = csv[position++];
                if (character == '"')
                {
                    if (position == csv.Length || IsSeparator(csv[position])) return field.ToString();
                    if (csv[position] != '"') throw Error(line, "字段的结束双引号后应为英文逗号或换行。");
                    position++;
                }
                else if (character == '\r' || character == '\n')
                {
                    position--;
                    ReadLineBreak(csv, ref position, ref line);
                    character = '\n';
                }
                field.Append(character);
            }
            throw Error(firstLine, "字段开头的双引号没有闭合。");
        }

        private static bool IsSeparator(char value)
        {
            return value == ',' || value == '\r' || value == '\n';
        }

        private static void ReadLineBreak(string csv, ref int position, ref int line)
        {
            if (csv[position++] == '\r' && position < csv.Length && csv[position] == '\n') position++;
            line++;
        }

        private static FormatException Error(int line, string message, Exception inner = null)
        {
            return new FormatException("CSV 第 " + line + " 行：" + message, inner);
        }
    }
}
