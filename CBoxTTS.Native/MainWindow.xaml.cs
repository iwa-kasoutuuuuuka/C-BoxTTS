using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Microsoft.ML.OnnxRuntime;

namespace CBoxTTS.Native
{
    public partial class MainWindow : Window
    {
        private TTSEngine? _engine;
        private MorphemeEngine? _morph;
        private Tokenizer? _tokenizer;
        private AudioEngine _audio = new();
        private bool _isInitialized = false;
        private bool _isUpdatingSelection = false;

        public MainWindow()
        {
            InitializeComponent();
            ApplyBuildSpecificUI();
            Loaded += MainWindow_Loaded;
            
            _audio.PlaybackStopped += () =>
            {
                Dispatcher.InvokeAsync(() =>
                {
                    if (StatusText.Text == GetMsg("再生中...", "Playing..."))
                    {
                        StatusText.Text = GetReadyMsg();
                    }
                });
            };
        }

        private void ApplyBuildSpecificUI()
        {
#if EN_BUILD
            Title = "C-Box TTS 英語版 (English Edition)";
            TitleText.Text = "C-Box TTS EN";
            
            LanguageLabel.Visibility = Visibility.Collapsed;
            LanguageCombo.Visibility = Visibility.Collapsed;
            
            ModelLabel.Text = "音声モデル設定";
            ModelCombo.Items.Clear();
            ModelCombo.Items.Add(new ComboBoxItem { Content = "Chatterbox Standard (英語専用)" });
            ModelCombo.Items.Add(new ComboBoxItem { Content = "Chatterbox Multilingual (多言語・高品質)" });
            ModelCombo.SelectedIndex = 0;
            
            ParametersLabel.Text = "詳細パラメーター";
            ExaggerationLabel.Text = "誇張度 (Exaggeration)";
            SpeedLabel.Text = "話速 (Speed)";
            TemperatureLabel.Text = "安定性 (Stability)";
            CfgWeightLabel.Text = "CFG/ペース (CFG/Pace)";
            RepetitionPenaltyLabel.Text = "反復ペナルティ (Repetition)";
            VoicePromptLabel.Text = "参照音声 (Voice Prompt)";
            
            WatermarkText.Text = "ここに読み上げたいテキストを入力してください...";
            ClearButton.Content = "クリア";
            PlayButton.Content = "再生";
            SaveButton.Content = "WAV保存";
            
            StatusText.Text = "初期化中...";

            SelectVoiceButton.Content = "選択";
            MinimizeBtn.ToolTip = "最小化";
            MaximizeBtn.ToolTip = "最大化";
            ExitBtn.ToolTip = "閉じる";
            CharCountText.Text = "0 文字";
#elif JA_BUILD
            Title = "C-Box TTS 日本語版 (Japanese Edition)";
            TitleText.Text = "C-Box TTS JA";
            
            ModelCombo.Items.Clear();
            ModelCombo.Items.Add(new ComboBoxItem { Content = "Chatterbox Turbo (日本語専用・高速)" });
            ModelCombo.Items.Add(new ComboBoxItem { Content = "Chatterbox Multilingual (多言語・高品質)" });
            ModelCombo.SelectedIndex = 0;
            
            TemperatureLabel.Text = "安定性 (Stability)";
            CfgWeightLabel.Text = "CFG/ペース (CFG/Pace)";
            RepetitionPenaltyLabel.Text = "反復ペナルティ (Repetition)";
            StatusText.Text = "初期化中...";
#endif
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeEnginesAsync();
        }

