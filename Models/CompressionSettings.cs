namespace StelleVideoCompressorGUI.Models
{
    public class CompressionSettings
    {
        public string CRF { get; set; } = "23";
        public string Preset { get; set; } = "medium";
        public string Label { get; set; } = "light";
        public double? Scale { get; set; }
    }

    public class CodecSettings
    {
        public string VideoCodec { get; set; } = "libx264";
        public string Extension { get; set; } = "mp4";
        public string Parameters { get; set; } = string.Empty;
        public string Name { get; set; } = "x264";
    }

    public class AudioSettings
    {
        public bool Compress { get; set; }
        public string Codec { get; set; } = "copy";
        public string Bitrate { get; set; } = string.Empty;
        public string Suffix { get; set; } = "copy";
    }
}