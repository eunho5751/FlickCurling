namespace FlickCurling.Game.Account
{
    /// <summary>
    /// The nickname rules: 3 to 12 characters counted by string.Length, letters and digits only (any script, so Korean
    /// and Japanese are fine); no symbols, emoji (surrogate pairs) or spaces.
    /// </summary>
    public static class Nickname
    {
        public const int MinLength = 3, MaxLength = 12;

        public enum Check { Ok, Empty, TooShort, TooLong, BadCharacter }

        public static Check Validate(string name)
        {
            if (string.IsNullOrEmpty(name)) return Check.Empty;
            foreach (char c in name)
                if (!char.IsLetterOrDigit(c)) return Check.BadCharacter;   // a surrogate half (emoji) is neither
            if (name.Length < MinLength) return Check.TooShort;
            if (name.Length > MaxLength) return Check.TooLong;
            return Check.Ok;
        }

        public static bool IsValid(string name) => Validate(name) == Check.Ok;

        /// <summary>A UGS player name without the "#1234" tag UGS adds to keep names apart.</summary>
        public static string Display(string playerName)
        {
            if (string.IsNullOrEmpty(playerName)) return "";
            int hash = playerName.LastIndexOf('#');
            return hash > 0 ? playerName.Substring(0, hash) : playerName;
        }
    }
}
