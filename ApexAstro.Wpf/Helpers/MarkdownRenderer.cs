using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WpfBrush = System.Windows.Media.Brush;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfApplication = System.Windows.Application;

namespace ApexAstro.Wpf.Helpers
{
    /// <summary>
    /// Shared static helper that converts a subset of Markdown into a WPF FlowDocument.
    /// Handles: headings (# ## ###), bullet lists (- *), paragraphs, bold (**), inline code (`), fenced code blocks (```).
    /// </summary>
    public static class MarkdownRenderer
    {
        public static string? LoadEmbeddedMarkdown(string resourceName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) return null;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch { return null; }
        }

        public static void Render(string markdownContent, FlowDocument doc)
        {
            doc.Blocks.Clear();
            var lines = markdownContent.Split('\n');
            Paragraph? currentParagraph = null;
            bool inCodeBlock = false;
            Paragraph? codeBlock = null;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("```"))
                {
                    if (currentParagraph != null) { doc.Blocks.Add(currentParagraph); currentParagraph = null; }
                    if (!inCodeBlock)
                    {
                        inCodeBlock = true;
                        codeBlock = new Paragraph();
                        if (WpfApplication.Current.TryFindResource("ThemeCodeBlockBackground") is WpfBrush bg)
                            codeBlock.Background = bg;
                        if (WpfApplication.Current.TryFindResource("ThemeCodeBlockForeground") is WpfBrush fg)
                            codeBlock.Foreground = fg;
                        codeBlock.Margin = new Thickness(10, 5, 10, 5);
                        codeBlock.Padding = new Thickness(10);
                        codeBlock.FontFamily = new WpfFontFamily("Consolas, Courier New, monospace");
                        codeBlock.FontSize = 13;
                    }
                    else
                    {
                        inCodeBlock = false;
                        if (codeBlock != null) { doc.Blocks.Add(codeBlock); codeBlock = null; }
                    }
                    continue;
                }

                if (inCodeBlock)
                {
                    if (codeBlock != null)
                    {
                        if (codeBlock.Inlines.Count > 0) codeBlock.Inlines.Add(new LineBreak());
                        codeBlock.Inlines.Add(new Run(line));
                    }
                    continue;
                }

                if (string.IsNullOrEmpty(trimmedLine))
                {
                    if (currentParagraph != null) { doc.Blocks.Add(currentParagraph); currentParagraph = null; }
                    continue;
                }

