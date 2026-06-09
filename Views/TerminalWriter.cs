using System.Text.RegularExpressions;
using System.Windows.Documents;
using RichTextBox = System.Windows.Controls.RichTextBox;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace FrpManager.Views
{
    /// <summary>
    /// Writes color-coded, ANSI-stripped text to a WPF RichTextBox terminal.
    /// Each line is appended as a Paragraph with a color based on content analysis:
    /// error lines → red, warning lines → yellow, info lines → cyan, success → green.
    /// Automatically trims oldest lines beyond MaxLines to prevent memory bloat.
    /// </summary>
    public class TerminalWriter
    {
        private readonly RichTextBox _box;
        private const int MaxLines = 2000;

        // ── Color Palette ────────────────────────────────────────────────────
        /// <summary>Info/standard output — light cyan.</summary>
        public static readonly Brush BrushInfo = new SolidColorBrush(Color.FromRgb(0xB8, 0xD8, 0xEE));
        /// <summary>Warning lines — amber/gold.</summary>
        public static readonly Brush BrushWarn = new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x60));
        /// <summary>Error/stderr lines — light red.</summary>
        public static readonly Brush BrushError = new SolidColorBrush(Color.FromRgb(0xF0, 0x80, 0x80));
        /// <summary>Success/startup lines — light green.</summary>
        public static readonly Brush BrushSuccess = new SolidColorBrush(Color.FromRgb(0x70, 0xD0, 0xA0));
        /// <summary>Muted/dim text for system messages — slate blue.</summary>
        public static readonly Brush BrushMuted = new SolidColorBrush(Color.FromRgb(0x60, 0x88, 0xA0));

        /// <summary>
        /// Creates a new TerminalWriter bound to the specified RichTextBox.
        /// </summary>
        /// <param name="terminalBox">The RichTextBox to write terminal output to.</param>
        public TerminalWriter(RichTextBox terminalBox)
        {
            _box = terminalBox;
        }

        /// <summary>
        /// Appends a line to the terminal with automatic color detection.
        /// Strips ANSI escape codes and classifies the line by content.
        /// </summary>
        /// <param name="line">Raw line from frpc stdout or stderr.</param>
        /// <param name="isStderr">True if this line came from stderr (always colored as error).</param>
        public void AppendLine(string line, bool isStderr = false)
        {
            // Strip ANSI escape sequences (e.g., \x1B[31m for red, \x1B[0m for reset)
            // frpc may output colored logs on some terminals
            line = Regex.Replace(line, @"\x1B\[[0-9;]*m", "");

            // Determine brush color by priority:
            // 1. stderr → always error (red)
            // 2. Line contains [E] → error log level
            // 3. Line contains [W] → warning log level
            // 4. Line contains [I] → info log level (default)
            // 5. Line contains "success"/"started" → success (green)
            Brush brush;
            if (isStderr) brush = BrushError;
            else if (line.Contains("[E]")) brush = BrushError;
            else if (line.Contains("[W]")) brush = BrushWarn;
            else if (line.Contains("[I]")) brush = BrushInfo;
            else if (line.Contains("success", StringComparison.OrdinalIgnoreCase) ||
                     line.Contains("started", StringComparison.OrdinalIgnoreCase))
                brush = BrushSuccess;
            else brush = BrushInfo;

            Append(line, brush);
        }

        /// <summary>
        /// Appends a text paragraph to the terminal with the specified brush color.
        /// Automatically scrolls to the end and trims oldest lines when exceeding MaxLines.
        /// Each call creates a new Paragraph — text within a single call does NOT wrap
        /// automatically; the RichTextBox handles word wrap based on its viewport width.
        /// </summary>
        /// <param name="text">The text to append.</param>
        /// <param name="brush">Brush to use for the paragraph foreground color.</param>
        public void Append(string text, Brush brush)
        {
            // Create a new Paragraph for this line — the RichTextBox will
            // automatically wrap long lines based on its actual viewport width
            var para = new Paragraph(new Run(text)) { Foreground = brush };

            _box.Document.Blocks.Add(para);
            _box.ScrollToEnd();

            // Trim oldest lines to prevent unbounded memory growth
            // Each Paragraph is a Block; remove from the front (oldest)
            while (_box.Document.Blocks.Count > MaxLines)
                _box.Document.Blocks.Remove(_box.Document.Blocks.FirstBlock);
        }

        /// <summary>
        /// Clears all terminal output.
        /// </summary>
        public void Clear()
        {
            _box.Document.Blocks.Clear();
        }
    }
}
