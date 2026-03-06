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
            var metadata = BugReporterSDK.GetFieldMetadata();
            if (!metadata.IsReady) {
                Debug.LogError("[BugReporter] Cannot upload: field metadata not loaded.");
                onComplete?.Invoke(false, "Field metadata not loaded");
                yield break;
            }

            var form = new List<IMultipartFormSection>();

            // ── 构建 data JSON ──
            var fields = report.Fields ?? new Dictionary<string, string>();
            var dataJson = BuildDataJson(fields, metadata, report);
            form.Add(new MultipartFormDataSection("data", dataJson, Encoding.UTF8, "application/json"));

            // ── 所有文件统一用 "files" 作为字段名 ──
            if (report.Files != null) {
                foreach (var kv in report.Files) {
                    var fileName = kv.Key;
                    var mime = DetectMime(kv.Value, fileName);

                    if (!fileName.Contains(".")) {
                        fileName += "." + DetectExt(kv.Value);
                    }

                    form.Add(new MultipartFormFileSection("files", kv.Value, fileName, mime));
                }
            }

            // ── 打印上报详情 ──
            var sb = new StringBuilder();
            sb.AppendLine("[BugReporter] ===== Upload Details =====");
            sb.AppendLine($"  URL: {_serverUrl}");
            sb.AppendLine($"  Token: {(string.IsNullOrEmpty(_webhookToken) ? "(none)" : _webhookToken.Substring(0, 8) + "...")}");
            sb.AppendLine($"  [data] {dataJson}");
            foreach (var section in form) {
                if (!string.IsNullOrEmpty(section.fileName)) {
                    var sizeMB = section.sectionData != null ? section.sectionData.Length / (1024f * 1024f) : 0;
                    sb.AppendLine($"  [File] {section.fileName} ({sizeMB:F2}MB)");
                }
            }
            sb.AppendLine("[BugReporter] ========================");
            Debug.Log(sb.ToString());

            // ── 发送请求 ──
            var uploadUrl = _serverUrl.TrimEnd('/') + "/api/issue/add";
            using var request = UnityWebRequest.Post(uploadUrl, form);
            request.timeout = _timeout;

            if (!string.IsNullOrEmpty(_webhookToken)) {
                request.SetRequestHeader("X-API-Token", _webhookToken);
            }

            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success) {
                Debug.Log($"[BugReporter] Upload success: {request.downloadHandler.text}");
                onComplete?.Invoke(true, request.downloadHandler.text);
            } else {
                var responseBody = request.downloadHandler != null ? request.downloadHandler.text : "";
                Debug.LogWarning($"[BugReporter] Upload failed: {request.error}\n[Response]: {responseBody}");
                onComplete?.Invoke(false, request.error);
            }
        }

        /// <summary>
        /// 根据字段元数据动态构建 data JSON。
        /// 按 field_type 生成正确的传值格式。
        /// </summary>
        private string BuildDataJson(Dictionary<string, string> fields, FieldMetadataManager metadata, BugReport report) {
            var sb = new StringBuilder();
            sb.Append("{");
            var first = true;

            // 采集器产出的字段映射到后端 field_key
            // 采集器 key → 后端 field_key 的映射
            var collectorToFieldKey = new Dictionary<string, string> {
                ["issueTitle"] = "name",
                ["issueDec"] = "description",
            };

            // 先把采集器字段按映射转换
            var mergedFields = new Dictionary<string, string>();
            foreach (var kv in fields) {
                if (collectorToFieldKey.TryGetValue(kv.Key, out var mappedKey)) {
                    mergedFields[mappedKey] = kv.Value;
                } else {
                    mergedFields[kv.Key] = kv.Value;
                }
            }

            // 遍历元数据中定义的所有字段，按 field_type 格式化
            foreach (var fieldDef in metadata.Fields.Values) {
                if (fieldDef.field_type == "business") continue; // 无需传递

                var key = fieldDef.field_key;
                mergedFields.TryGetValue(key, out var rawValue);

                // 跳过空值（非必填字段）
                if (string.IsNullOrEmpty(rawValue) && !fieldDef.required) continue;

                if (!first) sb.Append(",");
                first = false;

                sb.Append("\"").Append(EscapeJson(key)).Append("\":");

                switch (fieldDef.field_type) {
                    case "select":
                        // select 类型必须传 {"label":"xxx","value":"xxx"}
                        AppendSelectValue(sb, fieldDef, rawValue);
                        break;

                    case "work_item_related_multi_select":
                        // 版本类字段传数字数组
                        AppendVersionArray(sb, rawValue);
                        break;

                    case "multi_user":
                        // 多选用户传字符串数组
                        sb.Append("[");
                        if (!string.IsNullOrEmpty(rawValue)) {
                            sb.Append("\"").Append(EscapeJson(rawValue)).Append("\"");
                        }
                        sb.Append("]");
                        break;

                    case "text":
                    case "multi_text":
                    default:
                        // 文本类型传字符串
                        sb.Append("\"").Append(EscapeJson(rawValue ?? "")).Append("\"");
                        break;
                }
            }

            // 附加不在元数据里的额外字段（如 device、version、appId 等采集器信息）
            var metadataKeys = new HashSet<string>(metadata.Fields.Keys);
            // 也排除已映射的采集器 key
            var skipKeys = new HashSet<string>(collectorToFieldKey.Keys);
            foreach (var kv in fields) {
                var actualKey = collectorToFieldKey.TryGetValue(kv.Key, out var mapped) ? mapped : kv.Key;
                if (metadataKeys.Contains(actualKey)) continue;
                if (skipKeys.Contains(kv.Key) && metadataKeys.Contains(mapped ?? "")) continue;
                if (kv.Key == "runtimeLog") continue;

                if (!first) sb.Append(",");
                first = false;
                sb.Append("\"").Append(EscapeJson(kv.Key)).Append("\":\"").Append(EscapeJson(kv.Value ?? "")).Append("\"");
            }

            // appId
            if (!string.IsNullOrEmpty(report.AppId)) {
                if (!first) sb.Append(",");
                sb.Append("\"appId\":\"").Append(EscapeJson(report.AppId)).Append("\"");
            }

            sb.Append("}");
            return sb.ToString();
        }

        /// <summary>
        /// select 类型：根据 rawValue 在 options 中查找匹配项，输出 {"label":"xxx","value":"xxx"}。
        /// rawValue 可以是 option 的 value 或 label。
        /// </summary>
        private void AppendSelectValue(StringBuilder sb, FieldDefinition fieldDef, string rawValue) {
            if (fieldDef.options != null && !string.IsNullOrEmpty(rawValue)) {
                foreach (var opt in fieldDef.options) {
                    if (opt.value == rawValue || opt.label == rawValue) {
                        sb.Append("{\"label\":\"").Append(EscapeJson(opt.label))
                          .Append("\",\"value\":\"").Append(EscapeJson(opt.value))
                          .Append("\"}");
                        return;
                    }
                }
            }

            // 找不到匹配项：用第一个选项作为默认值（如果有的话）
            if (fieldDef.options != null && fieldDef.options.Count > 0) {
                var fallback = fieldDef.options[0];
                sb.Append("{\"label\":\"").Append(EscapeJson(fallback.label))
                  .Append("\",\"value\":\"").Append(EscapeJson(fallback.value))
                  .Append("\"}");
            } else {
                sb.Append("null");
            }
        }

        /// <summary>
        /// 版本类字段：传数字数组 [6839092028]。
        /// rawValue 可能是逗号分隔的多个值。
        /// </summary>
        private void AppendVersionArray(StringBuilder sb, string rawValue) {
            sb.Append("[");
            if (!string.IsNullOrEmpty(rawValue)) {
                var parts = rawValue.Split(',');
                var first = true;
                foreach (var part in parts) {
                    var trimmed = part.Trim();
                    if (string.IsNullOrEmpty(trimmed)) continue;
                    if (!first) sb.Append(",");
                    first = false;
                    // 尝试作为数字输出（去掉引号）
                    if (long.TryParse(trimmed, out _)) {
                        sb.Append(trimmed);
                    } else {
                        sb.Append("\"").Append(EscapeJson(trimmed)).Append("\"");
                    }
                }
            }
            sb.Append("]");
        }

        private static string EscapeJson(string s) {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\")
                    .Replace("\"", "\\\"")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
        }

        private static string DetectMime(byte[] data, string fileName) {
            if (data.Length > 4 && data[0] == 0x89 && data[1] == 0x50)
                return "image/png";
            if (data.Length > 2 && data[0] == 0xFF && data[1] == 0xD8)
                return "image/jpeg";
            if (data.Length > 8 && data[4] == 0x66 && data[5] == 0x74
                && data[6] == 0x79 && data[7] == 0x70)
                return "video/mp4";
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