        private async Task InitializeEnginesAsync()
        {
            try
            {
                StatusProgress.Visibility = Visibility.Visible;
                StatusProgress.IsIndeterminate = true;
                
                var baseDir = AppContext.BaseDirectory;
                _engine = new TTSEngine(baseDir);
                
                // 以前のバグによって誤ってダウンロードされたモデルキャッシュの自動クリーンアップ
                var modelsDir = Path.Combine(baseDir, "models");
                var cleanMarker = Path.Combine(modelsDir, ".cleaned_v2");
                if (Directory.Exists(modelsDir) && !File.Exists(cleanMarker))
                {
                    StatusText.Text = GetMsg("古いモデルキャッシュをクリーンアップ中...", "Cleaning up legacy model cache...");
                    foreach (var file in Directory.GetFiles(modelsDir))
                    {
                        string name = Path.GetFileName(file);
                        if (name.Contains("_turbo") || name.Contains("_en"))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                    try { File.WriteAllText(cleanMarker, "cleaned"); } catch { }
                }

                _morph = new MorphemeEngine(baseDir);

#if EN_BUILD
                StatusText.Text = "コンポーネントを初期化中...";
#else
                StatusText.Text = GetMsg("辞書ファイルをチェック中...", "Checking dictionary files...");
                await _morph.EnsureDictionaryExistsAsync();
#endif

                // 初回ロード (初期設定に基づく)
                await ApplyModelSettingsAsync();

                _isInitialized = true;
                PlayButton.IsEnabled = true;
                SaveButton.IsEnabled = true;
                StatusText.Text = GetReadyMsg();
                StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                StatusProgress.Visibility = Visibility.Collapsed;
                UpdateModelComboItemsAvailability(LanguageCombo.SelectedIndex);
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void ShowError(Exception ex)
        {
            StatusText.Text = GetMsg("エラー発生。詳細は error.log を確認してください。", "Error occurred. Check error.log for details.");
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
            StatusProgress.Visibility = Visibility.Collapsed;
            string errorMsg = ex.ToString();
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "error.log"), errorMsg); } catch { }
            MessageBox.Show(GetMsg($"エラーが発生しました:\n{ex.Message}", $"An error occurred:\n{ex.Message}"), GetMsg("エラー", "Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private bool _isApplyingSettings = false;

        private async Task ApplyModelSettingsAsync()
        {
            if (_engine == null || _isApplyingSettings) return;

            try
            {
                _isApplyingSettings = true;

                // WPFのSelectionChangedイベントが完全に完了するまで一瞬待機し、
                // コントロールの無効化による選択状態適用のキャンセルを防ぐ
                await Task.Yield();

                PlayButton.IsEnabled = false;
                SaveButton.IsEnabled = false;
                if (LanguageCombo != null) LanguageCombo.IsEnabled = false;
                if (ModelCombo != null) ModelCombo.IsEnabled = false;
                StatusProgress.Visibility = Visibility.Visible;
                StatusProgress.IsIndeterminate = true;

                ModelType selectedType = ModelType.Multilingual;
#if EN_BUILD
                if (ModelCombo?.SelectedIndex == 0) selectedType = ModelType.English;
#elif JA_BUILD
                if (ModelCombo?.SelectedIndex == 0) selectedType = ModelType.Turbo;
#else
                if (ModelCombo?.SelectedIndex == 0) selectedType = ModelType.Turbo;
                else if (ModelCombo?.SelectedIndex == 2) selectedType = ModelType.English;
#endif

                // モデルの種類に応じて TemperatureSlider のデフォルト値を自動セット
                float defaultTemp = selectedType switch
                {
                    ModelType.English => 0.8f,
                    ModelType.Turbo => 0.6f,
                    _ => 0.7f
                };
                if (TemperatureSlider != null)
                {
                    TemperatureSlider.Value = defaultTemp;
                }

                // モデルの種類に応じて RepetitionPenaltySlider のデフォルト値を自動セット
                float defaultRepetitionPenalty = selectedType switch
                {
                    ModelType.Turbo => 1.35f,
                    ModelType.English => 1.35f,
                    _ => 1.2f
                };
                if (RepetitionPenaltySlider != null)
                {
                    RepetitionPenaltySlider.Value = defaultRepetitionPenalty;
                }

                StatusText.Text = GetMsg($"{selectedType} モデルを準備中...", $"Preparing {selectedType} model...");
                StatusText.Foreground = System.Windows.Media.Brushes.Orange;
                
                // モデルファイルの存在確認とダウンロード
                await _engine.EnsureModelExistsAsync(selectedType, (msg, progress) =>
                {
                    Dispatcher.Invoke(() => {
                        StatusText.Text = msg;
                        if (progress >= 0) { StatusProgress.IsIndeterminate = false; StatusProgress.Value = progress; }
                    });
                });

                // ロード
                await Task.Run(() =>
                {
                    _engine.LoadModel(selectedType, (msg, progress) =>
                    {
                        Dispatcher.Invoke(() => {
                            StatusText.Text = msg;
                            StatusProgress.IsIndeterminate = false;
                            StatusProgress.Value = progress;
                        });
                    });

                    string subDir = selectedType switch
                    {
                        ModelType.Turbo => "turbo",
                        ModelType.English => "english",
                        _ => "multilingual"
                    };
                    _tokenizer = new Tokenizer(Path.Combine(AppContext.BaseDirectory, "models", subDir, "tokenizer.json"));
                    _morph?.Initialize();
                });

                StatusText.Text = GetReadyMsg();
                StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            catch (Exception ex)
            {
                _isInitialized = false; // 初期化状態をクリアして、壊れたモデルが使われないようにする
                ShowError(ex);
            }
            finally
            {
                _isApplyingSettings = false;
                PlayButton.IsEnabled = _isInitialized; // ロードに成功している場合のみ有効化する
                SaveButton.IsEnabled = _isInitialized;
                if (LanguageCombo != null) LanguageCombo.IsEnabled = true;
                if (ModelCombo != null) ModelCombo.IsEnabled = true;
                StatusProgress.Visibility = Visibility.Collapsed;

#if DEBUG_TEST
                // Debug Test
                try {
                    StatusText.Text = "テスト音声を合成中...";
                    string voicePath = Path.Combine(AppContext.BaseDirectory, "models", "default_voice.wav");
                    long langToken = 723;
                    var ids = _tokenizer!.Encode("こんにちは。", langToken);
                    var wav = await _engine!.GenerateAsync(ids, voicePath, 0.5f);
                    File.WriteAllText("test_result.txt", $"Success: {wav.Length} samples");
                    StatusText.Text = "テスト完了";
                } catch(Exception ex) {
                    File.WriteAllText("test_result.txt", $"Error: {ex}");
                }
#endif
            }
        }

        private async void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
#if EN_BUILD || JA_BUILD
            if (!_isInitialized) return;
            await ApplyModelSettingsAsync();
#else
            if (ModelCombo == null || WatermarkText == null || InputTextBox == null || !_isInitialized || _isUpdatingSelection) return;

            try
            {
                _isUpdatingSelection = true;

                int langIdx = LanguageCombo.SelectedIndex;
                int modelIdx = ModelCombo.SelectedIndex;

                UpdateModelComboItemsAvailability(langIdx);

                if (langIdx == 0) // 日本語
                {
                    WatermarkText.Text = "ここに読み上げたい日本語を入力してください...";
                    if (modelIdx == 2 || ModelCombo.SelectedIndex == -1)
                    {
                        ModelCombo.SelectedIndex = 1; // Multilingual
                    }
                }
                else // 英語
                {
                    WatermarkText.Text = "Enter the English text you want to read aloud here...";
                    if (modelIdx == 0 || ModelCombo.SelectedIndex == -1)
                    {
                        ModelCombo.SelectedIndex = 1; // Multilingual
                    }
                }
                InputTextBox.Text = "";
            }
            finally
            {
                _isUpdatingSelection = false;
            }

            await ApplyModelSettingsAsync();
#endif
        }

        private void UpdateModelComboItemsAvailability(int langIdx)
        {
#if EN_BUILD || JA_BUILD
            // ビルド専用版ではコンボボックスの選択肢は常に有効なものだけを配置しているため、何もしない
            return;
#else
            if (ModelCombo == null) return;

            // 0: Turbo (Fast) - 日本語専用とするため、英語の時は無効化
            if (ModelCombo.Items.Count > 0 && ModelCombo.Items[0] is ComboBoxItem turboItem)
            {
                turboItem.IsEnabled = (langIdx == 0);
            }

            // 2: English (Exclusive) - 英語専用のため、英語の時のみ有効化
            if (ModelCombo.Items.Count > 2 && ModelCombo.Items[2] is ComboBoxItem englishItem)
            {
                englishItem.IsEnabled = (langIdx == 1);
            }
#endif
        }

        private async void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
#if EN_BUILD || JA_BUILD
            if (!_isInitialized || _isUpdatingSelection) return;
            await ApplyModelSettingsAsync();
#else
            if (LanguageCombo == null || !_isInitialized || _isUpdatingSelection) return;

            try
            {
                _isUpdatingSelection = true;

                int modelIdx = ModelCombo.SelectedIndex;

                // 1. モデルに適合する言語に切り替え
                if (modelIdx == 0) // Turbo (日本語専用)
                {
                    LanguageCombo.SelectedIndex = 0; // 日本語
                }
                else if (modelIdx == 2) // English (英語専用)
                {
                    LanguageCombo.SelectedIndex = 1; // 英語
                }

                // 2. 新しい言語に基づいてコンボボックス項目の有効無効状態を更新
                UpdateModelComboItemsAvailability(LanguageCombo.SelectedIndex);
            }
            finally
            {
                _isUpdatingSelection = false;
            }

            // 選択変更の確定後に非同期でモデルのロードを実行
            await ApplyModelSettingsAsync();
#endif
        }


        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                if (WindowState == WindowState.Maximized) WindowState = WindowState.Normal;
                else WindowState = WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaximizeBtn.Content = "";
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaximizeBtn.Content = "";
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }



        private void ExaggerationText_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(ExaggerationText.Text, out double val))
            {
                if (val < 0.0) val = 0.0;
                if (val > 1.0) val = 1.0;
                ExaggerationSlider.Value = val;
            }
            ExaggerationText.Text = ExaggerationSlider.Value.ToString("F2");
        }

