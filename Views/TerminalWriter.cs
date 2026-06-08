using System.Text.RegularExpressions;
using System.Windows.Documents;
using RichTextBox = System.Windows.Controls.RichTextBox;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace FrpManager.Views
{
    public class TerminalWriter
    {
        private readonly RichTextBox _box;
        private const int MaxLines = 2000;

        public static readonly Brush BrushInfo = new SolidColorBrush(Color.FromRgb(0xB8, 0xD8, 0xEE));
        public static readonly Brush BrushWarn = new SolidColorBrush(Color.FromRgb(0xF0, 0xC0, 0x60));
        public static readonly Brush BrushError = new SolidColorBrush(Color.FromRgb(0xF0, 0x80, 0x80));
        public static readonly Brush BrushSuccess = new SolidColorBrush(Color.FromRgb(0x70, 0xD0, 0xA0));
        public static readonly Brush BrushMuted = new SolidColorBrush(Color.FromRgb(0x60, 0x88, 0xA0));

        public TerminalWriter(RichTextBox terminalBox)
        {
            _box = terminalBox;
        }

        public void AppendLine(string line, bool isStderr = false)
        {
            // Strip ANSI escape codes
            line = Regex.Replace(line, @"\x1B\[[0-9;]*m", "");
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

        public void Append(string text, Brush brush)
        {
            var para = new Paragraph(new Run(text)) { Foreground = brush };
            _box.Document.Blocks.Add(para);
            _box.ScrollToEnd();
            while (_box.Document.Blocks.Count > MaxLines)
                _box.Document.Blocks.Remove(_box.Document.Blocks.FirstBlock);
        }

        public void Clear()
        {
            _box.Document.Blocks.Clear();
        }
    }
}
