using System;
using System.Collections.Generic;
using System.Linq;
using PeachPDF.SourceGenerators.Json;

namespace PeachPDF.SourceGenerators.Model
{
    /// <summary>
    /// Turns a parsed <see cref="JsonValue"/> tree (already syntactically valid JSON — see
    /// <see cref="JsonParser"/>) into <see cref="PropertyEntry"/> objects, reporting every
    /// schema-shape problem (PPG001-PPG009, PPG012, PPG014) it finds along the way. Symbol-based
    /// checks against the real <c>CssBox</c>/<c>SvgElement</c> shape (PPG010/PPG011) need a
    /// <see cref="Microsoft.CodeAnalysis.Compilation"/> and are done separately by the generator
    /// itself once this model is built — see <c>CssPropertyGenerator</c>.
    /// </summary>
    internal static class PropertyModelParser
    {
        /// <summary>
        /// The <c>ComputedStyleAreas.cs</c> record names a valid <c>html.area</c> may reference.
        /// Hand-maintained on purpose (PPG012 is a shape check, not full symbol resolution) - kept
        /// in sync with that file, which changes rarely (adding a 17th area is itself a notable event).
        /// </summary>
        private static readonly HashSet<string> KnownAreas = new(StringComparer.Ordinal)
        {
            "BorderArea", "VisualEffectsArea", "GeneratedContentArea", "BoxModelArea", "BreakArea",
            "PaginationArea", "BackgroundArea", "DisplayPositioningArea", "TableArea", "TextArea",
            "TextDecorationArea", "FontArea", "ListArea", "FlexArea", "GridArea", "MultiColumnArea",
        };

        public static (IReadOnlyList<PropertyEntry> Entries, IReadOnlyList<ModelDiagnostic> Diagnostics) Parse(JsonValue root)
        {
            var diagnostics = new List<ModelDiagnostic>();
            var entries = new List<PropertyEntry>();

            if (!root.IsObject || !root.TryGetProperty("properties", out var propertiesValue) || !propertiesValue.IsArray)
            {
                diagnostics.Add(Error(DiagnosticCode.JsonMalformed,
                    "The document root must be an object with a \"properties\" array.", root));
                return (entries, diagnostics);
            }

            foreach (var item in propertiesValue.ArrayItems)
            {
                var entry = ParseEntry(item, diagnostics);
                if (entry is not null) entries.Add(entry);
            }

            ValidateCrossEntry(entries, diagnostics);

            return (entries, diagnostics);
        }

        private static void ValidateCrossEntry(List<PropertyEntry> entries, List<ModelDiagnostic> diagnostics)
        {
            var byName = new Dictionary<string, PropertyEntry>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                if (byName.ContainsKey(entry.Name))
                {
                    diagnostics.Add(new ModelDiagnostic(DiagnosticCode.DuplicateName,
                        $"Duplicate property name \"{entry.Name}\".", entry.Line, entry.Column));
                    continue;
                }

                byName[entry.Name] = entry;
            }

            foreach (var entry in entries)
            {
                if (entry.AliasOf is null) continue;

                if (!byName.TryGetValue(entry.AliasOf, out var target))
                {
                    diagnostics.Add(new ModelDiagnostic(DiagnosticCode.AliasMismatch,
                        $"\"{entry.Name}\" declares aliasOf \"{entry.AliasOf}\", which is not a known property.",
                        entry.Line, entry.Column));
                    continue;
                }

                if (!InitialValuesEqual(entry, target))
                {
                    diagnostics.Add(new ModelDiagnostic(DiagnosticCode.AliasMismatch,
                        $"\"{entry.Name}\" and its alias target \"{entry.AliasOf}\" must declare the same initialValue " +
                        "(a legacy alias and the property it aliases must never disagree on their default, or reverting " +
                        "one to `initial` would pick a winner at random).",
                        entry.Line, entry.Column));
                }
            }
        }

        private static bool InitialValuesEqual(PropertyEntry a, PropertyEntry b) =>
            a.InitialValueKind == b.InitialValueKind && a.InitialValueText == b.InitialValueText;

