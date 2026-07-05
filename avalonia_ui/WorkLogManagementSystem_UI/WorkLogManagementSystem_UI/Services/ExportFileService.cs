using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using WorkLogManagementSystem_UI.Views;

namespace WorkLogManagementSystem_UI.Services;

public sealed class ExportFileService
{
    public async Task<ExportDateRange?> SelectExportRangeAsync(
        DateTime? defaultStartDate,
        DateTime? defaultEndDate,
        CancellationToken cancellationToken)
    {
        Window? mainWindow = ResolveMainWindow();
        if (mainWindow is null)
        {
            DateTime fallbackDate = DateTime.Today;
            return new ExportDateRange(
                (defaultStartDate ?? fallbackDate).Date,
                (defaultEndDate ?? defaultStartDate ?? fallbackDate).Date);
        }

        ExportRangeDialog dialog = new(defaultStartDate, defaultEndDate);
        using CancellationTokenRegistration registration = cancellationToken.Register(dialog.Close);
        return await dialog.ShowDialog<ExportDateRange?>(mainWindow);
    }

    public async Task<string?> SaveExcelAsync(byte[] content, string suggestedFileName, CancellationToken cancellationToken)
    {
        IStorageProvider? storageProvider = ResolveStorageProvider();
        if (storageProvider is not null && storageProvider.CanSave)
        {
            return await SaveWithPickerAsync(storageProvider, content, suggestedFileName, cancellationToken);
        }

        return await SaveToDownloadsAsync(content, suggestedFileName, cancellationToken);
    }

    private async Task<string?> SaveWithPickerAsync(
        IStorageProvider storageProvider,
        byte[] content,
        string suggestedFileName,
        CancellationToken cancellationToken)
    {
        IStorageFile? file = await storageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title = "업무일지 엑셀 내보내기",
                SuggestedFileName = suggestedFileName,
                DefaultExtension = "xlsx",
                ShowOverwritePrompt = true,
                FileTypeChoices =
                [
                    new FilePickerFileType("Excel Workbook")
                    {
                        Patterns = ["*.xlsx"],
                        MimeTypes = ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"]
                    }
                ]
            });

        if (file is null)
        {
            return null;
        }

        await using Stream stream = await file.OpenWriteAsync();
        await stream.WriteAsync(content, cancellationToken);
        return file.Path.LocalPath;
    }

    private async Task<string> SaveToDownloadsAsync(byte[] content, string suggestedFileName, CancellationToken cancellationToken)
    {
        string directory = ResolveExportDirectory();
        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, suggestedFileName);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
        return path;
    }

    private static IStorageProvider? ResolveStorageProvider()
    {
        Window? mainWindow = ResolveMainWindow();
        if (mainWindow is not null)
        {
            return mainWindow.StorageProvider;
        }

        IApplicationLifetime? lifetime = Application.Current?.ApplicationLifetime;
        if (lifetime is ISingleViewApplicationLifetime { MainView: { } mainView })
        {
            return TopLevel.GetTopLevel(mainView)?.StorageProvider;
        }

        return null;
    }

    private static Window? ResolveMainWindow()
    {
        IApplicationLifetime? lifetime = Application.Current?.ApplicationLifetime;
        return lifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } mainWindow } ? mainWindow : null;
    }

    private static string ResolveExportDirectory()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(userProfile, "Downloads");
        }

        return AppContext.BaseDirectory;
    }
}
