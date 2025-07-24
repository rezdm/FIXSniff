using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FIXSniff.Models;
using FIXSniff.Services;
// ReSharper disable InvertIf

namespace FIXSniff.ViewModels;

public sealed class MainWindowViewModel : INotifyPropertyChanged {
    private string _inputText = string.Empty;
    private string _selectedFieldDescription = string.Empty;
    private readonly FixParserService _parserService;

    public MainWindowViewModel() {
        _parserService = new FixParserService();
        ParseCommand = new RelayCommand(ParseMessages, CanParseMessages);
        ParsedTabs = [];
    }

    public string InputText {
        get => _inputText;
        set {
            if (_inputText != value) {
                _inputText = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(InputText)); // Explicit notification
                ((RelayCommand)ParseCommand).RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedFieldDescription {
        get => _selectedFieldDescription;
        set {
            if (_selectedFieldDescription != value) {
                _selectedFieldDescription = value;
                OnPropertyChanged();
            }
        }
    }

    public ICommand ParseCommand { get; }
    public ObservableCollection<ParsedTabViewModel> ParsedTabs { get; }

    private async void ParseMessages() {
        try {
            if (string.IsNullOrWhiteSpace(InputText))
                return;

            // Clear existing tabs
            ParsedTabs.Clear();

            var lines = InputText.Split(['\n'], StringSplitOptions.RemoveEmptyEntries);
            
            for (var i = 0; i < lines.Length; i++) {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // Parse with version detection and spec download
                var parsedMessage = await _parserService.ParseMessageAsync(line);
                var tabViewModel = new ParsedTabViewModel($"Message {i + 1}", parsedMessage, this);
                ParsedTabs.Add(tabViewModel);
            }
        } catch (Exception ex) {
            // Create an error tab if parsing fails
            var errorMessage = new ParsedFixMessage {
                RawMessage = InputText,
                ErrorMessage = $"Parsing failed: {ex.Message}"
            };
            var errorTab = new ParsedTabViewModel("Error", errorMessage, this);
            ParsedTabs.Add(errorTab);
        }
    }

    private bool CanParseMessages() {
        return !string.IsNullOrWhiteSpace(InputText);
    }

    public void UpdateSelectedFieldDescription(string description) {
        SelectedFieldDescription = description;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class ParsedTabViewModel(string header, ParsedFixMessage parsedMessage, MainWindowViewModel mainViewModel) : INotifyPropertyChanged {
    public string TabHeader { get; } = header;
    public ParsedFixMessage ParsedMessage { get; } = parsedMessage;
    public ObservableCollection<FixFieldInfo> Fields { get; } = new(parsedMessage.Fields);

    public void OnFieldSelected(FixFieldInfo? field) {
        mainViewModel.UpdateSelectedFieldDescription(field != null ? field.Description : string.Empty);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

// Simple command implementation without ReactiveUI
public class RelayCommand(Action execute, Func<bool>? canExecute = null) : ICommand {
    private readonly Action _execute = execute ?? throw new ArgumentNullException(nameof(execute));

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) {
        return canExecute?.Invoke() ?? true;
    }

    public void Execute(object? parameter) {
        _execute();
    }

    public void RaiseCanExecuteChanged() {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}