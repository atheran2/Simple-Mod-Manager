using System.Diagnostics;
using System.Windows;
using VintageStoryModManager.Models;

namespace VintageStoryModManager.Views.Dialogs;

public partial class ModDescriptionDialog : Window
{
    private readonly int _assetId;

    public ModDescriptionDialog(Window owner, DownloadableModOnList mod, string htmlContent)
    {
        InitializeComponent();
        Owner = owner;

        _assetId = mod.AssetId;

        ModNameText.Text = mod.Name;
        ModAuthorText.Text = $"by {mod.Author}";

        // Wrap HTML content with styling for dark theme
        var styledHtml = WrapHtmlWithStyle(htmlContent);
        DescriptionBrowser.NavigateToString(styledHtml);
    }

    private static string WrapHtmlWithStyle(string content)
    {
        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <style>
        body {{
            background-color: #2d2d2d;
            color: #e0e0e0;
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            font-size: 14px;
            line-height: 1.6;
            padding: 16px;
            margin: 0;
        }}
        a {{
            color: #7cb7ff;
        }}
        a:hover {{
            color: #a8d1ff;
        }}
        img {{
            max-width: 100%;
            height: auto;
        }}
        h1, h2, h3, h4, h5, h6 {{
            color: #ffffff;
            margin-top: 1em;
            margin-bottom: 0.5em;
        }}
        code, pre {{
            background-color: #1e1e1e;
            padding: 2px 6px;
            border-radius: 4px;
            font-family: Consolas, monospace;
        }}
        pre {{
            padding: 12px;
            overflow-x: auto;
        }}
        blockquote {{
            border-left: 3px solid #555;
            margin-left: 0;
            padding-left: 16px;
            color: #aaa;
        }}
        table {{
            border-collapse: collapse;
            width: 100%;
        }}
        th, td {{
            border: 1px solid #444;
            padding: 8px;
            text-align: left;
        }}
        th {{
            background-color: #333;
        }}
        hr {{
            border: none;
            border-top: 1px solid #444;
            margin: 1em 0;
        }}
    </style>
</head>
<body>
{content}
</body>
</html>";
    }

    private void DescriptionBrowser_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
    {
        // Open external links in default browser instead of the WebBrowser control
        if (e.Uri != null && !string.IsNullOrEmpty(e.Uri.ToString()))
        {
            e.Cancel = true;
            Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true });
        }
    }

    private void ViewOnWebsite_Click(object sender, RoutedEventArgs e)
    {
        var url = $"https://mods.vintagestory.at/show/mod/{_assetId}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
