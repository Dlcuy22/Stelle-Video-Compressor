using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StelleVideoCompressorGUI.Models;

namespace StelleVideoCompressorGUI.Services
{
    public class FFmpegService
    {
        public event Action<string>? LogReceived;
        public event Action<string>? ProgressReceived;

        public async Task<VideoInfo> AnalyzeVideoAsync(string inputFile)
        {
            var videoInfo = new VideoInfo();

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "./ffprobe",
                    Arguments = $"-v quiet -print_format json -show_streams \"{inputFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    LogReceived?.Invoke("Failed to start ffprobe process");
                    return videoInfo;
                }

                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    LogReceived?.Invoke($"ffprobe failed with exit code: {process.ExitCode}");
                    return videoInfo;
                }

                ParseFFprobeOutput(output, videoInfo);
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"Error analyzing video: {ex.Message}");
            }

            return videoInfo;
        }

        private void ParseFFprobeOutput(string output, VideoInfo videoInfo)
        {
            try
            {
                using var doc = JsonDocument.Parse(output);
                var streams = doc.RootElement.GetProperty("streams").EnumerateArray();

                foreach (var stream in streams)
                {
                    var codecType = stream.TryGetProperty("codec_type", out var type) ? type.GetString() : "";

                    if (codecType == "video" && videoInfo.Width == 0)
                    {
                        if (stream.TryGetProperty("width", out var width))
                            videoInfo.Width = width.GetInt32();
                        
                        if (stream.TryGetProperty("height", out var height))
                            videoInfo.Height = height.GetInt32();
                        
                        if (stream.TryGetProperty("codec_name", out var codec))
                            videoInfo.Codec = codec.GetString()?.ToUpper() ?? "";
                        
                        if (stream.TryGetProperty("bit_rate", out var bitrate))
                        {
                            if (long.TryParse(bitrate.GetString(), out var bitrateValue))
                                videoInfo.Bitrate = $"{bitrateValue / 1000000.0:F1} Mbps";
                        }

                        if (stream.TryGetProperty("duration", out var duration))
                        {
                            if (double.TryParse(duration.GetString(), out var durationValue))
                                videoInfo.Duration = durationValue;
                        }
                    }
                    else if (codecType == "audio" && string.IsNullOrEmpty(videoInfo.AudioCodec))
                    {
                        if (stream.TryGetProperty("codec_name", out var audioCodec))
                            videoInfo.AudioCodec = audioCodec.GetString()?.ToUpper() ?? "";
                        
                        if (stream.TryGetProperty("bit_rate", out var audioBitrate))
                        {
                            if (long.TryParse(audioBitrate.GetString(), out var audioBitrateValue))
                                videoInfo.AudioBitrate = $"{audioBitrateValue / 1000} kbps";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"Error parsing ffprobe output: {ex.Message}");
            }
        }

        public async Task<bool> CompressVideoAsync(string inputFile, string outputFile, 
            CompressionSettings compression, CodecSettings codec, AudioSettings audio, VideoInfo videoInfo)
        {
            try
            {
                var args = BuildFFmpegCommand(inputFile, outputFile, compression, codec, audio, videoInfo);
                
                LogReceived?.Invoke($"Starting compression with {codec.VideoCodec}...");
                LogReceived?.Invoke($"Command: ffmpeg {string.Join(" ", args)}");

                var startInfo = new ProcessStartInfo
                {
                    FileName = "./ffmpeg",
                    Arguments = string.Join(" ", args),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    LogReceived?.Invoke("Failed to start ffmpeg process");
                    return false;
                }

                // Read stderr for progress information
                var errorTask = Task.Run(async () =>
                {
                    while (!process.StandardError.EndOfStream)
                    {
                        var line = await process.StandardError.ReadLineAsync();
                        if (!string.IsNullOrEmpty(line))
                        {
                            LogReceived?.Invoke(line);
                            
                            // Parse progress from ffmpeg stderr
                            if (line.Contains("time="))
                            {
                                var timeMatch = Regex.Match(line, @"time=(\d{2}):(\d{2}):(\d{2})");
                                if (timeMatch.Success)
                                {
                                    var hours = int.Parse(timeMatch.Groups[1].Value);
                                    var minutes = int.Parse(timeMatch.Groups[2].Value);
                                    var seconds = int.Parse(timeMatch.Groups[3].Value);
                                    var currentTime = hours * 3600 + minutes * 60 + seconds;
                                    
                                    if (videoInfo.Duration > 0)
                                    {
                                        var progress = (currentTime / videoInfo.Duration) * 100;
                                        ProgressReceived?.Invoke($"Progress: {progress:F1}%");
                                    }
                                }
                            }
                        }
                    }
                });

                await process.WaitForExitAsync();
                await errorTask;

                if (process.ExitCode == 0)
                {
                    LogReceived?.Invoke("✅ Compression completed successfully!");
                    return true;
                }
                else
                {
                    LogReceived?.Invoke($"❌ Compression failed with exit code: {process.ExitCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"❌ Error during compression: {ex.Message}");
                return false;
            }
        }

        private List<string> BuildFFmpegCommand(string inputFile, string outputFile,
            CompressionSettings compression, CodecSettings codec, AudioSettings audio, VideoInfo videoInfo)
        {
            var args = new List<string>
            {
                "-i", $"\"{inputFile}\"",
                "-c:v", codec.VideoCodec,
                "-crf", compression.CRF,
                "-preset", compression.Preset
            };

            // Add scaling if needed
            if (compression.Scale.HasValue)
            {
                var scaleValue = compression.Scale.Value;
                var newWidth = (int)Math.Round(videoInfo.Width * scaleValue);
                var newHeight = (int)Math.Round(videoInfo.Height * scaleValue);

                // Ensure dimensions are even numbers for H.264/H.265 compatibility
                if (newWidth % 2 != 0) newWidth--;
                if (newHeight % 2 != 0) newHeight--;

                var filter = $"scale={newWidth}:{newHeight}:flags=lanczos";
                args.AddRange(new[] { "-vf", filter });
            }

            // Audio settings
            if (audio.Codec == "copy")
            {
                args.AddRange(new[] { "-c:a", "copy" });
            }
            else if (audio.Codec == "none")
            {
                args.Add("-an"); // Remove audio
            }
            else
            {
                args.AddRange(new[] { "-c:a", audio.Codec, "-b:a", audio.Bitrate });
            }

            // Add codec-specific parameters
            if (!string.IsNullOrEmpty(codec.Parameters))
            {
                args.AddRange(codec.Parameters.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            }

            // Add output file
            args.AddRange(new[] { "-y", $"\"{outputFile}\"" }); // -y to overwrite without asking

            return args;
        }

        public void ShowSizeComparison(string inputFile, string outputFile)
        {
            try
            {
                var inputInfo = new FileInfo(inputFile);
                var outputInfo = new FileInfo(outputFile);

                if (!outputInfo.Exists)
                {
                    LogReceived?.Invoke("Output file not found for size comparison");
                    return;
                }

                var inputSize = inputInfo.Length / (1024.0 * 1024.0);
                var outputSize = outputInfo.Length / (1024.0 * 1024.0);
                var reduction = (inputSize - outputSize) / inputSize * 100;

                LogReceived?.Invoke("");
                LogReceived?.Invoke("📊 Size Comparison:");
                LogReceived?.Invoke($"   Original: {inputSize:F2} MB");
                LogReceived?.Invoke($"   Compressed: {outputSize:F2} MB");
                LogReceived?.Invoke($"   Reduction: {reduction:F1}%");
                LogReceived?.Invoke($"   Output: {outputFile}");
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"Error calculating size comparison: {ex.Message}");
            }
        }
    }
}