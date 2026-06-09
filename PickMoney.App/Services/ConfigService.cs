using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using PickMoney.App.Models;

namespace PickMoney.App.Services;

public class ConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _configPath;

    public ConfigService(string baseDirectory)
    {
        _configPath = Path.Combine(baseDirectory, "config", "appsettings.json");
    }

    public async Task<AppConfig> LoadAsync()
    {
        var directory = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(directory);

        if (!File.Exists(_configPath))
        {
            var defaultConfig = CreateDefault();
            await SaveAsync(defaultConfig);
            return defaultConfig;
        }

        try
        {
            var rawJson = await File.ReadAllTextAsync(_configPath);
            var normalizedJson = NormalizeLegacyConfig(rawJson);
            var config = JsonSerializer.Deserialize<AppConfig>(normalizedJson, JsonOptions);
            return MergeWithDefaults(config);
        }
        catch
        {
            return CreateDefault();
        }
    }

    public async Task SaveAsync(AppConfig config)
    {
        var directory = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(directory);
        await using var stream = File.Create(_configPath);
        await JsonSerializer.SerializeAsync(stream, config, JsonOptions);
    }

    private static AppConfig CreateDefault()
    {
        return new AppConfig
        {
            Accounts = new List<AccountConfig>
            {
                new()
                {
                    AccountName = "Primary Account",
                    ApiKey = string.Empty,
                    SecretKey = string.Empty,
                    AllocationParts = 5,
                    BuyAmount = 100m,
                    EnableOpenPosition = true,
                    EnableTakeProfit = true
                }
            },
            Strategy = new StrategyConfig
            {
                TriggerDropPercent = 3m,
                TakeProfitMultiplier = 1.03m,
                ScanIntervalSeconds = 60
            },
            Notification = new NotificationConfig
            {
                FeishuWebhooks = new List<string>
                {
                    "https://open.feishu.cn/open-apis/bot/v2/hook/your-webhook"
                }
            }
        };
    }

    private static string NormalizeLegacyConfig(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return rawJson;
        }

        var root = JsonNode.Parse(rawJson)?.AsObject();
        var accounts = root?["accounts"]?.AsArray();
        if (accounts is null)
        {
            return rawJson;
        }

        var changed = false;
        foreach (var accountNode in accounts.OfType<JsonObject>())
        {
            if (accountNode["allocationParts"] is null && accountNode["maxPositionSymbols"] is not null)
            {
                accountNode["allocationParts"] = accountNode["maxPositionSymbols"]?.DeepClone();
                changed = true;
            }
        }

        return changed
            ? root!.ToJsonString(JsonOptions)
            : rawJson;
    }

    private static AppConfig MergeWithDefaults(AppConfig? config)
    {
        var defaults = CreateDefault();
        if (config is null)
        {
            return defaults;
        }

        config.Accounts ??= new List<AccountConfig>();
        config.Strategy ??= new StrategyConfig();
        config.Notification ??= new NotificationConfig();
        config.Notification.FeishuWebhooks ??= new List<string>();
        config.Notification.State ??= new NotificationState();

        if (config.Accounts.Count == 0)
        {
            config.Accounts = defaults.Accounts;
        }

        return config;
    }
}
