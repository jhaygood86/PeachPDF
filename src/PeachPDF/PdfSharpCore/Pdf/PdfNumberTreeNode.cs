// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using PeachPDF.PdfSharpCore.Pdf.Advanced;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace PeachPDF.PdfSharpCore.Pdf
{
    /// <summary>
    /// Represents a number tree node (ISO 32000-1 7.9.7) - structurally identical to
    /// PdfNameTreeNode except keys are integers ("/Nums") instead of strings ("/Names"), compared
    /// numerically instead of via string.CompareOrdinal. Used for the structure tree's
    /// "/ParentTree". Only ever populated as a single root node in this fork (no "/Kids"
    /// splitting) - an accepted simplification for realistic document sizes; the ported
    /// Kids/PrepareForSave machinery below would support splitting later if ever needed.
    /// </summary>
    [DebuggerDisplay("({" + nameof(DebuggerDisplay) + "})")]
    internal sealed class PdfNumberTreeNode : PdfDictionary
    {
        public PdfNumberTreeNode()
        { }

        /// <summary>
        /// Constructor required for lazy auto-creation via VCF.CreateIndirect (see
        /// DictionaryElements.CreateDictionary, which specifically looks for a single-PdfDocument
        /// constructor) - used by PdfStructureTreeRoot.ParentTree.
        /// </summary>
        public PdfNumberTreeNode(PdfDocument document)
            : base(document)
        { }

        public PdfNumberTreeNode(PdfDictionary dict)
            : base(dict)
        {
            Initialize();
        }

        /// <summary>
        /// Gets the parent of this node or null if this is the root node.
        /// </summary>
        public PdfNumberTreeNode? Parent { get; set; }

        /// <summary>
        /// Gets a value indicating whether this instance is a root node.
        /// </summary>
        public bool IsRoot => Parent == null;

        /// <summary>
        /// Gets the kids of this item.
        /// </summary>
        public IEnumerable<PdfNumberTreeNode> Kids => _kids;

        private readonly List<PdfNumberTreeNode> _kids = new();

        private void Initialize()
        {
            var kids = Elements.GetArray(Keys.Kids);
            if (kids != null)
            {
                for (var i = 0; i < kids.Elements.Count; i++)
                {
                    var kidDict = kids.Elements.GetDictionary(i);
                    if (kidDict != null)
                    {
                        var kid = new PdfNumberTreeNode(kidDict) { Parent = this };
                        _kids.Add(kid);
                    }
                }
            }
            _updateRequired = true;
            UpdateLimits();
        }

        /// <summary>
        /// Determines whether this node contains the specified <paramref name="key"/>.
        /// </summary>
        public bool ContainsNumber(int key, bool includeKids = false)
        {
            var nums = Elements.GetArray(Keys.Nums);
            if (nums != null)
            {
                for (var i = 0; i < nums.Elements.Count; i += 2)
                {
                    if (nums.Elements.GetInteger(i) == key)
                        return true;
                }
            }
            if (includeKids)
            {
                foreach (var kid in _kids)
                {
                    if (kid.ContainsNumber(key, true))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Gets the value of the item with the specified <paramref name="key"/>. If the value
        /// represents a reference, the referenced value is returned.
        /// </summary>
        public PdfItem? GetValue(int key, bool includeKids = false)
        {
            var nums = Elements.GetArray(Keys.Nums);
            if (nums != null)
            {
                for (var i = 0; i < nums.Elements.Count; i += 2)
                {
                    if (nums.Elements.GetInteger(i) == key)
                    {
                        var item = nums.Elements[i + 1];
                        return item is PdfReference itRef ? itRef.Value : item;
                    }
                }
            }
            if (includeKids)
            {
                foreach (var kid in _kids)
                {
                    var value = kid.GetValue(key, true);
                    if (value != null)
                        return value;
                }
            }
            return null;
        }

        /// <summary>
        /// Adds a child node to this node.
        /// </summary>
        public void AddKid(PdfNumberTreeNode kidNode)
        {
            var kids = Elements.GetArray(Keys.Kids);
            if (kids == null)
            {
                kids = new PdfArray();
                Elements.SetObject(Keys.Kids, kids);
            }
            kidNode.Parent = this;
            kids.Elements.Add(kidNode);

            // The /Kids array above is what actually gets written out, but ContainsNumber/
            // GetValue/CollectKeys/the public Kids property all walk the in-memory _kids list
            // (only otherwise populated by the dict-wrapping constructor parsing an existing
            // /Kids array) - without this, a kid added at runtime via AddKid would be invisible
            // to every includeKids:true traversal and to LeastKey/GreatestKey.
            _kids.Add(kidNode);
            _updateRequired = true;
        }

        /// <summary>
        /// Adds a key/value pair to the Nums array of this node, keeping entries sorted by key.
        /// </summary>
        public void AddNumber(int key, PdfItem value)
        {
            var nums = Elements.GetArray(Keys.Nums);
            if (nums == null)
            {
                nums = new PdfArray();
                Elements.SetObject(Keys.Nums, nums);
            }

            // Insert entries sorted by key. Entries are key/value pairs, so step by 2.
            int i = 0;
            while (i < nums.Elements.Count && nums.Elements.GetInteger(i) < key)
                i += 2;

            nums.Elements.Insert(i, new PdfInteger(key));
            nums.Elements.Insert(i + 1, value);
            _updateRequired = true;
        }

        /// <summary>
        /// Gets the least key.
        /// </summary>
        public int LeastKey
        {
            get
            {
                UpdateLimits();
                return _leastKey;
            }
        }
        private int _leastKey;

        /// <summary>
        /// Gets the greatest key.
        /// </summary>
        public int GreatestKey
        {
            get
            {
                UpdateLimits();
                return _greatestKey;
            }
        }
        private int _greatestKey;

        bool _updateRequired;

        void UpdateLimits()
        {
            if (_updateRequired)
            {
                var keys = new List<int>();
                CollectKeys(keys, true);
                if (keys.Count > 0)
                {
                    keys.Sort();
                    _leastKey = keys[0];
                    _greatestKey = keys[^1];
                    Elements[Keys.Limits] = new PdfArray(Owner,
                        new PdfInteger(_leastKey), new PdfInteger(_greatestKey));
                }
                _updateRequired = false;
            }
        }

        void CollectKeys(List<int> keys, bool includeKids)
        {
            var nums = Elements.GetArray(Keys.Nums);
            if (nums != null)
            {
                for (var i = 0; i < nums.Elements.Count; i += 2)
                    keys.Add(nums.Elements.GetInteger(i));
            }
            if (includeKids)
            {
                foreach (var kid in _kids)
                    kid.CollectKeys(keys, true);
            }
        }

        internal override void PrepareForSave()
        {
            UpdateLimits();
            base.PrepareForSave();
        }

        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        internal sealed class Keys : KeysBase
        {
            // Reference: ISO 32000-1 Table 37 - Entries in a number tree node dictionary

            [KeyInfo(KeyType.Array)]
            public const string Kids = "/Kids";

            [KeyInfo(KeyType.Array)]
            public const string Nums = "/Nums";

            [KeyInfo(KeyType.Array)]
            public const string Limits = "/Limits";

            public static DictionaryMeta Meta => _meta ??= CreateMeta(typeof(Keys));

            static DictionaryMeta? _meta;
        }

        internal override DictionaryMeta Meta => Keys.Meta;

        string DebuggerDisplay => String.Format("root:{0}", IsRoot);
    }
}
