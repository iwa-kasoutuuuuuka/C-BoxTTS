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
            Loaded += MainWindow_Loaded;
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
                    StatusText.Text = "古いモデルキャッシュをクリーンアップ中...";
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

                StatusText.Text = "辞書ファイルをチェック中...";
                await _morph.EnsureDictionaryExistsAsync();

                // 初回ロード (初期設定に基づく)
                await ApplyModelSettingsAsync();

                StatusText.Text = "準備完了";
                StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
                StatusProgress.Visibility = Visibility.Collapsed;
                UpdateModelComboItemsAvailability(LanguageCombo.SelectedIndex);
                _isInitialized = true;
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
        }

        private void ShowError(Exception ex)
        {
            StatusText.Text = "エラー発生。詳細は error.log を確認してください。";
            StatusText.Foreground = System.Windows.Media.Brushes.Red;
            StatusProgress.Visibility = Visibility.Collapsed;
            string errorMsg = ex.ToString();
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "error.log"), errorMsg); } catch { }
            MessageBox.Show($"エラーが発生しました:\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
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
                if (ModelCombo?.SelectedIndex == 0) selectedType = ModelType.Turbo;
                else if (ModelCombo?.SelectedIndex == 2) selectedType = ModelType.English;

                StatusText.Text = $"{selectedType} モデルを準備中...";
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

                StatusText.Text = "準備完了";
                StatusText.Foreground = System.Windows.Media.Brushes.LightGreen;
            }
            catch (Exception ex)
            {
                ShowError(ex);
            }
            finally
            {
                _isApplyingSettings = false;
                PlayButton.IsEnabled = true;
                SaveButton.IsEnabled = true;
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
            if (ModelCombo == null || WatermarkText == null || InputTextBox == null || !_isInitialized || _isUpdatingSelection) return;

            try
            {
                _isUpdatingSelection = true;

                int langIdx = LanguageCombo.SelectedIndex;
                int modelIdx = ModelCombo.SelectedIndex;

                // 1. まずモデルコンボの項目有効無効状態を更新
                UpdateModelComboItemsAvailability(langIdx);

                // 2. 言語に適合しないモデルが選択されていた場合は、安全なフォールバックに切り替え
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

            // 選択変更の確定後に非同期でモデルのロードを実行
            await ApplyModelSettingsAsync();
        }

        private void UpdateModelComboItemsAvailability(int langIdx)
        {
            if (ModelCombo == null) return;

            // 0: Turbo (Fast) - 日本語専用とするため、英語の時は無効化
            if (ModelCombo.Items[0] is ComboBoxItem turboItem)
            {
                turboItem.IsEnabled = (langIdx == 0);
            }

            // 2: English (Exclusive) - 英語専用のため、英語の時のみ有効化
            if (ModelCombo.Items[2] is ComboBoxItem englishItem)
            {
                englishItem.IsEnabled = (langIdx == 1);
            }
        }

        private async void ModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
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
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
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
                        MessageBox.Show("WAVまたはMP3ファイルのみドラッグ＆ドロップ可能です。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
            }
        }

        private void SelectVoice_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "音声ファイル (*.wav;*.mp3)|*.wav;*.mp3",
                Title = "参照音声 (ボイスプロンプト) を選択"
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
            int modelIdx = ModelCombo?.SelectedIndex ?? 1;
            int langIdx  = LanguageCombo?.SelectedIndex ?? 0;

            // English 専用モデル (index=2)
            if (modelIdx == 2) return 1;
            // 日本語
            if (langIdx == 0) return 723;
            // Multilingual + 英語 ([en] トークン, ID=708)
            return 708;
        }

        private async void PlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                MessageBox.Show("現在エンジンの初期化中です。しばらくお待ちください...", "初期化中", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }


            string text = InputTextBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            try
            {
                PlayButton.IsEnabled = false;
                StatusProgress.Visibility = Visibility.Visible;
                StatusText.Text = "音声合成中...";

                float exaggeration = (float)ExaggerationSlider.Value;
                float speed = (float)SpeedSlider.Value;
                string voicePath = VoicePromptPathText.Text;
                
                if (!Path.IsPathRooted(voicePath))
                {
                    voicePath = Path.Combine(AppContext.BaseDirectory, "models", voicePath);
                }

                long langToken = GetCurrentLangToken();

                var wav = await Task.Run(async () =>
                {
                    return await _engine!.GenerateBatchAsync(text, voicePath, exaggeration,
                        _morph!, _tokenizer!, langToken, msg =>
                        {
                            Dispatcher.Invoke(() => StatusText.Text = msg);
                        });
                });


                StatusText.Text = "再生中...";
                _audio.Play(wav, speed);
                StatusText.Text = "準備完了";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"合成エラー: {ex.Message}");
                StatusText.Text = "エラー発生";
            }
            finally
            {
                PlayButton.IsEnabled = true;
                StatusProgress.Visibility = Visibility.Collapsed;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            string text = InputTextBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            // 改行で分割して空行を除外
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None)
                            .Select(l => l.Trim())
                            .Where(l => !string.IsNullOrEmpty(l))
                            .ToList();

            if (lines.Count == 0) return;

            var sfd = new SaveFileDialog
            {
                Filter = "WAVファイル (*.wav)|*.wav",
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
                    string voicePath = VoicePromptPathText.Text;
                    
                    if (!Path.IsPathRooted(voicePath))
                    {
                        voicePath = Path.Combine(AppContext.BaseDirectory, "models", voicePath);
                    }

                    long langToken = GetCurrentLangToken();
                    string baseFilePath = sfd.FileName;

                    if (lines.Count == 1)
                    {
                        // 1行のみの場合は従来どおり直接その名前で保存
                        StatusText.Text = "保存用に合成中...";
                        var wav = await Task.Run(async () =>
                        {
                            return await _engine!.GenerateBatchAsync(lines[0], voicePath, exaggeration,
                                _morph!, _tokenizer!, langToken, msg =>
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
                            
                            StatusText.Text = $"保存用に合成中... ({i + 1}/{lines.Count})";
                            
                            var wav = await Task.Run(async () =>
                            {
                                return await _engine!.GenerateBatchAsync(line, voicePath, exaggeration,
                                    _morph!, _tokenizer!, langToken, msg =>
                                    {
                                        Dispatcher.Invoke(() => StatusText.Text = $"{msg} ({i + 1}/{lines.Count})");
                                    });
                            });
                            _audio.SaveWav(wav, numberedPath, speed);
                        }
                    }

                    StatusText.Text = "保存完了";
                    MessageBox.Show(lines.Count == 1 
                        ? "WAVファイルを保存しました。" 
                        : $"{lines.Count} 個のWAVファイルに分割して保存しました。");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存エラー: {ex.Message}");
                    StatusText.Text = "エラー発生";
                }
                finally
                {
                    SaveButton.IsEnabled = true;
                    PlayButton.IsEnabled = true;
                    StatusProgress.Visibility = Visibility.Collapsed;
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _engine?.Dispose();
            _morph?.Dispose();
            base.OnClosed(e);
        }
    }
}
