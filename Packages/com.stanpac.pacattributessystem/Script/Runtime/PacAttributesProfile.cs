using UnityEngine;
using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor; 
#endif

namespace PacAttributesSystem
{
    /** Base ScriptableObject to hold a profile of attributes */
    public class PacAttributesProfile<T> : ScriptableObject where T : Enum
    {
        [Serializable]
        public class PacAttributeDef
        {
            public T Type;
    
            [Min(0f)]
            public float BaseValue = 0f;
        }
        
        public List<PacAttributeDef> Attributes = new();
        
        // Helper method to add all enum values to the profile
        public void AddAllAttributes()
        {
            Array attributeTypes = Enum.GetValues(typeof(T));
            foreach (T type in attributeTypes)
            {
                if (!Attributes.Exists(attr => EqualityComparer<T>.Default.Equals(attr.Type, type)))
                {
                    PacAttributeDef newAttr = new PacAttributeDef { Type = type, BaseValue = 0f };
                    Attributes.Add(newAttr);
                }
            }
        }

        // Editor Validation to Warn about duplicate attribute types
        private void OnValidate()
        {
            if (Attributes == null || Attributes.Count <= 0) return;

            HashSet<T> types = new HashSet<T>();
            foreach (PacAttributeDef attr in Attributes)
            {
                if (types.Contains(attr.Type))
                {
                    Debug.LogWarning($"Duplicate attribute type '{attr.Type}' in profile '{this.name}'");
                }
                types.Add(attr.Type);
            }
        }
    }
    
#if UNITY_EDITOR
    [CustomEditor(typeof(PacAttributesProfile<>), true)]
    public class GAttributeProfileEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            
            if (GUILayout.Button("Add All Attributes", GUILayout.Height(30)))
            {
                // Use reflection to call AddAllAttributes method
                var addAllAttributesMethod = target.GetType().GetMethod("AddAllAttributes");
                if (addAllAttributesMethod != null)
                {
                    addAllAttributesMethod.Invoke(target, null);
                    EditorUtility.SetDirty(target);
                    AssetDatabase.SaveAssets();
                }
                else
                {
                    Debug.LogError("AddAllAttributes method not found on target");
                }
            }
            
            EditorGUILayout.HelpBox("Click 'Add All Attributes' to automatically add all enum values to the profile.", MessageType.Info);
        }
    }
#endif
    
}

