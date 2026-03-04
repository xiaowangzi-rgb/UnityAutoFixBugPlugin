using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace JoyCastle.BugReporter {
    public class ReportUploader {
        private readonly string _serverUrl;
        private readonly string _webhookToken;
        private readonly int _timeout;

        public ReportUploader(string serverUrl, string webhookToken = "", int timeout = 30) {
            _serverUrl = serverUrl;
            _webhookToken = webhookToken;
            _timeout = timeout;
        }

        public IEnumerator Upload(BugReport report, Action<bool, string> onComplete = null) {
            var form = new List<IMultipartFormSection>();

            // ── 字段映射：SDK 内部字段名 → 后端字段名 ──
            var fields = report.Fields ?? new Dictionary<string, string>();

            // title: 优先用 issueTitle，fallback 到 Description
            var title = GetField(fields, "issueTitle", report.Description ?? "");
            form.Add(new MultipartFormDataSection("title", title));

            // userId
            var userId = GetField(fields, "userId", "");
            form.Add(new MultipartFormDataSection("userId", userId));

            // device: 合并 deviceModel + osVersion
            var deviceModel = GetField(fields, "deviceModel", "");
            var osVersion = GetField(fields, "osVersion", "");
            var device = !string.IsNullOrEmpty(deviceModel) && !string.IsNullOrEmpty(osVersion)
                ? $"{deviceModel} / {osVersion}"
                : deviceModel + osVersion;
            form.Add(new MultipartFormDataSection("device", device));

            // version
            var version = GetField(fields, "versionName", "");
            form.Add(new MultipartFormDataSection("version", version));

            // branch
            var branch = GetField(fields, "gitBranch", "");
            form.Add(new MultipartFormDataSection("branch", branch));

            // commit
            var commit = GetField(fields, "gitCommit", "");
            form.Add(new MultipartFormDataSection("commit", commit));

            // appId
            form.Add(new MultipartFormDataSection("appId", report.AppId ?? ""));

            // 其他未映射的文本字段也带上
            var mappedKeys = new HashSet<string> {
                "issueTitle", "userId", "deviceModel", "osVersion",
                "versionName", "gitBranch", "gitCommit"
            };
            foreach (var kv in fields) {
                if (!mappedKeys.Contains(kv.Key)) {
                    form.Add(new MultipartFormDataSection(kv.Key, kv.Value ?? ""));
                }
            }

            // ── 所有文件统一用 "files" 作为字段名 ──
            if (report.Files != null) {
                foreach (var kv in report.Files) {
                    var fileName = kv.Key;
                    var mime = DetectMime(kv.Value, fileName);

                    // 确保文件名有扩展名
                    if (!fileName.Contains(".")) {
                        fileName += "." + DetectExt(kv.Value);
                    }

                    form.Add(new MultipartFormFileSection("files", kv.Value, fileName, mime));
                }
            }

            // ── 发送请求 ──
            using var request = UnityWebRequest.Post(_serverUrl, form);
            request.timeout = _timeout;

            // 添加认证 Header
            if (!string.IsNullOrEmpty(_webhookToken)) {
                request.SetRequestHeader("X-Webhook-Token", _webhookToken);
            }

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success) {
                Debug.Log($"[BugReporter] Upload success: {request.downloadHandler.text}");
                onComplete?.Invoke(true, request.downloadHandler.text);
            } else {
                Debug.LogWarning($"[BugReporter] Upload failed: {request.error}");
                onComplete?.Invoke(false, request.error);
            }
        }

        private static string GetField(Dictionary<string, string> fields, string key,
            string fallback = "") {
            return fields.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value)
                ? value
                : fallback;
        }

        private static string DetectMime(byte[] data, string fileName) {
            // PNG
            if (data.Length > 4 && data[0] == 0x89 && data[1] == 0x50)
                return "image/png";
            // JPEG
            if (data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8)
                return "image/jpeg";
            // MP4 (ftyp)
            if (data.Length > 8 && data[4] == 0x66 && data[5] == 0x74
                && data[6] == 0x79 && data[7] == 0x70)
                return "video/mp4";

            // 按文件名后缀判断
            if (fileName.EndsWith(".log") || fileName.EndsWith(".txt"))
                return "text/plain";

            return "application/octet-stream";
        }

        private static string DetectExt(byte[] data) {
            if (data.Length > 4 && data[0] == 0x89 && data[1] == 0x50) return "png";
            if (data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8) return "jpg";
            if (data.Length > 8 && data[4] == 0x66 && data[5] == 0x74
                && data[6] == 0x79 && data[7] == 0x70) return "mp4";
            return "bin";
        }
    }
}
