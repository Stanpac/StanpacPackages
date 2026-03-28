using UnityEngine;
using System;
using System.Collections.Generic;

// For custom editor
#if UNITY_EDITOR
using System.Collections;
using System.Reflection;
using UnityEditor;
using Object = UnityEngine.Object;
#endif


namespace PacAttributesSystem
{
    /** Controller responsible for managing a collection of attributes for a game entity. */
    public class PacAttributesController<T> : MonoBehaviour where T : Enum
    {
        /** Type of modifier to apply to an attribute. */
        public enum EModifierType
        {
            Additive = 1,       // ex : +10 = 10
            Multiplicative = 2, // ex : +10% = 0.1, Each Multiplicative modifier is summed before being applied
        }
        
        /** Holding the state of a single attribute. */
        private struct SAttribute
        {
            public float Base;
            public float CachedFinal; // Always valid 
        }
        
        [SerializeField, Tooltip("If true, attributes will be truncates to integer")]
        private bool IntegerAttributes = true;

        /** All attributes hold by the controller */
        private readonly Dictionary<T, SAttribute> Attributes = new();
        
        /** All modifiers applied to attributes */
        private readonly Dictionary<T, List<PacAttributeModifier<T>>> Mods = new();

        /** Simple per-attribute event (new value only) */
        private readonly Dictionary<T, Action<float, float>> OnAttributeChangedCallback = new();
        
        public void LoadProfile(PacAttributesProfile<T> profile)
        {
            if (profile == null) return;

            Attributes.Clear();
            Mods.Clear();

            // Attributes
            for (int i = 0; i < profile.Attributes.Count; i++)
            {
                PacAttributesProfile<T>.PacAttributeDef def = profile.Attributes[i];

                Attributes[def.Type] = new SAttribute
                {
                    Base = def.BaseValue,
                    CachedFinal = 0f
                };

                Mods[def.Type] = new List<PacAttributeModifier<T>>(4);

                // Compute initial Final (no event on load by default)
                RecomputeFinal_NoNotify(def.Type);
            }
        }

        /** Add a callback for when an attribute changes ( oldValue, newValue ) */
        public void SubscribeCallBack(T type, Action<float, float> callback)
        {
            if (callback == null) return;

            if (OnAttributeChangedCallback.TryGetValue(type, out Action<float, float> existing))
                OnAttributeChangedCallback[type] = existing + callback;
            else
                OnAttributeChangedCallback[type] = callback;
        }

        /** Remove a callback for when an attribute changes ( oldValue, newValue ) */
        public void UnsubscribeCallBack(T type, Action<float, float> callback)
        {
            if (callback == null) return;

            if (OnAttributeChangedCallback.TryGetValue(type, out Action<float, float> existing))
            {
                existing -= callback;
                if (existing == null) OnAttributeChangedCallback.Remove(type);
                else OnAttributeChangedCallback[type] = existing;
            }
        }

        /** Clear all callbacks for all attributes */
        public void ClearAllCallbacks()
        {
            OnAttributeChangedCallback.Clear();
        }

        /** Clear all callbacks for a specific attribute type */
        public void ClearAllAttributesCallbacks(T type)
        {
            if (OnAttributeChangedCallback.ContainsKey(type))
                OnAttributeChangedCallback.Remove(type);
        }

        public bool Has(T type) => Attributes.ContainsKey(type);

        // TODO : Transform in TryGet pattern ! 

        /** returns -1f if attribute not found */
        public float GetBase(T type) => Attributes.TryGetValue(type, out SAttribute attribute) ? attribute.Base : -1f;

        /** returns -1f if attribute not found */
        public float GetFinal(T type) => Attributes.TryGetValue(type, out SAttribute attribute) ? attribute.CachedFinal : -1f;

        public void SetBase(T type, float value)
        {
            if (!Attributes.TryGetValue(type, out SAttribute attribute))
            {
                Debug.LogError($"[Attributes] SetBase on unknown attribute '{type}'.");
                return;
            }

            attribute.Base = value;
            Attributes[type] = attribute;

            RecomputeFinal_NotifyIfChanged(type);
        }

        public void AddModifier(PacAttributeModifier<T> mod)
        {
            if (!Mods.TryGetValue(mod.Type, out List<PacAttributeModifier<T>> list))
            {
                Debug.LogError($"[Attributes] AddModifier on unknown attribute '{mod.Type}'.");
                return;
            }

            list.Add(mod);
            RecomputeFinal_NotifyIfChanged(mod.Type);
        }

        /** Helper method to create and add a modifier in one call */
        public PacAttributeModifier<T> AddMod(T type, EModifierType modType, float value, object source)
        {
            PacAttributeModifier<T> m = new PacAttributeModifier<T>(type, modType, value, source);
            AddModifier(m);
            return m;
        }

