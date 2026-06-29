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

        // 頭字語の例外（そのまま単語として発音する既知の頭字語）
        // これらは文字ごとのスペルアウトを行わず、単語として読み上げる
        private static readonly HashSet<string> KnownAcronymsAsWords = new(StringComparer.OrdinalIgnoreCase)
        {
            // 一般的
            "NASA", "NATO", "ASAP", "SCUBA", "LASER", "RADAR", "AIDS", "JPEG", "GIF",
            // PC業界
            "BIOS", "LAN", "WAN", "RAM", "SIM", "VRAM",
            // FA業界
            "SCADA", "PID"
        };

        // 一般英単語の大文字形（頭字語として処理してはならない）
        // 全大文字で書かれたテキスト内の一般的な英単語を保護する
        private static readonly HashSet<string> KnownCommonWords = new(StringComparer.Ordinal)
        {
            // 2文字
            "IT", "IS", "IN", "AT", "UP", "ON", "NO", "SO", "DO", "WE", "HE", "ME",
            "MY", "BE", "IF", "OR", "BY", "AN", "AS", "AM", "OF", "TO",
            // 3文字
            "THE", "AND", "FOR", "ARE", "BUT", "NOT", "YOU", "ALL", "ANY", "CAN",
            "HER", "WAS", "ONE", "OUR", "OUT", "DAY", "HAD", "HAS", "HIS", "HOW",
            "ITS", "LET", "MAY", "NEW", "NOW", "OLD", "SEE", "WAY", "WHO", "BOY",
            "DID", "GET", "HIM", "HIT", "MAN", "RUN", "SAY", "SHE", "TOO", "USE",
            // 4文字
            "ALSO", "BACK", "BEEN", "CALL", "COME", "EACH", "FIND", "FROM", "GIVE",
            "GOOD", "HAVE", "HERE", "HIGH", "JUST", "KNOW", "LAST", "LIKE", "LONG",
            "LOOK", "MADE", "MAKE", "MANY", "MORE", "MOST", "MUCH", "MUST", "NAME",
            "ONLY", "OVER", "PART", "SAID", "SAME", "SOME", "TAKE", "TELL", "THAN",
            "THAT", "THEM", "THEN", "THIS", "TIME", "UPON", "VERY", "WANT", "WELL",
            "WENT", "WERE", "WHAT", "WHEN", "WILL", "WITH", "WORD", "WORK", "YEAR",
            "YOUR", "DOES", "DONE", "DOWN", "EVEN", "HAND", "HELP", "HOME", "INTO",
            "LIFE", "LINE", "LIVE", "MOVE", "NEXT", "OPEN", "PLAY", "REAL", "SEEM",
            "SHOW", "SIDE", "TURN", "USED",
            // 3文字（追加）
            "DAY", "AGO", "AGE", "AIR", "ARM", "ART", "ASK", "BIG", "BIT", "BOX",
            "BUY", "CAR", "CUT", "EAR", "EAT", "END", "EYE", "FAR", "FEW", "FLY",
            "FUN", "GUN", "GOT", "HOT", "JOB", "JOY", "KEY", "KID", "LAW", "LAY",
            "LOT", "LOW", "MAP", "MIX", "OWN", "PAY", "POP", "POT", "PUT", "RAW",
            "RED", "ROW", "SAD", "SIT", "SIX", "SKY", "SUM", "SUN", "TEN", "TIE",
            "TIP", "TON", "TOP", "TRY", "TWO", "WIN", "WON", "YET", "ZEN",
            // 5〜6文字
            "ABOUT", "AFTER", "COULD", "EVERY", "FIRST", "FOUND", "GREAT", "HOUSE",
            "LARGE", "NEVER", "OTHER", "PLACE", "POINT", "RIGHT", "SMALL", "STILL",
            "THEIR", "THERE", "THESE", "THING", "THINK", "THREE", "UNDER", "WATER",
            "WHERE", "WHICH", "WHILE", "WORLD", "WOULD", "WRITE", "BEING", "BELOW",
            "BRING", "BUILD", "CARRY", "CLEAN", "CLOSE", "GIVEN", "GREEN", "GROUP",
            "HUMAN", "LEARN", "LEAVE", "MIGHT", "NIGHT", "OFTEN", "ORDER", "PAPER",
            "POWER", "PRESS", "QUITE", "ROUND", "SHALL", "SINCE", "SOUND", "STAND",
            "START", "STATE", "STORY", "STUDY", "TABLE", "TODAY", "UNTIL", "WATCH",
            "YOUNG", "CHANGE", "SHOULD", "BEFORE", "PEOPLE", "SYSTEM"
        };

        // 頭字語のスペルアウト読み辞書（文字ごとに分けて読み上げる）
        // KnownAcronymsAsWords に含まれない頭字語を正しくスペルアウトするためのマッピング
        private static readonly Dictionary<char, string> LetterPronunciations = new()
        {
            {'A', "ay"}, {'B', "bee"}, {'C', "see"}, {'D', "dee"}, {'E', "ee"},
            {'F', "eff"}, {'G', "jee"}, {'H', "aitch"}, {'I', "eye"}, {'J', "jay"},
            {'K', "kay"}, {'L', "ell"}, {'M', "em"}, {'N', "en"}, {'O', "oh"},
            {'P', "pee"}, {'Q', "queue"}, {'R', "ar"}, {'S', "ess"}, {'T', "tee"},
            {'U', "you"}, {'V', "vee"}, {'W', "double you"}, {'X', "ex"}, {'Y', "why"},
            {'Z', "zee"}
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

            // 0.5. 英語短縮形（contractions）の保護
            // アポストロフィを含む短縮形がトークナイザーで正しく処理されるよう保護する
            // 標準的な短縮形は保持し、特殊引用符をストレートアポストロフィに統一
            text = text.Replace("\u2019", "'"); // 右シングルクォート → アポストロフィ
            text = text.Replace("\u2018", "'"); // 左シングルクォート → アポストロフィ

            // 0.6. 頭字語のスペルアウト処理
            // KnownAcronymsAsWords に含まれない2〜6文字の大文字列をスペルアウトする
            text = AcronymRegex.Replace(text, m =>
            {
                string acr = m.Value;
                // 単語として発音する頭字語はそのまま返す
                if (KnownAcronymsAsWords.Contains(acr))
                {
                    return acr;
                }
                // 一般的な英単語の大文字形（IT, IS, A, THE など）はそのまま返す
                if (KnownCommonWords.Contains(acr))
                {
                    return acr;
                }
                // それ以外はアルファベットをスペルアウト
                var spelled = new List<string>();
                foreach (char c in acr)
                {
                    if (LetterPronunciations.TryGetValue(char.ToUpper(c), out string? pron))
                    {
                        spelled.Add(pron);
                    }
                    else
                    {
                        spelled.Add(c.ToString());
                    }
                }
                return string.Join(" ", spelled);
            });



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

            // 11. スラッシュの置換（I/O → eye oh、TCP/IP → 個別に処理）
            // 単語間のスラッシュを "slash" に変換（ただし分数は前段で処理済み）
            text = Regex.Replace(text, @"([A-Za-z])/((?:[A-Za-z]))", "$1 slash $2");

            // 12. 記号の置換
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
                
                // 開発時 (bin/Debug/net... 等) のために親ディレクトリを辿って探索
                var currentDir = new System.IO.DirectoryInfo(baseDir);
                int maxDepth = 5;
                while (!System.IO.File.Exists(path) && currentDir != null && maxDepth > 0)
                {
                    currentDir = currentDir.Parent;
                    if (currentDir != null)
                    {
                        path = System.IO.Path.Combine(currentDir.FullName, "user_dict_en.txt");
                    }
                    maxDepth--;
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
