using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;

namespace SQLLoadIntelliSense
{
    [Export(typeof(IVsTextViewCreationListener))]
    [ContentType("csharp")]  // 指定目标文件类型
    [TextViewRole(PredefinedTextViewRoles.Editable)]
    internal class SQLGoToDefinitionTextViewCreationListener : IVsTextViewCreationListener
    {
        [Import]
        internal IVsEditorAdaptersFactoryService AdapterService = null;
        public void VsTextViewCreated(IVsTextView textViewAdapter)
        {
            IWpfTextView view = AdapterService.GetWpfTextView(textViewAdapter);
            if (view == null)
                return;

            var handler = new SQLCtrlClickEventHandler(view);
            // 这里注册 Ctrl+Click 事件
            view.VisualElement.MouseLeftButtonUp += (sender, e) =>
            {
                handler.OnMouseLeftButtonUp(sender, e);
            };
            // 这里注册命令过滤器
            var filter = new SQLGotoDefinitionCommandFilter(view);
            filter.AttachToView(textViewAdapter);
        }
    }

    internal class SQLCtrlClickEventHandler
    {
        private readonly IWpfTextView _view;

        public SQLCtrlClickEventHandler(IWpfTextView view)
        {
            _view = view;
        }

        private async System.Threading.Tasks.Task OpenFileAsync(string path)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await ServiceProvider.GetGlobalServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            dte.ItemOperations.OpenFile(path);
        }

        private Point RelativeToView(Point position)
        {
            return new Point(position.X + this._view.ViewportLeft, position.Y + this._view.ViewportTop);
        }

        static string Pattern = @"SqlLoader\.Load\(\s*""([^""]+)""\s*\)";
        public void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left &&
               Keyboard.Modifiers == ModifierKeys.Control)
            {
                var position = RelativeToView(e.GetPosition(_view as IInputElement));
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
    internal class SQLGotoDefinitionCommandFilter : IOleCommandTarget
    {
        private readonly IWpfTextView _view;
        private IOleCommandTarget _nextCommandTarget;

        public SQLGotoDefinitionCommandFilter(IWpfTextView view)
        {
            _view = view;
        }

        public void AttachToView(IVsTextView textViewAdapter)
        {
            textViewAdapter.AddCommandFilter(this, out _nextCommandTarget);
        }
        public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            return _nextCommandTarget.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        static string Pattern = @"SqlLoader\.Load\(\s*""([^""]+)""\s*\)";
        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (pguidCmdGroup == VSConstants.GUID_VSStandardCommandSet97 && nCmdID == (uint)VSConstants.VSStd97CmdID.GotoDefn)
            {
                var caret = _view.Caret.Position.BufferPosition;
                var line = caret.GetContainingLine().GetText();
                var lineStart = caret.GetContainingLine().Start;
                Match match = Regex.Match(line, Pattern);
                if (match.Success)
                {
                    string sqlName = match.Groups[1].Value;
                    var sqlStartIndex = match.Groups[1].Index + lineStart ;
                    var sqlEndIndex = sqlStartIndex + match.Groups[1].Length;
                    if (caret.Position >= sqlStartIndex && caret.Position <= sqlEndIndex)
                    {
                        var filePath = SQLFileWatcher.GetCorrespondingPathByCode(sqlName);
                        if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                        {
                            ThreadHelper.JoinableTaskFactory.Run(async delegate
                            {
                                await OpenFileAsync(filePath);
                            });
                            return VSConstants.S_OK;
                        }
                    }
                }
            }
            // 默认执行
            return _nextCommandTarget.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
        }

        private async System.Threading.Tasks.Task OpenFileAsync(string path)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await ServiceProvider.GetGlobalServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            dte.ItemOperations.OpenFile(path);
        }
    }
}
