using System.Runtime.InteropServices;

namespace G27Hub.Core;

/// <summary>Nomes de tecla ⇄ códigos virtuais do Windows (VK). Combinação = "Ctrl+Shift+R".</summary>
public static class KeyNames
{
    static readonly Dictionary<string, int> ByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = 0x11, ["Shift"] = 0x10, ["Alt"] = 0x12, ["Win"] = 0x5B,
        ["Espaço"] = 0x20, ["Enter"] = 0x0D, ["Esc"] = 0x1B, ["Tab"] = 0x09, ["Backspace"] = 0x08,
        ["Cima"] = 0x26, ["Baixo"] = 0x28, ["Esquerda"] = 0x25, ["Direita"] = 0x27,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Home"] = 0x24, ["End"] = 0x23, ["PgUp"] = 0x21, ["PgDn"] = 0x22,
        ["CapsLock"] = 0x14, ["PrintScreen"] = 0x2C, ["Pause"] = 0x13,
        ["Num0"] = 0x60, ["Num1"] = 0x61, ["Num2"] = 0x62, ["Num3"] = 0x63, ["Num4"] = 0x64,
        ["Num5"] = 0x65, ["Num6"] = 0x66, ["Num7"] = 0x67, ["Num8"] = 0x68, ["Num9"] = 0x69,
        ["Num*"] = 0x6A, ["Num+"] = 0x6B, ["Num-"] = 0x6D, ["Num."] = 0x6E, ["Num/"] = 0x6F,
        ["-"] = 0xBD, ["="] = 0xBB, [","] = 0xBC, ["."] = 0xBE, [";"] = 0xBF, ["'"] = 0xC0, ["["] = 0xDB, ["]"] = 0xDD, ["\\"] = 0xE2,
        // mídia
        ["Play/Pausa"] = 0xB3, ["Próxima"] = 0xB0, ["Anterior"] = 0xB1, ["Parar"] = 0xB2,
        ["Volume+"] = 0xAF, ["Volume-"] = 0xAE, ["Mudo"] = 0xAD,
    };

    static readonly Dictionary<int, string> ByVk = ByName.GroupBy(kv => kv.Value).ToDictionary(g => g.Key, g => g.First().Key);

    /// <summary>Nome para mostrar (no idioma da interface); "Ctrl+Espaço" continua sendo o que fica salvo.</summary>
    public static string Display(string combo) => string.Join("+", combo.Split('+').Select(Lang.T));

    public static readonly string[] Media = ["Play/Pausa", "Próxima", "Anterior", "Parar", "Volume+", "Volume-", "Mudo"];

    public static string Name(int vk) =>
        ByVk.TryGetValue(vk, out var n) ? n
        : vk is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39 ? ((char)vk).ToString()
        : vk is >= 0x70 and <= 0x87 ? $"F{vk - 0x6F}"
        : $"VK{vk:X2}";

    public static int? Vk(string name)
    {
        name = name.Trim();
        if (ByName.TryGetValue(name, out var vk)) return vk;
        if (name.Length == 1 && char.IsAsciiLetterOrDigit(name[0])) return char.ToUpperInvariant(name[0]);
        if (name.Length is 2 or 3 && name[0] is 'F' or 'f' && int.TryParse(name[1..], out var f) && f is >= 1 and <= 24) return 0x6F + f;
        if (name.StartsWith("VK", StringComparison.OrdinalIgnoreCase) && int.TryParse(name[2..], System.Globalization.NumberStyles.HexNumber, null, out var raw)) return raw;
        return null;
    }

    /// <summary>"Ctrl+Shift+R" → [Ctrl, Shift, R]. Null se algum nome não existe.</summary>
    public static int[]? Parse(string combo)
    {
        if (string.IsNullOrWhiteSpace(combo)) return null;
        // "Num+" e "+" sozinho: o último '+' separa só se tiver algo depois
        var parts = new List<string>();
        var cur = "";
        foreach (var ch in combo)
        {
            if (ch == '+' && cur.Length > 0 && !cur.Equals("Num", StringComparison.OrdinalIgnoreCase) && !cur.Equals("Volume", StringComparison.OrdinalIgnoreCase)) { parts.Add(cur); cur = ""; }
            else cur += ch;
        }
        if (cur.Length > 0) parts.Add(cur);
        var vks = parts.Select(Vk).ToArray();
        return vks.All(v => v != null) ? vks.Select(v => v!.Value).ToArray() : null;
    }

    public static string Format(IEnumerable<int> vks) => string.Join("+", vks.Select(Name));

    public static bool IsModifier(int vk) => vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;
}

/// <summary>Onde as atribuições "apertam" teclas e abrem coisas (o real usa SendInput; os testes usam um falso).</summary>
public interface IKeySink
{
    void Down(int vk);
    void Up(int vk);
    void Launch(string pathOrUrl);
}

/// <summary>
/// SendInput com scan code (jogos com DirectInput de teclado ignoram tecla só por VK);
/// teclas de mídia vão por VK, porque não têm scan code.
/// </summary>
public sealed class SendInputSink : IKeySink
{
    public void Down(int vk) => Send(vk, up: false);
    public void Up(int vk) => Send(vk, up: true);

    public void Launch(string pathOrUrl) =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pathOrUrl) { UseShellExecute = true });

    /// <summary>
    /// Teclas de mídia/volume/navegador (VK 0xA6–0xB7) vão só pelo VK: o scan code delas é "estendido" e,
    /// mandado como scan code comum, vira letra (Volume− saía como C).
    /// </summary>
    public static bool UsesScanCode(int vk) => vk is not (>= 0xA6 and <= 0xB7);

    static void Send(int vk, bool up)
    {
        var scan = UsesScanCode(vk) ? (ushort)MapVirtualKey((uint)vk, 0) : (ushort)0; // MAPVK_VK_TO_VSC
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (scan != 0)
        {
            flags |= KEYEVENTF_SCANCODE;
            if (Extended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        }
        var input = new INPUT { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = scan != 0 ? (ushort)0 : (ushort)vk, wScan = scan, dwFlags = flags } } };
        if (SendInput(1, [input], Marshal.SizeOf<INPUT>()) != 1)
            G27Hub.Logging.AppLog.Aviso("PERFIL", $"O Windows recusou a tecla {KeyNames.Name(vk)} (erro {Marshal.GetLastWin32Error()}); um jogo rodando como administrador bloqueia teclas de apps comuns");
    }

    static bool Extended(int vk) => vk is >= 0x21 and <= 0x28 or 0x2D or 0x2E or 0x5B or 0x5C or 0x6F or 0xA3 or 0xA5 or 0x90;

    const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_SCANCODE = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nint dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public nint dwExtraInfo; }

    // INPUT é uma union; o tamanho tem que ser o do maior membro (MOUSEINPUT), senão o SendInput recusa.
    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint mapType);
}
