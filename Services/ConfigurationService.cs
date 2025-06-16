using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using TranslationToolUI.Models;

namespace TranslationToolUI.Services
{
    public class ConfigurationService
    {
        private readonly string _configFilePath;

        public ConfigurationService()
        {
            _configFilePath = PathManager.Instance.ConfigFilePath;
        }

        public async Task<AzureSpeechConfig> LoadConfigAsync()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = await File.ReadAllTextAsync(_configFilePath);
                    var config = JsonSerializer.Deserialize<AzureSpeechConfig>(json);
                    if (config != null)
                    {
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载配置失败: {ex.Message}");
            }

            var defaultConfig = new AzureSpeechConfig();
            await SaveConfigAsync(defaultConfig);
            return defaultConfig;
        }
        public async Task SaveConfigAsync(AzureSpeechConfig config)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                var json = JsonSerializer.Serialize(config, options);
                await File.WriteAllTextAsync(_configFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存配置失败: {ex.Message}");
                throw;
            }
        }

        public string GetConfigFilePath()
        {
            return _configFilePath;
        }
    }
}
