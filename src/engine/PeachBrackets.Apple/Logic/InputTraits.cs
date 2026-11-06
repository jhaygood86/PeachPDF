namespace PeachBrackets.Apple.Logic;

/// <summary>The keyboard a kind of field asks for.</summary>
internal enum AppleKeyboard
{
    Default,
    EmailAddress,
    Url,
    PhonePad,
    DecimalPad,
}

/// <summary>What the on-screen keyboard is told about a text field, so it shows the right keys and does not echo a password.</summary>
/// <param name="Keyboard">which keys</param>
/// <param name="Secure">whether typed characters are hidden</param>
/// <param name="ReturnKey">the label of the return key: <c>default</c>, <c>search</c>, <c>go</c> or <c>done</c></param>
/// <param name="Autocorrect">whether the system corrects and capitalizes what is typed (not for addresses, passwords and the like)</param>
internal readonly record struct AppleInputTraits(AppleKeyboard Keyboard, bool Secure, string ReturnKey, bool Autocorrect)
{
    /// <summary>The traits for an <c>&lt;input&gt;</c> of a given <c>type</c>, or a text area.</summary>
    public static AppleInputTraits For(string type) => type switch
    {
        "password" => new(AppleKeyboard.Default, true, "done", false),
        "email" => new(AppleKeyboard.EmailAddress, false, "done", false),
        "url" => new(AppleKeyboard.Url, false, "go", false),
        "tel" => new(AppleKeyboard.PhonePad, false, "done", false),
        "number" => new(AppleKeyboard.DecimalPad, false, "done", false),
        "search" => new(AppleKeyboard.Default, false, "search", true),
        "textarea" => new(AppleKeyboard.Default, false, "default", true),
        _ => new(AppleKeyboard.Default, false, "done", true),
    };
}
