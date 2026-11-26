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
        Engine.Invoke(Engine.Evaluate(FormScripts.Source), JsValue.Undefined, [native, Engine.Global]);
        Js.Method(Engine, native, "fire", 4, (_, args) =>
        {
            var ev = CreateEvent(Js.AsText(Js.Arg(args, 1)), Jint.Runtime.TypeConverter.ToBoolean(Js.Arg(args, 2)), Jint.Runtime.TypeConverter.ToBoolean(Js.Arg(args, 3)));
            ev.IsTrusted = true;
            return Js.Bool(DispatchEvent((TargetObject)Js.Arg(args, 0), ev));
        });
        Engine.Invoke(Engine.Evaluate(PopoverScripts.Source), JsValue.Undefined, [native, Engine.Global]);
    }
}
