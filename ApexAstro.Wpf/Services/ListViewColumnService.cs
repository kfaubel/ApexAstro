
using ApexAstro.Wpf.Services;
using ApexAstro.Wpf.Converters;
using System.Windows.Controls;
using ApexAstro.Wpf.ViewModels;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Linq;
using System.Windows.Controls.Primitives;

namespace ApexAstro.Wpf.Services
{
    public class ListViewColumnService : IListViewColumnService
    {
        private enum MetadataColumnType
        {
            Custom,
            Fits,
            Csv,
            Arw
        }

        private readonly System.Windows.Controls.ListView _fileListView;
        private readonly AppConfig _appConfig;
        private GridSplitter? _gridSplitter; // Track the splitter for manual move detection
        private List<string> _defaultColumnOrder = new();

        public ListViewColumnService(System.Windows.Controls.ListView fileListView, AppConfig appConfig, System.Windows.Media.Brush greenTextBrush, System.Windows.Media.Brush blueTextBrush)
        {
            _fileListView = fileListView;
            _appConfig = appConfig;
            // Note: greenTextBrush and blueTextBrush parameters are no longer needed as we bind to theme resources directly

            // Capture column reorder/resize drag completion so we can persist user column order.
            _fileListView.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnColumnDragCompleted));
        }
        
        /// <summary>
        /// Creates a clickable column header that triggers sorting when clicked
        /// </summary>
        private GridViewColumnHeader CreateSortableHeader(string headerText, string? displayName = null)
        {
            var header = new GridViewColumnHeader
            {
                Content = displayName ?? headerText,
                Tag = headerText, // Store column name for sorting
                ToolTip = "Click to sort (A→Z or 0→9)\nClick again to reverse"
            };
            
            // Add click handler for sorting
            header.Click += ColumnHeader_Click;
            
            return header;
        }
        
        /// <summary>
        /// Gets the display name for a metadata keyword, shortening long names to save space
        /// </summary>
        private string GetDisplayName(string keyword)
        {
            return keyword switch
            {
                "EXPOSURE" => "EXP",
                "EXPTIME" => "EXP",
                "CCD_TIME" => "TEMP",
                "CCD_TEMP" => "TEMP",
                "CCD-TEMP" => "TEMP",
                _ => keyword
            };
        }

        private static string NormalizeColumnKey(string columnName)
        {
            return columnName
                .Trim()
                .Replace("-", "_", StringComparison.Ordinal)
                .ToUpperInvariant();
        }

        private static double GetKeywordMinimumWidth(string columnName)
        {
            string normalized = NormalizeColumnKey(columnName);

            return normalized switch
            {
                // Ensure these short metric columns can display values like "12.34".
                "RMS" or "HFR" or "ECC" => 62,

                // Ensure exposure/temperature values are never collapsed to "1....".
                "EXPOSURE" or "EXPTIME" or "CCD_TIME" or "CCD_TEMP" => 74,

                _ => 0
            };
        }

        private static System.Windows.Media.Brush GetCellSeparatorBrush()
        {
            return System.Windows.Application.Current.TryFindResource("ThemeBorderBrush") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Gray;
        }

        private static DataTemplate CreateBorderedElementTemplate(FrameworkElementFactory innerFactory, System.Windows.Thickness? padding = null)
        {
            var borderFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Border));
            borderFactory.SetValue(System.Windows.Controls.Border.BorderBrushProperty, GetCellSeparatorBrush());
            borderFactory.SetValue(System.Windows.Controls.Border.BorderThicknessProperty, new System.Windows.Thickness(0, 0, 1, 0));
            borderFactory.SetValue(System.Windows.Controls.Border.PaddingProperty, padding ?? new System.Windows.Thickness(2, 1, 2, 1));
            borderFactory.SetValue(System.Windows.Controls.Border.SnapsToDevicePixelsProperty, true);
            borderFactory.AppendChild(innerFactory);
            return new DataTemplate { VisualTree = borderFactory };
        }

        private static DataTemplate CreateTextCellTemplate(
            System.Windows.Data.BindingBase textBinding,
            System.Windows.Data.BindingBase? foregroundBinding = null,
            System.Windows.TextAlignment textAlignment = System.Windows.TextAlignment.Left,
            bool centerHorizontally = false,
            string? toolTip = null)
        {
            var textFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
            textFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty, textBinding);
            if (foregroundBinding != null)
            {
                textFactory.SetBinding(System.Windows.Controls.TextBlock.ForegroundProperty, foregroundBinding);
            }

            textFactory.SetValue(System.Windows.FrameworkElement.MarginProperty, new System.Windows.Thickness(0));
            textFactory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, System.Windows.TextTrimming.None);
            textFactory.SetValue(System.Windows.Controls.TextBlock.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            textFactory.SetValue(System.Windows.Controls.TextBlock.TextAlignmentProperty, textAlignment);
            textFactory.SetValue(System.Windows.Controls.TextBlock.TextWrappingProperty, System.Windows.TextWrapping.NoWrap);
            textFactory.SetValue(System.Windows.FrameworkElement.HorizontalAlignmentProperty, centerHorizontally ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Stretch);

            if (!string.IsNullOrWhiteSpace(toolTip))
            {
                textFactory.SetValue(System.Windows.FrameworkElement.ToolTipProperty, toolTip);
            }

            return CreateBorderedElementTemplate(textFactory);
        }

        private static DataTemplate CreateCheckBoxCellTemplate(System.Windows.Data.BindingBase isCheckedBinding)
        {
            var checkFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.CheckBox));
            checkFactory.SetBinding(System.Windows.Controls.CheckBox.IsCheckedProperty, isCheckedBinding);
            checkFactory.SetValue(System.Windows.FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            checkFactory.SetValue(System.Windows.FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);

            return CreateBorderedElementTemplate(checkFactory, new System.Windows.Thickness(2, 0, 2, 0));
        }

        private static DataTemplate CreateButtonCellTemplate(string content, string toolTip, System.Windows.Data.BindingBase commandBinding, System.Windows.Data.BindingBase parameterBinding)
        {
            var buttonFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.Button));
            buttonFactory.SetValue(System.Windows.Controls.Button.ContentProperty, content);
            buttonFactory.SetValue(System.Windows.Controls.Button.WidthProperty, 25.0);
            buttonFactory.SetValue(System.Windows.Controls.Button.HeightProperty, 25.0);
            buttonFactory.SetValue(System.Windows.Controls.Button.ToolTipProperty, toolTip);
            buttonFactory.SetValue(System.Windows.FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
            buttonFactory.SetValue(System.Windows.FrameworkElement.VerticalAlignmentProperty, System.Windows.VerticalAlignment.Center);
            buttonFactory.SetValue(System.Windows.Controls.Button.FontSizeProperty, 14.0);
            buttonFactory.SetValue(System.Windows.Controls.Button.FontWeightProperty, System.Windows.FontWeights.Bold);
            buttonFactory.SetBinding(System.Windows.Controls.Button.CommandProperty, commandBinding);
            buttonFactory.SetBinding(System.Windows.Controls.Button.CommandParameterProperty, parameterBinding);

            var buttonForeground = System.Windows.Application.Current.TryFindResource("ThemeAccentBlue") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.DarkBlue;
            buttonFactory.SetValue(System.Windows.Controls.Button.ForegroundProperty, buttonForeground);
            buttonFactory.SetValue(System.Windows.Controls.Button.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
            buttonFactory.SetValue(System.Windows.Controls.Button.BorderBrushProperty, System.Windows.Media.Brushes.Transparent);
            buttonFactory.SetValue(System.Windows.Controls.Button.BorderThicknessProperty, new System.Windows.Thickness(0));
            buttonFactory.SetValue(System.Windows.Controls.Button.PaddingProperty, new System.Windows.Thickness(2));

            return CreateBorderedElementTemplate(buttonFactory, new System.Windows.Thickness(2, 0, 2, 0));
        }
        
        /// <summary>
        /// Updates column header visual indicators based on current sort state
        /// </summary>
        public void UpdateSortIndicators(string? sortColumn, System.ComponentModel.ListSortDirection? sortDirection)
        {
            if (!(_fileListView.View is GridView gridView)) return;
            
            foreach (var column in gridView.Columns)
            {
                if (column.Header is GridViewColumnHeader header && header.Tag is string columnName)
                {
                    if (columnName == sortColumn && sortDirection.HasValue)
                    {
                        var arrow = sortDirection.Value == System.ComponentModel.ListSortDirection.Ascending ? "▲" : "▼";
                        header.Content = $"{columnName} {arrow}";
                    }
                    else
                    {
                        header.Content = columnName;
                    }
                }
            }
        }
        
        /// <summary>
        /// Handles column header clicks to trigger sorting
        /// </summary>
        private void ColumnHeader_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is GridViewColumnHeader header && header.Tag is string columnName)
            {
                // Get the ViewModel from the ListView's DataContext
                if (_fileListView.DataContext is MainWindowViewModel viewModel)
                {
                    viewModel.SortByColumn(columnName);
                    // Update visual indicators to show which column is sorted and in which direction
                    UpdateSortIndicators(viewModel.CurrentSortColumn, viewModel.CurrentSortDirection);
                }
            }
        }

        public void UpdateListViewColumns()
        {
            // Prevent recursive or excessive column updates (not needed if only called from VM, but keep for safety)
            // Clear all columns and rebuild from scratch
            if (!(_fileListView.View is GridView gridView)) return;
            gridView.Columns.Clear();

            bool hasArwFiles = HasArwFilesInCurrentList();
            bool isArwMode = hasArwFiles;

            // Calculate optimal column widths based on content
            var columnWidths = CalculateOptimalColumnWidths();


            // Checkbox column (IsSelected)
            // Create a sortable header for the checkbox column
            var checkboxHeader = CreateSortableHeader("Mark");
            
            var checkboxColumn = new GridViewColumn
            {
                Header = checkboxHeader,
                Width = 30,
                CellTemplate = CreateCheckBoxCellTemplate(new System.Windows.Data.Binding("IsSelected") { Mode = System.Windows.Data.BindingMode.TwoWay })
            };
            gridView.Columns.Add(checkboxColumn);

            // File column
            // When ShowFullFilename is off, keep the File column visible for ARW files so their
            // full file names are always shown.
            if (_appConfig.ShowFullFilename || hasArwFiles)
            {
                var fileColumn = new GridViewColumn
                {
                    Header = CreateSortableHeader("File"),
                    Width = GetFileColumnWidth(), // Always calculate fresh (narrow responsive width)
                    CellTemplate = CreateTextCellTemplate(new System.Windows.Data.Binding("Path")
                    {
                        Converter = new FileNameDisplayConverter(),
                        ConverterParameter = _appConfig.ShowFullFilename
                    })
                };
                gridView.Columns.Add(fileColumn);
            }

            if (_appConfig.ShowFilenameDateColumn && !isArwMode)
            {
                var dateColumn = new GridViewColumn
                {
                    Header = CreateSortableHeader("Date"),
                    Width = CalculateDataDrivenColumnWidth("Date"),
                    CellTemplate = CreateTextCellTemplate(new System.Windows.Data.Binding("FilenameDate"))
                };
                gridView.Columns.Add(dateColumn);
            }

            if (_appConfig.ShowFilenameTimeColumn && !isArwMode)
            {
                var timeColumn = new GridViewColumn
                {
                    Header = CreateSortableHeader("Time"),
                    Width = CalculateDataDrivenColumnWidth("Time"),
                    CellTemplate = CreateTextCellTemplate(new System.Windows.Data.Binding("FilenameTime"))
                };
                gridView.Columns.Add(timeColumn);
            }

            if (_appConfig.ShowFilenameFrameColumn && !isArwMode)
            {
                var frameHeader = CreateSortableHeader("Frame");
                frameHeader.Content = "Seq";

                var frameColumn = new GridViewColumn
                {
                    Header = frameHeader,
                    Width = CalculateDataDrivenColumnWidth("Frame"),
                    CellTemplate = CreateTextCellTemplate(new System.Windows.Data.Binding("FilenameFrame"))
                };
                gridView.Columns.Add(frameColumn);
            }

            // Metadata button column with info icon (no gray box)
            // Bind the command to the ViewModel's ShowFileMetadataCommand
            // We need to find the MainWindowViewModel from the DataContext
            var commandBinding = new System.Windows.Data.Binding("DataContext.ShowFileMetadataCommand");
            commandBinding.RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.FindAncestor, typeof(System.Windows.Controls.ListView), 1);
            // Pass the current FileItem as the command parameter
            var metadataColumn = new GridViewColumn
            {
                Header = "Info",
                Width = 35,
                CellTemplate = CreateButtonCellTemplate("ⓘ", "Show file metadata and headers", commandBinding, new System.Windows.Data.Binding("."))
            };
            gridView.Columns.Add(metadataColumn);

            // Size column (purple, with converter) - always use fresh width calculation (no saved widths)
            if (_appConfig.ShowSizeColumn)
            {
                var sizeFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
                var sizeBinding = new System.Windows.Data.Binding("Size");
                sizeBinding.Converter = new ApexAstro.Wpf.Converters.FileSizeConverter();
                sizeFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty, sizeBinding);
                
                // Bind to purple theme resource
                var purpleForegroundBinding = new System.Windows.Data.Binding();
                purpleForegroundBinding.Source = System.Windows.Application.Current;
                purpleForegroundBinding.Path = new System.Windows.PropertyPath("Resources[ThemeAccentPurple]");
                sizeFactory.SetBinding(System.Windows.Controls.TextBlock.ForegroundProperty, purpleForegroundBinding);
                sizeFactory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, System.Windows.TextTrimming.CharacterEllipsis);
                
                var sizeTemplate = new DataTemplate { VisualTree = sizeFactory };
                var sizeColumn = new GridViewColumn
                {
                    Header = CreateSortableHeader("Size"),
                    Width = 80, // Wider to prevent cutoff
                    CellTemplate = CreateTextCellTemplate(sizeBinding)
                };
                gridView.Columns.Add(sizeColumn);
            }

            // Median column (purple, with converter)
            if (_appConfig.ShowMedianColumn)
            {
                var medianFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
                var medianBinding = new System.Windows.Data.Binding("Median");
                medianBinding.Converter = new ApexAstro.Wpf.Converters.MedianConverter();
                medianBinding.ConverterParameter = _appConfig; // Pass AppConfig to converter
                medianFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty, medianBinding);
                
                // Bind to purple theme resource
                var purpleForegroundBinding = new System.Windows.Data.Binding();
                purpleForegroundBinding.Source = System.Windows.Application.Current;
                purpleForegroundBinding.Path = new System.Windows.PropertyPath("Resources[ThemeAccentPurple]");
                medianFactory.SetBinding(System.Windows.Controls.TextBlock.ForegroundProperty, purpleForegroundBinding);
                medianFactory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, System.Windows.TextTrimming.CharacterEllipsis);
                
                var medianColumn = new GridViewColumn
                {
                    Header = CreateSortableHeader("Median"),
                    Width = 70,
                    CellTemplate = CreateTextCellTemplate(medianBinding)
                };
                gridView.Columns.Add(medianColumn);

                // Mean column (purple, same converter as median)
                var meanFactory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
                var meanBinding = new System.Windows.Data.Binding("Mean");
                meanBinding.Converter = new ApexAstro.Wpf.Converters.MedianConverter();
                meanBinding.ConverterParameter = _appConfig; // Use same display mode as median
                meanFactory.SetBinding(System.Windows.Controls.TextBlock.TextProperty, meanBinding);
                
                var meanPurpleBinding = new System.Windows.Data.Binding();
                meanPurpleBinding.Source = System.Windows.Application.Current;
                meanPurpleBinding.Path = new System.Windows.PropertyPath("Resources[ThemeAccentPurple]");
                meanFactory.SetBinding(System.Windows.Controls.TextBlock.ForegroundProperty, meanPurpleBinding);
                meanFactory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, System.Windows.TextTrimming.CharacterEllipsis);
                
                var meanColumn = new GridViewColumn
                {
                    Header = CreateSortableHeader("Mean"),
                    Width = 70,
                    CellTemplate = CreateTextCellTemplate(meanBinding)
                };
                gridView.Columns.Add(meanColumn);
            }

            // Custom keyword columns (green) - each has individual width based on keyword length
            foreach (var keyword in _appConfig.CustomKeywords)
            {
                if (isArwMode)
                    continue;

                if (!ShouldIncludeMetadataColumn(keyword, MetadataColumnType.Custom))
                    continue;

                var factory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
                
                // Use SafeDictionaryConverter with parameter to avoid KeyNotFoundException
                var binding = new System.Windows.Data.Binding("CustomKeywords");
                binding.Converter = new SafeDictionaryConverter();
                binding.ConverterParameter = keyword;
                
                factory.SetBinding(System.Windows.Controls.TextBlock.TextProperty, binding);
                // Bind to the row-level auto-select state so custom keywords are red when the file would be selected
                // and green when it would be kept.
                var foregroundBinding = new System.Windows.Data.Binding(".");
                foregroundBinding.Converter = new ApexAstro.Wpf.Converters.AutoSelectRowForegroundConverter();
                foregroundBinding.ConverterParameter = new ApexAstro.Wpf.Converters.AutoSelectKeywordForegroundContext
                {
                    Keyword = keyword,
                    Criteria = _appConfig.AutoSelectCriteria
                };
                factory.SetBinding(System.Windows.Controls.TextBlock.ForegroundProperty, foregroundBinding);
                factory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, System.Windows.TextTrimming.CharacterEllipsis);
                // Calculate width based on actual data content - different for each keyword
                double calculatedWidth = CalculateDataDrivenColumnWidth(keyword, true);
                
                var column = new GridViewColumn
                {
                    Header = CreateSortableHeader(keyword, GetDisplayName(keyword)),
                    Width = calculatedWidth,
                    CellTemplate = CreateTextCellTemplate(binding, foregroundBinding)
                };
                gridView.Columns.Add(column);
            }

            // FITS keyword columns (blue) - each has individual width based on keyword length
            foreach (var keyword in _appConfig.FitsKeywords)
            {
                if (isArwMode)
                    continue;

                if (!ShouldIncludeMetadataColumn(keyword, MetadataColumnType.Fits))
                    continue;

                var factory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));
                
                // Use SafeDictionaryConverter with parameter to avoid KeyNotFoundException
                var binding = new System.Windows.Data.Binding("FitsKeywords");
                binding.Converter = new SafeDictionaryConverter();
                binding.ConverterParameter = keyword;
                
                factory.SetBinding(System.Windows.Controls.TextBlock.TextProperty, binding);
                // Bind to theme resource so foreground color updates when theme changes
                var foregroundBinding = new System.Windows.Data.Binding();
                foregroundBinding.Source = System.Windows.Application.Current;
                foregroundBinding.Path = new System.Windows.PropertyPath("Resources[ThemeAccentBlue]");
                factory.SetBinding(System.Windows.Controls.TextBlock.ForegroundProperty, foregroundBinding);
                factory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, System.Windows.TextTrimming.CharacterEllipsis);
                // Calculate width based on actual data content - different for each keyword
                double calculatedWidth = CalculateDataDrivenColumnWidth(keyword, false);
                
                var column = new GridViewColumn
                {
                    Header = CreateSortableHeader(keyword, GetDisplayName(keyword)),
                    Width = calculatedWidth,
                    CellTemplate = CreateTextCellTemplate(binding, foregroundBinding)
                };
                gridView.Columns.Add(column);
            }

            // CSV metadata columns (orange) - data from ImageMetaData.csv in the current folder
            foreach (var keyword in _appConfig.CsvKeywords)
            {
                if (isArwMode)
                    continue;

                if (!ShouldIncludeMetadataColumn(keyword, MetadataColumnType.Csv))
                    continue;

                var factory = new FrameworkElementFactory(typeof(System.Windows.Controls.TextBlock));

                var binding = new System.Windows.Data.Binding("CsvKeywords");
                binding.Converter = new SafeDictionaryConverter();
                binding.ConverterParameter = keyword;

                factory.SetBinding(System.Windows.Controls.TextBlock.TextProperty, binding);
                var foregroundBinding = new System.Windows.Data.Binding();
                foregroundBinding.Source = System.Windows.Application.Current;
                foregroundBinding.Path = new System.Windows.PropertyPath("Resources[ThemeAccentOrange]");
                factory.SetBinding(System.Windows.Controls.TextBlock.ForegroundProperty, foregroundBinding);
                factory.SetValue(System.Windows.Controls.TextBlock.TextTrimmingProperty, System.Windows.TextTrimming.CharacterEllipsis);
                double calculatedWidth = CalculateDataDrivenColumnWidth(keyword, false);

                var column = new GridViewColumn
                {
                    Header = CreateSortableHeader(keyword, GetDisplayName(keyword)),
                    Width = calculatedWidth,
                    CellTemplate = CreateTextCellTemplate(binding, foregroundBinding)
                };
                gridView.Columns.Add(column);
            }

            bool showArwColumns = _appConfig.ArwKeywords.Count > 0 && isArwMode;
            if (showArwColumns)
            {
                foreach (var keyword in _appConfig.ArwKeywords)
                {
                    if (!ShouldIncludeMetadataColumn(keyword, MetadataColumnType.Arw))
                        continue;

                    var binding = new System.Windows.Data.Binding("ArwKeywords");
                    binding.Converter = new SafeDictionaryConverter();
                    binding.ConverterParameter = keyword;

                    var foregroundBinding = new System.Windows.Data.Binding();
                    foregroundBinding.Source = System.Windows.Application.Current;
                    foregroundBinding.Path = new System.Windows.PropertyPath("Resources[ThemeAccentPurple]");

                    var column = new GridViewColumn
                    {
                        Header = CreateSortableHeader(keyword, GetDisplayName(keyword)),
                        Width = CalculateDataDrivenColumnWidth(keyword, false),
                        CellTemplate = CreateTextCellTemplate(binding, foregroundBinding)
                    };
                    gridView.Columns.Add(column);
                }
            }

            // Skip automatic auto-resize to keep columns narrow
            // Auto-resize will only happen when explicitly called via AutoResizeColumns() method

            _defaultColumnOrder = gridView.Columns
                .Select(GetColumnHeaderText)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .ToList();

            ApplySavedColumnOrder(gridView);
            
            // Force all columns to use their calculated optimal widths (ignore any cached widths)
            ResetAllColumnWidths();

            // Recalculate splitter and fill columns after layout settles.
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                AdjustSplitterPosition();
                ExpandFileColumnToFillAvailableWidth();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        /// <summary>
        /// Updates the File column width to be responsive to the current window size (25% of window width)
        /// </summary>
        public void UpdateFileColumnWidth()
        {
            if (_fileListView?.View is GridView gridView)
            {
                var fileColumn = gridView.Columns.FirstOrDefault(c => 
                    c.Header is GridViewColumnHeader header && 
                    header.Content?.ToString() == "File");
                    
                if (fileColumn != null)
                {
                    fileColumn.Width = GetFileColumnWidth();
                    ExpandFileColumnToFillAvailableWidth();
                }
            }
        }

        /// <summary>
        /// Forces all columns to reset to their calculated optimal widths.
        /// This ensures no cached/saved widths are used - everything is recalculated fresh.
        /// Each column gets individually calculated width to avoid uniform sizing.
        /// </summary>
        public void ResetAllColumnWidths()
        {
            if (_fileListView?.View is GridView gridView)
            {
                foreach (var column in gridView.Columns)
                {
                    if (column.Header is GridViewColumnHeader header)
                    {
                        var headerText = header.Content?.ToString() ?? "";
                        double newWidth;
                        
                        switch (headerText.ToLowerInvariant())
                        {
                            case "file":
                                newWidth = GetFileColumnWidth();
                                break;
                            case "date":
                                newWidth = CalculateDataDrivenColumnWidth("Date");
                                break;
                            case "time":
                                newWidth = CalculateDataDrivenColumnWidth("Time");
                                break;
                            case "Frame":
                                newWidth = CalculateDataDrivenColumnWidth("Frame");
                                break;
                            case "size":
                                newWidth = 85; // Fixed width for size column
                                break;
                            case "":
                                // Button/checkbox columns - keep existing width (usually 30-35px)
                                newWidth = column.Width < 30 ? 30 : (column.Width > 40 ? 35 : column.Width);
                                break;
                            default:
                                // Keyword columns - use data-driven calculation based on actual content
                                // Check if it's a custom keyword first
                                bool isCustom = _appConfig.CustomKeywords.Contains(headerText);
                                newWidth = CalculateDataDrivenColumnWidth(headerText, isCustom);
                                break;
                        }
                        
                        column.Width = newWidth;
                    }
                }
            }
        }

        /// <summary>
        /// Calculates column width based on actual data content in the current file list.
        /// Analyzes all values in the column to determine optimal width.
        /// </summary>
        /// <param name="columnName">The column name/header</param>
        /// <param name="isCustomKeyword">True if this is a custom keyword column</param>
        /// <returns>Optimal width based on actual content</returns>
        private double CalculateDataDrivenColumnWidth(string columnName, bool isCustomKeyword = false)
        {
            if (string.IsNullOrEmpty(columnName))
                return 60;

            // Get the file items from the ViewModel
            var fileItems = GetFileItemsFromDataContext();
            if (fileItems == null || !fileItems.Any())
            {
                // No data - fall back to header-based calculation
                return Math.Max(60, columnName.Length * 7 + 20);
            }

            // Calculate header width (minimum width needed)
            double headerWidth = MeasureTextWidth(columnName) + 20; // Add padding for sorting arrows

            // Calculate maximum content width
            double maxContentWidth = 0;
            
            foreach (var item in fileItems)
            {
                string? content = null;
                
                // Get content based on column type
                switch (columnName.ToLowerInvariant())
                {
                    case "file":
                        content = GetDisplayFileName(item);
                        break;
                    case "date":
                        content = item.FilenameDate;
                        break;
                    case "time":
                        content = item.FilenameTime;
                        break;
                    case "Frame":
                        content = item.FilenameFrame;
                        break;
                    case "size":
                        content = FormatFileSize(item.Size);
                        break;
                    default:
                        // Keyword column - check custom, FITS, and CSV/session metadata values.
                        if (isCustomKeyword && item.CustomKeywords.ContainsKey(columnName))
                        {
                            content = item.CustomKeywords[columnName];
                        }
                        else if (!isCustomKeyword && item.FitsKeywords.ContainsKey(columnName))
                        {
                            content = item.FitsKeywords[columnName];
                        }
                        else if (!isCustomKeyword && item.CsvKeywords.ContainsKey(columnName))
                        {
                            content = item.CsvKeywords[columnName];
                        }
                        else if (!isCustomKeyword && item.ArwKeywords.ContainsKey(columnName))
                        {
                            content = item.ArwKeywords[columnName];
                        }
                        break;
                }
                
                if (!string.IsNullOrEmpty(content))
                {
                    // Add more generous padding for keyword columns to prevent cutoff
                    double padding = columnName.Length > 6 ? 20 : 15; // More padding for longer column names
                    double contentWidth = MeasureTextWidth(content) + padding;
                    maxContentWidth = Math.Max(maxContentWidth, contentWidth);
                }
            }
            
            // Use the larger of header width or content width, with appropriate limits
            double optimalWidth = Math.Max(headerWidth, maxContentWidth);
            
            // Special handling for File column - should be 25% of window width
            if (columnName.ToLowerInvariant() == "file")
            {
                return GetFileColumnWidth(); // Use responsive width calculation
            }

            if (columnName.Equals("Date", StringComparison.OrdinalIgnoreCase))
            {
                return 95;
            }

            if (columnName.Equals("Time", StringComparison.OrdinalIgnoreCase))
            {
                return 85;
            }

            if (columnName.Equals("Frame", StringComparison.OrdinalIgnoreCase))
            {
                return 80;
            }
            
            // Apply reasonable min/max limits for other columns.
            // Keyword-specific minimums prevent decimal values from being ellipsized.
            double keywordMinWidth = GetKeywordMinimumWidth(columnName);
            optimalWidth = Math.Max(optimalWidth, Math.Max(40, keywordMinWidth));
            // Allow columns to grow enough to avoid truncation.
            optimalWidth = Math.Min(optimalWidth, 800); // Maximum 800px to fit long metadata values
            
            return Math.Round(optimalWidth);
        }

        /// <summary>
        /// Gets the file items from the ListView's DataContext (MainWindowViewModel).
        /// </summary>
        /// <returns>Collection of FileItem objects or null if not available</returns>
        private IEnumerable<Models.FileItem>? GetFileItemsFromDataContext()
        {
            if (_fileListView?.DataContext is MainWindowViewModel viewModel)
            {
                // Access the Files property directly (it's an ObservableCollection<FileItem>)
                return viewModel.Files;
            }
            return null;
        }

        /// <summary>
        /// Measures the width of text using WPF's text measurement.
        /// </summary>
        /// <param name="text">Text to measure</param>
        /// <returns>Width in pixels</returns>
        private double MeasureTextWidth(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            var formattedText = new System.Windows.Media.FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface("Segoe UI"), // Default font
                13, // Font size - match ListView font size
                System.Windows.Media.Brushes.Black,
                new System.Windows.Media.NumberSubstitution(),
                1); // PixelsPerDip

            return formattedText.Width;
        }

        /// <summary>
        /// Formats file size for display (used in width calculation).
        /// </summary>
        /// <param name="sizeInBytes">File size in bytes</param>
        /// <returns>Formatted size string</returns>
        private string FormatFileSize(long sizeInBytes)
        {
            const int scale = 1024;
            string[] orders = { "GB", "MB", "KB", "Bytes" };
            long max = (long)Math.Pow(scale, orders.Length - 1);

            foreach (string order in orders)
            {
                if (sizeInBytes > max)
                    return string.Format("{0:##.##} {1}", decimal.Divide(sizeInBytes, max), order);

                max /= scale;
            }
            return "0 Bytes";
        }

        /// <summary>
        /// Calculates optimal column widths based on content and headers.
        /// Returns a dictionary with column identifiers as keys and optimal widths as values.
        /// NOTE: These widths are calculated fresh each time - no saved widths are used.
        /// </summary>
        private Dictionary<string, double> CalculateOptimalColumnWidths()
        {
            var widths = new Dictionary<string, double>();
            
            // Always use fresh calculations - never load saved column widths
            // This ensures columns are always sized based on current data and window size
            bool hasArwFiles = HasArwFilesInCurrentList();
            if (_appConfig.ShowFullFilename || hasArwFiles)
            {
                widths["File"] = GetFileColumnWidth();  // Responsive filename column (35% of window width)
            }
            if (!hasArwFiles)
            {
                widths["Date"] = 95;
                widths["Time"] = 85;
                widths["Frame"] = 60;

                // Custom/FITS/Session metadata are only shown in FITS/XISF mode.
                foreach (var keyword in _appConfig.CustomKeywords)
                {
                    widths[$"Custom_{keyword}"] = CalculateDataDrivenColumnWidth(keyword, true);
                }

                foreach (var keyword in _appConfig.CsvKeywords)
                {
                    widths[$"CSV_{keyword}"] = CalculateDataDrivenColumnWidth(keyword, false);
                }

                foreach (var keyword in _appConfig.FitsKeywords)
                {
                    widths[$"FITS_{keyword}"] = CalculateDataDrivenColumnWidth(keyword, false);
                }
            }
            widths["Size"] = 85;       // Fixed width for size column

            if (_appConfig.ArwKeywords.Count > 0 && hasArwFiles)
            {
                foreach (var keyword in _appConfig.ArwKeywords)
                {
                    widths[$"ARW_{keyword}"] = CalculateDataDrivenColumnWidth(keyword, false);
                }
            }
            
            return widths;
        }

        private bool HasArwFilesInCurrentList()
        {
            var fileItems = GetFileItemsFromDataContext();
            return fileItems != null && fileItems.Any(item =>
                item.Path.EndsWith(".arw", StringComparison.OrdinalIgnoreCase));
        }

        private bool ShouldIncludeMetadataColumn(string keyword, MetadataColumnType metadataColumnType)
        {
            if (!_appConfig.HideUnusedMetadataColumns)
                return true;

            var fileItems = GetFileItemsFromDataContext();
            if (fileItems == null || !fileItems.Any())
                return true;

            foreach (var item in fileItems)
            {
                string? value = metadataColumnType switch
                {
                    MetadataColumnType.Custom => item.CustomKeywords.TryGetValue(keyword, out var custom) ? custom : null,
                    MetadataColumnType.Fits => item.FitsKeywords.TryGetValue(keyword, out var fits) ? fits : null,
                    MetadataColumnType.Csv => item.CsvKeywords.TryGetValue(keyword, out var csv) ? csv : null,
                    MetadataColumnType.Arw => item.ArwKeywords.TryGetValue(keyword, out var arw) ? arw : null,
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(value))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Calculates the total width needed for all columns in the file list.
        /// Includes padding for ListView margins, borders, and internal spacing.
        /// </summary>
        /// <returns>Total width in pixels needed for all columns plus necessary padding</returns>
        public double CalculateTotalColumnsWidth()
        {
            double totalWidth = 0;
            int columnCount = 0;
            
            if (_fileListView?.View is GridView gridView)
            {
                foreach (var column in gridView.Columns)
                {
                    totalWidth += column.Width;
                    columnCount++;
                }
                
                // Account for ListView margins, borders, and internal spacing
                // ListView margins (4px left + 4px right) + borders + ScrollViewer padding + header spacing
                double padding = 43; // 8px margins + ~25px for borders, scrollviewer, and internal spacing
                totalWidth += padding;
            }
            
            return totalWidth;
        }

        /// <summary>
        /// Recalculates all column widths based on the actual data content in the current file list.
        /// This should be called whenever the file list changes to ensure optimal column sizing.
        /// </summary>
        public void RecalculateColumnWidthsFromData()
        {
            if (_fileListView?.View is GridView gridView)
            {
                var fileItems = GetFileItemsFromDataContext();
                if (fileItems != null && fileItems.Any())
                {
                    foreach (var column in gridView.Columns)
                    {
                        if (column.Header is GridViewColumnHeader header)
                        {
                            var headerText = header.Content?.ToString() ?? "";
                            double newWidth;
                            
                            switch (headerText.ToLowerInvariant())
                            {
                                case "file":
                                    newWidth = CalculateDataDrivenColumnWidth(headerText, false);
                                    break;
                                case "size":
                                    newWidth = CalculateDataDrivenColumnWidth(headerText, false);
                                    break;
                                case "":
                                    // Button/checkbox columns - keep existing width
                                    continue;
                                default:
                                    // Keyword columns - determine if custom or FITS keyword
                                    bool isCustom = _appConfig.CustomKeywords.Contains(headerText);
                                    newWidth = CalculateDataDrivenColumnWidth(headerText, isCustom);
                                    break;
                            }
                            
                            column.Width = newWidth;

                        }
                    }
                    
                    // After calculating column widths, adjust the splitter position
                    AdjustSplitterPosition();
                }
            }
        }

        /// <summary>
        /// Public method to adjust splitter position for optimal file list width.
        /// </summary>
        public void AdjustSplitterForOptimalWidth()
        {
            AdjustSplitterPosition();
        }

        /// <summary>
        /// Adjusts the GridSplitter position so the file list is just wide enough to show all columns
        /// without needing a horizontal scrollbar, but never more than 70% of the window width.
        /// </summary>
        private void AdjustSplitterPosition()
        {
            // Try to find the main window and its GridSplitter
            var mainWindow = System.Windows.Application.Current.MainWindow;
            if (mainWindow != null)
            {
                // Find the main Grid with the 3 columns (file list, splitter, image viewer)
                // Look for the Grid in row 1 (the main content area, not the menu)
                var mainGrid = FindMainContentGrid(mainWindow);
                if (mainGrid != null && mainGrid.ColumnDefinitions.Count >= 3)
                {
                    // If the image column is collapsed (floating mode), keep a single-pane layout.
                    // This prevents metadata/dialog actions from restoring split ratios.
                    var imageColumn = mainGrid.ColumnDefinitions[2];
                    var splitterColumn = mainGrid.ColumnDefinitions[1];
                    bool imageCollapsed = imageColumn.MaxWidth == 0 ||
                        (imageColumn.Width.GridUnitType == GridUnitType.Pixel && imageColumn.Width.Value <= 0.0);

                    if (imageCollapsed)
                    {
                        mainGrid.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
                        splitterColumn.Width = new GridLength(0, GridUnitType.Pixel);
                        imageColumn.Width = new GridLength(0, GridUnitType.Pixel);
                        ExpandFileColumnToFillAvailableWidth();
                        return;
                    }


                    double totalColumnsWidth = CalculateTotalColumnsWidth();

                    double windowWidth = mainWindow.ActualWidth;
                    if (windowWidth <= 0)
                        return;
                    
                    // Calculate what proportion of the window the file list should take
                    // Small buffer for rounding/layout issues (reduced since padding is now more accurate)
                    double bufferedColumnsWidth = totalColumnsWidth + 3;
                    double idealRatio = bufferedColumnsWidth / windowWidth;
                    
                    // Apply constraints: minimum 30%, maximum based on image column minimum width.
                    // This allows wider file-list panes when many columns are shown, while preserving
                    // at least the image pane's minimum width.
                    double imageMinWidth = mainGrid.ColumnDefinitions[2].MinWidth;
                    double splitterWidth = mainGrid.ColumnDefinitions[1].ActualWidth > 0
                        ? mainGrid.ColumnDefinitions[1].ActualWidth
                        : 5;
                    double maxFileListRatio = Math.Max(0.3, (windowWidth - imageMinWidth - splitterWidth) / windowWidth);
                    double fileListRatio = Math.Max(0.3, Math.Min(maxFileListRatio, idealRatio));
                    double imageViewerRatio = 1.0 - fileListRatio;
                    
                    // Set the column definitions
                    mainGrid.ColumnDefinitions[0].Width = new GridLength(fileListRatio, GridUnitType.Star);
                    mainGrid.ColumnDefinitions[2].Width = new GridLength(imageViewerRatio, GridUnitType.Star);
                    
                    // Force layout update to get actual sizes
                    mainWindow.UpdateLayout();
                    
                    // Get actual file list pane width after layout
                    double actualFileListWidth = 0;
                    if (_fileListView?.ActualWidth > 0)
                    {
                        actualFileListWidth = _fileListView.ActualWidth;
                    }
                    
                    // Calculate the difference between what we need and what we got
                    double widthDifference = totalColumnsWidth - actualFileListWidth;
                    
                    // Log detailed debugging information
                    bool willHaveScrollbar = idealRatio > 0.7;

                    // If there is spare width in the file pane (for example after adding metadata columns),
                    // grow the File column so the visible columns fill the pane.
                    ExpandFileColumnToFillAvailableWidth();

                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("Could not find main content grid for splitter adjustment");
                }
            }
        }

        /// <summary>
        /// Finds the main content grid (the one with 3 columns: file list, splitter, image viewer).
        /// This is more specific than the generic FindChild method.
        /// </summary>
        private Grid? FindMainContentGrid(DependencyObject parent)
        {
            // Look for a Grid with exactly 3 column definitions
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is Grid grid && grid.ColumnDefinitions.Count == 3)
                {
                    // Check if this looks like our main content grid by seeing if it has a GridSplitter
                    bool hasGridSplitter = false;
                    for (int j = 0; j < VisualTreeHelper.GetChildrenCount(grid); j++)
                    {
                        var gridChild = VisualTreeHelper.GetChild(grid, j);
                        if (gridChild is GridSplitter)
                        {
                            hasGridSplitter = true;
                            break;
                        }
                    }
                    
                    if (hasGridSplitter)
                    {

                        
                        // Hook up splitter event if we haven't already
                        SetupSplitterMonitoring(grid);
                        
                        return grid;
                    }
                }

                var result = FindMainContentGrid(child);
                if (result != null) return result;
            }
            return null;
        }

        /// <summary>
        /// Sets up monitoring for manual splitter movements to debug actual pane sizes.
        /// </summary>
        private void SetupSplitterMonitoring(Grid mainGrid)
        {
            // Find the GridSplitter if we haven't already hooked it up
            if (_gridSplitter == null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(mainGrid); i++)
                {
                    var child = VisualTreeHelper.GetChild(mainGrid, i);
                    if (child is GridSplitter splitter)
                    {
                        _gridSplitter = splitter;
                        
                        // Hook up to DragCompleted event to detect manual moves
                        _gridSplitter.DragCompleted += GridSplitter_DragCompleted;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Called when the user manually moves the splitter.
        /// </summary>
        private void GridSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            // Force layout update after manual splitter adjustment
            System.Windows.Application.Current.MainWindow?.UpdateLayout();
        }

        private void OnColumnDragCompleted(object sender, DragCompletedEventArgs e)
        {
            // Defer until WPF finishes any pending column reorder operation.
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                SaveCurrentColumnOrder();
                _appConfig.Save();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        /// <summary>
        /// Helper method to find a child control of a specific type in the visual tree.
        /// </summary>
        private T? FindChild<T>(DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T result) return result;

                var childResult = FindChild<T>(child);
                if (childResult != null) return childResult;
            }
            return null;
        }

        /// <summary>
        /// Applies our compact column widths (replaces auto-resize to prevent wide columns).
        /// Instead of auto-sizing to content, this uses our optimized compact widths.
        /// </summary>
        public void AutoResizeColumns()
        {
            // Use data-driven calculation for optimal widths
            RecalculateColumnWidthsFromData();
            
            // Schedule multiple attempts to ensure splitter adjustment happens after layout
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                AdjustSplitterPosition();
                ExpandFileColumnToFillAvailableWidth();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            
            // Also try with lower priority as backup
            System.Windows.Application.Current.Dispatcher.BeginInvoke(new System.Action(() =>
            {
                AdjustSplitterPosition();
                ExpandFileColumnToFillAvailableWidth();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        public void SaveCurrentColumnOrder()
        {
            if (!(_fileListView.View is GridView gridView))
            {
                return;
            }

            var currentOrder = gridView.Columns
                .Select(GetColumnHeaderText)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .ToList();

            if (ColumnOrderMatchesDefaults(currentOrder))
            {
                _appConfig.HasCustomColumnOrder = false;
                _appConfig.FileListColumnOrder.Clear();
                return;
            }

            _appConfig.HasCustomColumnOrder = true;
            _appConfig.FileListColumnOrder = currentOrder;
        }

        private void ApplySavedColumnOrder(GridView gridView)
        {
            if (!_appConfig.HasCustomColumnOrder || _appConfig.FileListColumnOrder == null || _appConfig.FileListColumnOrder.Count == 0)
            {
                return;
            }

            var columnsToOrder = gridView.Columns.Cast<GridViewColumn>().ToList();
            var insertionIndex = 0;

            foreach (var header in _appConfig.FileListColumnOrder)
            {
                var match = columnsToOrder.FirstOrDefault(c =>
                    string.Equals(GetColumnHeaderText(c), header, StringComparison.OrdinalIgnoreCase));

                if (match == null)
                {
                    continue;
                }

                gridView.Columns.Remove(match);
                gridView.Columns.Insert(insertionIndex, match);
                insertionIndex++;
                columnsToOrder.Remove(match);
            }
        }

        private bool ColumnOrderMatchesDefaults(List<string> currentOrder)
        {
            if (_defaultColumnOrder.Count != currentOrder.Count)
            {
                return false;
            }

            for (int i = 0; i < _defaultColumnOrder.Count; i++)
            {
                if (!string.Equals(_defaultColumnOrder[i], currentOrder[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            return true;
        }

        private static string GetColumnHeaderText(GridViewColumn column)
        {
            if (column.Header is GridViewColumnHeader gridHeader)
            {
                // Use Tag for columns that have it (internal keyword name), fall back to Content for display
                // This ensures column order tracking uses the actual keyword, not shortened display names
                if (gridHeader.Tag != null && !string.IsNullOrWhiteSpace(gridHeader.Tag.ToString()))
                {
                    return gridHeader.Tag.ToString() ?? string.Empty;
                }
                return gridHeader.Content?.ToString() ?? string.Empty;
            }

            return column.Header?.ToString() ?? string.Empty;
        }

        /// <summary>
        /// Expands the File column to consume any spare width in the visible ListView pane.
        /// This keeps the list visually full-width when additional metadata columns are added.
        /// </summary>
        private void ExpandFileColumnToFillAvailableWidth()
        {
            if (!(_fileListView?.View is GridView gridView))
                return;

            if (_fileListView.ActualWidth <= 0)
                return;

            var fileColumn = gridView.Columns.FirstOrDefault(c =>
                c.Header is GridViewColumnHeader header &&
                header.Content?.ToString() == "File");

            if (fileColumn == null)
                return;

            var currentTotalWidth = CalculateTotalColumnsWidth();
            var availableWidth = _fileListView.ActualWidth;
            var targetWidth = GetFileColumnWidth();

            if (fileColumn.Width > targetWidth)
            {
                fileColumn.Width = targetWidth;
                return;
            }

            var extraWidth = availableWidth - currentTotalWidth;

            // Only grow toward the compact target width; do not expand beyond it.
            if (extraWidth > 1 && fileColumn.Width < targetWidth)
            {
                fileColumn.Width = Math.Min(targetWidth, fileColumn.Width + extraWidth);
            }
        }

        /// <summary>
        /// Automatically resizes all columns to fit their content.
        /// This method measures the actual content and adjusts column widths accordingly.
        /// </summary>
        private void AutoResizeAllColumns()
        {
            if (!(_fileListView.View is GridView gridView)) return;
            
            try
            {
                // Measure and resize each column
                foreach (var column in gridView.Columns)
                {
                    if (column != null)
                    {
                        // Store original width
                        var originalWidth = column.Width;
                        
                        // Set to NaN to auto-size based on content
                        column.Width = double.NaN;
                        
                        // Force layout update
                        _fileListView.UpdateLayout();
                        
                        // Get the auto-calculated width
                        var autoWidth = double.IsNaN(column.ActualWidth) ? originalWidth : column.ActualWidth;
                        
                        // Apply minimum width constraints and minimal padding
                        var minWidth = GetMinimumColumnWidth(column);
                        var maxWidth = GetMaximumColumnWidth(column);  // Prevent columns from getting too wide
                        var finalWidth = Math.Min(maxWidth, Math.Max(minWidth, autoWidth + 2)); // Only 2px padding and cap maximum
                        
                        // Set the final calculated width
                        column.Width = finalWidth;
                    }
                }
            }
            catch (Exception ex)
            {
                // If auto-resizing fails, log the error but don't crash the application
                System.Diagnostics.Debug.WriteLine($"Error auto-resizing columns: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the responsive width for the File column (narrower responsive width)
        /// </summary>
        private double GetFileColumnWidth()
        {
            double targetWidth = 200; // Default fallback for a compact file column

            // Try to get the main window width first
            if (System.Windows.Application.Current.MainWindow?.ActualWidth > 0)
            {
                targetWidth = System.Windows.Application.Current.MainWindow.ActualWidth * 0.18;
            }
            // Then try ListView width as secondary option
            else if (_fileListView?.ActualWidth > 0)
            {
                targetWidth = _fileListView.ActualWidth * 0.18;
            }
            

            
            // Ensure reasonable bounds: keep the name column compact but readable.
            return Math.Max(110, Math.Min(260, targetWidth));
        }

        /// <summary>
        /// Gets the minimum width for a specific column based on its type.
        /// </summary>
        private double GetMinimumColumnWidth(GridViewColumn column)
        {
            // Checkbox and button columns should be narrow
            if (string.IsNullOrEmpty(column.Header?.ToString()))
            {
                // Determine by width: checkbox=30, metadata button=35
                return column.Width < 32 ? 30 : 35;
            }
            
            var header = column.Header?.ToString() ?? "";
            
            // Different minimum widths based on column type
            switch (header.ToLowerInvariant())
            {
                case "file":
                    return GetFileColumnWidth(); // Responsive to window width (35%)
                case "size":
                    return 50;  // Just enough for "Size" header + values like "1.2 MB"
                default:
                    return Math.Max(40, header.Length * 4 + 5); // Very tight for keyword columns
            }
        }
        
        /// <summary>
        /// Gets the maximum width for a specific column to prevent them from getting too wide
        /// </summary>
        private double GetMaximumColumnWidth(GridViewColumn column)
        {
            // Checkbox and button columns should stay narrow
            if (string.IsNullOrEmpty(column.Header?.ToString()))
            {
                return column.Width < 32 ? 30 : 35;
            }
            
            var header = column.Header?.ToString() ?? "";
            
            // Set maximum widths to keep columns very compact
            switch (header.ToLowerInvariant())
            {
                case "file":
                    return GetFileColumnWidth() + 20; // File column can be a bit wider than minimum
                case "size":
                    return 60;  // Size should stay narrow
                default:
                    return Math.Min(800, Math.Max(45, header.Length * 6)); // Allow keyword columns to expand fully
            }
        }

        private string GetDisplayFileName(Models.FileItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Path) &&
                System.IO.Path.GetExtension(item.Path).Equals(".arw", StringComparison.OrdinalIgnoreCase))
            {
                return item.Name;
            }

            return _appConfig.ShowFullFilename ? item.Name : item.NameWithoutExtension;
        }
    }
}
