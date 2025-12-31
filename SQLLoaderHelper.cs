using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Elfie.Diagnostics;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        public const string PackageGuidString = "6f315631-bb1b-449d-8213-a40784dc2bf5";
        private string ConfigFilePath => Path.Combine(SolutionDirectory, ".sqlloadercfg.json");

        private string _solutionDirectory;
        private string SolutionDirectory
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                if (_solutionDirectory == null)
                {
                    var solution = (IVsSolution)GetService(typeof(SVsSolution));
                    string slnFile = null;
                    solution?.GetSolutionInfo(out _, out slnFile, out _);
                    _solutionDirectory = slnFile != null ? Path.GetDirectoryName(slnFile) : null;
                }
                return _solutionDirectory;
            }
        }

        private FileSystemWatcher _watcher;
        private AsyncManualResetEvent _fileChangedEvent = new AsyncManualResetEvent();
        private CancellationTokenSource _watcherCts;

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
            // Create Config File in Solution Root Directory: .sqlloadercfg.json
            // Set SQLRoot in .sqlloadercfg.json will enable code's auto-complete and support redirect from code file to.sql file;
            // Set SqlLoaderMetaPrefix in .sqlloadercfg.json file will enable the reference statistic of .sql file and support redirect from .sql file to code file;
            var solutionService = await GetServiceAsync(typeof(SVsSolution)) as IVsSolution;
            if (solutionService != null)
            {
                if (string.IsNullOrEmpty(SolutionDirectory)) return;
                StartWatchingConfigFile(ConfigFilePath);
                if (!File.Exists(ConfigFilePath)) return;
                await InitSqlLoaderHelperAsync();
            }
        }

        private async Task InitSqlLoaderHelperAsync()
        {
            var jsonConfig = File.ReadAllText(ConfigFilePath);
            string rootDir = null;
            string sqlLoaderMetaPrefix = null;
            try
            {
                JObject config = JObject.Parse(jsonConfig);
                rootDir = config["SQLRoot"]?.ToString();
                sqlLoaderMetaPrefix = config["SqlLoaderMetaPrefix"]?.ToString();
            }
            catch
            {
            }
            var rootFullPath = Path.GetFullPath(Path.Combine(SolutionDirectory, rootDir));
            if (!string.IsNullOrEmpty(rootDir) && Directory.Exists(rootFullPath))
            {
                SlnConfig.Instance.SQLRoot = rootFullPath;
                SqlFileCache.Instance.RebuildSolutionIndex();
                SqlFileCache.Instance.InitWatch();
                if (String.IsNullOrEmpty(sqlLoaderMetaPrefix)) return;
                SlnConfig.Instance.SqlLoaderMetaPrefix = sqlLoaderMetaPrefix;
                var componentModel = (IComponentModel)await GetServiceAsync(typeof(SComponentModel));
                if (componentModel != null)
                {
                    var workspace = componentModel.GetService<VisualStudioWorkspace>();
                    if (workspace != null)
                    {
                        workspace.WorkspaceChanged += Workspace_WorkspaceChanged;
                    }
                }
            }
            else
            {
                await StopSqlLoaderAsync();
            }
        }

        private void Workspace_WorkspaceChanged(object sender, WorkspaceChangeEventArgs e)
        {
            if (e.Kind == WorkspaceChangeKind.DocumentChanged ||
                                e.Kind == WorkspaceChangeKind.DocumentAdded ||
                                e.Kind == WorkspaceChangeKind.DocumentRemoved)
            {
                if (e.DocumentId == null) return;
                var document = ((VisualStudioWorkspace)sender).CurrentSolution.GetDocument(e.DocumentId);
                var filePath = document.FilePath;
                if (!string.IsNullOrEmpty(filePath) && filePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                {
                    SqlReferenceAnalyzer.Instance.NotifyWorkspaceChanged(sender, e);
                }
            }
        }

        private void StartWatchingConfigFile(string configFilePath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var configDir = Path.GetDirectoryName(configFilePath);
            var configFileName = Path.GetFileName(configFilePath);

            if (!Directory.Exists(configDir)) return;

            _watcher = new FileSystemWatcher(configDir, configFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };

            _watcher.Changed += OnConfigFileChanged;
            _watcher.Created += OnConfigFileChanged;
            _watcher.Deleted += OnConfigFileChanged;
            _watcher.Renamed += (s, e) => OnConfigFileChanged(s, e);

            _watcherCts = new CancellationTokenSource();

            // 启动一个防抖处理任务（避免频繁触发）
            _ = HandleFileChangesWithDebounceAsync(_watcherCts.Token);
        }

        private void OnConfigFileChanged(object sender, FileSystemEventArgs e)
        {
            _fileChangedEvent.Set();
        }

        private async Task HandleFileChangesWithDebounceAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await _fileChangedEvent.WaitAsync(cancellationToken);
                _fileChangedEvent.Reset();

                await Task.Delay(200, cancellationToken);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                await HandleConfigFileChangeAsync();
            }
        }

        private async Task HandleConfigFileChangeAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(DisposalToken);
            try
            {
                if (!File.Exists(ConfigFilePath))
                {
                    // 配置文件被删除
                    await OnConfigFileDeletedAsync();
                }
                else
                {
                    await OnConfigFileUpdatedAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error handling config file: {ex}");
            }
        }

        private async Task OnConfigFileDeletedAsync()
        {
            await StopSqlLoaderAsync();
        }

        private async Task OnConfigFileUpdatedAsync()
        {
            // 你的逻辑：配置文件内容已更新
            await InitSqlLoaderHelperAsync();
        }

        private async Task StopSqlLoaderAsync()
        {
            SlnConfig.Instance.SQLRoot = String.Empty;
            SlnConfig.Instance.SqlLoaderMetaPrefix = String.Empty;
            SqlFileCache.Instance.Dispose();
            SqlReferenceAnalyzer.Instance.Dispose();
            var componentModel = (IComponentModel)await GetServiceAsync(typeof(SComponentModel));
            if (componentModel != null)
            {
                var workspace = componentModel.GetService<VisualStudioWorkspace>();
                if (workspace != null)
                {
                    workspace.WorkspaceChanged -= Workspace_WorkspaceChanged;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _watcherCts?.Cancel();
                _watcher?.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