        private void ExaggerationText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ExaggerationText_LostFocus(sender, e);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void SpeedText_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(SpeedText.Text, out double val))
            {
                if (val < 0.5) val = 0.5;
                if (val > 2.0) val = 2.0;
                SpeedSlider.Value = val;
            }
            SpeedText.Text = SpeedSlider.Value.ToString("F2");
        }

        private void SpeedText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SpeedText_LostFocus(sender, e);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }
        private void TemperatureText_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(TemperatureText.Text, out double val))
            {
                if (val < 0.1) val = 0.1;
                if (val > 1.2) val = 1.2;
                TemperatureSlider.Value = val;
            }
            TemperatureText.Text = TemperatureSlider.Value.ToString("F2");
        }

        private void TemperatureText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                TemperatureText_LostFocus(sender, e);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void CfgWeightText_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(CfgWeightText.Text, out double val))
            {
                if (val < 0) val = 0;
                if (val > 1) val = 1;
                CfgWeightSlider.Value = val;
            }
            CfgWeightText.Text = CfgWeightSlider.Value.ToString("F2");
        }

        private void CfgWeightText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CfgWeightText_LostFocus(sender, e);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }

        private void RepetitionPenaltyText_LostFocus(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(RepetitionPenaltyText.Text, out double val))
            {
                if (val < 1.0) val = 1.0;
                if (val > 2.0) val = 2.0;
                RepetitionPenaltySlider.Value = val;
            }
            RepetitionPenaltyText.Text = RepetitionPenaltySlider.Value.ToString("F2");
        }

        private void RepetitionPenaltyText_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                RepetitionPenaltyText_LostFocus(sender, e);
                Keyboard.ClearFocus();
                e.Handled = true;
            }
        }
        private void VoicePrompt_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void VoicePrompt_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[]? files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    string file = files[0];
                    string ext = Path.GetExtension(file).ToLowerInvariant();
                    if (ext == ".wav" || ext == ".mp3")
                    {
                        VoicePromptPathText.Text = file;
                    }
                    else
                    {
                        MessageBox.Show(GetMsg("WAVまたはMP3ファイルのみドラッグ＆ドロップ可能です。", "Only WAV or MP3 files can be drag-and-dropped."), GetMsg("警告", "Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private void SelectVoice_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = GetMsg("音声ファイル (*.wav;*.mp3)|*.wav;*.mp3", "Audio Files (*.wav;*.mp3)|*.wav;*.mp3"),
                Title = GetMsg("参照音声 (ボイスプロンプト) を選択", "Select Voice Prompt (Reference Audio)")
            };
            if (ofd.ShowDialog() == true)
            {
                VoicePromptPathText.Text = ofd.FileName;
            }
        }

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CharCountText.Text = $"{InputTextBox.Text.Length} 文字";
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            InputTextBox.Text = "";
        }

        /// <summary>
        /// 現在選択中のモデルと言語に対応する langToken を返す。
        /// - Turbo / Multilingual + 日本語: 723 ([ja]トークン)
        /// - Multilingual + 英語: 708 ([en]トークン。tokenizer.jsonの実際のID)
        /// - English 専用モデル: 1 (UNK。英語専用モデルの語彙は704語のため言語IDトークンは不要)
        /// </summary>
        private long GetCurrentLangToken()
        {
#if EN_BUILD
            int modelIdx = ModelCombo?.SelectedIndex ?? 0;
            if (modelIdx == 0) return 1; // English (Exclusive)
            return 708; // Multilingual (English)
#elif JA_BUILD
            return 723; // 日本語固定
#else
            int modelIdx = ModelCombo?.SelectedIndex ?? 1;
            int langIdx  = LanguageCombo?.SelectedIndex ?? 0;

            // English 専用モデル (index=2)
            if (modelIdx == 2) return 1;
            // 日本語
            if (langIdx == 0) return 723;
            // Multilingual + 英語 ([en] トークン, ID=708)
            return 708;
#endif
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                MessageBox.Show(GetMsg("現在エンジンの初期化中です。しばらくお待ちください...", "Initializing engine. Please wait..."), GetMsg("初期化中", "Initializing"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }


            string text = InputTextBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            // 入力テキスト長の上限チェック（極端に長いテキストによるメモリ圧迫を防止）
            const int MaxInputLength = 10000;
            if (text.Length > MaxInputLength)
            {
                MessageBox.Show(GetMsg($"入力テキストが長すぎます。{MaxInputLength}文字以内にしてください。\n現在: {text.Length}文字", $"Input text is too long. Please limit to {MaxInputLength} characters.\nCurrent: {text.Length} characters"), GetMsg("警告", "Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                PlayButton.IsEnabled = false;
                StatusProgress.Visibility = Visibility.Visible;
                StatusText.Text = GetMsg("音声合成中...", "Generating speech...");

                float exaggeration = (float)ExaggerationSlider.Value;
                float speed = (float)SpeedSlider.Value;
                float temperature = (float)TemperatureSlider.Value;
                float cfgWeight = (float)CfgWeightSlider.Value;
                float repetitionPenalty = (float)RepetitionPenaltySlider.Value;
                string voicePath = VoicePromptPathText.Text;
                
                if (!Path.IsPathRooted(voicePath))
                {
                    voicePath = Path.Combine(AppContext.BaseDirectory, "models", voicePath);
                }

                if (!File.Exists(voicePath))
                {
                    throw new FileNotFoundException(GetMsg($"参照音声ファイルが見つかりません:\n{voicePath}", $"Reference voice file not found:\n{voicePath}"));
                }

                long langToken = GetCurrentLangToken();

                var wav = await Task.Run(async () =>
                {
                    return await _engine!.GenerateBatchAsync(text, voicePath, exaggeration, temperature,
                        _morph!, _tokenizer!, langToken, cfgWeight, repetitionPenalty, msg =>
                        {
                            Dispatcher.Invoke(() => StatusText.Text = msg);
                        });
                });


                StatusText.Text = GetMsg("再生中...", "Playing...");
                _audio.Play(wav, speed);
            }
            catch (Exception ex)
            {
                MessageBox.Show(GetMsg($"合成エラー: {ex.Message}", $"Generation error: {ex.Message}"), GetMsg("エラー", "Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = GetMsg("エラー発生", "Error occurred");
            }
            finally
            {
                PlayButton.IsEnabled = true;
                StatusProgress.Visibility = Visibility.Collapsed;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                MessageBox.Show(GetMsg("現在エンジンの初期化中です。しばらくお待ちください...", "Initializing engine. Please wait..."), GetMsg("初期化中", "Initializing"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string text = InputTextBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show(GetMsg("読み上げたいテキストを入力してください。", "Please enter the text you want to read."), GetMsg("警告", "Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 入力テキスト長の上限チェック
            const int MaxInputLength = 10000;
            if (text.Length > MaxInputLength)
            {
                MessageBox.Show(GetMsg($"入力テキストが長すぎます。{MaxInputLength}文字以内にしてください。\n現在: {text.Length}文字", $"Input text is too long. Please limit to {MaxInputLength} characters.\nCurrent: {text.Length} characters"), GetMsg("警告", "Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 改行で分割して空行を除外
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                            .Select(l => l.Trim())
                            .Where(l => !string.IsNullOrEmpty(l))
                            .ToList();

            if (lines.Count == 0)
            {
                MessageBox.Show(GetMsg("有効な読み上げテキストがありません。", "No valid text to read."), GetMsg("警告", "Warning"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var sfd = new SaveFileDialog
            {
                Filter = GetMsg("WAVファイル (*.wav)|*.wav", "WAV Files (*.wav)|*.wav"),
                FileName = "output.wav"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    SaveButton.IsEnabled = false;
                    PlayButton.IsEnabled = false;
                    StatusProgress.Visibility = Visibility.Visible;

                    float exaggeration = (float)ExaggerationSlider.Value;
                    float speed = (float)SpeedSlider.Value;
                    float temperature = (float)TemperatureSlider.Value;
                    float cfgWeight = (float)CfgWeightSlider.Value;
                    float repetitionPenalty = (float)RepetitionPenaltySlider.Value;
                    string voicePath = VoicePromptPathText.Text;
                    
                    if (!Path.IsPathRooted(voicePath))
                    {
                        voicePath = Path.Combine(AppContext.BaseDirectory, "models", voicePath);
                    }

                    if (!File.Exists(voicePath))
                    {
                        throw new FileNotFoundException(GetMsg($"参照音声ファイルが見つかりません:\n{voicePath}", $"Reference voice file not found:\n{voicePath}"));
                    }

                    long langToken = GetCurrentLangToken();
                    string baseFilePath = sfd.FileName;

                    if (lines.Count == 1)
                    {
                        // 1行のみの場合は従来どおり直接その名前で保存
                        StatusText.Text = GetMsg("保存用に合成中...", "Generating for saving...");
                        var wav = await Task.Run(async () =>
                        {
                            return await _engine!.GenerateBatchAsync(lines[0], voicePath, exaggeration, temperature,
                                _morph!, _tokenizer!, langToken, cfgWeight, repetitionPenalty, msg =>
                                {
                                    Dispatcher.Invoke(() => StatusText.Text = msg);
                                });
                        });
                        _audio.SaveWav(wav, baseFilePath, speed);
                    }
                    else
                    {
                        // 複数行の場合は連番を付与して保存
                        string dir = Path.GetDirectoryName(baseFilePath) ?? "";
                        string filenameNoExt = Path.GetFileNameWithoutExtension(baseFilePath);
                        string ext = Path.GetExtension(baseFilePath);

                        for (int i = 0; i < lines.Count; i++)
                        {
                            string line = lines[i];
                            string numberedPath = Path.Combine(dir, $"{filenameNoExt}_{i + 1}{ext}");
                            
                            StatusText.Text = GetMsg($"保存用に合成中... ({i + 1}/{lines.Count})", $"Generating for saving... ({i + 1}/{lines.Count})");
                            
                            var wav = await Task.Run(async () =>
                            {
                                return await _engine!.GenerateBatchAsync(line, voicePath, exaggeration, temperature,
                                    _morph!, _tokenizer!, langToken, cfgWeight, repetitionPenalty, msg =>
                                    {
                                        Dispatcher.Invoke(() => StatusText.Text = $"{msg} ({i + 1}/{lines.Count})");
                                    });
                            });
                            _audio.SaveWav(wav, numberedPath, speed);
                        }
                    }

                    StatusText.Text = GetMsg("保存完了", "Save completed");
                    MessageBox.Show(lines.Count == 1 
                        ? GetMsg("WAVファイルを保存しました。", "Saved WAV file successfully.") 
                        : GetMsg($"{lines.Count} 個のWAVファイルに分割して保存しました。", $"Saved {lines.Count} split WAV files successfully."), GetMsg("情報", "Info"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(GetMsg($"保存エラー: {ex.Message}", $"Save error: {ex.Message}"), GetMsg("エラー", "Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusText.Text = GetMsg("エラー発生", "Error occurred");
                }
                finally
                {
                    SaveButton.IsEnabled = true;
                    PlayButton.IsEnabled = true;
                    StatusProgress.Visibility = Visibility.Collapsed;
                }
            }
        }

        private string GetReadyMsg()
        {
            return GetMsg($"準備完了 ({_engine?.ActiveBackend ?? "CPU"})", $"Ready ({_engine?.ActiveBackend ?? "CPU"})");
        }

        private string GetMsg(string ja, string en)
        {
#if EN_BUILD
            return en;
#else
            return ja;
#endif
        }

        protected override void OnClosed(EventArgs e)
        {
            _engine?.Dispose();
            _morph?.Dispose();
            _audio?.Dispose();
            base.OnClosed(e);
        }
    }
}
