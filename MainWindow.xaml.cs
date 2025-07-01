using Microsoft.Win32;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using StelleVideoCompressorGUI.Pages;
using StelleVideoCompressorGUI.Models;
using StelleVideoCompressorGUI.Services;

namespace StelleVideoCompressorGUI
{
    public partial class MainWindow : Window
    {
        private readonly FFmpegService _ffmpegService;
        private VideoInfo? _currentVideoInfo;
        private bool _isCompressing = false;

        public MainWindow()
        {
            InitializeComponent();
            _ffmpegService = new FFmpegService();
            _ffmpegService.LogReceived += OnLogReceived;
            _ffmpegService.ProgressReceived += OnProgressReceived;
            
            LogMessage("Stelle Video Compressor v0.1");
            LogMessage("Ready. Please select a video file to analyze.");
            
            // Set initial UI state
            UpdateUIState();
        }

        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Video File",
                Filter = "Video Files|*.mp4;*.avi;*.mkv;*.mov;*.wmv;*.flv;*.webm;*.m4v;*.3gp;*.ts;*.mts|All Files|*.*",
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await AnalyzeVideoFile(openFileDialog.FileName);
            }
        }

        private async Task AnalyzeVideoFile(string filePath)
        {
            try
            {
                // Update UI to show analysis is in progress
                FilePathTextBox.Text = filePath;
                StatusTextBlock.Text = "🔍 Analyzing video...";
                VideoInfoTextBlock.Text = "Analyzing video file, please wait...";
                CompressButton.IsEnabled = false;
                BrowseButton.IsEnabled = false;
                
                LogMessage("");
                LogMessage($"📁 Selected file: {Path.GetFileName(filePath)}");
                LogMessage("🔍 Starting analysis...");

                _currentVideoInfo = await _ffmpegService.AnalyzeVideoAsync(filePath);
                
                if (_currentVideoInfo.Width > 0 && _currentVideoInfo.Height > 0)
                {
                    VideoInfoTextBlock.Text = _currentVideoInfo.ToString();
                    CompressButton.IsEnabled = true;
                    StatusTextBlock.Text = "✅ Video analyzed successfully. Ready to compress.";
                    
                    LogMessage($"✅ Video analyzed successfully:");
                    LogMessage($"   Resolution: {_currentVideoInfo.Width}x{_currentVideoInfo.Height}");
                    LogMessage($"   Codec: {_currentVideoInfo.Codec}");
                    if (!string.IsNullOrEmpty(_currentVideoInfo.Bitrate))
                        LogMessage($"   Bitrate: {_currentVideoInfo.Bitrate}");
                    if (_currentVideoInfo.Duration > 0)
                        LogMessage($"   Duration: {FormatDuration(_currentVideoInfo.Duration)}");
                    
                    // Show file size
                    var fileInfo = new FileInfo(filePath);
                    LogMessage($"   File size: {fileInfo.Length / (1024.0 * 1024.0):F2} MB");
                }
                else
                {
                    VideoInfoTextBlock.Text = "❌ Failed to analyze video. Please check if the file is valid and FFmpeg.exe and ffprobe.exe is in the install folder!";
                    StatusTextBlock.Text = "❌ Failed to analyze video";
                    LogMessage("❌ Failed to analyze video.");
                    LogMessage("   Make sure FFmpeg is in the Install folder.");
                    LogMessage("   Check if the selected file is a valid video file.");
                }
            }
            catch (Exception ex)
            {
                VideoInfoTextBlock.Text = $"❌ Error: {ex.Message}";
                StatusTextBlock.Text = "❌ Analysis failed";
                LogMessage($"❌ Error analyzing video: {ex.Message}");
            }
            finally
            {
                BrowseButton.IsEnabled = true;
                UpdateUIState();
            }
        }

        private async void CompressButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(FilePathTextBox.Text) || _currentVideoInfo == null || _isCompressing)
                return;

            try
            {
                _isCompressing = true;
                UpdateUIState();
                
                var inputFile = FilePathTextBox.Text;
                var compressionSettings = GetCompressionSettings();
                var codecSettings = GetCodecSettings();
                var audioSettings = GetAudioSettings();
                
                var outputFile = GenerateOutputFilename(inputFile, compressionSettings.Label, 
                    codecSettings.Name, audioSettings.Suffix);

                LogMessage("");
                LogMessage("🚀 Starting compression...");
                LogMessage($"📤 Output file: {Path.GetFileName(outputFile)}");
                LogMessage($"⚙️ Settings: {compressionSettings.Label} quality, {codecSettings.Name} codec, {audioSettings.Suffix} audio");

                var success = await _ffmpegService.CompressVideoAsync(inputFile, outputFile, 
                    compressionSettings, codecSettings, audioSettings, _currentVideoInfo);

                if (success)
                {
                    _ffmpegService.ShowSizeComparison(inputFile, outputFile);
                    StatusTextBlock.Text = "🎉 Compression completed successfully!";
                    var outputWindow = new OutputWindow();
                    
                    bool? results = outputWindow.ShowDialog(); // SHOW NEW OUTPUT WINDOW
                    
                    // Ask if user wants to open output folder
                    // var result = MessageBox.Show(
                    //     $"🎉 Compression completed successfully!\n\n📁 Output: {Path.GetFileName(outputFile)}\n\nWould you like to open the output folder?",
                    //     "Compression Complete",
                    //     MessageBoxButton.YesNo,
                    //     MessageBoxImage.Information);
                    //
                    // if (result == MessageBoxResult.Yes)
                    // {
                    //     try
                    //     {
                    //         System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{outputFile}\"");
                    //     }
                    //     catch (Exception ex)
                    //     {
                    //         LogMessage($"❌ Failed to open folder: {ex.Message}");
                    //     }
                    // }
                }
                else
                {
                    StatusTextBlock.Text = "❌ Compression failed";
                    MessageBox.Show("❌ Compression failed. Please check the log for details.", 
                        "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"❌ Unexpected error: {ex.Message}");
                StatusTextBlock.Text = "❌ Compression failed";
                MessageBox.Show($"❌ An unexpected error occurred: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isCompressing = false;
                UpdateUIState();
            }
        }

        private void UpdateUIState()
        {
            if (_isCompressing)
            {
                CompressButton.IsEnabled = false;
                CompressButton.Content = "🔄 COMPRESSING...";
                BrowseButton.IsEnabled = false;
                StatusTextBlock.Text = "🔄 Compressing video...";
            }
            else
            {
                CompressButton.IsEnabled = _currentVideoInfo != null && _currentVideoInfo.Width > 0;
                CompressButton.Content = "🚀 COMPRESS VIDEO";
                BrowseButton.IsEnabled = true;
                
                if (StatusTextBlock.Text.Contains("Compressing"))
                {
                    StatusTextBlock.Text = "Ready";
                }
            }
        }

        private CompressionSettings GetCompressionSettings()
        {
            if (LightRadio.IsChecked == true)
                return new CompressionSettings { CRF = "23", Preset = "medium", Label = "light" };
            else if (MediumRadio.IsChecked == true)
                return new CompressionSettings { CRF = "26", Preset = "fast", Label = "medium" };
            else if (HighRadio.IsChecked == true)
                return new CompressionSettings { CRF = "30", Preset = "faster", Label = "high" };
            else if (ExtremeRadio.IsChecked == true)
                return new CompressionSettings { CRF = "35", Preset = "veryfast", Label = "extreme", Scale = 0.5 };
            
            return new CompressionSettings(); // Default to light
        }

        private CodecSettings GetCodecSettings()
        {
            if (H264Radio.IsChecked == true)
                return new CodecSettings 
                { 
                    VideoCodec = "libx264", 
                    Extension = "mp4", 
                    Parameters = "-movflags +faststart", 
                    Name = "x264" 
                };
            else if (H265Radio.IsChecked == true)
                return new CodecSettings 
                { 
                    VideoCodec = "libx265", 
                    Extension = "mp4", 
                    Parameters = "-x265-params log-level=error -movflags +faststart", 
                    Name = "x265" 
                };
            
            return new CodecSettings(); // Default to H.264
        }

        private AudioSettings GetAudioSettings()
        {
            if (AudioCopyRadio.IsChecked == true)
                return new AudioSettings { Compress = false, Codec = "copy", Suffix = "copy" };
            else if (Audio128Radio.IsChecked == true)
                return new AudioSettings { Compress = true, Codec = "aac", Bitrate = "128k", Suffix = "128k" };
            else if (Audio96Radio.IsChecked == true)
                return new AudioSettings { Compress = true, Codec = "aac", Bitrate = "96k", Suffix = "96k" };
            else if (Audio64Radio.IsChecked == true)
                return new AudioSettings { Compress = true, Codec = "aac", Bitrate = "64k", Suffix = "64k" };
            else if (AudioNoneRadio.IsChecked == true)
                return new AudioSettings { Compress = false, Codec = "none", Suffix = "noaudio" };
            
            return new AudioSettings(); // Default to copy
        }

        private string GenerateOutputFilename(string inputFile, string label, string codecName, string audioSuffix)
        {
            var directory = Path.GetDirectoryName(inputFile) ?? "";
            var fileNameWithoutExt = Path.GetFileNameWithoutExtension(inputFile);
            var codecSettings = GetCodecSettings();
            return Path.Combine(directory, $"{fileNameWithoutExt}_compressed_{label}_{codecName}_{audioSuffix}.{codecSettings.Extension}");
        }

        private string FormatDuration(double seconds)
        {
            if (seconds <= 0) return "Unknown";
            
            var timeSpan = TimeSpan.FromSeconds(seconds);
            if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours:D2}:{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
            else
                return $"{timeSpan.Minutes:D2}:{timeSpan.Seconds:D2}";
        }

        private void OnLogReceived(string message)
        {
            Dispatcher.Invoke(() => LogMessage(message));
        }

        private void OnProgressReceived(string progress)
        {
            Dispatcher.Invoke(() => StatusTextBlock.Text = progress);
        }

        private void LogMessage(string message)
        {
            if (!string.IsNullOrEmpty(LogTextBlock.Text))
                LogTextBlock.Text += Environment.NewLine;
            
            LogTextBlock.Text += $"[{DateTime.Now:HH:mm:ss}] {message}";
            LogScrollViewer.ScrollToEnd();
        }

        protected override void OnClosed(EventArgs e)
        {
            _ffmpegService.LogReceived -= OnLogReceived;
            _ffmpegService.ProgressReceived -= OnProgressReceived;
            base.OnClosed(e);
        }
    }
}