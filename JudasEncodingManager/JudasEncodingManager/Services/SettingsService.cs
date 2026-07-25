using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using JudasEncodingManager.Models;

namespace JudasEncodingManager.Services
{
    public class SettingsService
    {
        private readonly JsonSerializerSettings _jsonSettings;

        public SettingsService()
        {
            _jsonSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Include
            };
        }

        public async Task<AppSettings> LoadSettingsAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Settings file not found: {filePath}");
            }

            var json = await File.ReadAllTextAsync(filePath);
            var settings = JsonConvert.DeserializeObject<AppSettings>(json, _jsonSettings);
            return settings ?? new AppSettings();
        }

        public AppSettings LoadSettings(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Settings file not found: {filePath}");
            }

            var json = File.ReadAllText(filePath);
            var settings = JsonConvert.DeserializeObject<AppSettings>(json, _jsonSettings);
            return settings ?? new AppSettings();
        }

        public async Task SaveSettingsAsync(string filePath, AppSettings settings)
        {
            var json = JsonConvert.SerializeObject(settings, _jsonSettings);
            await File.WriteAllTextAsync(filePath, json);
        }

        public void SaveSettings(string filePath, AppSettings settings)
        {
            var json = JsonConvert.SerializeObject(settings, _jsonSettings);
            File.WriteAllText(filePath, json);
        }

        public AppSettings CreateDefaultSettings()
        {
            return new AppSettings
            {
                MachineName = Environment.MachineName,
                QBittorrent = new QBittorrentSettings
                {
                    LocalIpPort = "http://127.0.0.1:8080/"
                },
                Remuxer = new RemuxerSettings
                {
                    MkvmergePath = "C://Program Files/MKVToolNix/mkvmerge.exe",
                    FfmpegPath = "C://FFmpeg/bin/ffmpeg.exe"
                }
            };
        }
    }
}
