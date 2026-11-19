using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Chooser
{
    internal static class CsvCoreTests
    {
        private const string Header = "题目,A,B,C,D,答案,解析";
        private static int passed;

        public static int Main(string[] args)
        {
            try
            {
                Run("单选、多选、判断题可导入并导出 XML", delegate {
                    string csv = Header + "\r\n2+2等于多少？,3,4,,,B\r\n" +
                        "哪些是偶数？,2,3,4,5,AC,2和4都是偶数。\r\n地球是行星。,正确,错误,,,A";
                    var bank = CsvQuiz.Parse(csv, "课堂题.csv");
                    Check(bank.Name == "课堂题" && bank.Questions.Count == 3, "题库名或题数错误。");
                    Check(bank.Questions[0].Answers.SequenceEqual(new[] { "B" }), "单选答案错误。");
                    Check(bank.Questions[1].Answers.SequenceEqual(new[] { "A", "C" }), "多选答案错误。");
                    Check(bank.Questions[2].Answers.SequenceEqual(new[] { "A" }) && bank.Questions[2].Options.Count == 2, "判断答案或选项错误。");
                    Check(XmlQuiz.Parse(bank.Xml, "导出.xml").Id == bank.Id, "XML 导出后内容标识改变。");
                });
                Run("短表头、无解析行、空行和重排列", delegate {
                    var bank = CsvQuiz.Parse("\uFEFF\n答案,题目,B,A,解析\n\nA,真的吗？,否,是\n", "简表.csv");
                    Check(bank.Questions.Count == 1 && bank.Questions[0].Options[0].Text == "是", "短表头或重排列解析错误。");
                });
                Run("标准引号、英文逗号、CRLF 换行与带逗号多选答案", delegate {
                    string csv = Header + "\r\n\"他说\"\"一,二\"\"\r\n下一行 < & >\",\"A, B\",乙,丙,,\"A,C\",\"解释\r\n换行\"";
                    var question = CsvQuiz.Parse(csv, "引号.csv").Questions.Single();
                    Check(question.Text == "他说\"一,二\" 下一行 < & >" && question.Options[0].Text == "A, B", "题目或选项文本未正确解码。");
                    Check(question.Explanation == "解释\n换行" && question.Answers.SequenceEqual(new[] { "A", "C" }), "解析或带逗号答案错误。");
                });
                Run("报出物理行号并拒绝坏表头和坏题目", delegate {
                    Reject("题目,A,答案\n题干,甲,A", 1, "缺少列");
                    Reject("题目,A,B,答案,A\n题干,甲,乙,A", 1, "重复");
                    Reject(Header, 1, "至少需要一道题");
                    Reject(Header + "\r\n\"合法\r\n换行\",甲,乙,,,A\r\n缺答案,甲,乙,,,", 4, "答案不能为空");
                    Reject(Header + "\n空选项,甲,乙,,,C", 2, "空选项 C");
                    Reject(Header + "\n缺选项,甲,,,,A", 2, "选项 B 不能为空");
                    Reject(Header + "\n重复,甲,乙,,,AA", 2, "不能重复");
                    Reject(Header + "\n\"引号未结束,甲,乙,,,A", 2, "没有闭合");
                    Reject(Header + "\n未正确\"加引号,甲,乙,,,A", 2, "围住整个字段");
                    Reject(Header + "\n含\0控制字符,甲,乙,,,A", 2, "控制字符");
                });
                Run("文件导入支持 UTF-8 及旧版中文 Excel 的 GBK / GB18030", delegate {
                    string path = args[0];
                    try
                    {
                        string csv = Header + "\r\n中文题目,甲,乙,,,A";
                        File.WriteAllText(path, csv, new UTF8Encoding(false));
                        var bank = CsvQuiz.ParseFile(path);
                        File.WriteAllText(path, csv, new UTF8Encoding(true));
                        Check(CsvQuiz.ParseFile(path).Id == bank.Id, "BOM 改变解析或身份。");
                        byte[] legacyBytes = Encoding.GetEncoding(936).GetBytes(csv);
                        File.WriteAllBytes(path, legacyBytes);
                        Check(CsvQuiz.ParseFile(path).Id == bank.Id, "旧版 Excel 的 GBK 文件中文损坏。");
                        File.WriteAllText(path, csv.Replace("中文题目", "生僻字𠮷"), Encoding.GetEncoding(54936));
                        Check(CsvQuiz.ParseFile(path).Questions.Single().Text == "生僻字𠮷", "GB18030 四字节字符中文损坏。");
                        File.WriteAllBytes(path, new UTF8Encoding(true).GetPreamble().Concat(legacyBytes).ToArray());
                        RejectFile(path, "UTF-8");
                        File.WriteAllText(path, csv, Encoding.Unicode);
                        RejectFile(path, "UTF-8");
                        File.WriteAllText(path, Header + "\r\n坏题,甲,乙,,,", new UTF8Encoding(false));
                        RejectFile(path, "第 2 行");
                    }
                    finally { File.Delete(path); }
                });
                Run("随附 CSV 样例有 UTF-8 BOM 和 Windows 换行，可直接导入", delegate {
                    byte[] bytes = File.ReadAllBytes(args[1]);
                    Check(bytes.Take(3).SequenceEqual(new UTF8Encoding(true).GetPreamble()), "CSV 样例缺少 UTF-8 BOM。");
                    string csv = new UTF8Encoding(false, true).GetString(bytes, 3, bytes.Length - 3);
                    string withoutCrlf = csv.Replace("\r\n", "");
                    Check(csv.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries).Length == 4 &&
                        !withoutCrlf.Contains("\n") && !withoutCrlf.Contains("\r"), "CSV 样例有非 Windows 换行，或行数不正确。");
                    var sample = CsvQuiz.ParseFile(args[1]);
                    Check(sample.Questions.Count == 3 && sample.Questions.Any(question => question.Answers.Count == 2),
                        "帮助页样例应包含三题及多选示例。");
                });
                Console.WriteLine("CSV 验证通过 " + passed + " 项。");
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }

        private static void Run(string title, Action check)
        {
            check();
            passed++;
            Console.WriteLine("PASS " + title);
        }

        private static void Reject(string csv, int line, string message)
        {
            try { CsvQuiz.Parse(csv, "错误题库.csv"); }
            catch (FormatException error)
            {
                Check(error.Message.Contains("第 " + line + " 行") && error.Message.Contains(message), "错误行号或提示不符：" + error.Message);
                return;
            }
            throw new Exception("错误的 CSV 未被拒绝。");
        }

        private static void RejectFile(string path, string message)
        {
            try { CsvQuiz.ParseFile(path); }
            catch (FormatException error)
            {
                Check(error.Message.Contains(message), "CSV 导入错误提示不符：" + error.Message);
                return;
            }
            throw new Exception("错误或不支持的 CSV 文件未被拒绝。");
        }

        private static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }
    }
}
