using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace JoyCastle.BugReporter {
    public class ScreenshotCollector : IInfoCollector {
        public string Key => "screenshot";
        public bool IsEnabled { get; set; } = true;

        // 静态存储，跨 BugReportPanel 开关甚至 SDK 重建都保留
        private static readonly List<byte[]> _screenshots = new();

        public int Count => _screenshots.Count;
        public IReadOnlyList<byte[]> Screenshots => _screenshots;

        public IEnumerator CaptureScreenshot() {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            _screenshots.Add(tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        public void RemoveAt(int index) {
            if (index < 0 || index >= _screenshots.Count) return;
            _screenshots.RemoveAt(index);
        }

        public void Clear() {
            _screenshots.Clear();
        }

        public CollectResult Collect() {
            var result = new CollectResult();
            for (var i = 0; i < _screenshots.Count; i++) {
                result.Files[$"screenshot_{i}"] = _screenshots[i];
            }
            return result;
        }
    }
}
