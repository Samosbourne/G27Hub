using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using G27Hub.Core;
using static G27Hub.Core.Lang;

namespace G27Hub.App.Controls;

/// <summary>Campo "clique e aperte a tecla": guarda uma tecla ou combinação ("Ctrl+Shift+R").</summary>
public sealed class KeyCaptureBox : Border
{
    readonly TextBlock _text = new() { FontSize = 12.5, Foreground = Brushes.White };
    string _value = "";

    public event Action<string>? Changed;

    public string Value
    {
        get => _value;
        set { _value = value; _text.Text = value.Length > 0 ? KeyNames.Display(value) : T("Clique e aperte a tecla"); }
    }

    public KeyCaptureBox()
    {
        Background = Draw.Hex("#232B3A");
        BorderBrush = Draw.Hex("#2E3A4F");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(2);
        Padding = new Thickness(10, 7, 10, 7);
        Cursor = Cursors.Hand;
        Focusable = true;
        Child = _text;
        Value = "";
        MouseLeftButtonUp += (_, _) => Focus();
        GotKeyboardFocus += (_, _) => { BorderBrush = Draw.Hex("#00D2FF"); _text.Text = T("Aperte a tecla… (Esc cancela, Backspace apaga)"); };
        LostKeyboardFocus += (_, _) => { BorderBrush = Draw.Hex("#2E3A4F"); Value = _value; };
    }

    /// <summary>Capturando agora (as atribuições e a seleção pelo volante esperam).</summary>
    public bool Capturing => IsKeyboardFocused;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) { Keyboard.ClearFocus(); return; }
        if (key == Key.Back) { Set(""); return; }
        var vk = KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0 || KeyNames.IsModifier(vk)) return; // espera a tecla "de verdade"

        var vks = new List<int>();
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) vks.Add(0x11);
        if (mods.HasFlag(ModifierKeys.Shift)) vks.Add(0x10);
        if (mods.HasFlag(ModifierKeys.Alt)) vks.Add(0x12);
        if (mods.HasFlag(ModifierKeys.Windows)) vks.Add(0x5B);
        vks.Add(vk);
        Set(KeyNames.Format(vks));
    }

    void Set(string combo)
    {
        Value = combo;
        Changed?.Invoke(combo);
        Keyboard.ClearFocus();
    }
}
