using System.Collections;
namespace PeachPDF.Tests.CSS
{
    class ObjectArrayComparer : IEqualityComparer, IEqualityComparer<object[]>
    {
        public static readonly ObjectArrayComparer Instance = new ObjectArrayComparer();
        public bool Equals(object[] x, object[] y)
        {
            if (ReferenceEquals(x, y))
                return true;

            if (x == null || y == null)
                return false;

            if (x.Length != y.Length)
                return false;

            var comparer = EqualityComparer<object>.Default;
            for (int i = 0; i < x.Length; i++)
            {
                if (!comparer.Equals(x[i], x[i])) return false;
            }
            return true;
        }

        public int GetHashCode(object[] obj)
        {
            if (obj == null || obj.Length == 0)
                return 0;

            int hash = obj[0]?.GetHashCode() ?? 0;
            var comparer = EqualityComparer<object>.Default;
            for (int i = 1; i < obj.Length; i++)
            {
                uint temp = (uint)(hash << 5) | ((uint)hash >> 27);
                hash = ((int)temp + hash) ^ (obj[i]?.GetHashCode() ?? 0);
            }
            return hash;
        }

        bool IEqualityComparer.Equals(object x, object y)
        {
            if (x == y)
                return true;
            if (x == null || y == null)
                return false;
            if (x is object[] && y is object[])
                return Equals((object[])x, (object[])y);

            throw new ArgumentException("Type of argument is not compatible with this comparer.");
        }

        int IEqualityComparer.GetHashCode(object obj)
        {
            if (obj == null)
                return 0;
            if (obj is object[])
                return GetHashCode((object[])obj);

            throw new ArgumentException("Type of argument is not compatible with this comparer.");
        }
    }
}







