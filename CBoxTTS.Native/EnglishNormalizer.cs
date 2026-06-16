using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CBoxTTS.Native
{
    public static class EnglishNormalizer
    {
        private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
        {
            { "mr.", "mister" },
            { "mrs.", "missis" },
            { "ms.", "miss" },
            { "dr.", "doctor" },
            { "prof.", "professor" },
            { "etc.", "et cetera" },
            { "vs.", "versus" },
            { "st.", "street" },
            { "co.", "company" },
            { "ltd.", "limited" },
            { "inc.", "incorporated" },
            { "ave.", "avenue" },
            { "rd.", "road" },
            { "corp.", "corporation" },
            { "approx.", "approximately" },
            { "dept.", "department" }
        };

        private static readonly string[] Ones = { "", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
        private static readonly string[] Tens = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // 1. 略語の展開
            foreach (var kvp in Abbreviations)
            {
                string pattern = @"\b" + Regex.Escape(kvp.Key);
                text = Regex.Replace(text, pattern, kvp.Value, RegexOptions.IgnoreCase);
            }

            // 2. ドルの置換 ($12,345.67 -> twelve thousand three hundred and forty-five dollars and sixty-seven cents)
            text = Regex.Replace(text, @"\$(\d+(?:,\d+)*(?:\.\d+)?)", m =>
            {
                string valStr = m.Groups[1].Value.Replace(",", "");
                if (valStr.Contains('.'))
                {
                    string[] parts = valStr.Split('.');
                    if (parts.Length == 2)
                    {
                        if (long.TryParse(parts[0], out long dollars))
                        {
                            string centPart = parts[1];
                            if (centPart.Length == 1) centPart += "0";
                            else if (centPart.Length > 2) centPart = centPart.Substring(0, 2);

                            if (long.TryParse(centPart, out long cents))
                            {
                                string dollarStr = dollars == 1 ? "dollar" : "dollars";
                                string centStr = cents == 1 ? "cent" : "cents";

                                if (dollars > 0 && cents > 0)
                                    return $"{NumberToWords(dollars)} {dollarStr} and {NumberToWords(cents)} {centStr}";
                                else if (dollars > 0)
                                    return $"{NumberToWords(dollars)} {dollarStr}";
                                else if (cents > 0)
                                    return $"{NumberToWords(cents)} {centStr}";
                                else
                                    return "zero dollars";
                            }
                        }
                    }
                }
                else
                {
                    if (long.TryParse(valStr, out long dollars))
                    {
                        string dollarStr = dollars == 1 ? "dollar" : "dollars";
                        return $"{NumberToWords(dollars)} {dollarStr}";
                    }
                }
                return m.Value;
            });

            // 3. 小数の置換 (12.34 -> twelve point three four)
            text = Regex.Replace(text, @"\b\d+(?:,\d+)*\.\d+\b", m =>
            {
                string valStr = m.Value.Replace(",", "");
                string[] parts = valStr.Split('.');
                if (parts.Length == 2 && long.TryParse(parts[0], out long integerPart))
                {
                    string decimalPart = parts[1];
                    string[] ones = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };
                    var decimalWords = new List<string>();
                    foreach (char digit in decimalPart)
                    {
                        if (char.IsDigit(digit))
                        {
                            decimalWords.Add(ones[digit - '0']);
                        }
                    }
                    return $"{NumberToWords(integerPart)} point {string.Join(" ", decimalWords)}";
                }
                return m.Value;
            });

            // 4. 整数の置換 (12,345 -> twelve thousand three hundred and forty-five)
            text = Regex.Replace(text, @"\b\d+(?:,\d+)*\b", m =>
            {
                string valStr = m.Value.Replace(",", "");
                if (long.TryParse(valStr, out long num))
                {
                    return NumberToWords(num);
                }
                return m.Value;
            });

            // 5. 記号の置換
            text = text.Replace("%", " percent");
            text = Regex.Replace(text, @"\s+&\s+", " and ");
            text = Regex.Replace(text, @"\b&\b", "and");

            // 6. 余分なスペースの整理
            text = Regex.Replace(text, @"\s+", " ").Trim();

            return text;
        }

        private static string NumberToWords(long number)
        {
            if (number == 0) return "zero";
            if (number < 0) return "minus " + NumberToWords(Math.Abs(number));

            var words = new StringBuilder();

            if ((number / 1000000) > 0)
            {
                words.Append(NumberToWords(number / 1000000) + " million ");
                number %= 1000000;
            }

            if ((number / 1000) > 0)
            {
                words.Append(NumberToWords(number / 1000) + " thousand ");
                number %= 1000;
            }

            if ((number / 100) > 0)
            {
                words.Append(NumberToWords(number / 100) + " hundred ");
                number %= 100;
            }

            if (number > 0)
            {
                if (words.Length > 0) words.Append("and ");

                if (number < 20)
                {
                    words.Append(Ones[number]);
                }
                else
                {
                    words.Append(Tens[number / 10]);
                    if ((number % 10) > 0)
                    {
                        words.Append("-" + Ones[number % 10]);
                    }
                }
            }

            return words.ToString().Trim();
        }
    }
}
