using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace SqlLoaderHelper
{
    internal class SqlReferenceMargin : DockPanel, IWpfTextViewMargin
    {
        public const string MarginName = "SqlReferenceMargin";
        private readonly IWpfTextView _view;
        private readonly TextBlock _text;
        private readonly string _filePath;
        private Popup _popup;

        public static int RefreshCount = 0;
        public SqlReferenceMargin(IWpfTextView view, string filePath)
        {
            VSColorTheme.ThemeChanged += OnThemeChanged;
            _view = view;
            _filePath = filePath;
            Height = 24;
            VerticalAlignment = VerticalAlignment.Top;
            _text = new TextBlock
            {
                Margin = new Thickness(6, 4, 0, 0),
                Padding = new Thickness(108, 0, 0, 0),
                Text = "Loading SQL references...",
                FontSize = 12
            };
            // Initial theme binding
            _text.SetResourceReference(
                TextBlock.BackgroundProperty,
                EnvironmentColors.ToolboxBackgroundBrushKey);

            _text.SetResourceReference(
                TextBlock.ForegroundProperty,
                EnvironmentColors.ToolTipHintTextBrushKey);

            // Hover效果
            _text.MouseEnter += (s, e) =>
            {
                _text.SetResourceReference(
                    TextBlock.BackgroundProperty,
                    EnvironmentColors.AccentMediumBrushKey);
            };
            _text.MouseLeave += (s, e) =>
            {
                _text.SetResourceReference(
                    TextBlock.BackgroundProperty,
                    EnvironmentColors.ToolWindowBackgroundBrushKey);
            };

            _text.MouseLeftButtonUp += OnClick;

            Children.Add(_text);

            // 订阅 Reference 数据加载完成事件
            SqlReferenceAnalyzer.Instance.SubScribleLoaded(Refresh);

            _view.Closed += (s, e) =>
            {
                Dispose();
            };
        }

        private void Refresh()
        {
            if (_view.IsClosed || _text == null) return;
            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var code = SqlFileCache.GetCodeByFilePath(_filePath);

                int count = SqlReferenceAnalyzer.Instance.GetCount(code);

                if (count >= 0)
                {
                    _text.Text = $"{code}: {count} references";
                    if (count == 0)
                    {
                        _popup = null;
                    }
                    else
                    {
                        _popup = CreatePopup();
                    }
                }
            });
        }

        private void OnThemeChanged(ThemeChangedEventArgs e)
        {
            if (_text == null) return;
            // Re-apply VS theme resources
            _text.SetResourceReference(
                TextBlock.BackgroundProperty,
                EnvironmentColors.ToolWindowBackgroundBrushKey);

            _text.SetResourceReference(
                TextBlock.ForegroundProperty,
                EnvironmentColors.ToolTipHintTextBrushKey);
        }

        private void OnClick(object sender, MouseButtonEventArgs e)
        {
            if (_popup == null)
            {
                return;
            }
            _popup.PlacementTarget = _text;
            _popup.IsOpen = true;
            e.Handled = true;
        }

        private Popup CreatePopup()
        {
            var popup = new Popup
            {
                StaysOpen = false,
                AllowsTransparency = true,
                Placement = PlacementMode.Bottom,
                PopupAnimation = PopupAnimation.Fade,
                Name = "SqlReferencePopup",
                Focusable = true
            };
            // popup 内容：一个 ListBox
            var border = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(4)
            };

            border.SetResourceReference(
                Border.BorderBrushProperty,
                VsBrushes.ToolWindowBorderKey);
            border.SetResourceReference(
                Border.BackgroundProperty,
                VsBrushes.ToolWindowBackgroundKey);

            if (SqlReferenceAnalyzer.Instance.References.TryGetValue(
                SqlFileCache.GetCodeByFilePath(_filePath),
                out var references))
            {

                var listView = CreateReferenceListView(references);
                listView.SelectionChanged += OnListSelection;
                border.Child = listView;
            }
            
            popup.Child = border;

            return popup;
        }

        private ListView CreateReferenceListView(IEnumerable<ReferenceLocation> references)
        {
            var listView = new ListView
            {
                Name = "SqlReferenceListView",
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                MaxHeight = 300,
                MaxWidth = 960
            };

            ScrollViewer.SetVerticalScrollBarVisibility(listView, ScrollBarVisibility.Auto);
            ScrollViewer.SetCanContentScroll(listView, true);

            var factory = new FrameworkElementFactory(typeof(StackPanel));
            factory.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
            factory.SetValue(StackPanel.MarginProperty, new Thickness(4));

            var fileText = new FrameworkElementFactory(typeof(TextBlock));
            fileText.SetBinding(TextBlock.TextProperty, new Binding("IndexName"));
            fileText.SetValue(TextBlock.FontWeightProperty, FontWeights.Bold);
            fileText.SetValue(TextBlock.FontSizeProperty, 12.0);
            fileText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.WindowTextKey);
            factory.AppendChild(fileText);

            var previewText = new FrameworkElementFactory(typeof(TextBlock));
            previewText.SetBinding(TextBlock.TextProperty, new Binding("LinePreview"));
            previewText.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Consolas"));
            previewText.SetValue(TextBlock.FontSizeProperty, 12.0);
            previewText.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.WordEllipsis);
            previewText.SetResourceReference(TextBlock.ForegroundProperty, VsBrushes.GrayTextKey);
            factory.AppendChild(previewText);
            listView.ItemTemplate = new DataTemplate { VisualTree = factory };

            foreach (var r in references.Select(ToViewModel))
                listView.Items.Add(r);

            return listView;
        }

        private void OnListSelection(object sender, SelectionChangedEventArgs e)
        {
            ListView list = (ListView)sender;
            if (!(list.SelectedItem is ReferenceItemViewModel))
            {
                return;
            }
            ReferenceItemViewModel location = (ReferenceItemViewModel)list.SelectedItem;
            _popup.IsOpen = false;
            _ = NavigateToSpanAsync(location);
            list.SelectedItem = null;
        }

        private async Task NavigateToSpanAsync(ReferenceItemViewModel loc)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var filePath = loc.FullPath;

            int line = loc.Line;
            int column = loc.Column + 5; // add the `Load(` length 5

            var serviceProvider = AsyncPackage.GetGlobalService(typeof(SDTE)) as IServiceProvider;

            var openDoc = await ServiceProvider.GetGlobalServiceAsync(typeof(IVsUIShellOpenDocument))
                as IVsUIShellOpenDocument;

            Guid logicalView = VSConstants.LOGVIEWID_Code;

            IVsUIHierarchy hierarchy;
            uint itemId;
            Microsoft.VisualStudio.OLE.Interop.IServiceProvider sp;
            IVsWindowFrame frame;

            int hr = openDoc.OpenDocumentViaProject(
                filePath,
                ref logicalView,
                out sp,
                out hierarchy,
                out itemId,
                out frame);

            if (frame != null)
                frame.Show();

            object docData;
            hr = frame.GetProperty((int)__VSFPROPID.VSFPROPID_DocData, out docData);
            ErrorHandler.ThrowOnFailure(hr);

            // 3) Try to get IVsTextLines from docData
            IVsTextLines textLines = null;

            // docData might be the text buffer itself (IVsTextLines) or a wrapper that implements IVsTextLines
            textLines = docData as IVsTextLines;

            // Sometimes docData is a VsTextBufferClass COM object so try a cast
            if (textLines == null && docData != null)
            {
                var comObj = System.Runtime.InteropServices.Marshal.GetIUnknownForObject(docData);
                try
                {
                    // Query for IVsTextLines
                    var textLinesGuid = typeof(IVsTextLines).GUID;
                    IntPtr ppv;
                    var hr2 = System.Runtime.InteropServices.Marshal.QueryInterface(comObj, ref textLinesGuid, out ppv);
                    if (hr2 == 0 && ppv != IntPtr.Zero)
                    {
                        textLines = (IVsTextLines)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(ppv);
                        System.Runtime.InteropServices.Marshal.Release(ppv);
                    }
                }
                finally
                {
                    System.Runtime.InteropServices.Marshal.Release(comObj);
                }
            }

            // If still null, try to get text buffer via IVsTextBufferProvider (fallback)
            if (textLines == null && docData is IVsTextBufferProvider bufProvider)
            {
                IVsTextLines tl;
                hr = bufProvider.GetTextBuffer(out tl);
                if (hr == VSConstants.S_OK)
                    textLines = tl;
            }

            if (textLines == null)
            {
                // Last fallback: use DTE to open and set caret (EnvDTE)
                // (showing here as a fallback; prefer IVsTextLines path)
                var dte = (EnvDTE.DTE)ServiceProvider.GlobalProvider.GetService(typeof(EnvDTE.DTE));
                if (dte == null) return;
                dte.ItemOperations.OpenFile(filePath);
                var sel = (EnvDTE.TextSelection)dte.ActiveDocument.Selection;
                sel.MoveToLineAndOffset(line + 1, column + 1); // EnvDTE is 1-based
                return;
            }

            // 4) Call IVsTextManager.NavigateToLineAndColumn
            var textManager = (IVsTextManager)ServiceProvider.GlobalProvider.GetService(typeof(SVsTextManager));
            if (textManager == null)
                throw new InvalidOperationException("IVsTextManager not available.");

            // NOTE: NavigateToLineAndColumn expects start and end rows/cols.
            // Use 0-based line/column values (Roslyn gives 0-based).
            int hrNav = textManager.NavigateToLineAndColumn(
                textLines,
                ref logicalView,
                line,
                column,
                line,
                column);

            ErrorHandler.ThrowOnFailure(hrNav);
        }

        public FrameworkElement VisualElement => this;
        public bool Enabled => true;

        public double MarginSize => this.ActualHeight;

        public ITextViewMargin GetTextViewMargin(string marginName) => marginName == MarginName ? this : null;

        public void Dispose()
        {
            VSColorTheme.ThemeChanged -= OnThemeChanged;
            SqlReferenceAnalyzer.Instance.DesubScribleLoaded(Refresh);
        }

        private class ReferenceItemViewModel
        {
            public string FilePath { get; set; }
            public string LinePreview { get; set; }
            public string Context { get; set; }
            public int Line { get; set; }
            public int Column { get; set; }
            public string FullPath { get; set; }

            public string IndexName
            {
                get
                {
                    return $"{FilePath};(Line: {Line + 1}, Column: {Column + 1})";
                }
            }
        }

        private ReferenceItemViewModel ToViewModel(ReferenceLocation r)
        {
            var lineSpan = r.Location.GetLineSpan();

            return new ReferenceItemViewModel
            {
                FilePath = System.IO.Path.GetFileName(lineSpan.Path),
                FullPath = lineSpan.Path,
                Line = lineSpan.StartLinePosition.Line,
                Column = lineSpan.StartLinePosition.Character,
                LinePreview = r.Location.SourceTree.GetText()
                    .Lines[lineSpan.StartLinePosition.Line]
                    .ToString().Trim(),
                Context = r.Document?.Name
            };
        }
    }
}
