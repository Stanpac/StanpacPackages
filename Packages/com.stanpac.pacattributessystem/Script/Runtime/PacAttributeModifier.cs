using System;
using UnityEngine;

namespace PacAttributesSystem
{
    /** Represents a modifier that can be applied to an attribute */
    [Serializable]
    public class PacAttributeModifier<T> where T : Enum
    {
        [field: SerializeField]
        public T Type { get; private set; }
        
        [field: SerializeField]
        public PacAttributesController<T>.EModifierType ModifierType { get; private set; }
        
        [field: SerializeField]
        public float Value { get; private set; }

        public object Source;

        public PacAttributeModifier(T type, PacAttributesController<T>.EModifierType modifierType, float value, object source)
        {
            this.Type = type;
            this.ModifierType = modifierType;
            this.Value = value;
            this.Source = source;
        }

        public PacAttributeModifier(T type, PacAttributesController<T>.EModifierType modifierType, float value, bool isTemporary, object source)
        {
            this.Type = type;
            this.ModifierType = modifierType;
            this.Value = value;
            this.Source = source;
        }

        public PacAttributeModifier(PacAttributeModifier<T> other)
        {
            this.Type = other.Type;
            this.ModifierType = other.ModifierType;
            this.Value = other.Value;
            this.Source = other.Source;
        }
    }
}

