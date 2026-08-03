; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
PPG001 | PeachPDF.SourceGenerators | Error | css-properties.json is malformed
PPG002 | PeachPDF.SourceGenerators | Error | Unknown cssDataType
PPG003 | PeachPDF.SourceGenerators | Error | Duplicate property name
PPG004 | PeachPDF.SourceGenerators | Error | "keyword" data type requires supportedValues
PPG005 | PeachPDF.SourceGenerators | Error | Binding has no propertyPath and no customSetter
PPG006 | PeachPDF.SourceGenerators | Error | initialValue must be declared explicitly
PPG007 | PeachPDF.SourceGenerators | Error | aliasOf target missing or initialValue mismatch
PPG008 | PeachPDF.SourceGenerators | Error | Custom setter code must not contain return
PPG009 | PeachPDF.SourceGenerators | Warning | supportedValues is ignored by this data type
PPG010 | PeachPDF.SourceGenerators | Error | svg.propertyPath does not match SvgElement's real shape
PPG011 | PeachPDF.SourceGenerators | Error | html.propertyPath does not match CssBox's real shape
PPG012 | PeachPDF.SourceGenerators | Error | Unknown ComputedStyleAreas record
PPG014 | PeachPDF.SourceGenerators | Error | Invalid logical-category entry
