using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Kopiraiter.Services;

internal sealed class NativeTextInjector
{
    public OperationResult<int> Inject(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return OperationResult<int>.Fail("Текст для ввода не задан.");
        }

        var inputs = BuildInputs(text);
        var sent = NativeMethods.SendInput(
            (uint)inputs.Count,
            inputs.ToArray(),
            Marshal.SizeOf<Input>());

        if (sent != inputs.Count)
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
            return OperationResult<int>.Fail($"Windows ввела не весь текст: {error}");
        }

        return OperationResult<int>.Ok(text.Length);
    }

    private static List<Input> BuildInputs(string text)
    {
        var inputs = new List<Input>(text.Length * 2);

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    index++;
                }

                AddVirtualKey(inputs, NativeMethods.VkReturn);
                continue;
            }

            AddUnicodeCharacter(inputs, character);
        }

        return inputs;
    }

    private static void AddUnicodeCharacter(List<Input> inputs, char character)
    {
        inputs.Add(CreateKeyboardInput(0, character, NativeMethods.KeyEventUnicode));
        inputs.Add(CreateKeyboardInput(
            0,
            character,
            NativeMethods.KeyEventUnicode | NativeMethods.KeyEventKeyUp));
    }

    private static void AddVirtualKey(List<Input> inputs, int virtualKey)
    {
        inputs.Add(CreateKeyboardInput((ushort)virtualKey, 0, 0));
        inputs.Add(CreateKeyboardInput((ushort)virtualKey, 0, NativeMethods.KeyEventKeyUp));
    }

    private static Input CreateKeyboardInput(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new KeyboardInput
            {
                VirtualKey = virtualKey,
                ScanCode = scanCode,
                Flags = flags
            }
        }
    };
}
