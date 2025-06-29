using System;

namespace StelleVideoCompressorGUI.Models
{
    public class VideoInfo
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public string Codec { get; set; } = string.Empty;
        public string Bitrate { get; set; } = string.Empty;
        public string AudioCodec { get; set; } = string.Empty;
        public string AudioBitrate { get; set; } = string.Empty;
        public double Duration { get; set; }

        public override string ToString()
        {
            var info = $"Video: {Width}x{Height}";
            if (!string.IsNullOrEmpty(Codec))
                info += $" ({Codec})";
            if (!string.IsNullOrEmpty(Bitrate))
                info += $"\nBitrate: {Bitrate}";
            if (!string.IsNullOrEmpty(AudioCodec))
                info += $"\nAudio: {AudioCodec}";
            if (Duration > 0)
                info += $"\nDuration: {FormatDuration(Duration)}";
            
            return info;
        }

        private string FormatDuration(double seconds)
        {
            var ts = TimeSpan.FromSeconds(seconds);
            return ts.Hours > 0 
                ? $"{ts.Hours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
    }
}