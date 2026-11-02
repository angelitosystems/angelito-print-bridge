// Copyright 2026 Angelito Systems S.A.C.
// SPDX-License-Identifier: Apache-2.0

using Angelito.PrintBridge.Desktop.ViewModels;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Angelito.PrintBridge.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm) vm.PickCertificateFile = PickCertificateAsync;
        };
    }

    /// <summary>When false, closing hides the window to the tray; printing keeps running in the agent.</summary>
    public bool AllowClose { get; set; }

    private async Task<string?> PickCertificateAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar certificado de aplicación",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Certificado PEM") { Patterns = ["*.pem", "*.crt", "*.cer"] }],
        });
        if (files.Count == 0) return null;
        await using var stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
