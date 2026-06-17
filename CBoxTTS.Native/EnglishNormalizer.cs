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
            { "dept.", "department" },
            { "govt.", "government" },
            { "jr.", "junior" },
            { "sr.", "senior" },
            { "gen.", "general" },
            { "sgt.", "sergeant" },
            { "capt.", "captain" },
            { "vol.", "volume" },
            { "fig.", "figure" },
            { "no.", "number" },
            { "jan.", "january" },
            { "feb.", "february" },
            { "mar.", "march" },
            { "apr.", "april" },
            { "jun.", "june" },
            { "jul.", "july" },
            { "aug.", "august" },
            { "sep.", "september" },
            { "sept.", "september" },
            { "oct.", "october" },
            { "nov.", "november" },
            { "dec.", "december" }
        };

        // 序数のマッピング（1st〜19th）
        private static readonly Dictionary<int, string> OrdinalWords = new()
        {
            { 1, "first" }, { 2, "second" }, { 3, "third" }, { 4, "fourth" },
            { 5, "fifth" }, { 6, "sixth" }, { 7, "seventh" }, { 8, "eighth" },
            { 9, "ninth" }, { 10, "tenth" }, { 11, "eleventh" }, { 12, "twelfth" },
            { 13, "thirteenth" }, { 14, "fourteenth" }, { 15, "fifteenth" },
            { 16, "sixteenth" }, { 17, "seventeenth" }, { 18, "eighteenth" },
            { 19, "nineteenth" }
        };

        // 十の位の序数
        private static readonly Dictionary<int, string> OrdinalTens = new()
        {
            { 20, "twentieth" }, { 30, "thirtieth" }, { 40, "fortieth" },
            { 50, "fiftieth" }, { 60, "sixtieth" }, { 70, "seventieth" },
            { 80, "eightieth" }, { 90, "ninetieth" }
        };

        private static readonly string[] Ones = { "", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
        private static readonly string[] TensArr = { "", "", "twenty", "thirty", "forty", "fifty", "sixty", "seventy", "eighty", "ninety" };

        // コンパイル済み正規表現キャッシュ（パフォーマンス最適化・ReDoS軽減）
        private static readonly Regex UrlRegex = new Regex(@"https?://\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex EmailRegex = new Regex(@"\b[\w.+-]+@[\w.-]+\.\w+\b", RegexOptions.Compiled);
        private static readonly Regex OrdinalRegex = new Regex(@"\b(\d+)(st|nd|rd|th)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex TimeRegex = new Regex(@"\b(\d{1,2}):(\d{2})(?:\s*(am|pm|a\.m\.|p\.m\.))?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex YearRegex = new Regex(@"\b(1[0-9]{3}|20[0-9]{2})\b", RegexOptions.Compiled);
        private static readonly Regex FractionRegex = new Regex(@"\b(\d+)/(\d+)\b", RegexOptions.Compiled);
        private static readonly Regex AcronymRegex = new Regex(@"\b([A-Z]{2,6})\b", RegexOptions.Compiled);
        private static readonly Regex DollarRegex = new Regex(@"\$(\d+(?:,\d+)*(?:\.\d+)?)", RegexOptions.Compiled);
        private static readonly Regex PoundRegex = new Regex(@"£(\d+(?:,\d+)*(?:\.\d+)?)", RegexOptions.Compiled);
        private static readonly Regex DecimalRegex = new Regex(@"\b\d+(?:,\d+)*\.\d+\b", RegexOptions.Compiled);
        private static readonly Regex IntegerRegex = new Regex(@"\b\d+(?:,\d+)*\b", RegexOptions.Compiled);
        private static readonly Regex AmpSpacedRegex = new Regex(@"\s+&\s+", RegexOptions.Compiled);
        private static readonly Regex AmpWordRegex = new Regex(@"\b&\b", RegexOptions.Compiled);
        private static readonly Regex MultiSpaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        // 頭字語の例外（そのまま発音する既知の頭字語）
        private static readonly HashSet<string> KnownAcronymsAsWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "NASA", "NATO", "ASAP", "SCUBA", "LASER", "RADAR", "AIDS", "JPEG", "GIF"
        };

        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            if (!_userDictLoaded)
            {
                LoadUserDictionary(AppDomain.CurrentDomain.BaseDirectory);
            }

            // ユーザー辞書による完全一致および単語単位の置換 (単語境界 \b を考慮)
            foreach (var kv in UserDictionary)
            {
                string escapedWord = Regex.Escape(kv.Key);
                string pattern = $@"\b{escapedWord}\b";
                text = Regex.Replace(text, pattern, kv.Value, RegexOptions.IgnoreCase);
            }

            // 0. URL・メールアドレスの除去（TTSで発音不能なため）
            text = UrlRegex.Replace(text, "");
            text = EmailRegex.Replace(text, "");


            // 1. 略語の展開
            foreach (var kvp in Abbreviations)
            {
                string pattern = @"\b" + Regex.Escape(kvp.Key);
                text = Regex.Replace(text, pattern, kvp.Value, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }

            // 2. 序数の変換 (1st → first, 22nd → twenty-second)
            text = OrdinalRegex.Replace(text, m =>
            {
                if (int.TryParse(m.Groups[1].Value, out int num) && num > 0 && num <= 100)
                {
                    return NumberToOrdinalWord(num);
                }
                return m.Value;
            });

            // 3. 時刻の変換 (3:00 → three o'clock, 3:30 pm → three thirty p m)
            text = TimeRegex.Replace(text, m =>
            {
                if (int.TryParse(m.Groups[1].Value, out int hour) && int.TryParse(m.Groups[2].Value, out int minute))
                {
                    string hourWord = NumberToWords(hour);
                    string ampm = m.Groups[3].Success ? " " + m.Groups[3].Value.Replace(".", " ").Replace("  ", " ").Trim() : "";
                    
                    if (minute == 0)
                        return $"{hourWord} o'clock{ampm}";
                    else
                    {
                        string minuteWord = NumberToWords(minute);
                        if (minute < 10) minuteWord = "oh " + minuteWord;
                        return $"{hourWord} {minuteWord}{ampm}";
                    }
                }
                return m.Value;
            });

            // 4. 年号の変換 (2024 → twenty twenty-four, 1999 → nineteen ninety-nine)
            text = YearRegex.Replace(text, m =>
            {
                if (int.TryParse(m.Value, out int year))
                {
                    return YearToWords(year);
                }
                return m.Value;
            });

            // 5. 分数の変換 (1/2 → one half, 3/4 → three quarters)
            text = FractionRegex.Replace(text, m =>
            {
                if (int.TryParse(m.Groups[1].Value, out int num) && int.TryParse(m.Groups[2].Value, out int den))
                {
                    return FractionToWords(num, den);
                }
                return m.Value;
            });

            // 6. ドルの置換 ($12,345.67 → twelve thousand three hundred and forty-five dollars and sixty-seven cents)
            text = DollarRegex.Replace(text, m => CurrencyToWords(m.Groups[1].Value, "dollar", "dollars", "cent", "cents"));

            // 7. ポンドの置換 (£100 → one hundred pounds)
            text = PoundRegex.Replace(text, m => CurrencyToWords(m.Groups[1].Value, "pound", "pounds", "penny", "pence"));

            // 8. 小数の置換 (12.34 → twelve point three four)
            text = DecimalRegex.Replace(text, m =>
            {
                string valStr = m.Value.Replace(",", "");
                string[] parts = valStr.Split('.');
                if (parts.Length == 2 && long.TryParse(parts[0], out long integerPart))
                {
                    string decimalPart = parts[1];
                    string[] digitNames = { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };
                    var decimalWords = new List<string>();
                    foreach (char digit in decimalPart)
                    {
                        if (char.IsDigit(digit))
                        {
                            decimalWords.Add(digitNames[digit - '0']);
                        }
                    }
                    string intPartWord = integerPart == 0 ? "zero" : NumberToWords(integerPart);
                    return $"{intPartWord} point {string.Join(" ", decimalWords)}";
                }
                return m.Value;
            });

            // 9. 整数の置換 (12,345 → twelve thousand three hundred and forty-five)
            text = IntegerRegex.Replace(text, m =>
            {
                string valStr = m.Value.Replace(",", "");
                if (long.TryParse(valStr, out long num))
                {
                    return NumberToWords(num);
                }
                return m.Value;
            });

            // 11. 記号の置換
            text = text.Replace("%", " percent");
            text = text.Replace("@", " at ");
            text = text.Replace("#", " number ");
            text = text.Replace("+", " plus ");
            text = text.Replace("=", " equals ");
            text = AmpSpacedRegex.Replace(text, " and ");
            text = AmpWordRegex.Replace(text, "and");

            // 12. 余分なスペースの整理
            text = MultiSpaceRegex.Replace(text, " ").Trim();

            return text;
        }

        /// <summary>
        /// 通貨文字列を英語の読みに変換するヘルパー。
        /// </summary>
        private static string CurrencyToWords(string valGroup, string singUnit, string plurUnit, string singSubunit, string plurSubunit)
        {
            string valStr = valGroup.Replace(",", "");
            if (valStr.Contains('.'))
            {
                string[] parts = valStr.Split('.');
                if (parts.Length == 2)
                {
                    if (long.TryParse(parts[0], out long main))
                    {
                        string subPart = parts[1];
                        if (subPart.Length == 1) subPart += "0";
                        else if (subPart.Length > 2) subPart = subPart.Substring(0, 2);

                        if (long.TryParse(subPart, out long sub))
                        {
                            string mainStr = main == 1 ? singUnit : plurUnit;
                            string subStr = sub == 1 ? singSubunit : plurSubunit;

                            if (main > 0 && sub > 0)
                                return $"{NumberToWords(main)} {mainStr} and {NumberToWords(sub)} {subStr}";
                            else if (main > 0)
                                return $"{NumberToWords(main)} {mainStr}";
                            else if (sub > 0)
                                return $"{NumberToWords(sub)} {subStr}";
                            else
                                return $"zero {plurUnit}";
                        }
                    }
                }
            }
            else
            {
                if (long.TryParse(valStr, out long main))
                {
                    string mainStr = main == 1 ? singUnit : plurUnit;
                    return $"{NumberToWords(main)} {mainStr}";
                }
            }
            return valStr;
        }

        /// <summary>
        /// 数値を序数英語に変換する（1→first, 22→twenty-second）。
        /// </summary>
        private static string NumberToOrdinalWord(int num)
        {
            if (num <= 0) return num.ToString();

            if (num <= 19 && OrdinalWords.TryGetValue(num, out string? word))
                return word;

            if (num < 100)
            {
                int tens = (num / 10) * 10;
                int ones = num % 10;

                if (ones == 0 && OrdinalTens.TryGetValue(tens, out string? tensOrd))
                    return tensOrd;

                // 21st → twenty-first
                string tensWord = TensArr[num / 10];
                if (ones > 0 && OrdinalWords.TryGetValue(ones, out string? onesOrd))
                    return $"{tensWord}-{onesOrd}";

                return tensWord;
            }

            // 100th → one hundredth
            if (num == 100) return "one hundredth";

            return NumberToWords(num) + "th";
        }

        /// <summary>
        /// 年号を英語読みに変換する（2024→twenty twenty-four, 1999→nineteen ninety-nine）。
        /// </summary>
        private static string YearToWords(int year)
        {
            // 2000, 1000 などの切りの良い年
            if (year % 100 == 0)
            {
                int century = year / 100;
                return NumberToWords(century) + " hundred";
            }

            // 2001-2009 のような年
            if (year >= 2000 && year < 2010)
            {
                return "two thousand and " + NumberToWords(year % 100);
            }

            // 2010-2099, 1910-1999 などの一般的なパターン
            int first = year / 100;
            int second = year % 100;
            string firstPart = NumberToWords(first);

            if (second < 10)
                return $"{firstPart} oh {NumberToWords(second)}";
            else
                return $"{firstPart} {NumberToWords(second)}";
        }

        /// <summary>
        /// 分数を英語読みに変換する（1/2→one half, 3/4→three quarters）。
        /// </summary>
        private static string FractionToWords(int numerator, int denominator)
        {
            if (denominator == 0) return $"{numerator}/{denominator}";

            // 特殊な分数
            if (denominator == 2)
            {
                string numWord = numerator == 1 ? "one" : NumberToWords(numerator);
                return numerator == 1 ? "one half" : $"{numWord} halves";
            }

            if (denominator == 4)
            {
                string numWord = numerator == 1 ? "one" : NumberToWords(numerator);
                return numerator == 1 ? "one quarter" : $"{numWord} quarters";
            }

            // 一般的な分数
            string num = NumberToWords(numerator);
            string den = NumberToOrdinalWord(denominator);
            if (numerator != 1) den += "s";
            return $"{num} {den}";
        }

        private static string NumberToWords(long number)
        {
            if (number == 0) return "zero";
            if (number < 0)
            {
                // long.MinValue の Math.Abs() は OverflowException をスローするためガード
                long absValue = (number == long.MinValue) ? long.MaxValue : Math.Abs(number);
                return "minus " + NumberToWords(absValue);
            }

            var words = new StringBuilder();

            if ((number / 1000000000) > 0)
            {
                words.Append(NumberToWords(number / 1000000000) + " billion ");
                number %= 1000000000;
            }

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
                    words.Append(TensArr[number / 10]);
                    if ((number % 10) > 0)
                    {
                        words.Append("-" + Ones[number % 10]);
                    }
                }
            }

            return words.ToString().Trim();
        }

        private static readonly Dictionary<string, string> UserDictionary = new(StringComparer.OrdinalIgnoreCase);
        private static bool _userDictLoaded = false;
        private static readonly object _lock = new();

        public static void LoadUserDictionary(string baseDir)
        {
            lock (_lock)
            {
                UserDictionary.Clear();
                string path = System.IO.Path.Combine(baseDir, "user_dict_en.txt");
                if (!System.IO.File.Exists(path))
                {
                    // 親ディレクトリも探索（デバッグ環境用）
                    var parent = System.IO.Directory.GetParent(baseDir);
                    if (parent != null)
                    {
                        path = System.IO.Path.Combine(parent.FullName, "user_dict_en.txt");
                    }
                }

                if (System.IO.File.Exists(path))
                {
                    try
                    {
                        var lines = System.IO.File.ReadAllLines(path, Encoding.UTF8);
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
                                continue;

                            var parts = trimmed.Split(',');
                            if (parts.Length >= 2)
                            {
                                string word = parts[0].Trim();
                                string read = parts[1].Trim();
                                if (!string.IsNullOrEmpty(word))
                                {
                                    UserDictionary[word] = read;
                                }
                            }
                        }
                    }
                    catch
                    {
                        // 失敗時は無視
                    }
                }
                _userDictLoaded = true;
            }
        }
    }
}
