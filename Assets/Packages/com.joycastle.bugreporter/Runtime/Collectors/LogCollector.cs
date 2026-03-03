using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace JoyCastle.BugReporter {
    public class LogCollector : IInfoCollector {
        public string Key => "log";
        public bool IsEnabled { get; set; } = true;

        private readonly int _maxLines;
        private readonly Queue<string> _logBuffer;
        private readonly List<string> _logFilePaths = new();
        private readonly int _maxFileReadBytes;
        private readonly bool _enableRuntimeLog;

        /// <param name="maxLines">运行时日志缓存行数</param>
        /// <param name="enableRuntimeLog">是否监听 Application.logMessageReceived</param>
        /// <param name="maxFileReadBytes">每个日志文件最大读取字节数（默认 64KB）</param>
        public LogCollector(int maxLines = 100, bool enableRuntimeLog = true,
            int maxFileReadBytes = 65536) {
            _maxLines = maxLines;
            _maxFileReadBytes = maxFileReadBytes;
            _enableRuntimeLog = enableRuntimeLog;
            _logBuffer = new Queue<string>(_maxLines);
            if (_enableRuntimeLog) {
                Application.logMessageReceived += OnLogReceived;
            }
        }

        /// <summary>
        /// 项目方调用：添加自定义日志文件路径。
        /// 上报时 SDK 会读取该文件最后 N 字节内容一并上报。
        /// </summary>
        public void AddLogFilePath(string path) {
            if (!string.IsNullOrEmpty(path) && !_logFilePaths.Contains(path)) {
                _logFilePaths.Add(path);
            }
        }

        private void OnLogReceived(string message, string stackTrace, LogType type) {
            var line = $"[{DateTime.Now:HH:mm:ss}][{type}] {message}";
            if (type == LogType.Exception || type == LogType.Error) {
                line += $"\n{stackTrace}";
            }
            if (_logBuffer.Count >= _maxLines) {
                _logBuffer.Dequeue();
            }
            _logBuffer.Enqueue(line);
        }

        public CollectResult Collect() {
            var result = new CollectResult();

            // 1. 运行时日志（如果启用）
            if (_enableRuntimeLog) {
                var sb = new StringBuilder();
                foreach (var line in _logBuffer) {
                    sb.AppendLine(line);
                }
                result.Fields["runtimeLog"] = sb.ToString();
            }

            // 2. 日志文件（逐个读取尾部内容）
            for (var i = 0; i < _logFilePaths.Count; i++) {
                var path = _logFilePaths[i];
                var content = ReadLogFileTail(path);
                if (content != null) {
                    var fieldKey = _logFilePaths.Count == 1
                        ? "logFile"
                        : $"logFile_{i}";
                    result.Fields[fieldKey] = content;
                }
            }

            return result;
        }

        private string ReadLogFileTail(string path) {
            try {
                if (!File.Exists(path)) return null;
                var fileInfo = new FileInfo(path);
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var readBytes = Math.Min(_maxFileReadBytes, fileInfo.Length);
                if (readBytes <= 0) return null;
                stream.Seek(-readBytes, SeekOrigin.End);
                var buffer = new byte[readBytes];
                stream.Read(buffer, 0, buffer.Length);
                return Encoding.UTF8.GetString(buffer);
            } catch (Exception e) {
                Debug.LogWarning($"[BugReporter] Failed to read log file '{path}': {e.Message}");
                return null;
            }
        }

        public void Dispose() {
            if (_enableRuntimeLog) {
                Application.logMessageReceived -= OnLogReceived;
            }
        }
    }
}
