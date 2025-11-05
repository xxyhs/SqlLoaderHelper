using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SqlLoaderHelper
{
    public class SQLFileWatcher : IVsSolutionEvents, IDisposable
    {
        public static List<string> SQLDict = new List<string>();

        public static string SQLRoot = string.Empty;

        private static bool _loading = false;

        private static readonly object _lock = new object();

        private readonly IVsSolution _solutionService;

        private uint _slnCooke;

        private FileSystemWatcher fileSystemWatcher;

        public SQLFileWatcher(IVsSolution solutionService, string rootDir)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SQLRoot = rootDir;
            _solutionService = solutionService;
            _solutionService.AdviseSolutionEvents(this, out _slnCooke);
        }

        /// <summary>
        /// 将sql文件转换成code
        /// </summary>
        private static void CalcCodeDict(List<string> files)
        {
            if (files == null || files.Count == 0)
                return;

            SQLDict = files.Where(t => t.StartsWith(SQLRoot)).Select(t =>
            {
                return t.Replace(SQLRoot, "").Replace(".sql", "")
                     .Trim(new char[] { Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar })
                     .Replace(Path.DirectorySeparatorChar, '.')
                     .Replace(Path.AltDirectorySeparatorChar, '.');
            }).ToList();
        }

        public void RebuildSolutionIndex()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            lock (_lock)
            {
                if (_loading)
                {
                    return;
                }
                _loading = true;
            }
            DirectoryInfo directoryInfo = new DirectoryInfo(SQLRoot);
            var sqlFiles = directoryInfo.GetFiles("*.sql", SearchOption.AllDirectories).Select(t => t.FullName).ToList();
            CalcCodeDict(sqlFiles);
            lock (_lock)
            {
                _loading = false;
            }
        }


        public static string GetCorrespondingPathByCode(string code)
        {
            if (string.IsNullOrEmpty(SQLRoot))
            {
                return string.Empty;
            }
            return Path.Combine(SQLRoot, code.Replace('.', Path.DirectorySeparatorChar) + ".sql");
        }

        public static string GetCodeByFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(SQLRoot))
            {
                return string.Empty;
            }
            var relativePath = filePath.Substring(SQLRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var code = relativePath.Replace(".sql", "").Replace(Path.DirectorySeparatorChar, '.').Replace(Path.AltDirectorySeparatorChar, '.').Trim(new char[] { '.' });
            return code;
        }

        public void InitWatch()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (fileSystemWatcher != null)
            {
                fileSystemWatcher.Dispose();
                fileSystemWatcher = null;
            }
            fileSystemWatcher = new FileSystemWatcher(SQLRoot)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };
            fileSystemWatcher.Created += (sender, args) =>
            {
                if (args.FullPath.EndsWith(".sql"))
                {
                    SQLDict.Add(GetCodeByFilePath(args.FullPath));
                }
            };
            fileSystemWatcher.Deleted += (sender, args) =>
            {
                var code = GetCodeByFilePath(args.FullPath);
                if (args.FullPath.EndsWith(".sql"))
                {
                    SQLDict.Remove(code);
                }
                else
                {
                    SQLDict.RemoveAll(t => t.StartsWith(code));
                }
            };
            fileSystemWatcher.Renamed += (sender, args) =>
            {
                var oldFullPath = args.OldFullPath;
                var newFullPath = args.FullPath;
                var newCode = GetCodeByFilePath(newFullPath);
                var oldCode = GetCodeByFilePath(oldFullPath);
                if (newFullPath.EndsWith(".sql"))
                {
                    SQLDict.Add(newCode);
                }
                if (oldFullPath.EndsWith(".sql"))
                {
                    SQLDict.Remove(oldCode);
                }
                else
                {
                    for (var i = 0; i < SQLDict.Count; i++)
                    {
                        if (SQLDict[i].StartsWith(oldCode))
                        {
                            SQLDict[i] = SQLDict[i].Replace(oldCode, newCode);
                        }
                    }
                }
            };
            fileSystemWatcher.Changed += (sender, args) => {
                RebuildSolutionIndex();
            };
        }
        public void Dispose()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            SQLDict.Clear();
            fileSystemWatcher.Dispose();
            if (_slnCooke != 0)
            {
                _solutionService.UnadviseSolutionEvents(_slnCooke);
                _slnCooke = 0;
            }
        }

        public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded)
        {
            return VSConstants.S_OK;
        }

        public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, ref int pfCancel)
        {
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
            RebuildSolutionIndex();
            InitWatch();
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
            Dispose();
            return VSConstants.S_OK;
        }
    }
}
