using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace SQLLoadIntelliSense
{
    [Export(typeof(IMouseProcessorProvider))]
    [ContentType("csharp")]
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class SQLCtrlClickHandlerProvider : IMouseProcessorProvider
    {
        public IMouseProcessor GetAssociatedProcessor(IWpfTextView wpfTextView)
        {
            return new SQLCtrlClickHandler(wpfTextView);
        }
    }

    internal class SQLCtrlClickHandler: MouseProcessorBase
    {
        private readonly IWpfTextView _view;

        public SQLCtrlClickHandler(IWpfTextView view)
        {
            _view = view;
        }

        private async System.Threading.Tasks.Task OpenFileAsync(string path)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await ServiceProvider.GetGlobalServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            dte.ItemOperations.OpenFile(path);
        }

        static string Pattern = @"SqlLoader\.Load\(\s*""([^""]+)""\s*\)";
        public override void PreprocessMouseUp(MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left &&
               Keyboard.Modifiers == ModifierKeys.Control)
            {
                var position = e.GetPosition(null);
                var textViewLine = _view.TextViewLines.GetTextViewLineContainingYCoordinate(position.Y);
                if (textViewLine == null) return;
                var line = textViewLine.Extent.GetText();
                Match match = Regex.Match(line, Pattern);
                if (match.Success)
                {
                    var bufferPosition = textViewLine.GetBufferPositionFromXCoordinate(position.X);
                    string sqlName = match.Groups[1].Value;
                    var sqlStartIndex = match.Groups[1].Index + textViewLine.Start;
                    var sqlEndIndex = sqlStartIndex + match.Groups[1].Length;
                    if (bufferPosition >= sqlStartIndex && bufferPosition <= sqlEndIndex)
                    {
                        var filePath = SQLFileWatcher.GetCorrespondingPathByCode(sqlName);
                        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                        {
                            ThreadHelper.JoinableTaskFactory.Run(async delegate
                            {
                                await OpenFileAsync(filePath);
                            });
                            e.Handled = true;
                        }
                    }
                }
            }
        }
    }
}
