using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeachBrackets.Scripting.Internal;

/// <summary>A <c>Range</c>: two boundary points, each a node and an offset into it.</summary>
internal sealed class RangeObject : ObjectInstance
{
    public RangeObject(Engine engine, ObjectInstance prototype, ScriptNode node) : base(engine)
    {
        Prototype = prototype;
        StartNode = EndNode = node;
    }

    public ScriptNode StartNode { get; set; }

    public int StartOffset { get; set; }

    public ScriptNode EndNode { get; set; }

    public int EndOffset { get; set; }

    public bool Collapsed => ReferenceEquals(StartNode, EndNode) && StartOffset == EndOffset;
}

internal sealed partial class ScriptRuntime
{
    private ObjectInstance AbstractRangePrototype = null!;
    private ObjectInstance RangePrototype = null!;
    private readonly List<RangeObject> _ranges = [];
    private bool _dataReplacedByRange;

    private static int LengthOf(ScriptNode node) => node.Kind switch
    {
        ScriptNodeKind.Text or ScriptNodeKind.Comment or ScriptNodeKind.ProcessingInstruction => node.Data!.Length,
        ScriptNodeKind.DocumentType => 0,
        _ => node.Children.Count,
    };

    private static bool IsCharacterData(ScriptNode node) => node.Kind is ScriptNodeKind.Text or ScriptNodeKind.Comment or ScriptNodeKind.ProcessingInstruction;

    private static ScriptNode RootOf(ScriptNode node)
    {
        while (node.Parent is { } parent)
            node = parent;

        return node;
    }

    private static int IndexOf(ScriptNode node) => node.Parent is { } parent ? parent.Children.ToList().IndexOf(node) : 0;

