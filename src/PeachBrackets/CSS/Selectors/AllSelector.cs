namespace PeachPDF.CSS
{
    internal sealed class AllSelector : SelectorBase
    {
        public static AllSelector Create()
        {
            return new AllSelector();
        }

        private AllSelector() : base(Priority.Zero, "*")
        {
        }
    }
}