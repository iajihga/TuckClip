using System.Text.Json;
using System.Text.Json.Serialization;
using TuckClip.Platform.Windows.Interop;

namespace TuckClip.Windows.Services;

public sealed class WindowsSettingsCorruptedException : Exception
{
    public WindowsSettingsCorruptedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class WindowsSettingsStore
{
    public const string FileName = "settings-v1.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = false,
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    private readonly string _dataDirectory;
    private readonly string _settingsPath;

    public WindowsSettingsStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _dataDirectory = Path.GetFullPath(dataDirectory);
        _settingsPath = Path.Combine(_dataDirectory, FileName);
    }

    public string DataDirectory => _dataDirectory;

    public string SettingsPath => _settingsPath;

    public async Task<WindowsAppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var stream = new FileStream(
                _settingsPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<PersistedSettings>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (document is null || document.SchemaVersion != 1)
            {
                throw new InvalidDataException("The settings file has an unsupported schema version.");
            }

            return new WindowsAppSettings
            {
                RecordingEnabled = document.RecordingEnabled,
                AutomaticPasteEnabled = document.AutomaticPasteEnabled,
                CapturesImages = document.CapturesImages,
                RetentionDays = document.RetentionDays,
                MaximumItemCount = document.MaximumItemCount,
                ExcludedProcessNames = document.ExcludedProcessNames ?? Array.Empty<string>(),
                GlobalHotKey = document.HotKeyVirtualKey.HasValue && document.HotKeyModifiers.HasValue
                    ? new GlobalHotKey(
                        document.HotKeyVirtualKey.Value,
                        (HotKeyModifiers)document.HotKeyModifiers.Value)
                    : GlobalHotKey.Default,
                AppLanguage = Enum.TryParse<AppLanguage>(document.AppLanguage, out var language)
                    ? language
                    : AppLanguage.System,
            }.Validate();
        }
        catch (FileNotFoundException)
        {
            return new WindowsAppSettings();
        }
        catch (DirectoryNotFoundException)
        {
            return new WindowsAppSettings();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is
            JsonException or
            InvalidDataException or
            ArgumentException or
            IOException or
            UnauthorizedAccessException)
        {
            throw new WindowsSettingsCorruptedException(
                "The local settings file is damaged or invalid. It was left unchanged.",
                exception);
        }
    }

    public async Task SaveAsync(
        WindowsAppSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Validate();
        var document = new PersistedSettings(
            SchemaVersion: 1,
            normalized.RecordingEnabled,
            normalized.AutomaticPasteEnabled,
            normalized.CapturesImages,
            normalized.RetentionDays,
            normalized.MaximumItemCount,
            normalized.ExcludedProcessNames.ToArray(),
            normalized.GlobalHotKey.VirtualKey,
            (uint)normalized.GlobalHotKey.Modifiers,
            normalized.AppLanguage.ToString());
        var contents = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);

        Directory.CreateDirectory(_dataDirectory);
        var temporaryPath = Path.Combine(
            _dataDirectory,
            $".{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(contents, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private sealed record PersistedSettings(
        int SchemaVersion,
        bool RecordingEnabled,
        bool AutomaticPasteEnabled,
        bool CapturesImages,
        int RetentionDays,
        int MaximumItemCount,
        string[]? ExcludedProcessNames,
        uint? HotKeyVirtualKey = null,
        uint? HotKeyModifiers = null,
        string? AppLanguage = null);
}