        public void AddModifiers(List<PacAttributeModifier<T>> mods)
        {
            if (mods == null || mods.Count == 0) return;

            HashSet<T> dirty = new HashSet<T>();

            for (int i = 0; i < mods.Count; i++)
            {
                PacAttributeModifier<T> mod = mods[i];

                if (!Mods.TryGetValue(mod.Type, out List<PacAttributeModifier<T>> list))
                {
                    Debug.LogError($"[Attributes] AddModifier on unknown attribute '{mod.Type}'.");
                    continue;
                }

                list.Add(mod);
                dirty.Add(mod.Type);
            }

            foreach (T type in dirty)
                RecomputeFinal_NotifyIfChanged(type);
        }

        public void RemoveModifier(PacAttributeModifier<T> mod)
        {
            if (!Mods.TryGetValue(mod.Type, out List<PacAttributeModifier<T>> list)) return;

            if (list.Remove(mod))
                RecomputeFinal_NotifyIfChanged(mod.Type);
        }

        /** Remove all modifiers from a given source */
        public void RemoveAllModifiersFromSource(object source)
        {
            if (source == null) return;

            List<T> TypeList = new List<T>();

            foreach (var mod in Mods)
            {
                List<PacAttributeModifier<T>> list = mod.Value;
                if (list == null || list.Count == 0) continue;

                int removed = 0;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (list[i].Source == source)
                    {
                        list.RemoveAt(i);
                        removed++;
                    }
                }

                if (removed > 0)
                    TypeList.Add(mod.Key);
            }

            for (int i = 0; i < TypeList.Count; i++)
                RecomputeFinal_NotifyIfChanged(TypeList[i]);
        }

        /** Clear all modifiers of a given type */
        public void ClearModifiers(T type)
        {
            if (!Mods.TryGetValue(type, out List<PacAttributeModifier<T>>list)) return;

            if (list.Count > 0)
            {
                list.Clear();
                RecomputeFinal_NotifyIfChanged(type);
            }
        }

        private void RecomputeFinal_NoNotify(T type)
        {
            if (!Attributes.TryGetValue(type, out var st)) return;
            st.CachedFinal = ComputeFinal(type, st.Base);
            Attributes[type] = st;
        }

        private void RecomputeFinal_NotifyIfChanged(T type)
        {
            if (!Attributes.TryGetValue(type, out SAttribute attribute)) return;

            float oldFinal = attribute.CachedFinal;
            float newFinal = ComputeFinal(type, attribute.Base);

            if (Mathf.Approximately(oldFinal, newFinal))
            {
                // Even if modifiers changed, final didn't; no need to notify
                attribute.CachedFinal = newFinal;
                Attributes[type] = attribute;
                return;
            }

            attribute.CachedFinal = newFinal;
            Attributes[type] = attribute;

            if (OnAttributeChangedCallback.TryGetValue(type, out Action<float, float> callback))
                callback?.Invoke(oldFinal, newFinal);
        }

        private float ComputeFinal(T type, float baseValue)
        {
            float value = ApplyModifiers(type, baseValue);

            // TODO : Modify ! 
            // For now, clamp to 0 minimum and no maximum 
            if (value < 0f) value = 0f;
            
            return IntegerAttributes ? (int)value : value;
        }

        private float ApplyModifiers(T type, float start)
        {
            if (!Mods.TryGetValue(type, out var list) || list.Count == 0)
                return start;

            float value = start;
            float sumPercent = 0f;

            for (int i = 0; i < list.Count; i++)
            {
                PacAttributeModifier<T> mod = list[i];
                if (mod.Value == 0f) continue;

                switch (mod.ModifierType)
                {
                    case EModifierType.Additive: value += mod.Value; break;
                    case EModifierType.Multiplicative: sumPercent += mod.Value; break;
                }
            }

            value *= (1f + sumPercent);
            return value;
        }
    } 

