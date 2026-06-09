using System.Net.Http;
using System.Text;
using System.Text.Json;
using PickMoney.App.Models;

namespace PickMoney.App.Services;

public class NotificationService
{
    private readonly HttpClient _httpClient = new();

    public async Task PushAsync(NotificationConfig notificationConfig, string message, CancellationToken cancellationToken = default)
    {
        var webhooks = notificationConfig.FeishuWebhooks
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (webhooks.Count == 0)
        {
            return;
        }

        var payload = JsonSerializer.Serialize(new
        {
            msg_type = "text",
            content = new
            {
                text = message
            }
        });

        var state = notificationConfig.State ??= new NotificationState();
        var webhookIndex = Math.Abs(state.NextWebhookIndex) % webhooks.Count;
        var webhook = webhooks[webhookIndex];
        state.NextWebhookIndex = (webhookIndex + 1) % webhooks.Count;

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        try
        {
            await _httpClient.PostAsync(webhook, content, cancellationToken);
        }
        catch
        {
        }
    }
}
