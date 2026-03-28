namespace PacAttributesSystem
{
    public class ExampleAttributeModifier : PacAttributeModifier<EExampleAttributes>
    {
        public ExampleAttributeModifier(EExampleAttributes type, PacAttributesController<EExampleAttributes>.EModifierType modifierType, float value, object source) 
            : base(type, modifierType, value, source)
        {
        }

        public ExampleAttributeModifier(EExampleAttributes type, PacAttributesController<EExampleAttributes>.EModifierType modifierType, float value, bool isTemporary, object source)
            : base(type, modifierType, value, isTemporary, source)
        {
        }

        public ExampleAttributeModifier(PacAttributeModifier<EExampleAttributes> other) 
            : base(other)
        {
        }
    }
}
