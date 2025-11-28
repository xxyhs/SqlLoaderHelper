using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Elfie.Diagnostics;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SqlLoaderHelper
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(SqlLoaderHelper.PackageGuidString)]
    public sealed class SqlLoaderHelper : AsyncPackage
    {
        /// <summary>
        /// SQLLoaderHelper GUID string.
        /// </summary>
        public const string PackageGuidString = "0d6bdf2e-9201-4526-bbfb-8ec6266397ed";

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            // Set SQLRoot in .sln file will enable code's auto-complete and support redirect from code file to.sql file;
            // Set SqlLoaderMetaPrefix in .sln file will enable the reference statistic of .sql file and support redirect from .sql file to code file;
            var solutionService = await GetServiceAsync(typeof(SVsSolution)) as IVsSolution;
            if (solutionService != null)
            {
                solutionService.GetSolutionInfo(out string solutionDirectory, out string solutionFile, out string userOptsFile);
                if (string.IsNullOrEmpty(solutionFile)) return;
                var solutionLines = File.ReadAllLines(solutionFile);
                var sqlLoaderHelperConfig = solutionLines.FirstOrDefault(t => t.Contains("SQLRoot"));
                if (String.IsNullOrEmpty(sqlLoaderHelperConfig)) return;
                var roorDir = sqlLoaderHelperConfig.Replace("SQLRoot", "").Replace("=", "").Trim();
                var rootFullPath = Path.GetFullPath(Path.Combine(solutionDirectory, roorDir));
                if (!string.IsNullOrEmpty(roorDir) && Directory.Exists(rootFullPath))
                {
                    SlnConfig.Instance.SQLRoot = rootFullPath;
                    var sQLFileWatcher = new SqlFileCache(solutionService);
                    sQLFileWatcher.RebuildSolutionIndex();
                    sQLFileWatcher.InitWatch();

                    var sqlLoaderMetaPrefixConfig = solutionLines.FirstOrDefault(t => t.Contains("SqlLoaderMetaPrefix"));
                    if (String.IsNullOrEmpty(sqlLoaderMetaPrefixConfig)) return;
                    var sqlLoaderMetaPrefix = sqlLoaderMetaPrefixConfig.Replace("SqlLoaderMetaPrefix", "").Replace("=", "").Trim();
                    if (String.IsNullOrEmpty(sqlLoaderMetaPrefix)) return;
                    SlnConfig.Instance.SqlLoaderMetaPrefix = sqlLoaderMetaPrefix;
                    var componentModel = (IComponentModel)await GetServiceAsync(typeof(SComponentModel));
                    if (componentModel != null)
                    {
                        var workspace = componentModel.GetService<VisualStudioWorkspace>();
                        if (workspace != null)
                        {
                            workspace.WorkspaceChanged += (o, e) =>
                            {
                                if (e.Kind == WorkspaceChangeKind.DocumentChanged ||
                                    e.Kind == WorkspaceChangeKind.DocumentAdded ||
                                    e.Kind == WorkspaceChangeKind.DocumentRemoved)
                                {
                                    if (e.DocumentId == null) return;
                                    var document = workspace.CurrentSolution.GetDocument(e.DocumentId);
                                    var filePath = document.FilePath;
                                    if (!string.IsNullOrEmpty(filePath) && filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                                    {
                                        SqlReferenceAnalyzer.Instance.NotifyWorkspaceChanged(o, e);
                                    }
                                }
                            };
                        }
                    }
                }
            }
        }
        #endregion
    }
}