        private static PropertyEntry? ParseEntry(JsonValue json, List<ModelDiagnostic> diagnostics)
        {
            if (!json.IsObject)
            {
                diagnostics.Add(Error(DiagnosticCode.JsonMalformed, "Each entry in \"properties\" must be an object.", json));
                return null;
            }

            if (!json.TryGetProperty("name", out var nameValue) || !nameValue.IsString)
            {
                diagnostics.Add(Error(DiagnosticCode.JsonMalformed, "Entry is missing a string \"name\".", json));
                return null;
            }

            var name = nameValue.StringValue!;

            if (!json.TryGetProperty("inherited", out var inheritedValue) || !inheritedValue.IsBool)
            {
                diagnostics.Add(Error(DiagnosticCode.JsonMalformed, $"\"{name}\" is missing a boolean \"inherited\".", json));
                return null;
            }

            var inherited = inheritedValue.BoolValue;

            if (!json.TryGetProperty("initialValue", out var initialValueJson))
            {
                diagnostics.Add(Error(DiagnosticCode.InitialValueMissing,
                    $"\"{name}\" must declare \"initialValue\" explicitly — use null to state \"no initial value, by design\".", json));
                return null;
            }

            var (initialValueKind, initialValueText, initialValueOk) = ParseInitialValue(name, initialValueJson, diagnostics);
            if (!initialValueOk) return null;

            var category = ParseCategory(name, json, diagnostics);

            if (!json.TryGetProperty("cssDataType", out var cssDataTypeJson))
            {
                diagnostics.Add(Error(DiagnosticCode.UnknownDataType, $"\"{name}\" is missing \"cssDataType\".", json));
                return null;
            }

            var dataTypes = ParseDataTypes(name, cssDataTypeJson, diagnostics);

            IReadOnlyList<string>? supportedValues = null;
            if (json.TryGetProperty("supportedValues", out var supportedValuesJson))
            {
                if (!supportedValuesJson.IsArray)
                {
                    diagnostics.Add(Error(DiagnosticCode.JsonMalformed, $"\"{name}\".supportedValues must be an array of strings.", json));
                }
                else
                {
                    supportedValues = supportedValuesJson.ArrayItems.Select(v => v.StringValue ?? "").ToList();
                }
            }

            var hasKeywordType = dataTypes.Any(d => d.Kind == DataTypeKind.Keyword);
            if (hasKeywordType && supportedValues is null)
            {
                diagnostics.Add(Error(DiagnosticCode.KeywordMissingSupportedValues,
                    $"\"{name}\" declares a \"keyword\" data type but no \"supportedValues\".", json));
            }
            else if (!hasKeywordType && supportedValues is not null)
            {
                diagnostics.Add(Warning(DiagnosticCode.SupportedValuesIgnored,
                    $"\"{name}\" declares \"supportedValues\" but no data type in its union uses it.", json));
            }

            var keywordComparison = ParseKeywordComparison(json, "keywordComparison");
            var aliasOf = json.TryGetProperty("aliasOf", out var aliasOfJson) && aliasOfJson.IsString ? aliasOfJson.StringValue : null;
            var resolvesTo = ParseResolvesTo(json);

            IReadOnlyList<DataTypeSpec>? supportsDataTypes = null;
            IReadOnlyList<string>? supportsSupportedValues = null;
            KeywordComparison? supportsKeywordComparison = null;
            if (json.TryGetProperty("supportsDataType", out var supportsDataTypeJson))
            {
                supportsDataTypes = ParseDataTypes(name, supportsDataTypeJson, diagnostics);

                if (json.TryGetProperty("supportsSupportedValues", out var supportsSupportedValuesJson) && supportsSupportedValuesJson.IsArray)
                    supportsSupportedValues = supportsSupportedValuesJson.ArrayItems.Select(v => v.StringValue ?? "").ToList();

                if (json.TryGetProperty("supportsKeywordComparison", out _))
                    supportsKeywordComparison = ParseKeywordComparison(json, "supportsKeywordComparison");
            }

            if (category == PropertyCategory.Logical)
            {
                if (initialValueKind != InitialValueKind.None)
                {
                    diagnostics.Add(Error(DiagnosticCode.InvalidLogicalEntry,
                        $"\"{name}\" is a logical property but declares a non-null initialValue — logical scratch fields " +
                        "have no initial value of their own (the physical property they resolve into already has one).", json));
                }
            }
            else if (resolvesTo is not null)
            {
                diagnostics.Add(Error(DiagnosticCode.InvalidLogicalEntry,
                    $"\"{name}\" declares \"resolvesTo\" but is not category \"logical\".", json));
            }

            var html = ParseHtmlBinding(name, json, dataTypes, diagnostics);
            var svg = ParseSvgBinding(name, json, dataTypes, diagnostics);

            return new PropertyEntry(name, inherited, initialValueKind, initialValueText, category, dataTypes,
                supportedValues, keywordComparison, aliasOf, resolvesTo, html, svg, json.Line, json.Column,
                supportsDataTypes, supportsSupportedValues, supportsKeywordComparison);
        }

