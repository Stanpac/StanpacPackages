using System;
using UnityEngine;

namespace PacAttributesSystem
{
    public enum EExampleAttributes
    {
        MaxHealth,
        Strength,
        Speed,
        Intelligence,
        Agility
    }

    public class ExampleAttributesController : PacAttributesController<EExampleAttributes>
    {
        [SerializeField]
        private ExampleAttributesProfile Profile;
        
        void Start()
        {
            LoadProfile(Profile);
            AddStrengthModifier();
        }
        
        /** how to add a modifiers from a script */
        void AddStrengthModifier()
        {
            ExampleAttributeModifier ExempleModifier = new ExampleAttributeModifier(EExampleAttributes.Strength, EModifierType.Additive, 10, this);
            AddModifier(ExempleModifier);
        }
    }
}