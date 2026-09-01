using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TwitchSubscriberPictures.Core.Abstractions;
using TwitchSubscriberPictures.Core.Models;

namespace TwitchSubscriberPictures.Core.Services;

public sealed class PhotoSyncEngine
{
    private readonly IAppLogger _logger;

    public PhotoSyncEngine(IAppLogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ReconciliationResult> ReconcileAsync(
        IReadOnlyCollection<ActiveSubscriber> activeSubscribers,
        string allPhotosPath,
        string activePhotosPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(allPhotosPath))
        {
            throw new ArgumentException("AllPhotos path is required.", nameof(allPhotosPath));
        }

        if (string.IsNullOrWhiteSpace(activePhotosPath))
        {
            throw new ArgumentException("ActivePhotos path is required.", nameof(activePhotosPath));
        }

        return await Task.Run(() => ReconcileCore(activeSubscribers, allPhotosPath, activePhotosPath), cancellationToken)
            .ConfigureAwait(false);
    }

    private ReconciliationResult ReconcileCore(
        IReadOnlyCollection<ActiveSubscriber> activeSubscribers,
        string allPhotosPath,
        string activePhotosPath)
    {
        var statuses = new List<SubscriberPhotoStatus>(activeSubscribers.Count);
        var errors = new List<string>();
        var activeLogins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var subscriber in activeSubscribers)
        {
            activeLogins.Add(subscriber.UserLogin);
        }

        if (!Directory.Exists(allPhotosPath))
        {
            _logger.Log(AppLogLevel.Warning, $"AllPhotos folder does not exist: {allPhotosPath}");
        }

        if (!Directory.Exists(activePhotosPath))
        {
            try
            {
                Directory.CreateDirectory(activePhotosPath);
            }
            catch (Exception ex)
            {
                var message = $"Could not create ActivePhotos folder {activePhotosPath}: {ex.Message}";
                _logger.Log(AppLogLevel.Error, message);
                errors.Add(message);
            }
        }

        foreach (var subscriber in activeSubscribers)
        {
            try
            {
                var source = FindSourcePhoto(allPhotosPath, subscriber.UserLogin);
                if (source is null)
                {
                    statuses.Add(new SubscriberPhotoStatus(
                        subscriber.UserLogin,
                        subscriber.UserName,
                        IsMissingPhoto: true));
                    continue;
                }

                var destination = Path.Combine(activePhotosPath, Path.GetFileName(source));
                File.Copy(source, destination, overwrite: true);
                statuses.Add(new SubscriberPhotoStatus(
                    subscriber.UserLogin,
                    subscriber.UserName,
                    IsMissingPhoto: false));
            }
            catch (Exception ex)
            {
                var message = $"Could not sync photo for {subscriber.UserLogin}: {ex.Message}";
                _logger.Log(AppLogLevel.Error, message);
                errors.Add(message);
                statuses.Add(new SubscriberPhotoStatus(
                    subscriber.UserLogin,
                    subscriber.UserName,
                    IsMissingPhoto: true));
            }
        }

        RemoveStaleActivePhotos(activePhotosPath, activeLogins, errors);
        return new ReconciliationResult(statuses, errors);
    }

    private string? FindSourcePhoto(string allPhotosPath, string login)
    {
        if (!Directory.Exists(allPhotosPath) || string.IsNullOrWhiteSpace(login))
        {
            return null;
        }

        var matches = Directory
            .EnumerateFiles(allPhotosPath, login + ".*", SearchOption.TopDirectoryOnly)
            .OrderBy(Path.GetExtension, StringComparer.OrdinalIgnoreCase)
            .ThenBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (matches.Count == 0)
        {
            return null;
        }

        if (matches.Count > 1)
        {
            _logger.Log(
                AppLogLevel.Warning,
                $"Multiple files found for subscriber '{login}'. Using first match: {Path.GetFileName(matches[0])}");
        }

        return matches[0];
    }

    private void RemoveStaleActivePhotos(
        string activePhotosPath,
        IReadOnlySet<string> activeLogins,
        ICollection<string> errors)
    {
        if (!Directory.Exists(activePhotosPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(activePhotosPath, "*", SearchOption.TopDirectoryOnly))
        {
            var login = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(login) || activeLogins.Contains(login))
            {
                continue;
            }

            try
            {
                File.Delete(file);
                _logger.Log(AppLogLevel.Info, $"Removed inactive subscriber photo: {Path.GetFileName(file)}");
            }
            catch (Exception ex)
            {
                var message = $"Could not remove inactive subscriber photo {Path.GetFileName(file)}: {ex.Message}";
                _logger.Log(AppLogLevel.Error, message);
                errors.Add(message);
            }
        }
    }
}
