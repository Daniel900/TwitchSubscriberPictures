using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using Hardcodet.Wpf.TaskbarNotification;
using TwitchSubscriberPictures.Core.Models;

namespace TwitchSubscriberPictures;

/// <summary>
/// Owns the taskbar icon for the entire application lifetime. The icon is created
/// once and disposed only when the process is shutting down, which avoids duplicate
/// tray icons after minimizing and restoring the window.
/// </summary>
public sealed class TrayIconController : IDisposable
{
    private readonly Action _showWindow;
    private readonly Action _closeImmediately;
    private TaskbarIcon? _taskbarIcon;

    public TrayIconController(Action showWindow, Action closeImmediately)
    {
        _showWindow = showWindow ?? throw new ArgumentNullException(nameof(showWindow));
        _closeImmediately = closeImmediately ?? throw new ArgumentNullException(nameof(closeImmediately));
    }

    public void Initialize()
    {
        if (_taskbarIcon is not null)
        {
            return;
        }

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Twitch Subscriber Pictures",
            Icon = SystemIcons.Application,
            ContextMenu = BuildContextMenu(),
            Visibility = Visibility.Visible
        };

        _taskbarIcon.TrayLeftMouseUp += (_, _) => _showWindow();
    }

    public void SetStatus(TwitchConnectionStatus status)
    {
        if (_taskbarIcon is null)
        {
            return;
        }

        (_taskbarIcon.Icon, _taskbarIcon.ToolTipText) = status switch
        {
            TwitchConnectionStatus.EventSubLive => (SystemIcons.Shield, "Twitch Subscriber Pictures - Connected, EventSub live"),
            TwitchConnectionStatus.Connected => (SystemIcons.Information, "Twitch Subscriber Pictures - Connected"),
            TwitchConnectionStatus.TokenInvalid => (SystemIcons.Warning, "Twitch Subscriber Pictures - Token invalid"),
            _ => (SystemIcons.Application, "Twitch Subscriber Pictures - Disconnected")
        };
    }

    private ContextMenu BuildContextMenu()
    {
        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => _showWindow();

        var close = new MenuItem { Header = "Close" };
        close.Click += (_, _) => _closeImmediately();

        return new ContextMenu
        {
            Items =
            {
                open,
                new Separator(),
                close
            }
        };
    }

    public void Dispose()
    {
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;
    }
}
