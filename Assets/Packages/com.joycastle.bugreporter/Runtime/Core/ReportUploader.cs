using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace JoyCastle.BugReporter {
    public class ReportUploader {
        private readonly string _serverUrl;
        private readonly int _timeout;

        public ReportUploader(string serverUrl, int timeout = 30) {
            _serverUrl = serverUrl;
            _timeout = timeout;
        }

        public IEnumerator Upload(BugReport report, Action<bool, string> onComplete = null) {
            var form = new List<IMultipartFormSection> {
                new MultipartFormDataSection("appId", report.AppId ?? ""),
                new MultipartFormDataSection("description", report.Description ?? ""),
            };

            // 所有采集器的文本字段
            if (report.Fields != null) {
                foreach (var kv in report.Fields) {
                    form.Add(new MultipartFormDataSection(kv.Key, kv.Value ?? ""));
                }
            }

            // 所有采集器的文件字段
            if (report.Files != null) {
                foreach (var kv in report.Files) {
                    var ext = "bin";
                    var mime = "application/octet-stream";
                    // 简单判断 PNG
                    if (kv.Value.Length > 4 && kv.Value[0] == 0x89
                        && kv.Value[1] == 0x50) {
                        ext = "png";
                        mime = "image/png";
                    }
                    form.Add(new MultipartFormFileSection(
                        kv.Key, kv.Value, $"{kv.Key}.{ext}", mime));
                }
            }

            using var request = UnityWebRequest.Post(_serverUrl, form);
            request.timeout = _timeout;

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success) {
                Debug.Log($"[BugReporter] Upload success: {request.downloadHandler.text}");
                onComplete?.Invoke(true, request.downloadHandler.text);
            } else {
                Debug.LogWarning($"[BugReporter] Upload failed: {request.error}");
                onComplete?.Invoke(false, request.error);
            }
        }
    }
}
