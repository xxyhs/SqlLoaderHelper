using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SqlLoaderHelper
{
    public class SqlFileCache : IDisposable
    {
        public static SqlFileCache Instance = new SqlFileCache();

        public static List<string> SQLDict = new List<string>();

        private static bool _loading = false;

        private static readonly object _lock = new object();

        private FileSystemWatcher fileSystemWatcher;

        /// <summary>
        /// 将sql文件转换成code
        /// </summary>
        private static void CalcCodeDict(List<string> files)
        {
            if (files == null || files.Count == 0)
                return;

            SQLDict = files.Where(t => t.StartsWith(SlnConfig.Instance.SQLRoot)).Select(t =>
            {
                return t.Replace(SlnConfig.Instance.SQLRoot, "").Replace(".sql", "")
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
            DirectoryInfo directoryInfo = new DirectoryInfo(SlnConfig.Instance.SQLRoot);
            var sqlFiles = directoryInfo.GetFiles("*.sql", SearchOption.AllDirectories).Select(t => t.FullName).ToList();
            CalcCodeDict(sqlFiles);
            lock (_lock)
            {
                _loading = false;
            }
        }

        public static string GetCorrespondingPathByCode(string code)
        {
            if (string.IsNullOrEmpty(SlnConfig.Instance.SQLRoot))
            {
                return string.Empty;
            }
            return Path.Combine(SlnConfig.Instance.SQLRoot, code.Replace('.', Path.DirectorySeparatorChar) + ".sql");
        }

        public static string GetCodeByFilePath(string filePath)
        {
            if (string.IsNullOrEmpty(SlnConfig.Instance.SQLRoot))
            {
                return string.Empty;
            }
            var relativePath = filePath.Substring(SlnConfig.Instance.SQLRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
            fileSystemWatcher = new FileSystemWatcher(SlnConfig.Instance.SQLRoot)
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
            fileSystemWatcher = null;
        }
    }
}
