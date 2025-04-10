using Microsoft.Win32;
using System.Windows;
using System.Windows.Threading;
using Ookii.Dialogs.Wpf;

namespace WTT_BundleMaster;

public interface IFileDialogService
{
    Task<string> PickDirectoryAsync(string title);
    Task<string> PickSaveFileAsync(string filter, string title);
    
    Task<string> PickFileAsync(string filter, string title);
}

public class WpfFileDialogService : IFileDialogService
{
    private static readonly Dispatcher _dispatcher = Application.Current.Dispatcher;
    
    public async Task<string> PickDirectoryAsync(string title)
    {
        return await _dispatcher.InvokeAsync(() =>
        {
            var dialog = new VistaFolderBrowserDialog
            {
                Description = title,
                UseDescriptionForTitle = true
            };
            
            return dialog.ShowDialog() == true 
                ? dialog.SelectedPath 
                : null;
        }, DispatcherPriority.ApplicationIdle);
    }

    public async Task<string> PickSaveFileAsync(string filter, string title)
    {
        return await _dispatcher.InvokeAsync(() =>
        {
            var dialog = new SaveFileDialog
            {
                Filter = filter,
                Title = title
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }, DispatcherPriority.ApplicationIdle);
    }
    public async Task<string> PickFileAsync(string filter, string title)
    {
        return await _dispatcher.InvokeAsync(() =>
        {
            var dialog = new OpenFileDialog
            {
                Filter = filter,
                Title = title
            };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }, DispatcherPriority.ApplicationIdle);
    }

}