    private static bool IsInclusiveAncestor(ScriptNode ancestor, ScriptNode node)
    {
        for (var current = node; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }

    // Whether a comes before b in tree order (both in the same tree).
    private static bool Precedes(ScriptNode a, ScriptNode b)
    {
        if (ReferenceEquals(a, b))
            return false;

        var chainA = new List<ScriptNode>();
        for (var n = a; n is not null; n = n.Parent)
            chainA.Insert(0, n);

        var chainB = new List<ScriptNode>();
        for (var n = b; n is not null; n = n.Parent)
            chainB.Insert(0, n);

        var i = 0;
        while (i < chainA.Count && i < chainB.Count && ReferenceEquals(chainA[i], chainB[i]))
            i++;

        if (i == chainA.Count)
            return true;
        if (i == chainB.Count)
            return false;

        return IndexOf(chainA[i]) < IndexOf(chainB[i]);
    }

    // -1 when the first point is before the second, 0 when they are the same, 1 when it is after.
    private static int ComparePoints(ScriptNode nodeA, int offsetA, ScriptNode nodeB, int offsetB)
    {
        if (ReferenceEquals(nodeA, nodeB))
            return offsetA.CompareTo(offsetB);

        if (Precedes(nodeB, nodeA) && !IsInclusiveAncestor(nodeB, nodeA))
            return -ComparePoints(nodeB, offsetB, nodeA, offsetA);

        if (IsInclusiveAncestor(nodeA, nodeB))
        {
            var child = nodeB;
            while (!ReferenceEquals(child.Parent, nodeA))
                child = child.Parent!;

            return IndexOf(child) < offsetA ? 1 : -1;
        }

        if (IsInclusiveAncestor(nodeB, nodeA))
            return -ComparePoints(nodeB, offsetB, nodeA, offsetA);

        return -1;
    }

    private static bool IsContained(RangeObject range, ScriptNode node) =>
        ReferenceEquals(RootOf(node), RootOf(range.StartNode))
        && ComparePoints(node, 0, range.StartNode, range.StartOffset) > 0
        && ComparePoints(node, LengthOf(node), range.EndNode, range.EndOffset) < 0;

    private static bool IsPartiallyContained(RangeObject range, ScriptNode node) =>
        IsInclusiveAncestor(node, range.StartNode) != IsInclusiveAncestor(node, range.EndNode);

    private ScriptNode RangeNodeArg(JsValue[] args, int index) =>
        Js.Arg(args, index) is NodeObject node ? node.Node : throw TypeError("The argument is not a Node.");

    private RangeObject Rg(JsValue self) => self as RangeObject ?? throw TypeError("Illegal invocation");

    private void SetBoundary(RangeObject range, ScriptNode node, int offset, bool start)
    {
        if (node.Kind == ScriptNodeKind.DocumentType)
            throw DomError("InvalidNodeTypeError", "A document type cannot be a boundary.");
        if (offset > LengthOf(node) || offset < 0)
            throw DomError("IndexSizeError", "The offset is larger than the node's length.");

        if (start)
        {
            if (!ReferenceEquals(RootOf(node), RootOf(range.StartNode)) || ComparePoints(node, offset, range.EndNode, range.EndOffset) > 0)
            {
                range.EndNode = node;
                range.EndOffset = offset;
            }

            range.StartNode = node;
            range.StartOffset = offset;
        }
        else
        {
            if (!ReferenceEquals(RootOf(node), RootOf(range.StartNode)) || ComparePoints(node, offset, range.StartNode, range.StartOffset) < 0)
            {
                range.StartNode = node;
                range.StartOffset = offset;
            }

            range.EndNode = node;
            range.EndOffset = offset;
        }
    }

    private static int OffsetArg(JsValue value) => (int)Math.Min(TypeConverter.ToUint32(value), int.MaxValue);

    // Replaces count characters of a node's data from offset, moving the ranges that point into it as the standard says.
    private void ReplaceData(ScriptNode node, int offset, int count, string replacement)
    {
        var data = node.Data ?? string.Empty;
        _dataReplacedByRange = true;
        try
        {
            node.Data = string.Concat(data.AsSpan(0, offset), replacement, data.AsSpan(offset + count));
        }
        finally
        {
            _dataReplacedByRange = false;
        }

        foreach (var range in _ranges)
        {
            if (ReferenceEquals(range.StartNode, node) && range.StartOffset > offset && range.StartOffset <= offset + count)
                range.StartOffset = offset;
            else if (ReferenceEquals(range.StartNode, node) && range.StartOffset > offset + count)
                range.StartOffset += replacement.Length - count;

            if (ReferenceEquals(range.EndNode, node) && range.EndOffset > offset && range.EndOffset <= offset + count)
                range.EndOffset = offset;
            else if (ReferenceEquals(range.EndNode, node) && range.EndOffset > offset + count)
                range.EndOffset += replacement.Length - count;
        }
    }

    // Splits a text node at an offset, the second part becoming its next sibling.
    private ScriptNode SplitTextNode(ScriptNode node, int offset)
    {
        var data = node.Data ?? string.Empty;
        if (offset > data.Length)
            throw DomError("IndexSizeError", "The offset is greater than the length of the data.");

        var tail = node.OwnerDocument.CreateTextNode(data[offset..]);
        var parent = node.Parent;
        parent?.InsertBefore(tail, node.NextSibling);
        if (parent is not null)
        {
            var index = IndexOf(node);
            foreach (var range in _ranges)
            {
                if (ReferenceEquals(range.StartNode, node) && range.StartOffset > offset)
                {
                    range.StartNode = tail;
                    range.StartOffset -= offset;
                }

                if (ReferenceEquals(range.EndNode, node) && range.EndOffset > offset)
                {
                    range.EndNode = tail;
                    range.EndOffset -= offset;
                }

                if (ReferenceEquals(range.StartNode, parent) && range.StartOffset == index + 1)
                    range.StartOffset++;
                if (ReferenceEquals(range.EndNode, parent) && range.EndOffset == index + 1)
                    range.EndOffset++;
            }
        }

        ReplaceData(node, offset, data.Length - offset, string.Empty);
        return tail;
    }

    // The live ranges follow what happened to the tree.
    private void AdjustRanges(ScriptMutation mutation)
    {
        switch (mutation.Kind)
        {
            case ScriptMutationKind.ChildList:
            {
                var parent = mutation.Target;
                var siblings = parent.Children.ToList();
                var at = mutation.Previous is null ? 0 : siblings.IndexOf(mutation.Previous) + 1;
                foreach (var removed in mutation.Removed ?? [])
                {
                    foreach (var range in _ranges)
                    {
                        if (IsInclusiveAncestor(removed, range.StartNode))
                        {
                            range.StartNode = parent;
                            range.StartOffset = at;
                        }
                        else if (ReferenceEquals(range.StartNode, parent) && range.StartOffset > at)
                        {
                            range.StartOffset--;
                        }

                        if (IsInclusiveAncestor(removed, range.EndNode))
                        {
                            range.EndNode = parent;
                            range.EndOffset = at;
                        }
                        else if (ReferenceEquals(range.EndNode, parent) && range.EndOffset > at)
                        {
                            range.EndOffset--;
                        }
                    }
                }

                if (mutation.Added is { Count: > 0 } added)
                {
                    var index = siblings.IndexOf(added[0]);
                    if (index >= 0)
                    {
                        foreach (var range in _ranges)
                        {
                            if (ReferenceEquals(range.StartNode, parent) && range.StartOffset > index)
                                range.StartOffset += added.Count;
                            if (ReferenceEquals(range.EndNode, parent) && range.EndOffset > index)
                                range.EndOffset += added.Count;
                        }
                    }
                }

                break;
            }

            case ScriptMutationKind.CharacterData when !_dataReplacedByRange:
            {
                // data = ... replaces all of it.
                var oldLength = mutation.OldValue?.Length ?? 0;
                var newLength = mutation.Target.Data?.Length ?? 0;
                foreach (var range in _ranges)
                {
                    if (ReferenceEquals(range.StartNode, mutation.Target))
                        range.StartOffset = range.StartOffset > 0 && range.StartOffset <= oldLength ? 0 : range.StartOffset > oldLength ? range.StartOffset + newLength - oldLength : range.StartOffset;
                    if (ReferenceEquals(range.EndNode, mutation.Target))
                        range.EndOffset = range.EndOffset > 0 && range.EndOffset <= oldLength ? 0 : range.EndOffset > oldLength ? range.EndOffset + newLength - oldLength : range.EndOffset;
                }

                break;
            }
        }
    }

    private ScriptNode CloneForRange(ScriptNode node, bool deep)
    {
        var copy = node.CloneNode(deep);
        CopyNamespaces(node, copy, deep);
        return copy;
    }

    // The extract and clone algorithm, which delete share: the fragment holds what the range covers.
    private ScriptNode ExtractOrClone(RangeObject range, bool extract)
    {
        var fragment = range.StartNode.OwnerDocument.CreateEmptyFragment();
        if (range.Collapsed)
            return fragment;

        var (sn, so, en, eo) = (range.StartNode, range.StartOffset, range.EndNode, range.EndOffset);
        if (ReferenceEquals(sn, en) && IsCharacterData(sn))
        {
            var clone = CloneForRange(sn, false);
            clone.Data = sn.Data![so..eo];
            fragment.AppendChild(clone);
            if (extract)
                ReplaceData(sn, so, eo - so, string.Empty);

            return fragment;
        }

        var common = sn;
        while (!IsInclusiveAncestor(common, en))
            common = common.Parent!;

        ScriptNode? firstPartial = IsInclusiveAncestor(sn, en) ? null : common.Children.First(c => IsPartiallyContained(range, c));
        ScriptNode? lastPartial = IsInclusiveAncestor(en, sn) ? null : common.Children.Last(c => IsPartiallyContained(range, c));
        var contained = common.Children.Where(c => IsContained(range, c)).ToList();
        if (contained.Any(c => c.Kind == ScriptNodeKind.DocumentType))
            throw DomError("HierarchyRequestError", "The range contains a document type.");

        ScriptNode newNode;
        int newOffset;
        if (IsInclusiveAncestor(sn, en))
        {
            (newNode, newOffset) = (sn, so);
        }
        else
        {
            var reference = sn;
            while (!IsInclusiveAncestor(reference.Parent!, en))
                reference = reference.Parent!;

            newNode = reference.Parent!;
            newOffset = IndexOf(reference) + 1;
        }

        if (firstPartial is not null && IsCharacterData(firstPartial))
        {
            var clone = CloneForRange(sn, false);
            clone.Data = sn.Data![so..];
            fragment.AppendChild(clone);
            if (extract)
                ReplaceData(sn, so, LengthOf(sn) - so, string.Empty);
        }
        else if (firstPartial is not null)
        {
            var clone = CloneForRange(firstPartial, false);
            fragment.AppendChild(clone);
            var sub = new RangeObject(Engine, RangePrototype, sn) { StartNode = sn, StartOffset = so, EndNode = firstPartial, EndOffset = LengthOf(firstPartial) };
            clone.AppendChild(ExtractOrClone(sub, extract));
        }

        foreach (var child in contained)
            fragment.AppendChild(extract ? child : CloneForRange(child, true));

        if (lastPartial is not null && IsCharacterData(lastPartial))
        {
            var clone = CloneForRange(en, false);
            clone.Data = en.Data![..eo];
            fragment.AppendChild(clone);
            if (extract)
                ReplaceData(en, 0, eo, string.Empty);
        }
        else if (lastPartial is not null)
        {
            var clone = CloneForRange(lastPartial, false);
            fragment.AppendChild(clone);
            var sub = new RangeObject(Engine, RangePrototype, lastPartial) { StartNode = lastPartial, StartOffset = 0, EndNode = en, EndOffset = eo };
            clone.AppendChild(ExtractOrClone(sub, extract));
        }

        if (extract)
        {
            range.StartNode = range.EndNode = newNode;
            range.StartOffset = range.EndOffset = newOffset;
        }

        return fragment;
    }

    private string RangeText(RangeObject range)
    {
        var text = new StringBuilder();
        if (ReferenceEquals(range.StartNode, range.EndNode) && range.StartNode.Kind == ScriptNodeKind.Text)
            return range.StartNode.Data![range.StartOffset..range.EndOffset];

        if (range.StartNode.Kind == ScriptNodeKind.Text)
            text.Append(range.StartNode.Data![range.StartOffset..]);

        var common = range.StartNode;
        while (!IsInclusiveAncestor(common, range.EndNode))
            common = common.Parent!;

        void Walk(ScriptNode node)
        {
            foreach (var child in node.Children)
            {
                if (child.Kind == ScriptNodeKind.Text && IsContained(range, child))
                    text.Append(child.Data);
                else if (child.Kind != ScriptNodeKind.Text)
                    Walk(child);
            }
        }

        Walk(common);
        if (range.EndNode.Kind == ScriptNodeKind.Text && !ReferenceEquals(range.StartNode, range.EndNode))
            text.Append(range.EndNode.Data![..range.EndOffset]);

        return text.ToString();
    }

    private void InstallRanges()
    {
        var objectPrototype = Engine.Intrinsics.Object.PrototypeObject;
        AbstractRangePrototype = new JsObject(Engine) { Prototype = objectPrototype };
        RangePrototype = new JsObject(Engine) { Prototype = AbstractRangePrototype };
        Tag(AbstractRangePrototype, "AbstractRange");
        Tag(RangePrototype, "Range");

        var a = AbstractRangePrototype;
        Js.Accessor(Engine, a, "startContainer", self => Wrap(Rg(self).StartNode));
        Js.Accessor(Engine, a, "startOffset", self => Js.Num(Rg(self).StartOffset));
        Js.Accessor(Engine, a, "endContainer", self => Wrap(Rg(self).EndNode));
        Js.Accessor(Engine, a, "endOffset", self => Js.Num(Rg(self).EndOffset));
        Js.Accessor(Engine, a, "collapsed", self => Js.Bool(Rg(self).Collapsed));

        var p = RangePrototype;
        Js.Accessor(Engine, p, "commonAncestorContainer", self =>
        {
            var range = Rg(self);
            var common = range.StartNode;
            while (!IsInclusiveAncestor(common, range.EndNode))
                common = common.Parent!;

            return Wrap(common);
        });

        Js.Method(Engine, p, "setStart", 2, (self, args) =>
        {
            SetBoundary(Rg(self), RangeNodeArg(args, 0), OffsetArg(Js.Arg(args, 1)), start: true);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "setEnd", 2, (self, args) =>
        {
            SetBoundary(Rg(self), RangeNodeArg(args, 0), OffsetArg(Js.Arg(args, 1)), start: false);
            return JsValue.Undefined;
        });
        foreach (var (name, start, after) in new[] { ("setStartBefore", true, false), ("setStartAfter", true, true), ("setEndBefore", false, false), ("setEndAfter", false, true) })
        {
            var (isStart, isAfter) = (start, after);
            Js.Method(Engine, p, name, 1, (self, args) =>
            {
                var node = RangeNodeArg(args, 0);
                var parent = node.Parent ?? throw DomError("InvalidNodeTypeError", "The node has no parent.");
                SetBoundary(Rg(self), parent, IndexOf(node) + (isAfter ? 1 : 0), isStart);
                return JsValue.Undefined;
            });
        }

        Js.Method(Engine, p, "collapse", 0, (self, args) =>
        {
            var range = Rg(self);
            if (args.Length > 0 && TypeConverter.ToBoolean(args[0]))
            {
                range.EndNode = range.StartNode;
                range.EndOffset = range.StartOffset;
            }
            else
            {
                range.StartNode = range.EndNode;
                range.StartOffset = range.EndOffset;
            }

            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "selectNode", 1, (self, args) =>
        {
            var node = RangeNodeArg(args, 0);
            var parent = node.Parent ?? throw DomError("InvalidNodeTypeError", "The node has no parent.");
            var range = Rg(self);
            var index = IndexOf(node);
            range.StartNode = range.EndNode = parent;
            range.StartOffset = index;
            range.EndOffset = index + 1;
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "selectNodeContents", 1, (self, args) =>
        {
            var node = RangeNodeArg(args, 0);
            if (node.Kind == ScriptNodeKind.DocumentType)
                throw DomError("InvalidNodeTypeError", "A document type cannot be a boundary.");

            var range = Rg(self);
            range.StartNode = range.EndNode = node;
            range.StartOffset = 0;
            range.EndOffset = LengthOf(node);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "compareBoundaryPoints", 2, (self, args) =>
        {
            var range = Rg(self);
            var how = (int)(TypeConverter.ToUint32(Js.Arg(args, 0)) & 0xFFFF);
            var other = Js.Arg(args, 1) as RangeObject ?? throw TypeError("The argument is not a Range.");
            if (how > 3)
                throw DomError("NotSupportedError", "The comparison type is not supported.");
            if (!ReferenceEquals(RootOf(range.StartNode), RootOf(other.StartNode)))
                throw DomError("WrongDocumentError", "The ranges are in different trees.");

            var (thisNode, thisOffset, otherNode, otherOffset) = how switch
            {
                0 => (range.StartNode, range.StartOffset, other.StartNode, other.StartOffset),
                1 => (range.EndNode, range.EndOffset, other.StartNode, other.StartOffset),
                2 => (range.EndNode, range.EndOffset, other.EndNode, other.EndOffset),
                _ => (range.StartNode, range.StartOffset, other.EndNode, other.EndOffset),
            };

            return Js.Num(ComparePoints(thisNode, thisOffset, otherNode, otherOffset));
        });
        Js.Method(Engine, p, "deleteContents", 0, (self, _) =>
        {
            var range = Rg(self);
            ExtractOrClone(range, extract: true);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "extractContents", 0, (self, _) => Wrap(ExtractOrClone(Rg(self), extract: true)));
        Js.Method(Engine, p, "cloneContents", 0, (self, _) => Wrap(ExtractOrClone(Rg(self), extract: false)));
        Js.Method(Engine, p, "insertNode", 1, (self, args) =>
        {
            var range = Rg(self);
            var node = RangeNodeArg(args, 0);
            var start = range.StartNode;
            if (start.Kind is ScriptNodeKind.ProcessingInstruction or ScriptNodeKind.Comment
                || (start.Kind == ScriptNodeKind.Text && start.Parent is null) || ReferenceEquals(node, start))
                throw DomError("HierarchyRequestError", "The node cannot be inserted here.");

            ScriptNode? reference = start.Kind == ScriptNodeKind.Text ? start : start.Children.ElementAtOrDefault(range.StartOffset);
            var parent = reference is null ? start : reference.Parent!;
            if (start.Kind == ScriptNodeKind.Text)
                reference = SplitTextNode(start, range.StartOffset);

            if (ReferenceEquals(node, reference))
                reference = reference!.NextSibling;

            node.Parent?.RemoveChild(node);
            var newOffset = reference is null ? LengthOf(parent) : IndexOf(reference);
            newOffset += node.Kind == ScriptNodeKind.Fragment ? LengthOf(node) : 1;
            Guard(() => parent.InsertBefore(node, reference));
            if (range.Collapsed)
            {
                range.EndNode = parent;
                range.EndOffset = newOffset;
            }

            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "surroundContents", 1, (self, args) =>
        {
            var range = Rg(self);
            var parent = RangeNodeArg(args, 0);
            var common = range.StartNode;
            while (!IsInclusiveAncestor(common, range.EndNode))
                common = common.Parent!;

            bool PartialNonText(ScriptNode node) =>
                node.Children.Any(c => (c.Kind != ScriptNodeKind.Text && IsPartiallyContained(range, c)) || (IsPartiallyContained(range, c) && PartialNonText(c)));

            if (range.StartNode.Kind != ScriptNodeKind.Text && IsPartiallyContained(range, range.StartNode) && !ReferenceEquals(range.StartNode, common)
                || range.EndNode.Kind != ScriptNodeKind.Text && IsPartiallyContained(range, range.EndNode) && !ReferenceEquals(range.EndNode, common)
                || common.Children.Any(c => c.Kind != ScriptNodeKind.Text && IsPartiallyContained(range, c)))
                throw DomError("InvalidStateError", "The range partially contains a node that is not text.");

            if (parent.Kind is ScriptNodeKind.Document or ScriptNodeKind.DocumentType or ScriptNodeKind.Fragment)
                throw DomError("InvalidNodeTypeError", "The node cannot be a parent.");

            var fragment = ExtractOrClone(range, extract: true);
            foreach (var child in parent.Children)
                parent.RemoveChild(child);

            InvokeMethod(self, "insertNode", Wrap(parent));
            parent.AppendChild(fragment);
            InvokeMethod(self, "selectNode", Wrap(parent));
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "cloneRange", 0, (self, _) =>
        {
            var range = Rg(self);
            var clone = new RangeObject(Engine, RangePrototype, range.StartNode) { StartNode = range.StartNode, StartOffset = range.StartOffset, EndNode = range.EndNode, EndOffset = range.EndOffset };
            _ranges.Add(clone);
            return clone;
        });
        Js.Method(Engine, p, "detach", 0, (_, _) => JsValue.Undefined);
        Js.Method(Engine, p, "isPointInRange", 2, (self, args) =>
        {
            var range = Rg(self);
            var node = RangeNodeArg(args, 0);
            var offset = OffsetArg(Js.Arg(args, 1));
            if (!ReferenceEquals(RootOf(node), RootOf(range.StartNode)))
                return JsBoolean.False;
            if (node.Kind == ScriptNodeKind.DocumentType)
                throw DomError("InvalidNodeTypeError", "A document type cannot be a boundary.");
            if (offset > LengthOf(node))
                throw DomError("IndexSizeError", "The offset is larger than the node's length.");

            return Js.Bool(ComparePoints(node, offset, range.StartNode, range.StartOffset) >= 0 && ComparePoints(node, offset, range.EndNode, range.EndOffset) <= 0);
        });
        Js.Method(Engine, p, "comparePoint", 2, (self, args) =>
        {
            var range = Rg(self);
            var node = RangeNodeArg(args, 0);
            var offset = OffsetArg(Js.Arg(args, 1));
            if (!ReferenceEquals(RootOf(node), RootOf(range.StartNode)))
                throw DomError("WrongDocumentError", "The node is in another tree.");
            if (node.Kind == ScriptNodeKind.DocumentType)
                throw DomError("InvalidNodeTypeError", "A document type cannot be a boundary.");
            if (offset > LengthOf(node))
                throw DomError("IndexSizeError", "The offset is larger than the node's length.");

            if (ComparePoints(node, offset, range.StartNode, range.StartOffset) < 0)
                return Js.Num(-1);

            return Js.Num(ComparePoints(node, offset, range.EndNode, range.EndOffset) > 0 ? 1 : 0);
        });
        Js.Method(Engine, p, "intersectsNode", 1, (self, args) =>
        {
            var range = Rg(self);
            var node = RangeNodeArg(args, 0);
            if (!ReferenceEquals(RootOf(node), RootOf(range.StartNode)))
                return JsBoolean.False;

            var parent = node.Parent;
            if (parent is null)
                return JsBoolean.True;

            var offset = IndexOf(node);
            return Js.Bool(ComparePoints(parent, offset, range.EndNode, range.EndOffset) < 0 && ComparePoints(parent, offset + 1, range.StartNode, range.StartOffset) > 0);
        });
        Js.Method(Engine, p, "toString", 0, (self, _) => Js.Str(RangeText(Rg(self))));
        Js.Method(Engine, p, "createContextualFragment", 1, (self, args) =>
            Wrap(Rg(self).StartNode.OwnerDocument.CreateFragment(Js.AsText(Js.Arg(args, 0)))));

        var constructor = Js.Constructor(Engine, "Range", RangePrototype, 0, (_, _, _) => MakeRange(Document.Root));
        foreach (var (name, value) in new[] { ("START_TO_START", 0), ("START_TO_END", 1), ("END_TO_END", 2), ("END_TO_START", 3) })
        {
            constructor.FastSetProperty(name, new PropertyDescriptor(Js.Num(value), PropertyFlag.AllForbidden));
            RangePrototype.FastSetProperty(name, new PropertyDescriptor(Js.Num(value), PropertyFlag.AllForbidden));
        }

        Define(Engine.Global, "Range", constructor);
        Define(Engine.Global, "AbstractRange", Js.Constructor(Engine, "AbstractRange", AbstractRangePrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));
        Js.Method(Engine, DocumentPrototype, "createRange", 0, (self, _) => MakeRange(D(self).Root));
    }

    private ObjectInstance SelectionPrototype = null!;
    private ObjectInstance? _selection;

    private sealed class SelectionObject : ObjectInstance
    {
        public SelectionObject(Engine engine, ObjectInstance prototype) : base(engine)
        {
            Prototype = prototype;
        }

        public RangeObject? Range { get; set; }

        public bool Backward { get; set; }
    }

    private void InstallSelection()
    {
        SelectionPrototype = new JsObject(Engine) { Prototype = Engine.Intrinsics.Object.PrototypeObject };
        Tag(SelectionPrototype, "Selection");
        var p = SelectionPrototype;
        SelectionObject Sel(JsValue self) => self as SelectionObject ?? throw TypeError("Illegal invocation");

        bool InDocument(ScriptNode node) => ReferenceEquals(RootOf(node), Document.Root);

        void Point(JsValue[] args, int nodeIndex, out ScriptNode node, out int offset)
        {
            node = RangeNodeArg(args, nodeIndex);
            offset = OffsetArg(Js.Arg(args, nodeIndex + 1));
            if (node.Kind == ScriptNodeKind.DocumentType)
                throw DomError("InvalidNodeTypeError", "A document type cannot be a boundary.");
            if (offset > LengthOf(node))
                throw DomError("IndexSizeError", "The offset is larger than the node's length.");
        }

        void SetRange(SelectionObject selection, ScriptNode anchor, int anchorOffset, ScriptNode focus, int focusOffset)
        {
            var backward = ComparePoints(anchor, anchorOffset, focus, focusOffset) > 0;
            var range = new RangeObject(Engine, RangePrototype, anchor);
            _ranges.Add(range);
            if (backward)
                (range.StartNode, range.StartOffset, range.EndNode, range.EndOffset) = (focus, focusOffset, anchor, anchorOffset);
            else
                (range.StartNode, range.StartOffset, range.EndNode, range.EndOffset) = (anchor, anchorOffset, focus, focusOffset);

            if (selection.Range is { } old)
                _ranges.Remove(old);

            selection.Range = range;
            selection.Backward = backward;
        }

        Js.Accessor(Engine, p, "rangeCount", self => Js.Num(Sel(self).Range is null ? 0 : 1));
        Js.Accessor(Engine, p, "isCollapsed", self => Js.Bool(Sel(self).Range?.Collapsed ?? true));
        Js.Accessor(Engine, p, "type", self => Js.Str(Sel(self).Range is not { } r ? "None" : r.Collapsed ? "Caret" : "Range"));
        Js.Accessor(Engine, p, "direction", self => Js.Str(Sel(self).Range is not { } r || r.Collapsed ? "none" : Sel(self).Backward ? "backward" : "forward"));
        Js.Accessor(Engine, p, "anchorNode", self => Sel(self).Range is { } r ? Wrap(Sel(self).Backward ? r.EndNode : r.StartNode) : JsValue.Null);
        Js.Accessor(Engine, p, "anchorOffset", self => Js.Num(Sel(self).Range is { } r ? (Sel(self).Backward ? r.EndOffset : r.StartOffset) : 0));
        Js.Accessor(Engine, p, "focusNode", self => Sel(self).Range is { } r ? Wrap(Sel(self).Backward ? r.StartNode : r.EndNode) : JsValue.Null);
        Js.Accessor(Engine, p, "focusOffset", self => Js.Num(Sel(self).Range is { } r ? (Sel(self).Backward ? r.StartOffset : r.EndOffset) : 0));
        Js.Method(Engine, p, "getRangeAt", 1, (self, args) =>
            TypeConverter.ToUint32(Js.Arg(args, 0)) == 0 && Sel(self).Range is { } r ? r : throw DomError("IndexSizeError", "The index is out of range."));
        Js.Method(Engine, p, "addRange", 1, (self, args) =>
        {
            var selection = Sel(self);
            var range = Js.Arg(args, 0) as RangeObject ?? throw TypeError("The argument is not a Range.");
            if (selection.Range is null && InDocument(range.StartNode))
            {
                selection.Range = range;
                selection.Backward = false;
            }

            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "removeRange", 1, (self, args) =>
        {
            var selection = Sel(self);
            if (!ReferenceEquals(selection.Range, Js.Arg(args, 0)))
                throw DomError("NotFoundError", "The range is not in the selection.");

            selection.Range = null;
            return JsValue.Undefined;
        });
        foreach (var name in new[] { "removeAllRanges", "empty" })
        {
            Js.Method(Engine, p, name, 0, (self, _) =>
            {
                Sel(self).Range = null;
                return JsValue.Undefined;
            });
        }

        foreach (var name in new[] { "collapse", "setPosition" })
        {
            Js.Method(Engine, p, name, 2, (self, args) =>
            {
                var selection = Sel(self);
                if (Js.IsNullish(Js.Arg(args, 0)))
                {
                    selection.Range = null;
                    return JsValue.Undefined;
                }

                Point(args, 0, out var node, out var offset);
                if (InDocument(node))
                    SetRange(selection, node, offset, node, offset);

                return JsValue.Undefined;
            });
        }

        Js.Method(Engine, p, "collapseToStart", 0, (self, _) =>
        {
            var selection = Sel(self);
            var r = selection.Range ?? throw DomError("InvalidStateError", "There is no range.");
            SetRange(selection, r.StartNode, r.StartOffset, r.StartNode, r.StartOffset);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "collapseToEnd", 0, (self, _) =>
        {
            var selection = Sel(self);
            var r = selection.Range ?? throw DomError("InvalidStateError", "There is no range.");
            SetRange(selection, r.EndNode, r.EndOffset, r.EndNode, r.EndOffset);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "extend", 2, (self, args) =>
        {
            var selection = Sel(self);
            Point(args, 0, out var node, out var offset);
            var r = selection.Range ?? throw DomError("InvalidStateError", "There is no range.");
            if (!InDocument(node))
                return JsValue.Undefined;

            var (anchorNode, anchorOffset) = selection.Backward ? (r.EndNode, r.EndOffset) : (r.StartNode, r.StartOffset);
            SetRange(selection, anchorNode, anchorOffset, node, offset);
            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "selectAllChildren", 1, (self, args) =>
        {
            var node = RangeNodeArg(args, 0);
            if (node.Kind == ScriptNodeKind.DocumentType)
                throw DomError("InvalidNodeTypeError", "A document type cannot be a boundary.");

            if (InDocument(node))
                SetRange(Sel(self), node, 0, node, LengthOf(node));

            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "setBaseAndExtent", 4, (self, args) =>
        {
            Point(args, 0, out var anchor, out var anchorOffset);
            Point(args, 2, out var focus, out var focusOffset);
            if (InDocument(anchor) && InDocument(focus))
                SetRange(Sel(self), anchor, anchorOffset, focus, focusOffset);

            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "containsNode", 2, (self, args) =>
        {
            var node = RangeNodeArg(args, 0);
            if (Sel(self).Range is not { } r || !ReferenceEquals(RootOf(node), RootOf(r.StartNode)))
                return JsBoolean.False;

            if (TypeConverter.ToBoolean(Js.Arg(args, 1)))
                return Js.Bool(ComparePoints(node, 0, r.EndNode, r.EndOffset) < 0 && ComparePoints(node, LengthOf(node), r.StartNode, r.StartOffset) > 0);

            return Js.Bool(IsContained(r, node));
        });
        Js.Method(Engine, p, "deleteFromDocument", 0, (self, _) =>
        {
            if (Sel(self).Range is { } r)
                ExtractOrClone(r, extract: true);

            return JsValue.Undefined;
        });
        Js.Method(Engine, p, "toString", 0, (self, _) => Js.Str(Sel(self).Range is { } r ? RangeText(r) : string.Empty));

        Define(Engine.Global, "Selection", Js.Constructor(Engine, "Selection", SelectionPrototype, 0, (_, _, _) => throw TypeError("Illegal constructor")));
        Func<JsValue, JsValue[], JsValue> get = (_, _) => _selection ??= new SelectionObject(Engine, SelectionPrototype);
        Js.Method(Engine, Engine.Global, "getSelection", 0, get);
        Js.Method(Engine, DocumentPrototype, "getSelection", 0, (self, _) =>
            ReferenceEquals(D(self), Document) ? get(self, []) : JsValue.Null);
    }

    private RangeObject MakeRange(ScriptNode node)
    {
        var range = new RangeObject(Engine, RangePrototype, node);
        _ranges.Add(range);
        return range;
    }

    private JsValue InvokeMethod(JsValue self, string name, params JsValue[] args) =>
        ((ObjectInstance)self).Get(name).Call(self, args);
}
