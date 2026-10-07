using System;
using System.Collections.Generic;
using System.Timers;

namespace xLostProHome.Core.Services;

/// <summary>
/// ZENITH-APEX TOAST ORCHESTRATOR
/// Manages non-intrusive system feedback. This service is linked to the 
/// user-defined "Error Hide Delay" to ensure consistency with the legacy timer.
/// </summary>
public class ToastService
{
    public List<ToastMessage> ActiveToasts { get; } = new();
    public event Action? OnChange;

    /// <summary>
    /// Dispatches a feedback message. 
    /// If durationSeconds is 0, the toast remains until manually dismissed.
    /// </summary>
    public void ShowToast(string message, ToastLevel level = ToastLevel.Info, int durationSeconds = 5)
    {
        var toast = new ToastMessage 
        { 
            Id = Guid.NewGuid(), 
            Message = message, 
            Level = level 
        };

        ActiveToasts.Add(toast);
        NotifyStateChanged();

        // INTEGRATED TIMER LOGIC: Respects the 'ValidationErrorHideSeconds' setting.
        if (durationSeconds > 0)
        {
            var timer = new System.Timers.Timer(durationSeconds * 1000);
            timer.Elapsed += (s, e) => 
            {
                RemoveToast(toast.Id);
                timer.Dispose();
            };
            timer.AutoReset = false;
            timer.Start();
        }
    }

    public void RemoveToast(Guid id)
    {
        ActiveToasts.RemoveAll(x => x.Id == id);
        NotifyStateChanged();
    }

    public void ClearAll()
    {
        ActiveToasts.Clear();
        NotifyStateChanged();
    }

    private void NotifyStateChanged() => OnChange?.Invoke();
}

public class ToastMessage
{
    public Guid Id { get; set; }
    public string Message { get; set; } = string.Empty;
    public ToastLevel Level { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public enum ToastLevel { Info, Success, Warning, Error }