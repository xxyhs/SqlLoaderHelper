using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.VisualStudio.LanguageServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SqlLoaderHelper
{
    public class SqlReferenceAnalyzer: IDisposable
    {
        private readonly SemaphoreSlim _calcLock = new SemaphoreSlim(1, 1);
        private DateTime LastUpdateTime = DateTime.MinValue;
        private DateTime LastWorkspaceChangeTime = DateTime.Now;
        public static SqlReferenceAnalyzer Instance { get; } = new SqlReferenceAnalyzer();

        public List<Action> RefreshCallbacks = new List<Action>();

        private readonly Dictionary<string, List<ReferenceLocation>> _sqlRefs =
            new Dictionary<string, List<ReferenceLocation>>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, List<ReferenceLocation>> References => _sqlRefs;

        public int GetCount(string sqlCode)
        {
            if (LastUpdateTime == DateTime.MinValue)
                return -1; // 尚未计算完成
            return _sqlRefs.TryGetValue(sqlCode, out var list) ? list?.Count ?? 0 : 0;
        }

        public void SubScribleLoaded(Action callback)
        {
            RefreshCallbacks.Add(callback);
        }

        public void DesubScribleLoaded(Action callback)
        {
            RefreshCallbacks.Remove(callback);
        }

        public void NotifyWorkspaceChanged(object sender, WorkspaceChangeEventArgs e)
        {
            LastWorkspaceChangeTime = DateTime.Now;
        }

        public async Task RecalculateAsync(VisualStudioWorkspace workspace)
        {
            if (!RefreshCallbacks.Any())
            {
                return;
            }
            if (LastWorkspaceChangeTime < LastUpdateTime)
            {
                // 防止新注册的Margin停在Loading不使用缓存数据
                RefreshCallbacks.ForEach(cb => cb());
                return;
            }
            if (_calcLock.CurrentCount == 0)
                return;
            await _calcLock.WaitAsync();
            try
            {
                _sqlRefs.Clear();
                var solution = workspace.CurrentSolution;
                var uniqueLocSet = new HashSet<(string File, int Start, int End)>();
                foreach (var project in solution.Projects)
                {
                    var loadMethod = await GetSqLoaderLoadMethodAsync(project);
                    if (loadMethod == null) continue;

                    var refs = await SymbolFinder.FindReferencesAsync(loadMethod, solution);
                    foreach (var reference in refs)
                    {
                        foreach (var loc in reference.Locations)
                        {
                            var doc = loc.Document;
                            var root = await doc.GetSyntaxRootAsync();
                            if (root == null) continue;

                            // 找到调用表达式
                            var node = root.FindNode(loc.Location.SourceSpan);
                            var invocation = node.AncestorsAndSelf()
                                .OfType<InvocationExpressionSyntax>()
                                .FirstOrDefault();

                            if (invocation == null)
                                continue;

                            // 找参数（包括变量/常量）
                            var sqlCode = await ResolveSqlArgumentAsync(invocation, doc);
                            if (sqlCode == null)
                                continue;

                            if (!_sqlRefs.TryGetValue(sqlCode, out var list))
                                list = _sqlRefs[sqlCode] = new List<ReferenceLocation>();

                            if (uniqueLocSet.Add((File: loc.Location.SourceTree?.FilePath, 
                                Start: loc.Location.SourceSpan.Start,
                                End: loc.Location.SourceSpan.End)))
                            {
                                list.Add(loc);
                            }
                        }
                    }
                    LastUpdateTime = DateTime.Now;
                }
                RefreshCallbacks.ForEach(cb => cb());
            }
            finally
            {
                _calcLock.Release();
            }
        }

        private async Task<IMethodSymbol> GetSqLoaderLoadMethodAsync(Project project)
        {
            var compilation = await project.GetCompilationAsync();
            if (compilation == null) return null;

            var sqLoader = compilation.GetTypeByMetadataName(SlnConfig.Instance.SqlLoaderMetaPrefix + ".SqlLoader");
            if (sqLoader == null) return null;

            return sqLoader
                .GetMembers("Load")
                .OfType<IMethodSymbol>()
                .Where(m => m.Parameters.Length == 1 &&
                            m.Parameters[0].Type.SpecialType == SpecialType.System_String)
                .FirstOrDefault();
        }

        // 支持字符串变量、常量、字段引用
        private async Task<string> ResolveSqlArgumentAsync(
            InvocationExpressionSyntax invocation,
            Document doc)
        {
            var semantic = await doc.GetSemanticModelAsync();
            if (semantic == null) return null;

            var arg = invocation.ArgumentList.Arguments.FirstOrDefault()?.Expression;
            if (arg == null) return null;

            // 直接字符串字面量
            if (arg is LiteralExpressionSyntax lit && lit.Token.Value is string s)
                return s;

            // 变量/常量
            var symbol = semantic.GetSymbolInfo(arg).Symbol;
            if (symbol == null) return null;

            // 支持 const / static readonly / local var 初始化
            var decl = await symbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntaxAsync();
            if (decl is VariableDeclaratorSyntax varDecl &&
                varDecl.Initializer?.Value is LiteralExpressionSyntax lit2 &&
                lit2.Token.Value is string s2)
            {
                return s2;
            }

            return null;
        }

        public void Dispose()
        {
            LastWorkspaceChangeTime = DateTime.Now;
            LastUpdateTime = DateTime.MinValue;
            _sqlRefs.Clear();
            RefreshCallbacks.Clear();
        }
    }
}

