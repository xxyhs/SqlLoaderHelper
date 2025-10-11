using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;
using System.Composition;
using System.Linq;
using System.Threading.Tasks;

namespace SqlLoaderHelper
{
    [ExportCompletionProvider(nameof(SQLLoadCompletionProvider), LanguageNames.CSharp)]
    [Shared]
    public class SQLLoadCompletionProvider : CompletionProvider
    {
        public override async Task ProvideCompletionsAsync(CompletionContext context)
        {
            var document = context.Document;
            var position = context.Position;
            var cancellationToken = context.CancellationToken;

            var text = await document.GetTextAsync(cancellationToken);
            var line = text.Lines.GetLineFromPosition(position);
            var lineText = line.ToString();
            var caretIndexInLine = position - line.Start;

            // 查找触发前缀
            string prefix = GetPrefix(lineText, caretIndexInLine);
            if (prefix == null)
                return;

            // 过滤缓存的 SQL 文件
            var matches = SQLFileWatcher.SQLDict
                .Where(f => f.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase));

            foreach (var match in matches)
            {
                context.AddItem(CompletionItem.Create(match));
            }
        }

        private string GetPrefix(string lineText, int caretIndex)
        {
            // 找到 SqlLoader.Load(" 前缀
            var trigger = "SqlLoader.Load(\"";
            int triggerPos = lineText.LastIndexOf(trigger, caretIndex - 1);
            if (triggerPos == -1)
                return null;

            int start = triggerPos + trigger.Length;
            if (caretIndex <= start)
                return "";

            // 光标前输入的文本作为前缀
            return lineText.Substring(start, caretIndex - start);
        }

        public override bool ShouldTriggerCompletion(SourceText text, int position, CompletionTrigger trigger, OptionSet options)
        {
            // 可以根据需要进一步控制触发条件
            return trigger.Kind == CompletionTriggerKind.Insertion;
        }
    }
}
