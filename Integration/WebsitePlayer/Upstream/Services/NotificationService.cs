using System;
using System.Threading.Tasks;

namespace xLostProHome.Core.Services;

/// <summary>
/// ZENITH-APEX NOTIFICATION ORCHESTRATOR
/// A decoupled service for managing global UI interrupts. 
/// Replaces legacy browser-native popups with an awaitable, testable state machine.
/// </summary>
public class NotificationService
{
    // The current active request being displayed to the user
    public NotificationRequest? CurrentRequest { get; private set; }

    // Event fired when a notification is triggered or dismissed to update the UI Portal
    public event Action? OnChange;

    /// <summary>
    /// Triggers a confirmation dialogue that returns a boolean result.
    /// Usage: if (await Notify.ConfirmAsync("Initialise deletion?")) { ... }
    /// </summary>
    public Task<bool> ConfirmAsync(string message, string title = "System Confirmation", string confirmText = "Confirm", string cancelText = "Cancel")
    {
        var tcs = new TaskCompletionSource<bool>();

        CurrentRequest = new NotificationRequest
        {
            Title = title,
            Message = message,
            ConfirmText = confirmText,
            CancelText = cancelText,
            Type = NotificationType.Confirmation,
            CompletionSource = tcs
        };

        NotifyStateChanged();
        return tcs.Task;
    }

    /// <summary>
    /// Triggers a standard alert message.
    /// </summary>
    public void ShowAlert(string message, string title = "System Alert")
    {
        CurrentRequest = new NotificationRequest
        {
            Title = title,
            Message = message,
            Type = NotificationType.Alert
        };
        NotifyStateChanged();
    }

    public void HandleResponse(bool result)
    {
        if (CurrentRequest?.CompletionSource != null)
        {
            CurrentRequest.CompletionSource.SetResult(result);
        }
        Dismiss();
    }

    public void Dismiss()
    {
        CurrentRequest = null;
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}

public class NotificationRequest
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string ConfirmText { get; set; } = "OK";
    public string CancelText { get; set; } = "Cancel";
    public NotificationType Type { get; set; }
    public TaskCompletionSource<bool>? CompletionSource { get; set; }
}

public enum NotificationType { Alert, Confirmation }