        private static (InitialValueKind Kind, string? Text, bool Ok) ParseInitialValue(string name, JsonValue json, List<ModelDiagnostic> diagnostics)
        {
            if (json.IsNull) return (InitialValueKind.None, null, true);
            if (json.IsString) return (InitialValueKind.Literal, json.StringValue, true);

            if (json.IsObject && json.TryGetProperty("expression", out var expressionValue) && expressionValue.IsString)
                return (InitialValueKind.Expression, expressionValue.StringValue, true);

            diagnostics.Add(Error(DiagnosticCode.JsonMalformed,
                $"\"{name}\".initialValue must be a string, null, or {{\"expression\": \"...\"}}.", json));
            return (InitialValueKind.None, null, false);
        }

        private static PropertyCategory ParseCategory(string name, JsonValue json, List<ModelDiagnostic> diagnostics)
        {
            if (!json.TryGetProperty("category", out var categoryValue)) return PropertyCategory.Normal;

            if (!categoryValue.IsString)
            {
                diagnostics.Add(Error(DiagnosticCode.JsonMalformed, $"\"{name}\".category must be a string.", json));
                return PropertyCategory.Normal;
            }

            switch (categoryValue.StringValue)
            {
                case "normal": return PropertyCategory.Normal;
                case "logical": return PropertyCategory.Logical;
                case "unsupported": return PropertyCategory.Unsupported;
                default:
                    diagnostics.Add(Error(DiagnosticCode.JsonMalformed,
                        $"\"{name}\".category \"{categoryValue.StringValue}\" is not one of normal/logical/unsupported.", json));
                    return PropertyCategory.Normal;
            }
        }

        private static KeywordComparison ParseKeywordComparison(JsonValue json, string fieldName)
        {
            if (!json.TryGetProperty(fieldName, out var value) || !value.IsString)
                return KeywordComparison.Ordinal;

            return value.StringValue switch
            {
                "ordinal-ignore-case" => KeywordComparison.OrdinalIgnoreCase,
                "invariant-ignore-case" => KeywordComparison.InvariantIgnoreCase,
                _ => KeywordComparison.Ordinal,
            };
        }

        private static LogicalTarget? ParseResolvesTo(JsonValue json)
        {
            if (!json.TryGetProperty("resolvesTo", out var value) || !value.IsObject) return null;

            value.TryGetProperty("group", out var group);
            value.TryGetProperty("axis", out var axis);
            value.TryGetProperty("side", out var side);

            return new LogicalTarget(group.StringValue ?? "", axis.StringValue ?? "", side.StringValue ?? "");
        }

        private static List<DataTypeSpec> ParseDataTypes(string name, JsonValue json, List<ModelDiagnostic> diagnostics)
        {
            var result = new List<DataTypeSpec>();

            if (json.IsArray)
            {
                foreach (var item in json.ArrayItems)
                {
                    var spec = ParseSingleDataType(name, item, diagnostics);
                    if (spec is not null) result.Add(spec);
                }
            }
            else
            {
                var spec = ParseSingleDataType(name, json, diagnostics);
                if (spec is not null) result.Add(spec);
            }

            return result;
        }

