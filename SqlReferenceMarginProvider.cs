using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SqlLoaderHelper
{
    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name(SqlReferenceMargin.MarginName)]
    [Order(After = PredefinedMarginNames.Top)]
    [MarginContainer(PredefinedMarginNames.Top)]
    [ContentType("code")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal class SqlReferenceMarginProvider : IWpfTextViewMarginProvider
    {
        [Import]
        internal VisualStudioWorkspace VSWorkspace { get; set; }

        public IWpfTextViewMargin CreateMargin(IWpfTextViewHost textViewHost, IWpfTextViewMargin containerMargin)
        {
            if (VSWorkspace == null) return null;
            if (string.IsNullOrEmpty(SlnConfig.Instance.SQLRoot)) return null;
            if (string.IsNullOrEmpty(SlnConfig.Instance.SqlLoaderMetaPrefix)) return null;
            var textView = textViewHost.TextView;
            if (textView == null)
                return null;
            if (textView != null)
            {
                if (textView.TextBuffer?.Properties?.TryGetProperty(
                    typeof(ITextDocument), out ITextDocument doc) == true)
                {
                    if (doc.FilePath.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) && doc.FilePath.StartsWith(SlnConfig.Instance.SQLRoot))
                    {
                        textView.GotAggregateFocus += OnTextViewFocused;
                        return new SqlReferenceMargin(textView, doc.FilePath);
                    }
                }
            }

            return null;
        }

        private void OnTextViewFocused(object sender, EventArgs e)
        {
            _ = Task.Run(async () => await SqlReferenceAnalyzer.Instance.RecalculateAsync(VSWorkspace));
        }
    }
}
