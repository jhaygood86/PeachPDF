namespace PeachBrackets.Updates;

/// <summary>What a form control holds (<see cref="HtmlDocument.GetFormState"/>).</summary>
/// <param name="Type">the kind of control: the <c>type</c> of an <c>&lt;input&gt;</c> in lower case (<c>text</c>, <c>checkbox</c>, <c>range</c>...) or <c>select</c></param>
/// <param name="Name">its <c>name</c>, or null</param>
/// <param name="Value">the text of a field, the number of a range, the value of a button or the chosen option of a drop-down; empty when it has none</param>
/// <param name="Checked">whether a check box or radio button is checked</param>
/// <param name="Disabled">whether the control is disabled</param>
public readonly record struct FormControlState(string Type, string? Name, string Value, bool Checked, bool Disabled);