        private static DataTypeSpec? ParseSingleDataType(string name, JsonValue json, List<ModelDiagnostic> diagnostics)
        {
            if (json.IsString)
            {
                return json.StringValue switch
                {
                    "cssom" => DataTypeSpec.Simple(DataTypeKind.CssOm),
                    "unsupported" => DataTypeSpec.Simple(DataTypeKind.Unsupported),
                    "length" => DataTypeSpec.Simple(DataTypeKind.Length),
                    "color" => DataTypeSpec.Simple(DataTypeKind.Color),
                    "current-color" => DataTypeSpec.Simple(DataTypeKind.CurrentColor),
                    "transform" => DataTypeSpec.Simple(DataTypeKind.Transform),
                    "keyword" => DataTypeSpec.Simple(DataTypeKind.Keyword),
                    "integer" => DataTypeSpec.Simple(DataTypeKind.Integer),
                    "number" => DataTypeSpec.Simple(DataTypeKind.Number),
                    "svg-paint" => DataTypeSpec.Simple(DataTypeKind.SvgPaint),
                    "svg-opacity" => DataTypeSpec.Simple(DataTypeKind.SvgOpacity),
                    "svg-length" => DataTypeSpec.Simple(DataTypeKind.SvgLength),
                    "svg-length-list" => DataTypeSpec.Simple(DataTypeKind.SvgLengthList),
                    "svg-transform" => DataTypeSpec.Simple(DataTypeKind.SvgTransform),
                    "svg-reference" => DataTypeSpec.Simple(DataTypeKind.SvgReference),
                    _ => Unknown()
                };

                DataTypeSpec? Unknown()
                {
                    diagnostics.Add(Error(DiagnosticCode.UnknownDataType,
                        $"\"{name}\" declares an unknown cssDataType \"{json.StringValue}\".", json));
                    return null;
                }
            }

            if (json.IsObject && json.TryGetProperty("type", out var typeValue) && typeValue.IsString)
            {
                switch (typeValue.StringValue)
                {
                    case "integer":
                        double? min = json.TryGetProperty("min", out var minValue) && minValue.Kind == JsonValueKind.Number ? minValue.NumberValue : null;
                        double? max = json.TryGetProperty("max", out var maxValue) && maxValue.Kind == JsonValueKind.Number ? maxValue.NumberValue : null;
                        return new DataTypeSpec(DataTypeKind.Integer, min: min, max: max);

                    case "parsed":
                        json.TryGetProperty("converter", out var converterValue);
                        json.TryGetProperty("resultType", out var resultTypeValue);
                        json.TryGetProperty("typedValueType", out var typedValueTypeValue);
                        return new DataTypeSpec(DataTypeKind.Parsed,
                            converter: converterValue.StringValue, resultType: resultTypeValue.StringValue,
                            typedValueType: typedValueTypeValue.StringValue);

                    case "enum-keyword":
                        json.TryGetProperty("enumType", out var enumTypeValue);
                        json.TryGetProperty("keywordMap", out var keywordMapValue);
                        json.TryGetProperty("fallback", out var fallbackValue);
                        return new DataTypeSpec(DataTypeKind.EnumKeyword,
                            enumType: enumTypeValue.StringValue, keywordMap: keywordMapValue.StringValue, fallback: fallbackValue.StringValue);

                    case "keyword-or-value":
                        json.TryGetProperty("enumType", out var korvEnumTypeValue);
                        json.TryGetProperty("keywordMap", out var korvKeywordMapValue);
                        json.TryGetProperty("fallback", out var korvFallbackValue);
                        json.TryGetProperty("valueType", out var korvValueTypeValue);
                        return new DataTypeSpec(DataTypeKind.KeywordOrValue,
                            enumType: korvEnumTypeValue.StringValue, keywordMap: korvKeywordMapValue.StringValue,
                            fallback: korvFallbackValue.StringValue, valueType: korvValueTypeValue.StringValue);

                    default:
                        diagnostics.Add(Error(DiagnosticCode.UnknownDataType,
                            $"\"{name}\" declares an unknown cssDataType object shape \"{typeValue.StringValue}\".", json));
                        return null;
                }
            }

            diagnostics.Add(Error(DiagnosticCode.UnknownDataType, $"\"{name}\" declares a cssDataType that is neither a recognized string nor a recognized object shape.", json));
            return null;
        }

        private static HtmlBinding? ParseHtmlBinding(string name, JsonValue json, List<DataTypeSpec> dataTypes, List<ModelDiagnostic> diagnostics)
        {
            if (!json.TryGetProperty("html", out var htmlJson) || htmlJson.IsNull) return null;
            if (!htmlJson.IsObject)
            {
                diagnostics.Add(Error(DiagnosticCode.JsonMalformed, $"\"{name}\".html must be an object.", json));
                return null;
            }

            var propertyPath = GetOptionalString(htmlJson, "propertyPath");
            var csharpDataType = GetOptionalString(htmlJson, "csharpDataType");
            var area = GetOptionalString(htmlJson, "area");
            var hasGetter = !htmlJson.TryGetProperty("hasGetter", out var hasGetterValue) || hasGetterValue.Kind != JsonValueKind.False;
            var getterExpression = GetOptionalString(htmlJson, "getterExpression");
            var customValidator = GetOptionalString(htmlJson, "customValidator");
            var customSetter = GetOptionalString(htmlJson, "customSetter");
            var snapshot = !htmlJson.TryGetProperty("snapshot", out var snapshotValue) ? hasGetter : snapshotValue.Kind != JsonValueKind.False;

            var isUnsupported = dataTypes.Count == 1 && dataTypes[0].Kind == DataTypeKind.Unsupported;
            if (!isUnsupported && propertyPath is null && customSetter is null)
            {
                diagnostics.Add(Error(DiagnosticCode.BindingMissingTarget,
                    $"\"{name}\".html declares neither a propertyPath nor a customSetter.", json));
            }

            if (area is not null && !KnownAreas.Contains(area))
            {
                diagnostics.Add(Error(DiagnosticCode.UnknownArea, $"\"{name}\".html.area \"{area}\" is not a recognized ComputedStyleAreas record.", json));
            }

            if (customSetter is not null) CheckCustomCodeHasNoReturn(name, "html.customSetter", customSetter, json, diagnostics);

            return new HtmlBinding(propertyPath, csharpDataType, area, hasGetter, getterExpression, customValidator, customSetter, snapshot);
        }

