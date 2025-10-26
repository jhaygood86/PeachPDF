namespace PeachPDF.CSS
{
    internal sealed class MatrixTransform : ITransform
    {
        private readonly float[] _values;

        internal MatrixTransform(float[] values)
        {
            _values = values;
        }

        public TransformMatrix ComputeMatrix()
        {
            return new(_values);
        }
    }
}