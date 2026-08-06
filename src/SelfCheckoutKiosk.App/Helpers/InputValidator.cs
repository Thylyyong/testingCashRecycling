using System.Text.RegularExpressions;

namespace SelfCheckoutKiosk.App.Helpers
{
    public static class InputValidator
    {
        // Emoji blocker regex (matches common emoji unicode ranges)
        private static readonly Regex EmojiRegex = new Regex(@"[\uD800-\uDBFF][\uDC00-\uDFFF]|[\u2600-\u27BF]|[\uD83C-\uD83F][\uDC00-\uDFFF]|[\uD83D-\uD83E][\uDC00-\uDFFF]", RegexOptions.Compiled);

        // 1. Letters only (No numbers, no symbols, no emojis)
        private static readonly Regex LettersOnlyRegex = new Regex("^[a-zA-Z]*$", RegexOptions.Compiled);

        // 2. Numbers only (Allows digits and an optional decimal point '.')
        private static readonly Regex NumbersWithDecimalRegex = new Regex(@"^[0-9]*\.?[0-9]*$", RegexOptions.Compiled);

        // 3. Both letters and numbers (Alphanumeric only, no symbols, no emojis)
        private static readonly Regex AlphanumericOnlyRegex = new Regex("^[a-zA-Z0-9]*$", RegexOptions.Compiled);

        /// <summary>
        /// Checks if the input contains any emojis.
        /// </summary>
        public static bool ContainsEmoji(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            return EmojiRegex.IsMatch(input);
        }

        /// <summary>
        /// Rule 1: Letters only (No symbols, numbers, or emojis).
        /// </summary>
        public static bool IsLettersOnly(string input, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                errorMessage = "Field cannot be empty.";
                return false;
            }
            if (ContainsEmoji(input))
            {
                errorMessage = "Emojis are not allowed.";
                return false;
            }
            if (!LettersOnlyRegex.IsMatch(input))
            {
                errorMessage = "Only letters are allowed (no numbers or symbols).";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Rule 2: Numbers only, with an optional decimal point '.' (No other symbols or emojis).
        /// </summary>
        public static bool IsNumbersOnly(string input, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                errorMessage = "Field cannot be empty.";
                return false;
            }
            if (ContainsEmoji(input))
            {
                errorMessage = "Emojis are not allowed.";
                return false;
            }
            if (!NumbersWithDecimalRegex.IsMatch(input))
            {
                errorMessage = "Only numbers and a decimal point ('.') are allowed.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Rule 3: Both letters and numbers combined (Alphanumeric, no symbols, no emojis).
        /// </summary>
        public static bool IsAlphanumericOnly(string input, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(input))
            {
                errorMessage = "Field cannot be empty.";
                return false;
            }
            if (ContainsEmoji(input))
            {
                errorMessage = "Emojis are not allowed.";
                return false;
            }
            if (!AlphanumericOnlyRegex.IsMatch(input))
            {
                errorMessage = "Only letters and numbers are allowed (no symbols or spaces).";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Sanitizes input by stripping out HTML/Script tags and trimming whitespace.
        /// </summary>
        public static string SanitizeInput(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // Remove HTML/Script tags
            string sanitized = Regex.Replace(input, "<.*?>", string.Empty);

            return sanitized.Trim();
        }
    }
}