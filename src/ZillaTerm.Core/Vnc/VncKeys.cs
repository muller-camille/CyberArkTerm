namespace ZillaTerm.Core.Vnc;

/// <summary>Symboles de touches X11 (keysyms) envoyés au serveur VNC.</summary>
public static class VncKeys
{
    public const uint BackSpace = 0xff08;
    public const uint Tab = 0xff09;
    public const uint Return = 0xff0d;
    public const uint Pause = 0xff13;
    public const uint ScrollLock = 0xff14;
    public const uint Escape = 0xff1b;
    public const uint Home = 0xff50;
    public const uint Left = 0xff51;
    public const uint Up = 0xff52;
    public const uint Right = 0xff53;
    public const uint Down = 0xff54;
    public const uint PageUp = 0xff55;
    public const uint PageDown = 0xff56;
    public const uint End = 0xff57;
    public const uint Print = 0xff61;
    public const uint Insert = 0xff63;
    public const uint Menu = 0xff67;
    public const uint NumLock = 0xff7f;
    public const uint KeypadEnter = 0xff8d;
    public const uint F1 = 0xffbe;
    public const uint ShiftLeft = 0xffe1;
    public const uint ShiftRight = 0xffe2;
    public const uint ControlLeft = 0xffe3;
    public const uint ControlRight = 0xffe4;
    public const uint CapsLock = 0xffe5;
    public const uint AltLeft = 0xffe9;
    public const uint AltRight = 0xffea;
    public const uint SuperLeft = 0xffeb;
    public const uint SuperRight = 0xffec;
    public const uint Delete = 0xffff;

    /// <summary>Touche de fonction F1 à F24.</summary>
    public static uint Function(int number) =>
        number is >= 1 and <= 24 ? F1 + (uint)(number - 1) : throw new ArgumentOutOfRangeException(nameof(number));

    /// <summary>
    /// Symbole d'un caractère tapé : le code Latin-1 pour les caractères imprimables jusqu'à U+00FF, sinon
    /// 0x01000000 + code Unicode (convention X11) ; 0 pour un caractère de contrôle (envoyé comme touche spéciale).
    /// </summary>
    public static uint FromChar(char c) => c switch
    {
        '\r' or '\n' => Return,
        '\t' => Tab,
        '\b' => BackSpace,
        _ when char.IsControl(c) || char.IsSurrogate(c) => 0,
        _ when c is (>= ' ' and <= '~') or (>= ' ' and <= 'ÿ') => c,
        _ => 0x01000000u + c,
    };
}
