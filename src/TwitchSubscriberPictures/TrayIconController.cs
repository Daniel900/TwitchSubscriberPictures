using System;
using System.Drawing;
using System.Drawing.Drawing2D;
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
    private Icon? _currentIcon;

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

        _currentIcon = CreateStatusIcon(TwitchConnectionStatus.Disconnected);

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "Twitch Subscriber Pictures",
            Icon = _currentIcon,
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

        var newIcon = CreateStatusIcon(status);
        var previousIcon = _currentIcon;
        _currentIcon = newIcon;
        _taskbarIcon.Icon = newIcon;
        previousIcon?.Dispose();

        _taskbarIcon.ToolTipText = status switch
        {
            TwitchConnectionStatus.EventSubLive => "Twitch Subscriber Pictures - Connected, EventSub live",
            TwitchConnectionStatus.Connected => "Twitch Subscriber Pictures - Connected",
            TwitchConnectionStatus.TokenInvalid => "Twitch Subscriber Pictures - Token invalid",
            _ => "Twitch Subscriber Pictures - Disconnected"
        };
    }

    private static Icon CreateStatusIcon(TwitchConnectionStatus status)
    {
        var color = status switch
        {
            TwitchConnectionStatus.EventSubLive => Color.FromArgb(46, 160, 67),
            TwitchConnectionStatus.Connected => Color.FromArgb(180, 140, 30),
            TwitchConnectionStatus.TokenInvalid => Color.FromArgb(200, 60, 60),
            _ => Color.FromArgb(120, 120, 120)
        };

        var glyph = status switch
        {
            TwitchConnectionStatus.EventSubLive => "T",
            TwitchConnectionStatus.Connected => "T",
            TwitchConnectionStatus.TokenInvalid => "!",
            _ => "-"
        };

        using var bitmap = new Bitmap(16, 16);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var backgroundBrush = new SolidBrush(color);
        graphics.FillEllipse(backgroundBrush, 1, 1, 14, 14);

        using var font = new Font("Segoe UI", 8f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        graphics.DrawString(glyph, font, textBrush, new RectangleF(1, 1, 14, 14), format);
        return Icon.FromHandle(bitmap.GetHicon());
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
        _currentIcon?.Dispose();
        _currentIcon = null;
        _taskbarIcon?.Dispose();
        _taskbarIcon = null;
    }
}
