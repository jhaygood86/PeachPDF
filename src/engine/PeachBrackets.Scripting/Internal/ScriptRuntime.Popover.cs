using Jint.Native;

namespace PeachBrackets.Scripting.Internal;

internal sealed partial class ScriptRuntime
{
    // The Popover API: script over the DOM, with the one native it cannot do itself (the showing state that :popover-open matches).
    private void InstallPopover()
    {
        var native = Js.NewObject(Engine);
        Js.Method(Engine, native, "setOpen", 2, (_, args) =>
        {
            Sn(Js.Arg(args, 0)).IsPopoverOpen = Jint.Runtime.TypeConverter.ToBoolean(Js.Arg(args, 1));
            return JsValue.Undefined;
        });
        Engine.Invoke(Engine.Evaluate(PopoverScripts.Focus), JsValue.Undefined, [Engine.Global]);
        Engine.Invoke(Engine.Evaluate(PopoverScripts.Source), JsValue.Undefined, [native, Engine.Global]);
    }
}
