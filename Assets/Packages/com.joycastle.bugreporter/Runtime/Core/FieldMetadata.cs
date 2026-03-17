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
        private Dictionary<string, string> _preferences = new();
        private readonly Dictionary<string, string> _defaultValues = new();
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
        /// 设置设备偏好数据（上次选择的字段值）。
        /// </summary>
        public void SetPreferences(Dictionary<string, string> preferences) {
            _preferences = preferences ?? new Dictionary<string, string>();
        }

        /// <summary>
        /// 获取指定字段的偏好值，不存在返回 null。
        /// </summary>
        public string GetPreference(string fieldKey) {
            return _preferences.TryGetValue(fieldKey, out var val) ? val : null;
        }

        /// <summary>
        /// 项目方设置 Dropdown 字段的默认值（优先级高于服务器 preferences）。
        /// </summary>
        public void SetDefaultValue(string fieldKey, string value) {
            if (!string.IsNullOrEmpty(fieldKey)) {
                _defaultValues[fieldKey] = value;
            }
        }

        /// <summary>
        /// 获取字段的默认选中值。优先级：项目方设置 > 服务器 preferences。
        /// </summary>
        public string GetDefaultValue(string fieldKey) {
            if (_defaultValues.TryGetValue(fieldKey, out var val)) return val;
            if (_preferences.TryGetValue(fieldKey, out val)) return val;
            return null;
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
