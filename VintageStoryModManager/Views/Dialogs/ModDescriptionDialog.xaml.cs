using System.Diagnostics;
using System.Text.RegularExpressions;
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

        // Process HTML content to handle special elements
        var processedHtml = ProcessHtmlContent(htmlContent);

        // Wrap HTML content with styling for dark theme
        var styledHtml = WrapHtmlWithStyle(processedHtml);
        DescriptionBrowser.NavigateToString(styledHtml);
    }

    /// <summary>
    /// Processes HTML content to convert YouTube embeds to thumbnails,
    /// strip highlights/marks, and convert spoilers to details elements.
    /// </summary>
    private static string ProcessHtmlContent(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return html;

        var doc = new HtmlAgilityPack.HtmlDocument();
        doc.LoadHtml(html);

        // Fix protocol-relative URLs (//www... -> https://www...)
        FixProtocolRelativeUrls(doc);

        // Process YouTube iframes - convert to clickable thumbnails
        ProcessYouTubeEmbeds(doc);

        // Process YouTube links - convert to clickable thumbnails
        ProcessYouTubeLinks(doc);

        // Strip highlight/mark elements while preserving text
        StripHighlightElements(doc);

        // Convert spoiler divs to details/summary elements
        ConvertSpoilerElements(doc);

        return doc.DocumentNode.InnerHtml;
    }

    /// <summary>
    /// Fixes protocol-relative URLs (//example.com) to use https://
    /// </summary>
    private static void FixProtocolRelativeUrls(HtmlAgilityPack.HtmlDocument doc)
    {
        // Fix src attributes (images, iframes)
        var elementsWithSrc = doc.DocumentNode.SelectNodes("//*[@src]")?.ToList();
        if (elementsWithSrc != null)
        {
            foreach (var element in elementsWithSrc)
            {
                var src = element.GetAttributeValue("src", "");
                if (src.StartsWith("//"))
                {
                    element.SetAttributeValue("src", "https:" + src);
                }
            }
        }

        // Fix href attributes (links)
        var elementsWithHref = doc.DocumentNode.SelectNodes("//*[@href]")?.ToList();
        if (elementsWithHref != null)
        {
            foreach (var element in elementsWithHref)
            {
                var href = element.GetAttributeValue("href", "");
                if (href.StartsWith("//"))
                {
                    element.SetAttributeValue("href", "https:" + href);
                }
            }
        }
    }

    /// <summary>
    /// Converts YouTube iframe embeds to clickable thumbnail images with play buttons.
    /// </summary>
    private static void ProcessYouTubeEmbeds(HtmlAgilityPack.HtmlDocument doc)
    {
        // Find all iframes
        var iframes = doc.DocumentNode.SelectNodes("//iframe")?.ToList();
        if (iframes == null) return;

        foreach (var iframe in iframes)
        {
            var src = iframe.GetAttributeValue("src", "");

            // Match YouTube embed URLs (youtube.com/embed/ID or youtube-nocookie.com/embed/ID)
            var youtubeMatch = Regex.Match(src, @"(?:youtube\.com|youtube-nocookie\.com)/embed/([a-zA-Z0-9_-]{11})");
            if (!youtubeMatch.Success) continue;

            var videoId = youtubeMatch.Groups[1].Value;
            var watchUrl = $"https://www.youtube.com/watch?v={videoId}";
            var thumbnailUrl = $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg";

            // Create a simple clickable thumbnail - just a linked image with text
            var containerHtml = $@"
                <div class=""youtube-thumbnail"">
                    <a href=""{watchUrl}"">
                        <img src=""{thumbnailUrl}"" alt=""YouTube Video"" style=""border: 2px solid #f00; border-radius: 8px;"" /><br/>
                        <span style=""color: #f00;"">&#9658; Watch on YouTube</span>
                    </a>
                </div>";

            var containerNode = HtmlAgilityPack.HtmlNode.CreateNode(containerHtml);
            iframe.ParentNode.ReplaceChild(containerNode, iframe);
        }
    }

    /// <summary>
    /// Converts YouTube link URLs (youtu.be, youtube.com/watch) to clickable thumbnails.
    /// </summary>
    private static void ProcessYouTubeLinks(HtmlAgilityPack.HtmlDocument doc)
    {
        // Find all anchor tags
        var links = doc.DocumentNode.SelectNodes("//a[@href]")?.ToList();
        if (links == null) return;

        foreach (var link in links)
        {
            var href = link.GetAttributeValue("href", "");

            // Match YouTube URLs: youtu.be/ID, youtube.com/watch?v=ID
            string? videoId = null;

            // youtu.be/VIDEO_ID format
            var shortMatch = Regex.Match(href, @"youtu\.be/([a-zA-Z0-9_-]{11})");
            if (shortMatch.Success)
            {
                videoId = shortMatch.Groups[1].Value;
            }
            else
            {
                // youtube.com/watch?v=VIDEO_ID format
                var watchMatch = Regex.Match(href, @"youtube\.com/watch\?.*v=([a-zA-Z0-9_-]{11})");
                if (watchMatch.Success)
                {
                    videoId = watchMatch.Groups[1].Value;
                }
            }

            if (videoId == null) continue;

            var watchUrl = $"https://www.youtube.com/watch?v={videoId}";
            var thumbnailUrl = $"https://img.youtube.com/vi/{videoId}/hqdefault.jpg";

            // Create a simple clickable thumbnail - just a linked image with text
            var containerHtml = $@"
                <div class=""youtube-thumbnail"">
                    <a href=""{watchUrl}"">
                        <img src=""{thumbnailUrl}"" alt=""YouTube Video"" style=""border: 2px solid #f00; border-radius: 8px;"" /><br/>
                        <span style=""color: #f00;"">&#9658; Watch on YouTube</span>
                    </a>
                </div>";

            var containerNode = HtmlAgilityPack.HtmlNode.CreateNode(containerHtml);
            link.ParentNode.ReplaceChild(containerNode, link);
        }
    }

    /// <summary>
    /// Strips mark, highlighted elements, and inline background-color styles
    /// from any element while preserving the text content.
    /// </summary>
    private static void StripHighlightElements(HtmlAgilityPack.HtmlDocument doc)
    {
        // Remove <mark> elements, keeping inner content
        var markElements = doc.DocumentNode.SelectNodes("//mark")?.ToList();
        if (markElements != null)
        {
            foreach (var mark in markElements)
            {
                // Replace mark with its children to preserve inner HTML structure
                var parent = mark.ParentNode;
                foreach (var child in mark.ChildNodes.ToList())
                {
                    parent.InsertBefore(child, mark);
                }
                parent.RemoveChild(mark);
            }
        }

        // Find ALL elements with background-color styling (highlights) - spans, anchors, divs, etc.
        var styledElements = doc.DocumentNode.SelectNodes("//*[@style]")?.ToList();
        if (styledElements != null)
        {
            foreach (var element in styledElements)
            {
                var style = element.GetAttributeValue("style", "");
                var styleLower = style.ToLowerInvariant();

                if (styleLower.Contains("background-color") || styleLower.Contains("background:"))
                {
                    // Remove the background styling but keep other styles if present
                    var newStyle = Regex.Replace(style, @"background(-color)?\s*:\s*[^;]+;?", "", RegexOptions.IgnoreCase).Trim();

                    // Clean up any leftover semicolons or spaces
                    newStyle = Regex.Replace(newStyle, @";\s*;", ";").Trim();
                    newStyle = newStyle.TrimEnd(';').Trim();

                    if (string.IsNullOrWhiteSpace(newStyle))
                    {
                        element.Attributes.Remove("style");
                    }
                    else
                    {
                        element.SetAttributeValue("style", newStyle);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Converts spoiler div structures to JavaScript-based collapsible elements.
    /// (WPF WebBrowser uses IE engine which doesn't support HTML5 details/summary)
    /// </summary>
    private static void ConvertSpoilerElements(HtmlAgilityPack.HtmlDocument doc)
    {
        // Find spoiler containers (div.spoiler)
        var spoilers = doc.DocumentNode.SelectNodes("//div[contains(@class, 'spoiler')]")?.ToList();
        if (spoilers == null) return;

        int spoilerId = 0;
        foreach (var spoiler in spoilers)
        {
            // Skip if this is a child spoiler element (toggle or text)
            var classes = spoiler.GetAttributeValue("class", "");
            if (classes.Contains("spoiler-toggle") || classes.Contains("spoiler-text"))
                continue;

            // Find the toggle (title) and text (content) elements
            var toggle = spoiler.SelectSingleNode(".//div[contains(@class, 'spoiler-toggle')]");
            var text = spoiler.SelectSingleNode(".//div[contains(@class, 'spoiler-text')]");

            if (toggle == null && text == null) continue;

            var toggleText = toggle?.InnerHtml ?? "Show/Hide";
            var contentHtml = text?.InnerHtml ?? "";
            var id = $"spoiler_{spoilerId++}";

            // Create JavaScript-based collapsible (IE compatible)
            var collapsibleHtml = $@"
                <div class=""collapsible-container"">
                    <div class=""collapsible-toggle"" onclick=""var c=document.getElementById('{id}');var a=document.getElementById('{id}_arrow');if(c.style.display==='none'){{c.style.display='block';a.innerHTML='&#9660;';}}else{{c.style.display='none';a.innerHTML='&#9654;';}}"">
                        <span id=""{id}_arrow"" class=""arrow"">&#9654;</span>
                        {toggleText}
                    </div>
                    <div id=""{id}"" class=""collapsible-content"" style=""display:none;"">
                        {contentHtml}
                    </div>
                </div>";

            var containerNode = HtmlAgilityPack.HtmlNode.CreateNode(collapsibleHtml);
            spoiler.ParentNode.ReplaceChild(containerNode, spoiler);
        }
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
        /* YouTube thumbnail styling */
        .youtube-thumbnail {{
            position: relative;
            display: inline-block;
            max-width: 480px;
            margin: 12px 0;
            cursor: pointer;
        }}
        .youtube-thumbnail img {{
            display: block;
            border-radius: 8px;
            box-shadow: 0 2px 8px rgba(0,0,0,0.3);
        }}
        .youtube-thumbnail .play-button {{
            position: absolute;
            top: 50%;
            left: 50%;
            transform: translate(-50%, -50%);
            opacity: 0.9;
            transition: opacity 0.2s, transform 0.2s;
        }}
        .youtube-thumbnail:hover .play-button {{
            opacity: 1;
            transform: translate(-50%, -50%) scale(1.1);
        }}
        .youtube-thumbnail a {{
            display: block;
            text-decoration: none;
        }}
        /* Collapsible spoiler styling */
        .collapsible-container {{
            background-color: #252525;
            border: 1px solid #444;
            border-radius: 6px;
            margin: 12px 0;
            overflow: hidden;
        }}
        .collapsible-toggle {{
            padding: 10px 14px;
            cursor: pointer;
            background-color: #333;
            color: #fff;
            font-weight: 500;
        }}
        .collapsible-toggle:hover {{
            background-color: #3a3a3a;
        }}
        .collapsible-toggle .arrow {{
            display: inline-block;
            margin-right: 8px;
            font-size: 10px;
        }}
        .collapsible-content {{
            padding: 14px;
            border-top: 1px solid #444;
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
        // e.Uri is null for NavigateToString, allow it
        if (e.Uri == null)
            return;

        var url = e.Uri.ToString();

        // Allow about:blank (used internally by NavigateToString)
        if (url.StartsWith("about:blank"))
            return;

        // Allow javascript: URLs to execute (for spoiler toggles)
        if (url.StartsWith("javascript:"))
            return;

        // Cancel navigation and open in external browser
        e.Cancel = true;

        if (url.StartsWith("http://") || url.StartsWith("https://"))
        {
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch
            {
                // Ignore errors opening URLs
            }
        }
    }

    private void ViewOnWebsite_Click(object sender, RoutedEventArgs e)
    {
        var url = $"https://mods.vintagestory.at/show/mod/{_assetId}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}
