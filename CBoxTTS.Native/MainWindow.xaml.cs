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
        private bool _isChangingSelection = false;

        private async Task ApplyModelSettingsAsync()
        {
            if (_engine == null || _isApplyingSettings) return;

            try
            {
                _isApplyingSettings = true;
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

                    string tokenizerFile = selectedType switch
                    {
                        ModelType.Turbo => "tokenizer_turbo.json",
                        ModelType.English => "tokenizer_en.json",
                        _ => "tokenizer_mtl.json"
                    };
                    _tokenizer = new Tokenizer(Path.Combine(AppContext.BaseDirectory, "models", tokenizerFile));
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
            if (ModelCombo == null || WatermarkText == null || InputTextBox == null || !_isInitialized) return;
            if (_isChangingSelection) return;

            _isChangingSelection = true;
            try
            {
                int langIdx = LanguageCombo.SelectedIndex;
                UpdateModelComboItemsAvailability(langIdx);
                int modelIdx = ModelCombo.SelectedIndex;

                if (langIdx == 0) // 日本語
                {
                    WatermarkText.Text = "ここに読み上げたい日本語を入力してください...";
                    // 英語専用モデルが選ばれていた場合は、マルチリンガルに切り替え
                    if (modelIdx == 2)
                    {
                        ModelCombo.SelectedIndex = 1; // Multilingual
                    }
                }
                else // 英語
                {
                    WatermarkText.Text = "Enter the English text you want to read aloud here...";
                    // 日本語専用モデルが選ばれていた場合は、マルチリンガルに切り替え
                    if (modelIdx == 0)
                    {
                        ModelCombo.SelectedIndex = 1; // Multilingual
                    }
                }
                InputTextBox.Text = "";
            }
            finally
            {
                _isChangingSelection = false;
            }

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
            if (LanguageCombo == null || !_isInitialized) return;
            if (_isChangingSelection) return;

            _isChangingSelection = true;
            try
            {
                int modelIdx = ModelCombo.SelectedIndex;

                if (modelIdx == 0) // Turbo (日本語専用)
                {
                    LanguageCombo.SelectedIndex = 0; // 日本語
                }
                else if (modelIdx == 2) // English (英語専用)
                {
                    LanguageCombo.SelectedIndex = 1; // 英語
                }
            }
            finally
            {
                _isChangingSelection = false;
            }

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



        private void SelectVoice_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "音声ファイル (*.wav)|*.wav",
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

                int selectedLangIndex = LanguageCombo.SelectedIndex;

                var wav = await Task.Run(async () =>
                {
                    long langToken = selectedLangIndex == 0 ? 723 : 1007;
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

            var sfd = new SaveFileDialog
            {
                Filter = "WAVファイル (*.wav)|*.wav",
                FileName = "output.wav"
            };

            if (sfd.ShowDialog() == true)
            {
                try
                {
                    StatusText.Text = "保存用に合成中...";
                    float exaggeration = (float)ExaggerationSlider.Value;
                    float speed = (float)SpeedSlider.Value;
                    string voicePath = VoicePromptPathText.Text;
                    
                    if (!Path.IsPathRooted(voicePath))
                    {
                        voicePath = Path.Combine(AppContext.BaseDirectory, "models", voicePath);
                    }
                    
                    int selectedLangIndex = LanguageCombo.SelectedIndex;

                    var wav = await Task.Run(async () =>
                    {
                        long langToken = selectedLangIndex == 0 ? 723 : 1007;
                        return await _engine!.GenerateBatchAsync(text, voicePath, exaggeration,
                            _morph!, _tokenizer!, langToken, msg =>
                            {
                                Dispatcher.Invoke(() => StatusText.Text = msg);
                            });
                    });


                    _audio.SaveWav(wav, sfd.FileName, speed);
                    StatusText.Text = "保存完了";
                    MessageBox.Show("WAVファイルを保存しました。");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"保存エラー: {ex.Message}");
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
