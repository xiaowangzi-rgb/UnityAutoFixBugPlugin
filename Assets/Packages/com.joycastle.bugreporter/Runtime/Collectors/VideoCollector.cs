using System;
using System.IO;
using UnityEngine;

namespace JoyCastle.BugReporter {
    /// <summary>
    /// 视频采集器。
    /// 测试同事通过系统文件选择器选择录屏视频，SDK 读取文件内容一并上报。
    /// 调用 PickVideo() 弹出文件选择器，选择后文件路径缓存，Collect() 时读取 bytes。
    /// </summary>
    public class VideoCollector : IInfoCollector {
        public string Key => "video";
        public bool IsEnabled { get; set; } = true;

        private readonly long _maxFileSizeBytes;
        private string _videoFilePath;
        private bool _hasVideo;

        /// <param name="maxFileSizeMB">视频文件大小上限（MB），默认 100</param>
        public VideoCollector(int maxFileSizeMB = 100) {
            _maxFileSizeBytes = (long)maxFileSizeMB * 1024 * 1024;
        }

        /// <summary>
        /// 弹出系统文件选择器，让用户选择视频文件。
        /// </summary>
        /// <param name="onResult">回调：(成功, 提示信息)</param>
        public void PickVideo(Action<bool, string> onResult = null) {
            var allowedTypes = new string[] { "video/*" };

            NativeFilePicker.PickFile(path => {
                if (string.IsNullOrEmpty(path)) {
                    _hasVideo = false;
                    _videoFilePath = null;
                    onResult?.Invoke(false, "用户取消选择");
                    return;
                }

                // 检查文件大小
                try {
                    var fileInfo = new FileInfo(path);
                    if (fileInfo.Length > _maxFileSizeBytes) {
                        var sizeMB = fileInfo.Length / (1024f * 1024f);
                        var limitMB = _maxFileSizeBytes / (1024f * 1024f);
                        var msg = $"视频文件太大（{sizeMB:F1}MB），请选择 {limitMB:F0}MB 以内的文件";
                        Debug.LogWarning($"[BugReporter] {msg}");
                        _hasVideo = false;
                        _videoFilePath = null;
                        onResult?.Invoke(false, msg);
                        return;
                    }
                } catch (Exception e) {
                    Debug.LogWarning($"[BugReporter] Failed to check video file: {e.Message}");
                    _hasVideo = false;
                    _videoFilePath = null;
                    onResult?.Invoke(false, $"无法读取文件: {e.Message}");
                    return;
                }

                _videoFilePath = path;
                _hasVideo = true;
                var fileSizeMB = new FileInfo(path).Length / (1024f * 1024f);
                onResult?.Invoke(true, $"已选择视频（{fileSizeMB:F1}MB）");
                Debug.Log($"[BugReporter] Video selected: {path} ({fileSizeMB:F1}MB)");
            }, allowedTypes);
        }

        /// <summary>
        /// 是否已选择视频文件。
        /// </summary>
        public bool HasVideo => _hasVideo;

        /// <summary>
        /// 清除已选择的视频。
        /// </summary>
        public void ClearVideo() {
            _videoFilePath = null;
            _hasVideo = false;
        }

        public CollectResult Collect() {
            var result = new CollectResult();

            if (!_hasVideo || string.IsNullOrEmpty(_videoFilePath)) return result;

            try {
                if (!File.Exists(_videoFilePath)) {
                    Debug.LogWarning($"[BugReporter] Video file not found: {_videoFilePath}");
                    return result;
                }

                var bytes = File.ReadAllBytes(_videoFilePath);
                var ext = Path.GetExtension(_videoFilePath)?.TrimStart('.') ?? "mp4";
                result.Files[$"video.{ext}"] = bytes;
                result.Fields["videoFileName"] = Path.GetFileName(_videoFilePath);
                result.Fields["videoFileSize"] = bytes.Length.ToString();
            } catch (Exception e) {
                Debug.LogWarning($"[BugReporter] Failed to read video file: {e.Message}");
            }

            return result;
        }
    }
}