        private static SvgBinding? ParseSvgBinding(string name, JsonValue json, List<DataTypeSpec> dataTypes, List<ModelDiagnostic> diagnostics)
        {
            if (!json.TryGetProperty("svg", out var svgJson) || svgJson.IsNull) return null;
            if (!svgJson.IsObject)
            {
                diagnostics.Add(Error(DiagnosticCode.JsonMalformed, $"\"{name}\".svg must be an object.", json));
                return null;
            }

            var propertyPath = GetOptionalString(svgJson, "propertyPath");
            var csharpDataType = GetOptionalString(svgJson, "csharpDataType");
            var inheritedFrom = GetOptionalString(svgJson, "inheritedFrom");
            var customValidator = GetOptionalString(svgJson, "customValidator");
            var customSetter = GetOptionalString(svgJson, "customSetter");

            SvgInvalidBehavior invalidBehavior = SvgInvalidBehavior.LeaveUnset;
            if (svgJson.TryGetProperty("invalidBehavior", out var invalidBehaviorValue) && invalidBehaviorValue.IsString)
            {
                invalidBehavior = invalidBehaviorValue.StringValue switch
                {
                    "inherit" => SvgInvalidBehavior.Inherit,
                    "initial" => SvgInvalidBehavior.Initial,
                    "leave-unset" => SvgInvalidBehavior.LeaveUnset,
                    _ => SvgInvalidBehavior.LeaveUnset,
                };
            }
            else
            {
                diagnostics.Add(Error(DiagnosticCode.JsonMalformed, $"\"{name}\".svg is missing required \"invalidBehavior\".", json));
            }

            var applyIn = SvgApplyIn.Common;
            if (svgJson.TryGetProperty("applyIn", out var applyInValue) && applyInValue.IsString && applyInValue.StringValue == "manual")
                applyIn = SvgApplyIn.Manual;

            var isUnsupported = dataTypes.Count == 1 && dataTypes[0].Kind == DataTypeKind.Unsupported;
            if (!isUnsupported && propertyPath is null && customSetter is null && inheritedFrom is null)
            {
                diagnostics.Add(Error(DiagnosticCode.BindingMissingTarget,
                    $"\"{name}\".svg declares no propertyPath, customSetter, or inheritedFrom.", json));
            }

            if (customSetter is not null) CheckCustomCodeHasNoReturn(name, "svg.customSetter", customSetter, json, diagnostics);

            return new SvgBinding(propertyPath, csharpDataType, inheritedFrom, invalidBehavior, applyIn, customValidator, customSetter);
        }

        private static void CheckCustomCodeHasNoReturn(string name, string field, string code, JsonValue json, List<ModelDiagnostic> diagnostics)
        {
            var i = 0;
            while ((i = code.IndexOf("return", i, StringComparison.Ordinal)) >= 0)
            {
                var beforeOk = i == 0 || !char.IsLetterOrDigit(code[i - 1]) && code[i - 1] != '_';
                var afterIndex = i + "return".Length;
                var afterOk = afterIndex >= code.Length || (!char.IsLetterOrDigit(code[afterIndex]) && code[afterIndex] != '_');

                if (beforeOk && afterOk)
                {
                    diagnostics.Add(Error(DiagnosticCode.CustomSetterHasReturn,
                        $"\"{name}\".{field} contains a `return` statement — custom setter code is statements only, " +
                        "run after validation already passed; success is implicit.", json));
                    return;
                }

                i = afterIndex;
            }
        }

        private static string? GetOptionalString(JsonValue json, string property) =>
            json.TryGetProperty(property, out var value) && value.IsString ? value.StringValue : null;

        private static ModelDiagnostic Error(DiagnosticCode code, string message, JsonValue at) =>
            new(code, message, at.Line, at.Column);

        private static ModelDiagnostic Warning(DiagnosticCode code, string message, JsonValue at) =>
            new(code, message, at.Line, at.Column);
    }
}
