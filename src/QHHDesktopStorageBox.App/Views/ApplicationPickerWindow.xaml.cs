using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using QHHDesktopStorageBox.Native.Applications;

namespace QHHDesktopStorageBox.App.Views;

public partial class ApplicationPickerWindow : Window
{
    private readonly ICollectionView _applicationsView;

    internal ApplicationPickerWindow()
    {
        InitializeComponent();
        Applications = [];
        DataContext = this;
        _applicationsView = CollectionViewSource.GetDefaultView(Applications);
        _applicationsView.Filter = FilterApplication;
        Loaded += OnLoaded;
    }

    public ObservableCollection<InstalledApplicationInfo> Applications { get; }

    public InstalledApplicationInfo? SelectedApplication { get; private set; }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        StatusText.Text = "正在读取开始菜单应用…";
        AddButton.IsEnabled = false;
        try
        {
            var applications = await InstalledApplicationCatalog.GetInstalledApplicationsAsync();
            foreach (var application in applications)
            {
                Applications.Add(application);
            }

            StatusText.Text = Applications.Count == 0
                ? "没有读取到开始菜单应用。"
                : string.Empty;
            StatusText.Visibility = Applications.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            SearchBox.Focus();
        }
        catch (Exception exception)
        {
            StatusText.Text = "读取应用列表失败：" + exception.Message;
            StatusText.Visibility = Visibility.Visible;
        }
    }

    private bool FilterApplication(object item)
    {
        if (item is not InstalledApplicationInfo application)
        {
            return false;
        }

        var query = SearchBox?.Text.Trim();
        return string.IsNullOrWhiteSpace(query)
            || application.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
            || application.AppId.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        _applicationsView.Refresh();
    }

    private void OnSelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        AddButton.IsEnabled = ApplicationsList.SelectedItem is InstalledApplicationInfo;
    }

    private void OnApplicationsMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ApplicationsList.SelectedItem is InstalledApplicationInfo)
        {
            ConfirmSelection();
        }
    }

    private void OnAddClick(object sender, RoutedEventArgs e)
    {
        ConfirmSelection();
    }

    private void ConfirmSelection()
    {
        if (ApplicationsList.SelectedItem is not InstalledApplicationInfo application)
        {
            return;
        }

        SelectedApplication = application;
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            DialogResult = false;
        }
    }
}
