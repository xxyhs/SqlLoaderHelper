using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SqlLoaderHelper
{
    public class SQLFileWatcher : IVsSolutionEvents, IVsTrackProjectDocumentsEvents2, IDisposable
    {
        private readonly IVsTrackProjectDocuments2 _tracker;
        private uint _trackCookie;
        private readonly IVsSolution _solutionService;
        private uint _slnCooke;

        public static List<string> SQLDict = new List<string>();

        public static string SQLRoot = string.Empty;

        public SQLFileWatcher(IVsTrackProjectDocuments2 tracker, IVsSolution solutionService)
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            _tracker = tracker;
            _tracker.AdviseTrackProjectDocumentsEvents(this, out _trackCookie);
            _solutionService = solutionService;
            _solutionService.AdviseSolutionEvents(this, out _slnCooke);
        }

        private static string[] excludeDir = new string[]
        {
            "bin", "obj", "Debug", "Release"
        };

        /// <summary>
        /// 获取所有文件的最近公共父目录
        /// </summary>
        private static string GetCommonParent(List<string> files)
        {
            if (files == null || files.Count == 0)
                return string.Empty;

            var splitPaths = files
                .Select(f => f.Split(Path.DirectorySeparatorChar))
                .ToList();

            var first = splitPaths[0];
            int commonLength = first.Length;

            for (int i = 1; i < splitPaths.Count; i++)
            {
                commonLength = Math.Min(commonLength, splitPaths[i].Length);
                for (int j = 0; j < commonLength; j++)
                {
                    if (!string.Equals(first[j], splitPaths[i][j], StringComparison.OrdinalIgnoreCase))
                    {
                        commonLength = j;
                        break;
                    }
                }
            }

            return string.Join(Path.DirectorySeparatorChar.ToString(), first.Take(commonLength));
        }
        private void ListSQLFiles()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _solutionService.GetSolutionInfo(out string solutionDir, out string solutionFile, out string userOptsFile);
            DirectoryInfo directoryInfo = new DirectoryInfo(solutionDir);
            FileInfo[] sqlFiles = directoryInfo.GetFiles("*.sql", SearchOption.AllDirectories);
            var allValidSqlFiles = sqlFiles.Where(f => !excludeDir.Any(exd => f.FullName.IndexOf(exd) >= 0));
            var allValidSqlFilePath = allValidSqlFiles.Select(t => t.FullName).ToList();
            SQLRoot = GetCommonParent(allValidSqlFilePath);
            SQLDict.Clear();
            var sqlCodes = allValidSqlFilePath.Select(filePath => filePath.Replace(SQLRoot, "")
                .Replace(".sql", "")
                .Replace(Path.DirectorySeparatorChar, '.')
                .TrimStart(new char[] { '.' }))
                .ToList();
            SQLDict.AddRange(sqlCodes);
        }

        public static string GetCorrespondingPathByCode(string code)
        {
            // Implement your logic to get the corresponding file path based on the code
            // This is a placeholder implementation
            if (string.IsNullOrEmpty(SQLRoot))
            {
                return string.Empty;
            }
            if (!SQLDict.Contains(code))
            {
                return string.Empty;
            }

            return Path.Combine(SQLRoot, code.Replace('.', Path.DirectorySeparatorChar) + ".sql");
        }

        public int OnQueryAddFiles(IVsProject pProject, int cFiles, string[] rgpszMkDocuments, VSQUERYADDFILEFLAGS[] rgFlags, VSQUERYADDFILERESULTS[] pSummaryResult, VSQUERYADDFILERESULTS[] rgResults)
        {
            return VSConstants.S_OK;
        }

        /// <summary>
        /// 新建文件
        /// </summary>
        /// <param name="cProjects"></param>
        /// <param name="cFiles"></param>
        /// <param name="rgpProjects"></param>
        /// <param name="rgFirstIndices"></param>
        /// <param name="rgpszMkDocuments"></param>
        /// <param name="rgFlags"></param>
        /// <returns></returns>
        public int OnAfterAddFilesEx(int cProjects, int cFiles, IVsProject[] rgpProjects, int[] rgFirstIndices, string[] rgpszMkDocuments, VSADDFILEFLAGS[] rgFlags)
        {
            if(rgpszMkDocuments.Any(t => t.EndsWith(".sql"))) {
                ListSQLFiles();
            }
            return VSConstants.S_OK;
        }

        public int OnAfterAddDirectoriesEx(int cProjects, int cDirectories, IVsProject[] rgpProjects, int[] rgFirstIndices, string[] rgpszMkDocuments, VSADDDIRECTORYFLAGS[] rgFlags)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterRemoveFiles(int cProjects, int cFiles, IVsProject[] rgpProjects, int[] rgFirstIndices, string[] rgpszMkDocuments, VSREMOVEFILEFLAGS[] rgFlags)
        {
            if (rgpszMkDocuments.Any(t => t.EndsWith(".sql"))) {
                ListSQLFiles();
            }
            return VSConstants.S_OK;
        }

        /// <summary>
        /// 删除文件夹
        /// </summary>
        /// <param name="cProjects"></param>
        /// <param name="cDirectories"></param>
        /// <param name="rgpProjects"></param>
        /// <param name="rgFirstIndices"></param>
        /// <param name="rgpszMkDocuments"></param>
        /// <param name="rgFlags"></param>
        /// <returns></returns>
        public int OnAfterRemoveDirectories(int cProjects, int cDirectories, IVsProject[] rgpProjects, int[] rgFirstIndices, string[] rgpszMkDocuments, VSREMOVEDIRECTORYFLAGS[] rgFlags)
        {
            ListSQLFiles();
            return VSConstants.S_OK;
        }

        public int OnQueryRenameFiles(IVsProject pProject, int cFiles, string[] rgszMkOldNames, string[] rgszMkNewNames, VSQUERYRENAMEFILEFLAGS[] rgFlags, VSQUERYRENAMEFILERESULTS[] pSummaryResult, VSQUERYRENAMEFILERESULTS[] rgResults)
        {
            return VSConstants.S_OK;
        }

        /// <summary>
        /// 重命名文件
        /// </summary>
        /// <param name="cProjects"></param>
        /// <param name="cFiles"></param>
        /// <param name="rgpProjects"></param>
        /// <param name="rgFirstIndices"></param>
        /// <param name="rgszMkOldNames"></param>
        /// <param name="rgszMkNewNames"></param>
        /// <param name="rgFlags"></param>
        /// <returns></returns>
        public int OnAfterRenameFiles(int cProjects, int cFiles, IVsProject[] rgpProjects, int[] rgFirstIndices, string[] rgszMkOldNames, string[] rgszMkNewNames, VSRENAMEFILEFLAGS[] rgFlags)
        {
            if (rgszMkNewNames.Any(t => t.EndsWith(".sql"))) {
                ListSQLFiles();
            }
            return VSConstants.S_OK;
        }

        public int OnQueryRenameDirectories(IVsProject pProject, int cDirs, string[] rgszMkOldNames, string[] rgszMkNewNames, VSQUERYRENAMEDIRECTORYFLAGS[] rgFlags, VSQUERYRENAMEDIRECTORYRESULTS[] pSummaryResult, VSQUERYRENAMEDIRECTORYRESULTS[] rgResults)
        {
            return VSConstants.S_OK;
        }

        /// <summary>
        /// 重命名文件夹
        /// </summary>
        /// <param name="cProjects"></param>
        /// <param name="cDirs"></param>
        /// <param name="rgpProjects"></param>
        /// <param name="rgFirstIndices"></param>
        /// <param name="rgszMkOldNames"></param>
        /// <param name="rgszMkNewNames"></param>
        /// <param name="rgFlags"></param>
        /// <returns></returns>
        public int OnAfterRenameDirectories(int cProjects, int cDirs, IVsProject[] rgpProjects, int[] rgFirstIndices, string[] rgszMkOldNames, string[] rgszMkNewNames, VSRENAMEDIRECTORYFLAGS[] rgFlags)
        {
            ListSQLFiles();
            return VSConstants.S_OK;
        }

        public int OnQueryAddDirectories(IVsProject pProject, int cDirectories, string[] rgpszMkDocuments, VSQUERYADDDIRECTORYFLAGS[] rgFlags, VSQUERYADDDIRECTORYRESULTS[] pSummaryResult, VSQUERYADDDIRECTORYRESULTS[] rgResults)
        {
            return VSConstants.S_OK;
        }

        public int OnQueryRemoveFiles(IVsProject pProject, int cFiles, string[] rgpszMkDocuments, VSQUERYREMOVEFILEFLAGS[] rgFlags, VSQUERYREMOVEFILERESULTS[] pSummaryResult, VSQUERYREMOVEFILERESULTS[] rgResults)
        {
            return VSConstants.S_OK;
        }

        public int OnQueryRemoveDirectories(IVsProject pProject, int cDirectories, string[] rgpszMkDocuments, VSQUERYREMOVEDIRECTORYFLAGS[] rgFlags, VSQUERYREMOVEDIRECTORYRESULTS[] pSummaryResult, VSQUERYREMOVEDIRECTORYRESULTS[] rgResults)
        {
            ListSQLFiles();
            return VSConstants.S_OK;
        }

        /// <summary>
        /// 源代码控制变化 git 等
        /// </summary>
        /// <param name="cProjects"></param>
        /// <param name="cFiles"></param>
        /// <param name="rgpProjects"></param>
        /// <param name="rgFirstIndices"></param>
        /// <param name="rgpszMkDocuments"></param>
        /// <param name="rgdwSccStatus"></param>
        /// <returns></returns>
        public int OnAfterSccStatusChanged(int cProjects, int cFiles, IVsProject[] rgpProjects, int[] rgFirstIndices, string[] rgpszMkDocuments, uint[] rgdwSccStatus)
        {
            ListSQLFiles();
            return VSConstants.S_OK;
        }

        public void Dispose()
        {
            Microsoft.VisualStudio.Shell.ThreadHelper.ThrowIfNotOnUIThread();
            if (_trackCookie != 0)
            {
                _tracker.UnadviseTrackProjectDocumentsEvents(_trackCookie);
                _trackCookie = 0;
            }
            if (_slnCooke != 0)
            {
                _solutionService.UnadviseSolutionEvents(_slnCooke);
                _slnCooke = 0;
            }
        }

        // 解决方案打开后触发
        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
        {
            ListSQLFiles();
            return VSConstants.S_OK;
        }

        // 解决方案关闭后触发
        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
        {
            SQLDict.Clear();
            return VSConstants.S_OK;
        }

        public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy)
        {
            return VSConstants.S_OK;
        }

        public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, ref int pfCancel)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
        {
            return VSConstants.S_OK;
        }

        public int OnQueryCloseSolution(object pUnkReserved, ref int pfCancel)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeCloseSolution(object pUnkReserved)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterCloseSolution(object pUnkReserved)
        {
            return VSConstants.S_OK;
        }
    }
}
