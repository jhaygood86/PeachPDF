using System.Collections.Generic;

namespace PeachPDF.SourceGenerators.Json
{
    internal enum JsonValueKind
    {
        Object,
        Array,
        String,
        Number,
        True,
        False,
        Null
    }

    /// <summary>
    /// A single parsed JSON node. Deliberately not a record (this project targets netstandard2.0,
    /// where init-only accessors need an IsExternalInit polyfill this repo doesn't otherwise need) -
    /// plain read-only fields set once by <see cref="JsonParser"/> are enough for a write-once,
    /// read-many generator input.
    /// </summary>
    internal sealed class JsonValue
    {
        public JsonValueKind Kind { get; }
        public string? StringValue { get; }
        public double NumberValue { get; }
        public IReadOnlyList<JsonValue> ArrayItems { get; }
        public IReadOnlyList<JsonMember> ObjectMembers { get; }

        /// <summary>1-based line/column of this value's first character, for diagnostic locations.</summary>
        public int Line { get; }
        public int Column { get; }

        private static readonly IReadOnlyList<JsonValue> EmptyArray = new JsonValue[0];
        private static readonly IReadOnlyList<JsonMember> EmptyObject = new JsonMember[0];

        public JsonValue(JsonValueKind kind, int line, int column, string? stringValue = null, double numberValue = 0,
            IReadOnlyList<JsonValue>? arrayItems = null, IReadOnlyList<JsonMember>? objectMembers = null)
        {
            Kind = kind;
            Line = line;
            Column = column;
            StringValue = stringValue;
            NumberValue = numberValue;
            ArrayItems = arrayItems ?? EmptyArray;
            ObjectMembers = objectMembers ?? EmptyObject;
        }

        public bool IsNull => Kind == JsonValueKind.Null;
        public bool IsString => Kind == JsonValueKind.String;
        public bool IsObject => Kind == JsonValueKind.Object;
        public bool IsArray => Kind == JsonValueKind.Array;
        public bool IsBool => Kind == JsonValueKind.True || Kind == JsonValueKind.False;
        public bool BoolValue => Kind == JsonValueKind.True;

        public bool TryGetProperty(string name, out JsonValue value)
        {
            foreach (var member in ObjectMembers)
            {
                if (member.Name == name)
                {
                    value = member.Value;
                    return true;
                }
            }

            value = null!;
            return false;
        }

        public bool HasProperty(string name) => TryGetProperty(name, out _);
    }

    internal readonly struct JsonMember
    {
        public string Name { get; }
        public JsonValue Value { get; }

        public JsonMember(string name, JsonValue value)
        {
            Name = name;
            Value = value;
        }
    }
}
