using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Styling;
using FIXSniff.Models;
using FIXSniff.ViewModels;

namespace FIXSniff;

public partial class MainWindow : Window {
    public MainWindow() {
        InitializeComponent();
        DataContext = new MainWindowViewModel();
    }

    // Handle DataGrid selection in code-behind since Avalonia's DataGrid binding can be tricky
    private void DataGrid_SelectionChanged(object? sender, SelectionChangedEventArgs e) {
        if (sender is DataGrid { SelectedItem: FixFieldInfo selectedField } && DataContext is MainWindowViewModel viewModel) {
            viewModel.UpdateSelectedFieldDescription(selectedField.Description);
        }
    }

    // Theme toggle button handler
    private void ToggleTheme_Click(object? sender, RoutedEventArgs e) {
        if (Application.Current is not { } app) return;
        app.RequestedThemeVariant = app.ActualThemeVariant == ThemeVariant.Dark
            ? ThemeVariant.Light
            : ThemeVariant.Dark;
    }

    // Paste button handler
    private async void PasteButton_Click(object? sender, RoutedEventArgs e) {
        try {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null) return;

            var text = await clipboard.TryGetTextAsync();
            if (!string.IsNullOrEmpty(text) && DataContext is MainWindowViewModel viewModel) {
                viewModel.InputText = text;
            }
        } catch (Exception ex) {
            Console.WriteLine($"Clipboard access failed: {ex.Message}");
        }
    }
}
