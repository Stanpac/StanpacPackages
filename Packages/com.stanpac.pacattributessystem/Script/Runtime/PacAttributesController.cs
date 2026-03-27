using System;
using System.Collections.Generic;
using UnityEngine;


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
        
        [SerializeField, Tooltip("If true, attributes will be rounded to the nearest integer")]
        private bool bRoundAttributes = true;

        /** All attributes hold by the controller */
        [SerializeField, HideInInspector]
        private readonly Dictionary<T, SAttribute> Attributes = new();
        
        /** All modifiers applied to attributes */
        [SerializeField, HideInInspector]
        private readonly Dictionary<T, List<PacAttributeModifier<T>>> Mods = new();

        /** Simple per-attribute event (new value only) */
        private readonly Dictionary<T, Action<float, float>> OnAttributeChangedCallback = new();
        
        public void LoadProfile(PacAttributeProfile<T> profile)
        {
            if (profile == null) return;

            Attributes.Clear();
            Mods.Clear();

            // Attributes
            for (int i = 0; i < profile.Attributes.Count; i++)
            {
                PacAttributeProfile<T>.PacAttributeDef def = profile.Attributes[i];

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
            
            return bRoundAttributes ? Mathf.Round(value) : value;
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
}
