using System.Collections;
using UnityEngine;

namespace JoyCastle.BugReporter {
    public class ScreenshotCollector : IInfoCollector {
        public string Key => "screenshot";
        public bool IsEnabled { get; set; } = true;

        private byte[] _lastScreenshot;

        public IEnumerator CaptureScreenshot() {
            yield return new WaitForEndOfFrame();
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            _lastScreenshot = tex.EncodeToPNG();
            Object.Destroy(tex);
        }

        public CollectResult Collect() {
            var result = new CollectResult();
            if (_lastScreenshot != null) {
                result.Files["screenshot"] = _lastScreenshot;
            }
            return result;
        }
    }
}