#if UNITY_EDITOR
    /** Custom Editor that show attributes and modifiers in the inspector */
    [CustomEditor(typeof(PacAttributesController<>), true)]
    public class PacAttributesControllerEditor : Editor
    {
        private bool _attributesFoldout = true;
        private bool _modsFoldout       = true;
 
        private GUIStyle _headerStyle;
        private GUIStyle _rowEvenStyle;
        private GUIStyle _rowOddStyle;
 
        private void InitStyles()
        {
            if (_headerStyle != null) return;
 
            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                normal   = { textColor = new Color(0.8f, 0.9f, 1f) }
            };
            _rowEvenStyle = new GUIStyle
            {
                normal  = { background = MakeTex(new Color(0.22f, 0.22f, 0.22f, 0.4f)) },
                padding = new RectOffset(4, 4, 2, 2)
            };
            _rowOddStyle = new GUIStyle
            {
                normal  = { background = MakeTex(new Color(0.18f, 0.18f, 0.18f, 0.4f)) },
                padding = new RectOffset(4, 4, 2, 2)
            };
        }
 
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            InitStyles();
 
            var controller      = target;
            var attributesField = FindFieldInHierarchy(controller.GetType(), "Attributes");
            var modsField       = FindFieldInHierarchy(controller.GetType(), "Mods");
 
            EditorGUILayout.Space(8);
            
            /* --- ATTRIBUTES ----------------------------------------------------------------- */
            
            // When collapsed, show a summary directly in the foldout label
            string attrLabel;
            if (attributesField == null)
            {
                attrLabel = "  Attributes  [field not found]";
            }
            else if (!_attributesFoldout)
            {
                int count = GetCount(attributesField.GetValue(controller));
                attrLabel = count == 0
                    ? "  Attributes  (empty)"
                    : $"  Attributes  ({count} loaded)";
            }
            else
            {
                attrLabel = "  Attributes";
            }
 
            _attributesFoldout = EditorGUILayout.Foldout(_attributesFoldout, attrLabel, true, _headerStyle);
 
            if (_attributesFoldout)
            {
                if (attributesField == null)
                {
                    EditorGUILayout.HelpBox(
                        "Could not find field 'Attributes' via reflection.\n" +
                        "Make sure your component inherits from PacAttributesController<T>.",
                        MessageType.Warning);
                }
                else
                {
                    var dict  = attributesField.GetValue(controller);
                    int count = GetCount(dict);
 
                    if (count == 0)
                    {
                        EditorGUILayout.HelpBox(
                            "No attributes loaded yet.\nCall LoadProfile() at runtime to populate.",
                            MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                        EditorGUILayout.LabelField("Attribute", EditorStyles.miniLabel, GUILayout.MinWidth(120));
                        EditorGUILayout.LabelField("Base",      EditorStyles.miniLabel, GUILayout.Width(70));
                        EditorGUILayout.LabelField("Final",     EditorStyles.miniLabel, GUILayout.Width(70));
                        EditorGUILayout.EndHorizontal();
 
                        int row = 0;
                        foreach (var (key, val) in IterateDict(dict))
                        {
                            float baseVal  = GetStructField<float>(val, "Base");
                            float finalVal = GetStructField<float>(val, "CachedFinal");
 
                            EditorGUILayout.BeginHorizontal(row % 2 == 0 ? _rowEvenStyle : _rowOddStyle);
                            EditorGUILayout.LabelField($"  {key}", GUILayout.MinWidth(120));
                            EditorGUILayout.LabelField(baseVal.ToString("F2"), GUILayout.Width(70));
 
                            var prev = GUI.contentColor;
                            GUI.contentColor = finalVal > baseVal ? Color.green
                                             : finalVal < baseVal ? new Color(1f, 0.5f, 0.4f)
                                             : Color.white;
                            EditorGUILayout.LabelField(finalVal.ToString("F2"), GUILayout.Width(70));
                            GUI.contentColor = prev;
 
                            EditorGUILayout.EndHorizontal();
                            row++;
                        }
                    }
                }
            }
 
            EditorGUILayout.Space(6);
            
            /* --- MODIFIERS ----------------------------------------------------------------- */
            
            string modsLabel;
            if (modsField == null)
            {
                modsLabel = "  Active Modifiers  [field not found]";
            }
            else if (!_modsFoldout)
            {
                int total = CountTotalMods(modsField.GetValue(controller));
                modsLabel = total == 0
                    ? "  Active Modifiers  (none)"
                    : $"  Active Modifiers  ({total} active)";
            }
            else
            {
                modsLabel = "  Active Modifiers";
            }
 
            _modsFoldout = EditorGUILayout.Foldout(_modsFoldout, modsLabel, true, _headerStyle);
 
            if (_modsFoldout)
            {
                if (modsField == null)
                {
                    EditorGUILayout.HelpBox(
                        "Could not find field 'Mods' via reflection.\n" +
                        "Make sure your component inherits from PacAttributesController<T>.",
                        MessageType.Warning);
                }
                else
                {
                    var  dict   = modsField.GetValue(controller);
                    bool anyMod = false;
 
                    foreach (var (key, listObj) in IterateDict(dict))
                    {
                        var list = listObj as IList;
                        if (list == null || list.Count == 0) continue;
                        anyMod = true;
 
                        EditorGUILayout.LabelField(
                            $"  {key}  ({list.Count} modifier{(list.Count > 1 ? "s" : "")})",
                            EditorStyles.miniLabel);
 
                        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                        EditorGUILayout.LabelField("Type",   EditorStyles.miniLabel, GUILayout.Width(110));
                        EditorGUILayout.LabelField("Value",  EditorStyles.miniLabel, GUILayout.Width(60));
                        EditorGUILayout.LabelField("Source", EditorStyles.miniLabel, GUILayout.MinWidth(80));
                        EditorGUILayout.EndHorizontal();
 
                        for (int i = 0; i < list.Count; i++)
                        {
                            var   mod       = list[i];
                            var   modType   = mod.GetType();
                            var   modKind   = modType.GetProperty("ModifierType")?.GetValue(mod);
                            float modVal    = GetClassProp<float>(mod, "Value");
                            var   modSource = modType.GetProperty("Source")?.GetValue(mod);
 
                            string sourceStr = modSource is Object uObj
                                ? (uObj != null ? uObj.name : "null")
                                : modSource?.ToString() ?? "null";
 
                            bool   isPercent = modKind?.ToString().Contains("Multiplicative") ?? false;
                            string valStr    = isPercent
                                ? $"{modVal * 100f:+0.##;-0.##}%"
                                : $"{modVal:+0.##;-0.##}";
 
                            EditorGUILayout.BeginHorizontal(i % 2 == 0 ? _rowEvenStyle : _rowOddStyle);
                            EditorGUILayout.LabelField($"  {modKind}", GUILayout.Width(110));
 
                            var prev = GUI.contentColor;
                            GUI.contentColor = modVal >= 0 ? Color.green : new Color(1f, 0.5f, 0.4f);
                            EditorGUILayout.LabelField(valStr, GUILayout.Width(60));
                            GUI.contentColor = prev;
 
                            EditorGUILayout.LabelField(sourceStr, GUILayout.MinWidth(80));
                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUILayout.Space(4);
                    }
 
                    if (!anyMod)
                        EditorGUILayout.HelpBox("No active modifiers.", MessageType.Info);
                }
            }
 
            if (Application.isPlaying)
                Repaint();
        }
 
        /* --- Helpers ----------------------------------------------------------------- */
 
        /** Walk up the type hierarchy to find a private field declared on a base class */
        private static FieldInfo FindFieldInHierarchy(Type type, string fieldName)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            while (type != null && type != typeof(object))
            {
                var fi = type.GetField(fieldName, flags);
                if (fi != null) return fi;
                type = type.BaseType;
            }
            return null;
        }

        private static int GetCount(object dict)
        {
            if (dict == null) return 0;
            return dict.GetType().GetProperty("Count")?.GetValue(dict) as int? ?? 0;
        }

        private static int CountTotalMods(object dict)
        {
            int total = 0;
            foreach (var (_, listObj) in IterateDict(dict))
            {
                if (listObj is IList list) total += list.Count;
            }
            return total;
        }

        /** Iterates any Dictionary via reflection, regardless of key/value generic types */
        private static IEnumerable<(object key, object value)> IterateDict(object dict)
        {
            if (dict == null) yield break;
            var getEnumerator = dict.GetType().GetMethod("GetEnumerator");
            if (getEnumerator == null) yield break;
            var enumerator  = getEnumerator.Invoke(dict, null);
            var enumType    = enumerator.GetType();
            var moveNext    = enumType.GetMethod("MoveNext");
            var currentProp = enumType.GetProperty("Current");
            if (moveNext == null || currentProp == null) yield break;
            while ((bool)moveNext.Invoke(enumerator, null))
            {
                var kvp     = currentProp.GetValue(enumerator);
                var kvpType = kvp.GetType();
                var key     = kvpType.GetProperty("Key")  ?.GetValue(kvp);
                var value   = kvpType.GetProperty("Value")?.GetValue(kvp);
                yield return (key, value);
            }
            (enumerator as IDisposable)?.Dispose();
        }

        /** Read a field from a boxed struct */
        private static TVal GetStructField<TVal>(object obj, string fieldName)
        {
            if (obj == null) return default;
            var fi = obj.GetType().GetField(fieldName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return fi != null ? (TVal)(fi.GetValue(obj) ?? default(TVal)) : default;
        }

        /** Read a property from a class instance */
        private static TVal GetClassProp<TVal>(object obj, string propName)
        {
            if (obj == null) return default;
            var pi = obj.GetType().GetProperty(propName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return pi != null ? (TVal)(pi.GetValue(obj) ?? default(TVal)) : default;
        }
 
        private static Texture2D MakeTex(Color col)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, col);
            tex.Apply();
            return tex;
        }
    }
#endif
}