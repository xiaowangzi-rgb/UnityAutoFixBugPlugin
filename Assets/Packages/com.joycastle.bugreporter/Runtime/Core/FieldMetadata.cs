using System;
using System.Collections.Generic;

namespace JoyCastle.BugReporter {
    /// <summary>
    /// 字段元数据，对应 GET /api/issue/fields 返回的单个字段定义。
    /// </summary>
    [Serializable]
    public class FieldDefinition {
        public string field_key;
        public string field_name;
        public string field_type;
        public bool required;
        public List<FieldOption> options;
    }

    [Serializable]
    public class FieldOption {
        public string label;
        public string value;
    }

    [Serializable]
    public class FieldsResponse {
        public List<FieldDefinition> fields;
    }

    /// <summary>
    /// 字段元数据管理器。SDK 启动时拉取并缓存，供 Panel 和 Uploader 使用。
    /// </summary>
    public class FieldMetadataManager {
        private readonly Dictionary<string, FieldDefinition> _fieldMap = new();
        private bool _ready;

        public bool IsReady => _ready;
        public IReadOnlyDictionary<string, FieldDefinition> Fields => _fieldMap;

        public void Load(List<FieldDefinition> fields) {
            _fieldMap.Clear();
            if (fields != null) {
                foreach (var f in fields) {
                    if (!string.IsNullOrEmpty(f.field_key)) {
                        _fieldMap[f.field_key] = f;
                    }
                }
            }
            _ready = true;
        }

        /// <summary>
        /// 根据 field_key 获取字段定义，不存在返回 null。
        /// </summary>
        public FieldDefinition Get(string fieldKey) {
            return _fieldMap.TryGetValue(fieldKey, out var def) ? def : null;
        }

        /// <summary>
        /// 获取所有需要在 UI 中展示的可填写字段（排除 business 类型）。
        /// </summary>
        public List<FieldDefinition> GetEditableFields() {
            var result = new List<FieldDefinition>();
            foreach (var f in _fieldMap.Values) {
                if (f.field_type != "business") {
                    result.Add(f);
                }
            }
            return result;
        }
    }
}
