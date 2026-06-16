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

            // 1. 記号の展開 ($ や % など)
            text = text.Replace("%", " percent");
            // $100 -> 100 dollars のような簡易変換
            text = Regex.Replace(text, @"\$(\d+)", "$1 dollars");

            // 2. 単語境界で略語を展開
            foreach (var kvp in Abbreviations)
            {
                // 単語境界を意識して置換
                string pattern = @"\b" + Regex.Escape(kvp.Key);
                text = Regex.Replace(text, pattern, kvp.Value, RegexOptions.IgnoreCase);
            }

            // 3. 数値の展開 (\b\d+\b)
            text = Regex.Replace(text, @"\b\d+\b", m =>
            {
                if (long.TryParse(m.Value, out long num))
                {
                    return NumberToWords(num);
                }
                return m.Value;
            });

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