                if (trimmedLine == "---" || trimmedLine == "***" || trimmedLine == "___")
                {
                    if (currentParagraph != null) { doc.Blocks.Add(currentParagraph); currentParagraph = null; }
                    var separator = new System.Windows.Shapes.Rectangle
                    {
                        Height = 1,
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
                    };
                    if (WpfApplication.Current.TryFindResource("ThemeBorder") is WpfBrush borderBrush)
                        separator.Fill = borderBrush;
                    else
                        separator.Fill = System.Windows.Media.Brushes.Gray;
                    doc.Blocks.Add(new BlockUIContainer(separator) { Margin = new Thickness(0, 8, 0, 8) });
                }
                else if (trimmedLine.StartsWith("# "))
                {
                    if (currentParagraph != null) { doc.Blocks.Add(currentParagraph); currentParagraph = null; }
                    var run = new Run(trimmedLine.Substring(2)) { FontSize = 22, FontWeight = FontWeights.Bold };
                    if (WpfApplication.Current.TryFindResource("ThemeAccentBlue") is WpfBrush accent)
                        run.Foreground = accent;
                    var h = new Paragraph(run);
                    h.Margin = new Thickness(0, 16, 0, 6);
                    h.BorderBrush = run.Foreground;
                    h.BorderThickness = new Thickness(0, 0, 0, 1);
                    h.Padding = new Thickness(0, 0, 0, 4);
                    doc.Blocks.Add(h);
                }
                else if (trimmedLine.StartsWith("## "))
                {
                    if (currentParagraph != null) { doc.Blocks.Add(currentParagraph); currentParagraph = null; }
                    var run = new Run(trimmedLine.Substring(3)) { FontSize = 17, FontWeight = FontWeights.Bold };
                    if (WpfApplication.Current.TryFindResource("ThemeAccentBlue") is WpfBrush accent)
                        run.Foreground = accent;
                    var h = new Paragraph(run);
                    h.Margin = new Thickness(0, 14, 0, 4);
                    doc.Blocks.Add(h);
                }
                else if (trimmedLine.StartsWith("### "))
                {
                    if (currentParagraph != null) { doc.Blocks.Add(currentParagraph); currentParagraph = null; }
                    var run = new Run(trimmedLine.Substring(4)) { FontSize = 15, FontWeight = FontWeights.Bold };
                    if (WpfApplication.Current.TryFindResource("ThemeAccentBlue") is WpfBrush accent)
                        run.Foreground = accent;
                    var h = new Paragraph(run);
                    h.Margin = new Thickness(0, 12, 0, 4);
                    doc.Blocks.Add(h);
                }
                else if (trimmedLine.StartsWith("- ") || trimmedLine.StartsWith("* "))
                {
                    if (currentParagraph != null) { doc.Blocks.Add(currentParagraph); currentParagraph = null; }
                    var listItem = new ListItem();
                    var itemPara = new Paragraph { Margin = new Thickness(0, 1, 0, 1) };
                    AddInlines(itemPara, trimmedLine.Substring(2));
                    listItem.Blocks.Add(itemPara);
                    // Wrap in a List so WPF handles indentation and wrapping correctly
                    var list = new List { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(0, 2, 0, 2), Padding = new Thickness(20, 0, 0, 0) };
                    list.ListItems.Add(listItem);
                    doc.Blocks.Add(list);
                }
                else if (trimmedLine.StartsWith("<img "))
                {
                    if (currentParagraph != null) { doc.Blocks.Add(currentParagraph); currentParagraph = null; }
                    var imgBlock = RenderImage(trimmedLine);
                    if (imgBlock != null) doc.Blocks.Add(imgBlock);
                }
                else
                {
                    if (currentParagraph == null)
                        currentParagraph = new Paragraph { Margin = new Thickness(0, 4, 0, 8), LineHeight = 20 };
                    else
                        currentParagraph.Inlines.Add(new LineBreak());
                    AddInlines(currentParagraph, trimmedLine);
                }
            }

            if (currentParagraph != null) doc.Blocks.Add(currentParagraph);
        }

        public static void RenderFallback(FlowDocument doc, string title)
        {
            doc.Blocks.Clear();
            doc.Blocks.Add(new Paragraph(new Run(title) { FontSize = 18, FontWeight = FontWeights.Bold })
                { Margin = new Thickness(0, 0, 0, 10) });
            doc.Blocks.Add(new Paragraph(new Run($"{title} content could not be loaded.")));
        }

        private static Block? RenderImage(string imgTag)
        {
            var srcMatch = System.Text.RegularExpressions.Regex.Match(imgTag, @"src=""([^""]+)""");
            if (!srcMatch.Success) return null;

            var filename = Path.GetFileName(srcMatch.Groups[1].Value);
            var resourceName = "ApexAstro.Wpf.Documentation." + filename;

            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) return null;

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.StreamSource = stream;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                var image = new System.Windows.Controls.Image
                {
                    Source = bitmap,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.Both,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                    Width = double.NaN
                };
                return new BlockUIContainer(image) { Margin = new Thickness(0, 8, 0, 8) };
            }
            catch { return null; }
        }

        private static void AddInlines(Paragraph paragraph, string text)
        {
            // Split on bold, inline-code, and markdown links
            var parts = System.Text.RegularExpressions.Regex.Split(text, @"(\*\*.*?\*\*|`.*?`|\[.*?\]\(.*?\))");
            foreach (var part in parts)
            {
                if (string.IsNullOrEmpty(part)) continue;
                if (part.StartsWith("**") && part.EndsWith("**") && part.Length > 4)
                {
                    paragraph.Inlines.Add(new Run(part.Substring(2, part.Length - 4)) { FontWeight = FontWeights.Bold });
                }
                else if (part.StartsWith("`") && part.EndsWith("`") && part.Length > 2)
                {
                    var r = new Run(part.Substring(1, part.Length - 2))
                        { FontFamily = new WpfFontFamily("Consolas, Courier New, monospace"), FontSize = 12 };
                    if (WpfApplication.Current.TryFindResource("ThemeCodeBlockBackground") is WpfBrush bg)
                        r.Background = bg;
                    if (WpfApplication.Current.TryFindResource("ThemeCodeBlockForeground") is WpfBrush fg)
                        r.Foreground = fg;
                    paragraph.Inlines.Add(r);
                }
                else if (System.Text.RegularExpressions.Regex.IsMatch(part, @"^\[.*?\]\(.*?\)$"))
                {
                    var linkMatch = System.Text.RegularExpressions.Regex.Match(part, @"^\[(.*?)\]\((.*?)\)$");
                    var linkText = linkMatch.Groups[1].Value;
                    var url = linkMatch.Groups[2].Value;
                    try
                    {
                        var hyperlink = new Hyperlink(new Run(linkText)) { NavigateUri = new Uri(url) };
                        hyperlink.RequestNavigate += (s, e) =>
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                        };
                        paragraph.Inlines.Add(hyperlink);
                    }
                    catch
                    {
                        paragraph.Inlines.Add(new Run(linkText));
                    }
                }
                else
                {
                    paragraph.Inlines.Add(new Run(part));
                }
            }
        }
    }
}
