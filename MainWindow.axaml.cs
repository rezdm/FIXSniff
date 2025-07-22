using Avalonia.Controls;
using Avalonia.Interactivity;
using FIXSniff.ViewModels;
using FIXSniff.Models;

namespace FIXSniff;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        
        // Subscribe to DataGrid selection changes
        var tabControl = this.FindControl<TabControl>("TabsControl");
        if (tabControl != null)
        {
            tabControl.SelectionChanged += OnTabSelectionChanged;
        }
    }

    private void OnTabSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Handle tab changes if needed
    }

    // Handle DataGrid selection in code-behind since Avalonia's DataGrid binding can be tricky
    private void DataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is DataGrid dataGrid && dataGrid.SelectedItem is FixFieldInfo selectedField)
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.UpdateSelectedFieldDescription(selectedField.Description);
            }
        }
    }
}