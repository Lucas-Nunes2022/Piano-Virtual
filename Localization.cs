using System.Globalization;

namespace piano
{
    // The app speaks English and Portuguese. Every user-facing string is written as a pair at the
    // place it is used: L.T("English", "Português").
    public static class L
    {
        public static bool IsPortuguese { get; private set; }

        public static string Code => IsPortuguese ? "pt" : "en";

        // setting: "en", "pt" or "auto" (follow the Windows display language)
        public static void Init(string setting)
        {
            IsPortuguese = setting switch
            {
                "pt" => true,
                "en" => false,
                _ => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "pt"
            };
        }

        public static string T(string english, string portuguese) => IsPortuguese ? portuguese : english;
    }
}
